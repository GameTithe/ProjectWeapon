#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEditor;

namespace ProjectWeapon.AutoAttack.Tests
{
    public sealed class StormSwordPatternTests
    {
        private const string CatalogPath = "Assets/ProjectWeapon/Weapons/PrototypeStormSword/";
        private const string Attacker = "storm-preview";
        private static readonly WorldPoint2D Origin = new WorldPoint2D(2, 1);
        private static readonly WorldPoint2D Impact = new WorldPoint2D(6, 2);
        private readonly List<StormSwordFlight> waves = new List<StormSwordFlight>();
        private WeaponDefinition weapon = null!;
        private StormSwordPattern pattern = null!;
        private RunState run = null!;
        private RunId runId;
        private AutoAttackScheduler scheduler = null!;
        private TimeSpan now;
        private IReadOnlyList<TargetCandidate> targets = null!;

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(CatalogPath + "PrototypeStormSwordWeaponCatalog.asset");
            weapon = catalog.CreateLookup()[new WeaponDefinitionId("storm_sword")];
            pattern = (StormSwordPattern)weapon.BasicAttack;
            waves.Clear();
            now = TimeSpan.Zero;
            runId = RunId.New();
            run = new RunState(() => now);
            Assert.That(run.TryStartRun(runId, 5, Ms(10_000)), Is.True);
            scheduler = new AutoAttackScheduler(run, Execute, initiallyEnabled: true);
            targets = new[] { Candidate("aim", 6, 1) };
        }

        [Test]
        public void StormSwordCatalogUsesOneConfiguredGradeFourPattern()
        {
            Assert.That(weapon.Grade, Is.EqualTo(4));
            Assert.That(weapon.Kind, Is.EqualTo(WeaponKind.Sword));
            Assert.That(pattern, Is.SameAs(AssetDatabase.LoadAssetAtPath<StormSwordPattern>(CatalogPath + "StormSwordWave.asset")));
            Assert.That(weapon.Skill, Is.Null);
            Assert.That(pattern.Range, Is.EqualTo(6.5f));
            Assert.That(pattern.Cooldown, Is.EqualTo(Ms(1450)));
            Assert.That(pattern.Speed, Is.EqualTo(12f));
            Assert.That(pattern.Lifetime, Is.EqualTo(Ms(625)));
            Assert.That(pattern.DirectDamage, Is.EqualTo(13f));
            Assert.That(pattern.LightningRadius, Is.EqualTo(1.5f));
            Assert.That(pattern.LightningDamage, Is.EqualTo(16f));
        }

        [Test]
        public void OneWaveFollowsTheOriginalNormalizedAim()
        {
            targets = new[] { Candidate("nearest", 5, 5), Candidate("farther", 8, 1) };
            Assert.That(CastAt(100), Is.True);
            StormSwordFlight wave = waves.Single();
            Assert.That(wave.RunId, Is.EqualTo(runId));
            Assert.That(wave.StartedAt, Is.EqualTo(Ms(100)));
            Assert.That(wave.Velocity.X, Is.EqualTo(7.2f).Within(0.0001f));
            Assert.That(wave.Velocity.Y, Is.EqualTo(9.6f).Within(0.0001f));
            Assert.That(wave.Position.X, Is.EqualTo(Origin.X));
            Assert.That(wave.Position.Y, Is.EqualTo(Origin.Y));

            targets = new[] { Candidate("nearest", -3, -3) };
            Assert.That(wave.Advance(wave.ApplicationId, Ms(350)), Is.True);
            Assert.That(wave.Position.X, Is.EqualTo(3.8f).Within(0.0001f));
            Assert.That(wave.Position.Y, Is.EqualTo(3.4f).Within(0.0001f));
            Assert.That(waves, Has.Count.EqualTo(1));
        }

