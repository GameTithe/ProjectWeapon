#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProjectWeapon.AutoAttack;
using ProjectWeapon.Combat;
using ProjectWeapon.Inventory;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.Run;
using ProjectWeapon.WeaponSwitch;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.DpsQA
{
    public static class DpsQaRunner
    {
        private const string Caster = "qa-caster";
        private const string Dummy = "qa-dummy";

        public static DpsQaReport Measure(DpsQaSettings settings, WorldPoint2D origin, WorldPoint2D target)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (!origin.IsFinite || !target.IsFinite)
                throw new ArgumentException("The QA caster and target positions must be finite.");
            settings.Validate();

            var offsets = new int[settings.Trials];
            uint state = unchecked((uint)settings.Seed);
            for (int trial = 0; trial < offsets.Length; trial++)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                offsets[trial] = (int)(state % (uint)settings.SkillOffsetSlots) * settings.StepMilliseconds;
            }

            WeaponDefinition[] weapons = settings.Weapons.CreateLookup().Values
                .OrderBy(weapon => weapon.Grade).ThenBy(weapon => weapon.Id.Value, StringComparer.Ordinal).ToArray();
            var sharedSkill = weapons.FirstOrDefault()?.Skill;
            var targetCandidate = new TargetCandidate(Dummy, target, isAlive: true, isAttackable: true);
            double seconds = settings.WindowMilliseconds / 1000d * settings.Trials;
            var results = new DpsWeaponResult[weapons.Length];
            for (int index = 0; index < weapons.Length; index++)
            {
                WeaponDefinition weapon = weapons[index];
                if (weapon.BasicAttack is not BasicAttackPattern attack
                    || (attack is not MeleeSlashPattern && attack is not SpearThrustPattern
                        && attack is not CircularStrikePattern && attack is not SingleArrowPattern)
                    || weapon.Skill != sharedSkill || weapon.Skill is not ManualSkillPattern skill || skill.AimKind != ManualSkillAimKind.Enemy)
                    throw new InvalidOperationException($"Unsupported QA attack or skill on {weapon.Id.Value}.");

                var noSkill = new TrialResult();
                var withSkill = new TrialResult();
                foreach (int offset in offsets)
                {
                    noSkill.Add(new Trial(settings, origin, targetCandidate, weapon).Run(offset, withSkill: false));
                    withSkill.Add(new Trial(settings, origin, targetCandidate, weapon).Run(offset, withSkill: true));
                }
                if (noSkill.BasicHits != withSkill.BasicHits || noSkill.BasicDamage != withSkill.BasicDamage)
                    throw new InvalidOperationException("The fixed QA skill unexpectedly interrupted a basic attack.");

                WeaponDefinition? baseline = settings.BaselineWeapons.FirstOrDefault(source => source &&
                    source.Grade == weapon.Grade && source.BasicAttack == weapon.BasicAttack);
                string provenance = baseline ? "baseline:" + baseline!.Id.Value : "calibration";
                results[index] = new DpsWeaponResult(weapon.Id.Value, provenance, weapon.Grade, attack.Range,
                    attack.Cooldown, noSkill.BasicHits, withSkill.SkillHits,
                    noSkill.BasicDamage / seconds, (withSkill.BasicDamage + withSkill.SkillDamage) / seconds);
            }

            string conditions = FormattableString.Invariant(
                $"Seed {settings.Seed} | {settings.Trials} trials | {Math.Sqrt(origin.SquaredDistanceTo(target)):F2} units | {settings.WindowMilliseconds}ms/{settings.StepMilliseconds}ms");
            conditions += FormattableString.Invariant(
                $"\nSkill every {settings.SkillPeriodMilliseconds}ms | damage {settings.SkillDamage} | first offsets {string.Join(", ", offsets)}ms");
            return new DpsQaReport(conditions, results);
        }

        private sealed class Trial : IManualSkillEffect
        {
            private readonly DpsQaSettings settings;
            private readonly WorldPoint2D origin;
            private readonly TargetCandidate[] targets;
            private readonly WeaponDefinition weapon;
            private readonly TrialResult result = new TrialResult();
            private readonly HashSet<Guid> applications = new HashSet<Guid>();
            private readonly List<(ProjectileLifecycle Arrow, TimeSpan StartedAt, TimeSpan ContactDelay)> arrows = new();
            private TimeSpan clock;

            public Trial(DpsQaSettings settings, WorldPoint2D origin, TargetCandidate target, WeaponDefinition weapon)
            {
                this.settings = settings;
                this.origin = origin;
                this.weapon = weapon;
                targets = new[] { target };
            }

            public TrialResult Run(int skillOffset, bool withSkill)
            {
                var run = new RunState(() => clock);
                TimeSpan limit = TimeSpan.FromMilliseconds(settings.WindowMilliseconds);
                if (!run.TryStartRun(RunId.New(), 1, limit))
                    throw new InvalidOperationException("The QA expedition could not start.");

                var scheduler = new AutoAttackScheduler(run, ExecuteBasicAttack, initiallyEnabled: true);
                var instance = WeaponInstance.Create(weapon.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
                var selection = new EquippedWeaponSelection(run, new InventoryService(new[] { instance }),
                    settings.Weapons, () => false, new WeaponActionLocks());
                var skill = new ManualSkillExecutor(run, selection, this, () => false,
                    id => id == Dummy ? targets[0] : null, Caster);

                for (long millisecond = 0; millisecond < settings.WindowMilliseconds; millisecond += settings.StepMilliseconds)
                {
                    clock = TimeSpan.FromMilliseconds(millisecond);
                    scheduler.TryAttack(clock, origin, weapon, targets);
                    AdvanceArrows();
                    if (withSkill && millisecond >= skillOffset
                        && (millisecond - skillOffset) % settings.SkillPeriodMilliseconds == 0)
                        skill.TryActivate(Guid.NewGuid(), instance.Id, clock, origin, ManualSkillAim.AtEnemy(Dummy));
                }
                return result;
            }

            private bool ExecuteBasicAttack(BasicAttackRequest request)
            {
                Guid application = Guid.NewGuid();
                switch (request.Pattern)
                {
                    case MeleeSlashPattern slash when slash.TryCreateSwing(request, application, Caster, targets, out var swing):
                        foreach (DamageRequest hit in swing!.Hits)
                            Apply(hit, skill: false);
                        return true;
                    case SpearThrustPattern spear when spear.TryCreateThrust(request, application, Caster, out SpearThrust thrust):
                        Apply(thrust.Hit, skill: false);
                        return true;
                    case CircularStrikePattern circle when circle.TryCreateStrike(request, application, Caster, targets, out var strike):
                        foreach (DamageRequest hit in strike!.Hits)
                            Apply(hit, skill: false);
                        return true;
                    case SingleArrowPattern bow:
                        var arrow = new ProjectileLifecycle();
                        if (!bow.TryLaunch(request, application, Caster, arrow))
                            return false;
                        double speed = Math.Sqrt(arrow.Velocity.SquaredDistanceTo(default));
                        double distance = Math.Sqrt(request.Origin.SquaredDistanceTo(request.Target.Position));
                        double flightSeconds = distance == 0 ? 0 : distance / speed;
                        TimeSpan contactDelay = flightSeconds >= arrow.Lifetime.TotalSeconds
                            ? arrow.Lifetime : TimeSpan.FromSeconds(flightSeconds);
                        arrows.Add((arrow, request.StartedAt, contactDelay));
                        return true;
                    default:
                        return false;
                }
            }

            private void AdvanceArrows()
            {
                for (int index = arrows.Count - 1; index >= 0; index--)
                {
                    var flight = arrows[index];
                    TimeSpan elapsed = clock - flight.StartedAt;
                    ProjectileLifecycle arrow = flight.Arrow;
                    if (elapsed >= flight.ContactDelay && flight.ContactDelay < arrow.Lifetime
                        && arrow.ContactTarget(arrow.ApplicationId, flight.ContactDelay, targets[0]) is DamageRequest impact)
                        Apply(impact, skill: false);
                    else
                        arrow.Advance(arrow.ApplicationId, elapsed);

                    if (arrow.Phase != ProjectilePhase.Active)
                        arrows.RemoveAt(index);
                }
            }

            public bool TryStart(ManualSkillActivationRequest request)
            {
                if (request.Target is not TargetCandidate target)
                    return false;

                var area = AreaDamageRequest.Circle(Guid.NewGuid(), request.AttackerId, target.Position, 0f, settings.SkillDamage);
                IReadOnlyList<DamageRequest> hits = AreaDamageSelector.FindTargets(area, targets);
                foreach (DamageRequest hit in hits)
                    Apply(hit, skill: true);
                return hits.Count > 0;
            }

            private void Apply(DamageRequest hit, bool skill)
            {
                if (hit.TargetId != Dummy || hit.AttackerId != Caster || !applications.Add(hit.ApplicationId))
                    return;

                if (skill)
                {
                    result.SkillHits++;
                    result.SkillDamage += hit.Amount;
                }
                else
                {
                    result.BasicHits++;
                    result.BasicDamage += hit.Amount;
                }
            }
        }

        private sealed class TrialResult
        {
            public int BasicHits { get; set; }
            public int SkillHits { get; set; }
            public double BasicDamage { get; set; }
            public double SkillDamage { get; set; }

            public void Add(TrialResult trial)
            {
                BasicHits += trial.BasicHits;
                SkillHits += trial.SkillHits;
                BasicDamage += trial.BasicDamage;
                SkillDamage += trial.SkillDamage;
            }
        }
    }
}
