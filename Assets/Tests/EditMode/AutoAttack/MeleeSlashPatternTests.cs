#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Extraction;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEditor;

namespace ProjectWeapon.AutoAttack.Tests
{
    public sealed class MeleeSlashPatternTests
    {
        private const string StarterPath = "Assets/ProjectWeapon/Weapons/PrototypeStarter/";
        private const string Attacker = "local-player";
        private static readonly WorldPoint2D Origin = new WorldPoint2D(2, 4);
        private static readonly RunId RunId = new RunId(new Guid("ee2bf289-c1a5-40a4-93d0-9bc16d1616f1"));
        private readonly List<BasicAttackRequest> attempts = new List<BasicAttackRequest>();
        private readonly List<MeleeSlashSwing> swings = new List<MeleeSlashSwing>();
        private TimeSpan now;
        private int applicationNumber;
        private Func<Guid> createApplicationId = null!;
        private RunState run = null!;
        private ExtractionChannel channel = null!;
        private WeaponDefinition sword = null!;
        private AutoAttackScheduler scheduler = null!;
        private IReadOnlyList<TargetCandidate> targets = Array.Empty<TargetCandidate>();

        [SetUp]
        public void SetUp()
        {
            now = TimeSpan.Zero;
            applicationNumber = 0;
            attempts.Clear();
            swings.Clear();
            targets = Array.Empty<TargetCandidate>();
            createApplicationId = () => new Guid(++applicationNumber, 0, 0, new byte[8]);
            run = new RunState(() => now);
            channel = new ExtractionChannel(run);
            Assert.That(run.TryStartRun(RunId, 1, Ms(20000)), Is.True);
            sword = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(StarterPath + "OldSword.asset");
            Assert.That(sword, Is.Not.Null);
            scheduler = new AutoAttackScheduler(run, Execute, initiallyEnabled: true);
        }

        [Test]
        public void ActualCatalogLinksOneGradeOneOldSwordToTheDraftSlashAsset()
        {
            var id = new WeaponDefinitionId("old_sword");
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(StarterPath + "PrototypeStarterWeaponCatalog.asset");
            var lookup = catalog.CreateLookup();
            var slash = AssetDatabase.LoadAssetAtPath<MeleeSlashPattern>(StarterPath + "OldSwordMeleeSlash.asset");

            Assert.That(slash, Is.Not.Null);
            Assert.That(lookup[id], Is.SameAs(sword));
            Assert.That(sword.Id, Is.EqualTo(id));
            Assert.That(sword.Grade, Is.EqualTo(1));
            Assert.That(sword.Kind, Is.EqualTo(WeaponKind.Sword));
            Assert.That(sword.BasicAttack, Is.SameAs(slash));
            Assert.That(lookup.Values.Count(weapon => weapon.BasicAttack is MeleeSlashPattern), Is.EqualTo(1));
            Assert.That(slash.Range, Is.EqualTo(1.5f));
            Assert.That(slash.Cooldown, Is.EqualTo(Ms(1000)));
            Assert.That(slash.ArcDegrees, Is.EqualTo(90f));
            Assert.That(slash.Damage, Is.EqualTo(10f));
        }

