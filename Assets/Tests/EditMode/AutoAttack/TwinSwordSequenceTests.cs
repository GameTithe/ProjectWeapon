#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEditor;

namespace ProjectWeapon.AutoAttack.Tests
{
    public sealed class TwinSwordSequenceTests
    {
        private const string Folder = "Assets/ProjectWeapon/Weapons/PrototypeTwinSwords/";
        private const string AttackerId = "player";
        private static readonly WeaponDefinitionId DefinitionId = new WeaponDefinitionId("twin_swords");
        private static readonly WorldPoint2D Origin = new WorldPoint2D(2, 4);

        private RunState run = null!;
        private RunId runId;
        private TimeSpan now;
        private WeaponDefinition swords = null!;
        private TwinSwordSequencePattern pattern = null!;
        private AutoAttackScheduler scheduler = null!;
        private TwinSwordSequence sequence = null!;
        private TwinSwordStrike firstStrike;
        private IReadOnlyList<TargetCandidate> targets = Array.Empty<TargetCandidate>();

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Folder + "PrototypeTwinSwordsWeaponCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            var lookup = catalog.CreateLookup();
            Assert.That(lookup.Keys, Is.EquivalentTo(new[] { DefinitionId }));
            swords = lookup[DefinitionId];
            Assert.That(swords.BasicAttack, Is.TypeOf<TwinSwordSequencePattern>());
            pattern = (TwinSwordSequencePattern)swords.BasicAttack;

            now = TimeSpan.Zero;
            run = new RunState(() => now);
            runId = RunId.New();
            Assert.That(run.TryStartRun(runId, 1, Ms(15000)), Is.True);
            scheduler = new AutoAttackScheduler(run, StartSequence, initiallyEnabled: true);
            targets = new[] { Candidate("aim", 1, 0) };
        }