        [Test]
        public void FirstPhysicalEnemyContactEmitsDistinctDirectHitAndOneLightningCircle()
        {
            Assert.That(CastAt(100), Is.True);
            StormSwordFlight wave = waves.Single();
            var hitTarget = Candidate("intercepts", 6, 2);
            var nearby = new[]
            {
                hitTarget,
                hitTarget,
                Candidate("edge", 7.5f, 2),
                Candidate("side", 6, 0.5f),
                Candidate("outside", 7.51f, 2),
                Candidate("dead", 6, 2.5f, alive: false),
                Candidate("untargetable", 6, 1.5f, attackable: false),
                Candidate(Attacker, 5.5f, 2)
            };

            StormSwordImpact? impact = wave.ContactTarget(wave.ApplicationId, Ms(350), Impact, hitTarget, nearby);
            Assert.That(impact, Is.Not.Null);
            Assert.That(wave.Phase, Is.EqualTo(ProjectilePhase.HitTarget));
            Assert.That(impact!.Position.X, Is.EqualTo(Impact.X));
            Assert.That(impact.Position.Y, Is.EqualTo(Impact.Y));
            Assert.That(impact.EffectiveAt, Is.EqualTo(Ms(350)));
            Assert.That(impact.DirectHit.TargetId, Is.EqualTo("intercepts"));
            Assert.That(impact.DirectHit.Amount, Is.EqualTo(13f));
            Assert.That(impact.DirectHit.ApplicationId, Is.EqualTo(wave.ApplicationId));
            Assert.That(impact.Lightning.ApplicationId, Is.Not.EqualTo(wave.ApplicationId));
            Assert.That(impact.Lightning.ApplicationId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(impact.Lightning.RunId, Is.EqualTo(runId));
            Assert.That(impact.Lightning.Hits.Select(hit => hit.TargetId),
                Is.EquivalentTo(new[] { "intercepts", "edge", "side" }));
            Assert.That(impact.Lightning.Hits.All(hit => hit.Amount == 16f && hit.AttackerId == Attacker
                && hit.ApplicationId == impact.Lightning.ApplicationId), Is.True);

            Assert.That(wave.ContactTarget(wave.ApplicationId, Ms(350), Impact, hitTarget, nearby), Is.Null);
            Assert.That(wave.ContactTarget(wave.ApplicationId, Ms(351), Impact, nearby[2], nearby), Is.Null);
            Assert.That(wave.ContactWorld(wave.ApplicationId, Ms(351)), Is.False);
        }

        [Test]
        public void ContactPointIsTheLightningOriginEvenIfItIsOnTheFarEdgeOfTheDirectTarget()
        {
            Assert.That(CastAt(100), Is.True);
            StormSwordFlight wave = waves.Single();
            TargetCandidate hitTarget = Candidate("large-collider", 8, 2);
            TargetCandidate nearby = Candidate("lightning-only", 5, 2);

            StormSwordImpact? impact = wave.ContactTarget(wave.ApplicationId, Ms(350), Impact,
                hitTarget, new[] { nearby });

            Assert.That(impact!.DirectHit.TargetId, Is.EqualTo("large-collider"));
            Assert.That(impact.Lightning.Hits.Single().TargetId, Is.EqualTo("lightning-only"));
        }

        [Test]
        public void InvalidContactWrongIdAndTimestampDoNotConsumeTheWave()
        {
            Assert.That(CastAt(100), Is.True);
            StormSwordFlight wave = waves.Single();
            Guid id = wave.ApplicationId;
            TargetCandidate valid = Candidate("valid", 6, 2);
            var empty = Array.Empty<TargetCandidate>();

            Assert.That(wave.ContactWorld(Guid.NewGuid(), Ms(999)), Is.False);
            Assert.That(wave.ContactTarget(id, Ms(200), new WorldPoint2D(float.NaN, 2), valid, empty), Is.Null);
            foreach (TargetCandidate invalid in new[]
            {
                default, Candidate(Attacker, 6, 2), Candidate("dead", 6, 2, alive: false),
                Candidate("untargetable", 6, 2, attackable: false)
            })
                Assert.That(wave.ContactTarget(id, Ms(210), Impact, invalid, empty), Is.Null);
            Assert.That(wave.ContactTarget(id, Ms(209), Impact, valid, empty), Is.Null);
            Assert.That(wave.Phase, Is.EqualTo(ProjectilePhase.Active));

            StormSwordImpact? accepted = wave.ContactTarget(id, Ms(220), Impact, valid, empty);
            Assert.That(accepted!.Lightning.Hits.Single().TargetId, Is.EqualTo("valid"));
        }

        [Test]
        public void SolidContactStopsTheWaveWithoutCreatingLightning()
        {
            Assert.That(CastAt(100), Is.True);
            StormSwordFlight wave = waves.Single();
            Assert.That(wave.ContactWorld(wave.ApplicationId, Ms(350)), Is.True);
            Assert.That(wave.Phase, Is.EqualTo(ProjectilePhase.Blocked));
            Assert.That(wave.ContactTarget(wave.ApplicationId, Ms(350), Impact, Candidate("behind-wall", 6, 2), targets), Is.Null);
        }

        [TestCase(724, true, ProjectilePhase.HitTarget)]
        [TestCase(725, false, ProjectilePhase.Expired)]
        [TestCase(726, false, ProjectilePhase.Expired)]
        public void LifetimeBoundaryAllowsOnlyEarlierConfirmedHits(int contactedAt, bool hits, ProjectilePhase phase)
        {
            Assert.That(CastAt(100), Is.True);
            StormSwordFlight wave = waves.Single();
            StormSwordImpact? hit = wave.ContactTarget(wave.ApplicationId, Ms(contactedAt), Impact,
                Candidate("target", 6, 2), Array.Empty<TargetCandidate>());
            Assert.That(hit != null, Is.EqualTo(hits));
            Assert.That(wave.Phase, Is.EqualTo(phase));
        }

        [TestCase("cancelled")]
        [TestCase("terminal")]
        [TestCase("new-run")]
        [TestCase("deadline")]
        public void CancelledTerminalStaleOrTimedOutWaveCannotHit(string scenario)
        {
            bool atDeadline = scenario == "deadline";
            Assert.That(CastAt(atDeadline ? 9800 : 100), Is.True);
            StormSwordFlight wave = waves.Single();
            if (scenario == "cancelled")
                wave.Cancel();
            if (scenario == "terminal" || scenario == "new-run")
            {
                now = Ms(200);
                run.ProcessSignals(new[] { RunSignal.PlayerDied(runId, now) });
                if (scenario == "new-run")
                {
                    Assert.That(run.TryReturnToTown(runId), Is.True);
                    Assert.That(run.TryStartRun(RunId.New(), 5, Ms(10_000)), Is.True);
                }
            }
            Assert.That(wave.ContactTarget(wave.ApplicationId, Ms(atDeadline ? 10_000 : 300), Impact,
                Candidate("target", 6, 2), targets), Is.Null);
            Assert.That(wave.Phase, Is.EqualTo(ProjectilePhase.Inactive));
        }

        [Test]
        public void OutsideAcquisitionDoesNotConsumeCooldownAndExactlyOneWaveLaunchesPerCast()
        {
            targets = new[] { Candidate("outside", 8.51f, 1) };
            Assert.That(CastAt(100), Is.False);
            Assert.That(waves, Is.Empty);

            targets = new[] { Candidate("exact-edge", 8.5f, 1) };
            Assert.That(CastAt(100), Is.True);
            Assert.That(CastAt(100), Is.False);
            Assert.That(CastAt(1549), Is.False);
            Assert.That(CastAt(1550), Is.True);
            Assert.That(waves.Select(wave => wave.StartedAt), Is.EqualTo(new[] { Ms(100), Ms(1550) }));
            Assert.That(waves.Select(wave => wave.ApplicationId).Distinct().Count(), Is.EqualTo(2));
        }

        [TestCase("lifetimeMilliseconds")]
        [TestCase("speed")]
        [TestCase("directDamage")]
        [TestCase("lightningRadius")]
        [TestCase("lightningDamage")]
        public void InvalidTuningCannotSpawnAWaveOrConsumeTheSchedulerCooldown(string field)
        {
            StormSwordPattern invalid = UnityEngine.Object.Instantiate(pattern);
            WeaponDefinition copy = UnityEngine.Object.Instantiate(weapon);
            try
            {
                var tuning = new SerializedObject(invalid);
                SerializedProperty property = tuning.FindProperty(field);
                if (property.propertyType == SerializedPropertyType.Integer)
                    property.intValue = 0;
                else
                    property.floatValue = -1f;
                tuning.ApplyModifiedPropertiesWithoutUndo();

                var definition = new SerializedObject(copy);
                definition.FindProperty("basicAttack").objectReferenceValue = invalid;
                definition.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(scheduler.TryAttack(now, Origin, copy, targets), Is.False);
                Assert.That(waves, Is.Empty);
                Assert.That(CastAt(0), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(invalid);
                UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        private bool Execute(BasicAttackRequest request)
        {
            if (request.Pattern is not StormSwordPattern current || !current.TryLaunch(request, run, Attacker, out var wave))
                return false;

            waves.Add(wave);
            return true;
        }

        private bool CastAt(int milliseconds)
        {
            now = Ms(milliseconds);
            return scheduler.TryAttack(now, Origin, weapon, targets);
        }

        private static TargetCandidate Candidate(string id, float x, float y, bool alive = true, bool attackable = true)
            => new TargetCandidate(id, new WorldPoint2D(x, y), alive, attackable);

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
