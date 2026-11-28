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
    public sealed class BattleAxeTests
    {
        private const string Folder = "Assets/ProjectWeapon/Weapons/PrototypeBattleAxe/";
        private const string AttackerId = "player";
        private static readonly WeaponDefinitionId DefinitionId = new WeaponDefinitionId("battle_axe");
        private static readonly WorldPoint2D Origin = new WorldPoint2D(2, -3);

        private readonly List<BasicAttackRequest> requests = new List<BasicAttackRequest>();
        private readonly List<MeleeSlashSwing> swings = new List<MeleeSlashSwing>();
        private IReadOnlyList<TargetCandidate> targets = Array.Empty<TargetCandidate>();
        private TimeSpan now;
        private bool acceptSwing;
        private RunState run = null!;
        private RunId runId;
        private WeaponDefinition axe = null!;
        private MeleeSlashPattern pattern = null!;
        private AutoAttackScheduler scheduler = null!;

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Folder + "PrototypeBattleAxeWeaponCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            var definitions = catalog.CreateLookup();
            Assert.That(definitions.Keys, Is.EquivalentTo(new[] { DefinitionId }));
            axe = definitions[DefinitionId];
            Assert.That(axe.BasicAttack, Is.TypeOf<MeleeSlashPattern>());
            pattern = (MeleeSlashPattern)axe.BasicAttack;

            now = TimeSpan.Zero;
            acceptSwing = true;
            requests.Clear();
            swings.Clear();
            targets = Array.Empty<TargetCandidate>();
            run = new RunState(() => now);
            runId = RunId.New();
            Assert.That(run.TryStartRun(runId, 1, TimeSpan.FromSeconds(15)), Is.True);
            scheduler = new AutoAttackScheduler(run, Execute, initiallyEnabled: true);
        }

        [Test]
        public void GradeTwoBattleAxeLinksOneWideSlashAndPreservesExistingKindNumbers()
        {
            Assert.That(axe, Is.SameAs(AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Folder + "BattleAxe.asset")));
            Assert.That(axe.Grade, Is.EqualTo(2));
            Assert.That(axe.Kind, Is.EqualTo(WeaponKind.Axe));
            Assert.That(axe.DisplayName, Is.EqualTo("전투도끼"));
            Assert.That(axe.PrototypeBasicAttackPattern, Is.EqualTo("넓은 베기"));
            Assert.That(axe.BasicAttack, Is.SameAs(AssetDatabase.LoadAssetAtPath<MeleeSlashPattern>(Folder + "BattleAxeMeleeSlash.asset")));
            Assert.That(axe.Skill, Is.Null);
            Assert.That(axe.AttackVfx, Is.Null);
            Assert.That(pattern.Range, Is.EqualTo(2f));
            Assert.That(pattern.ArcDegrees, Is.EqualTo(180f));
            Assert.That(pattern.Damage, Is.EqualTo(16f));
            Assert.That(pattern.Cooldown, Is.EqualTo(Ms(1200)));

            var kinds = new[] { WeaponKind.Unspecified, WeaponKind.Sword, WeaponKind.Spear, WeaponKind.Bow,
                WeaponKind.Mace, WeaponKind.Dagger, WeaponKind.Axe };
            Assert.That(kinds.Select(kind => (int)kind), Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5, 6 }));
        }

        [Test]
        public void SchedulerAimsAtNearestAndEmitsOneImmediateWideDamageBatch()
        {
            TargetCandidate wide = Candidate("wide", 0.25f, 1.5f);
            targets = new[]
            {
                Candidate("radius-boundary", 2, 0),
                Candidate("past-range", 2.001f, 0),
                Candidate("left-boundary", 0, 1),
                Candidate("right-boundary", 0, -1),
                Candidate("past-angle", -0.01f, 1),
                Candidate("behind", -1, 0),
                Candidate("dead", 0.1f, 0, isAlive: false),
                Candidate("friendly", 0.2f, 0, isAttackable: false),
                Candidate(AttackerId, 1, 0),
                Candidate("nearest", 0.75f, 0),
                wide,
                wide
            };

            Assert.That(AttackAt(123), Is.True);
            BasicAttackRequest request = requests.Single();
            MeleeSlashSwing swing = swings.Single();
            Assert.That(request.WeaponId, Is.EqualTo(DefinitionId));
            Assert.That(request.Target.TargetId, Is.EqualTo("nearest"));
            Assert.That(swing.RunId, Is.EqualTo(runId));
            Assert.That(swing.EffectiveAt, Is.EqualTo(request.StartedAt));
            Assert.That(swing.EffectiveAt, Is.EqualTo(Ms(123)));
            Assert.That(swing.Hits.Select(hit => hit.TargetId),
                Is.EquivalentTo(new[] { "nearest", "radius-boundary", "left-boundary", "right-boundary", "wide" }));
            Assert.That(swing.Hits.All(hit => hit.ApplicationId == swing.ApplicationId
                && hit.AttackerId == AttackerId && hit.Amount == pattern.Damage), Is.True);
        }

        [Test]
        public void CooldownAllowsExactlyOneNewSwingAtTheBoundary()
        {
            targets = new[] { Candidate("aim", 1, 0) };

            Assert.That(AttackAt(100), Is.True);
            Assert.That(AttackAt(100), Is.False);
            Assert.That(AttackAt(1299), Is.False);
            Assert.That(AttackAt(1300), Is.True);
            Assert.That(swings.Select(swing => swing.EffectiveAt), Is.EqualTo(new[] { Ms(100), Ms(1300) }));
            Assert.That(swings[0].ApplicationId, Is.Not.EqualTo(swings[1].ApplicationId));
            Assert.That(swings.All(swing => swing.Hits.Count == 1), Is.True);
        }

        [Test]
        public void MissingTargetAndRejectedSwingDoNotConsumeCooldown()
        {
            targets = new[] { Candidate("outside", 2.001f, 0) };
            Assert.That(AttackAt(75), Is.False);
            Assert.That(requests, Is.Empty);

            targets = new[] { Candidate("boundary", 2, 0) };
            acceptSwing = false;
            Assert.That(AttackAt(75), Is.False);
            Assert.That(swings, Is.Empty);
            acceptSwing = true;
            Assert.That(AttackAt(75), Is.True);
            Assert.That(swings.Single().Hits.Single().TargetId, Is.EqualTo("boundary"));
        }

        private bool Execute(BasicAttackRequest request)
        {
            requests.Add(request);
            Guid applicationId = acceptSwing ? Guid.NewGuid() : Guid.Empty;
            if (!pattern.TryCreateSwing(request, applicationId, AttackerId, targets, out var swing))
                return false;

            swings.Add(swing!);
            return true;
        }

        private bool AttackAt(int milliseconds)
        {
            now = Ms(milliseconds);
            return scheduler.TryAttack(now, Origin, axe, targets);
        }

        private static TargetCandidate Candidate(string id, float x, float y, bool isAlive = true, bool isAttackable = true)
            => new TargetCandidate(id, new WorldPoint2D(Origin.X + x, Origin.Y + y), isAlive, isAttackable);

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