        [Test]
        public void GradeTwoTwinSwordsLoadOneConfiguredBasicPattern()
        {
            Assert.That(swords, Is.SameAs(AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Folder + "TwinSwords.asset")));
            Assert.That(swords.DisplayName, Is.EqualTo("쌍검"));
            Assert.That(swords.Grade, Is.EqualTo(2));
            Assert.That(swords.Kind, Is.EqualTo(WeaponKind.Sword));
            Assert.That(swords.PrototypeBasicAttackPattern, Is.EqualTo("양손 연속 타격"));
            Assert.That(swords.Skill, Is.Null);
            Assert.That(swords.AttackVfx, Is.Null);
            Assert.That(pattern, Is.SameAs(AssetDatabase.LoadAssetAtPath<TwinSwordSequencePattern>(Folder + "TwinSwordsSequence.asset")));
            Assert.That(pattern.Range, Is.EqualTo(1.75f));
            Assert.That(pattern.StrikeInterval, Is.EqualTo(Ms(150)));
            Assert.That(pattern.LeftDamage, Is.EqualTo(7f));
            Assert.That(pattern.RightDamage, Is.EqualTo(9f));
            Assert.That(pattern.Cooldown, Is.EqualTo(Ms(760)));
        }

        [Test]
        public void OneActivationResolvesTwoHandsWithDifferentApplicationIds()
        {
            TargetCandidate aim = Candidate("aim", 0.75f, 0);
            targets = new[]
            {
                Candidate("farther", 0, 1.5f),
                Candidate("dead", 0.1f, 0, isAlive: false),
                Candidate("friendly", 0.2f, 0, isAttackable: false),
                aim
            };

            Assert.That(AttackAt(100), Is.True);
            Assert.That(sequence.RunId, Is.EqualTo(runId));
            Assert.That(sequence.TargetId, Is.EqualTo("aim"));
            Assert.That(sequence.StartedAt, Is.EqualTo(Ms(100)));
            Assert.That(sequence.NextStrikeAt, Is.EqualTo(Ms(250)));
            Assert.That(firstStrike.Hand, Is.EqualTo(TwinSwordHand.Left));
            Assert.That(firstStrike.EffectiveAt, Is.EqualTo(Ms(100)));
            Assert.That(firstStrike.Hit!.Value.TargetId, Is.EqualTo("aim"));
            Assert.That(firstStrike.Hit.Value.Amount, Is.EqualTo(pattern.LeftDamage));
            Assert.That(ResolveAt(249, Origin, aim, out _), Is.False);

            Assert.That(ResolveAt(250, Origin, aim, out TwinSwordStrike second), Is.True);
            Assert.That(second.Hand, Is.EqualTo(TwinSwordHand.Right));
            Assert.That(second.EffectiveAt, Is.EqualTo(Ms(250)));
            Assert.That(second.Hit!.Value.TargetId, Is.EqualTo("aim"));
            Assert.That(second.Hit.Value.Amount, Is.EqualTo(pattern.RightDamage));
            Assert.That(second.Hit.Value.ApplicationId, Is.Not.EqualTo(firstStrike.Hit.Value.ApplicationId));
            Assert.That(sequence.IsFinished, Is.True);
            Assert.That(ResolveAt(250, Origin, aim, out _), Is.False);
        }

        [Test]
        public void LateRightStrikeUsesActualTimeAndCurrentRangeBoundary()
        {
            Assert.That(AttackAt(100), Is.True);
            var movedOrigin = new WorldPoint2D(Origin.X + 1, Origin.Y);
            TargetCandidate boundary = Candidate("aim", 2.75f, 0);

            Assert.That(ResolveAt(99, movedOrigin, boundary, out _), Is.False);
            Assert.That(ResolveAt(286, movedOrigin, boundary, out TwinSwordStrike late), Is.True);
            Assert.That(late.EffectiveAt, Is.EqualTo(Ms(286)));
            Assert.That(late.Hit!.Value.TargetId, Is.EqualTo("aim"));
        }

        [TestCase("outside")]
        [TestCase("dead")]
        [TestCase("nonattackable")]
        [TestCase("missing")]
        [TestCase("different")]
        [TestCase("invalid-position")]
        public void InvalidOrDifferentTargetConsumesTheSecondStrikeWithoutDamage(string scenario)
        {
            Assert.That(AttackAt(100), Is.True);
            TargetCandidate target = scenario switch
            {
                "outside" => Candidate("aim", 1.751f, 0),
                "dead" => Candidate("aim", 1, 0, isAlive: false),
                "nonattackable" => Candidate("aim", 1, 0, isAttackable: false),
                "missing" => default,
                "different" => Candidate("replacement", 1, 0),
                _ => Candidate("aim", float.NaN, 0)
            };

            Assert.That(ResolveAt(250, Origin, target, out TwinSwordStrike miss), Is.True);
            Assert.That(miss.Hand, Is.EqualTo(TwinSwordHand.Right));
            Assert.That(miss.Hit, Is.Null);
            Assert.That(sequence.IsFinished, Is.True);
        }

        [Test]
        public void OutsideAcquisitionPreservesCooldownAndExactBoundaryStartsNextSequence()
        {
            targets = new[] { Candidate("outside", 1.751f, 0) };
            Assert.That(AttackAt(100), Is.False);
            TargetCandidate atBoundary = Candidate("edge", 1.75f, 0);
            targets = new[] { atBoundary };
            Assert.That(AttackAt(100), Is.True);
            Assert.That(firstStrike.Hit!.Value.TargetId, Is.EqualTo("edge"));
            Assert.That(ResolveAt(250, Origin, atBoundary, out _), Is.True);
            Assert.That(AttackAt(859), Is.False);
            Assert.That(AttackAt(860), Is.True);
            Assert.That(firstStrike.EffectiveAt, Is.EqualTo(Ms(860)));
        }

        [Test]
        public void PendingStrikeCancelsOnExtraction()
        {
            Assert.That(AttackAt(100), Is.True);
            now = Ms(200);
            Assert.That(run.TryBeginExtraction(runId, isInRange: true), Is.Not.Null);
            Assert.That(ResolveAt(250, Origin, targets[0], out _), Is.False);
            Assert.That(sequence.IsFinished, Is.True);
        }

        [Test]
        public void StaleSequenceCannotApplyInAnotherRun()
        {
            Assert.That(AttackAt(100), Is.True);
            var otherRun = new RunState(() => TimeSpan.Zero);
            Assert.That(otherRun.TryStartRun(RunId.New(), 1, Ms(15000)), Is.True);
            Assert.That(sequence.TryResolveNext(otherRun, Ms(250), Origin, targets[0], out _), Is.False);
            Assert.That(sequence.IsFinished, Is.True);
        }

        [Test]
        public void PendingStrikeCancelsAtExactDeadline()
        {
            Assert.That(AttackAt(14850), Is.True);
            Assert.That(ResolveAt(15000, Origin, targets[0], out _), Is.False);
            Assert.That(sequence.IsFinished, Is.True);
        }

        private bool StartSequence(BasicAttackRequest request)
        {
            if (!pattern.TryCreateSequence(request, AttackerId, out TwinSwordSequence? created)
                || !created.TryResolveNext(run, request.StartedAt, request.Origin, request.Target, out firstStrike))
                return false;

            sequence = created;
            return true;
        }

        private bool AttackAt(int milliseconds)
        {
            now = Ms(milliseconds);
            return scheduler.TryAttack(now, Origin, swords, targets);
        }

        private bool ResolveAt(int milliseconds, WorldPoint2D origin, TargetCandidate target, out TwinSwordStrike strike)
        {
            now = Ms(milliseconds);
            return sequence.TryResolveNext(run, now, origin, target, out strike);
        }

        private static TargetCandidate Candidate(string id, float x, float y, bool isAlive = true, bool isAttackable = true)
            => new TargetCandidate(id, new WorldPoint2D(Origin.X + x, Origin.Y + y), isAlive, isAttackable);
        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