        [Test]
        public void ActualSchedulerAimsAtNearestAndEmitsOneImmediateDeduplicatedDamageBatch()
        {
            TargetCandidate nearest = Candidate("nearest", 0.5f, 0);
            targets = new[]
            {
                Candidate("radius-boundary", 1.5f, 0),
                Candidate("arc-left", 1, 1),
                Candidate("arc-right", 1, -1),
                Candidate("past-arc", 1, 1.01f),
                Candidate("past-radius", 1.501f, 0),
                Candidate("side", 0, 1),
                Candidate("behind", -1, 0),
                Candidate("dead", 0.1f, 0, alive: false),
                Candidate(Attacker, 0, 0, attackable: false),
                nearest,
                nearest
            };

            Assert.That(AttackAt(123), Is.True);
            Assert.That(attempts.Count, Is.EqualTo(1));
            Assert.That(attempts[0].Target.TargetId, Is.EqualTo("nearest"));
            Assert.That(swings.Count, Is.EqualTo(1));

            MeleeSlashSwing swing = swings[0];
            Assert.That(swing.RunId, Is.EqualTo(RunId));
            Assert.That(swing.EffectiveAt, Is.EqualTo(attempts[0].StartedAt));
            Assert.That(swing.EffectiveAt, Is.EqualTo(Ms(123)));
            Assert.That(swing.ApplicationId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(swing.Hits.Select(hit => hit.TargetId),
                Is.EquivalentTo(new[] { "nearest", "radius-boundary", "arc-left", "arc-right" }));
            Assert.That(swing.Hits.All(hit => hit.ApplicationId == swing.ApplicationId
                && hit.AttackerId == Attacker && hit.Amount == 10f), Is.True);
        }

        [Test]
        public void CooldownAcceptsAnotherSingleSwingOnlyAtTheConfiguredBoundary()
        {
            targets = new[] { Candidate("closest", 1, 0) };

            Assert.That(AttackAt(100), Is.True);
            Assert.That(AttackAt(100), Is.False);
            Assert.That(AttackAt(1099), Is.False);
            Assert.That(AttackAt(1100), Is.True);

            Assert.That(swings.Select(swing => swing.EffectiveAt), Is.EqualTo(new[] { Ms(100), Ms(1100) }));
            Assert.That(swings[0].ApplicationId, Is.Not.EqualTo(swings[1].ApplicationId));
            Assert.That(swings.All(swing => swing.Hits.Count == 1), Is.True);
        }

        [Test]
        public void OverlappingTargetUsesPointContactWithoutAssumingAFacingDirection()
        {
            targets = new[]
            {
                Candidate("overlap", 0, 0),
                Candidate("overlap", 0, 0),
                Candidate("nearby", 0.25f, 0)
            };

            Assert.That(AttackAt(100), Is.True);
            Assert.That(attempts[0].Target.TargetId, Is.EqualTo("overlap"));
            Assert.That(swings[0].Hits.Select(hit => hit.TargetId), Is.EqualTo(new[] { "overlap" }));
        }

        [Test]
        public void MissingTargetAndInvalidApplicationDoNotStartAnAttackOrConsumeCooldown()
        {
            Assert.That(AttackAt(100), Is.False);
            Assert.That(attempts, Is.Empty);
            targets = new[] { Candidate("closest", 1, 0) };
            createApplicationId = () => Guid.Empty;

            Assert.That(AttackAt(100), Is.False);
            Assert.That(swings, Is.Empty);
            createApplicationId = () => new Guid("5cf4ecf9-2241-40eb-a30a-49de506db313");
            Assert.That(AttackAt(100), Is.True);
            Assert.That(swings.Count, Is.EqualTo(1));
        }

        [Test]
        public void ExtractionChannelSuppressesTheActualSlashConsumer()
        {
            targets = new[] { Candidate("closest", 1, 0) };
            now = Ms(100);
            Assert.That(run.TryBeginExtraction(RunId, isInRange: true), Is.Not.Null);
            Assert.That(channel.IsAutomaticAttackSuppressed, Is.True);

            Assert.That(AttackAt(200), Is.False);
            Assert.That(attempts, Is.Empty);
            Assert.That(swings, Is.Empty);
        }

        private bool Execute(BasicAttackRequest request)
        {
            attempts.Add(request);
            if (request.Pattern is not MeleeSlashPattern pattern
                || !pattern.TryCreateSwing(request, createApplicationId(), Attacker, targets, out MeleeSlashSwing? swing))
                return false;

            swings.Add(swing!);
            return true;
        }

        private bool AttackAt(int milliseconds)
        {
            now = Ms(milliseconds);
            channel.ProcessFrame(Array.Empty<RunSignal>());
            return scheduler.TryAttack(now, Origin, sword, targets);
        }

        private static TargetCandidate Candidate(
            string id, float offsetX, float offsetY, bool alive = true, bool attackable = true)
            => new TargetCandidate(id, new WorldPoint2D(Origin.X + offsetX, Origin.Y + offsetY), alive, attackable);

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
