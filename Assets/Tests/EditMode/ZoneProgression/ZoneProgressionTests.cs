#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Enemies;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Settlement;
using ProjectWeapon.Wallet;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectWeapon.Dungeon.Tests
{
    public sealed class ZoneProgressionTests
    {
        private const string BossId = "designated-boss";
        private readonly List<string> saveFolders = new List<string>();
        private ZoneCatalog catalog = null!;

        [SetUp]
        public void SetUp()
        {
            catalog = AssetDatabase.LoadAssetAtPath<ZoneCatalog>(
                "Assets/ProjectWeapon/Dungeon/PrototypeZoneCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
        }

        [TearDown]
        public void RemoveFixtureSaves()
        {
            foreach (string folder in saveFolders)
            {
                if (!Directory.Exists(folder))
                    continue;
                foreach (string file in Directory.EnumerateFiles(folder))
                    File.Delete(file);
                Directory.Delete(folder);
            }
            saveFolders.Clear();
        }

        [Test]
        public void DesignatedBossDeathAndCommittedReturnUnlockTheNextZoneOnce()
        {
            var progression = new ZoneProgression(catalog);
            Sortie sortie = Begin(1);
            var settings = ScriptableObject.CreateInstance<MeleeEnemySettings>();
            try
            {
                var boss = new MeleeEnemy(BossId, settings);
                boss.Died += defeatedAt => sortie.Boss.TryRecordEnemyDeath(sortie.Id, BossId, defeatedAt);
                var target = new TargetCandidate(BossId, default, isAlive: true, isAttackable: true);
                DamageRequest hit = AreaDamageSelector.FindTargets(
                    AreaDamageRequest.Circle(Guid.NewGuid(), "player", default, 1f, settings.MaxHealth),
                    new[] { target })[0];
                Assert.That(boss.ApplyDamage(hit, TimeSpan.FromSeconds(1)), Is.True);
                Assert.That(boss.Phase, Is.EqualTo(MeleeEnemyPhase.Dead));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }

            Assert.That(progression.IsUnlocked(1), Is.True);
            Assert.That(progression.IsUnlocked(2), Is.False);
            Assert.That(sortie.Boss.Evidence!.BossId, Is.EqualTo(BossId));
            RunResult result = sortie.Finish(RunEndReason.Extracted);
            RunSettlementCommit commit = sortie.Commit(result);
            Assert.That(commit.Status, Is.EqualTo(RunSettlementStatus.Committed));
            Assert.That(progression.TryUnlockNext(result, commit, sortie.Boss.Evidence), Is.EqualTo(2));
            Assert.That(progression.IsUnlocked(2), Is.True);
            Assert.That(progression.IsUnlocked(3), Is.False);

            RunSettlementCommit replay = sortie.Commit(result);
            Assert.That(replay.Status, Is.EqualTo(RunSettlementStatus.AlreadyCommitted));
            Assert.That(progression.TryUnlockNext(result, replay, sortie.Boss.Evidence), Is.Null);
            Assert.That(new ZoneProgression(catalog).TryUnlockNext(result, replay, sortie.Boss.Evidence), Is.EqualTo(2));
            var restored = new ZoneProgression(catalog, progression.HighestUnlockedZoneId);
            Assert.That(restored.IsUnlocked(1), Is.True);
            Assert.That(restored.IsUnlocked(2), Is.True);
            Assert.That(restored.IsUnlocked(3), Is.False);
        }

        [Test]
        public void ReturnWithoutABossStillCommitsButDoesNotUnlock()
        {
            var progression = new ZoneProgression(catalog);
            Sortie sortie = Begin(1);
            Assert.That(sortie.Boss.TryRecordEnemyDeath(sortie.Id, "ordinary-enemy", TimeSpan.FromSeconds(1)), Is.False);

            RunResult result = sortie.Finish(RunEndReason.Extracted);
            RunSettlementCommit commit = sortie.Commit(result);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(commit.Status, Is.EqualTo(RunSettlementStatus.Committed));
            Assert.That(progression.TryUnlockNext(result, commit, sortie.Boss.Evidence), Is.Null);
            Assert.That(progression.HighestUnlockedZoneId, Is.EqualTo(1));
            Assert.That(sortie.Boss.TryRecordEnemyDeath(sortie.Id, BossId, TimeSpan.FromSeconds(1)), Is.False);
        }

        [TestCase(RunEndReason.Death)]
        [TestCase(RunEndReason.TimeExpired)]
        public void DefeatFollowedByFailedReturnDoesNotUnlock(RunEndReason failure)
        {
            var progression = new ZoneProgression(catalog);
            Sortie sortie = Begin(1);
            sortie.DefeatBoss();
            RunResult result = sortie.Finish(failure);
            Assert.That(progression.TryUnlockNext(result, sortie.Commit(result), sortie.Boss.Evidence), Is.Null);
            Assert.That(progression.IsUnlocked(2), Is.False);
        }

        [Test]
        public void RequiresEvidenceAndReceiptFromTheSameRunAndZone()
        {
            var progression = new ZoneProgression(catalog);
            Sortie first = Begin(1);
            Sortie other = Begin(1);
            Sortie differentZone = Begin(2, id: first.Id);
            Assert.That(first.Boss.TryRecordEnemyDeath(other.Id, BossId, TimeSpan.FromSeconds(1)), Is.False);
            Assert.That(first.Boss.TryRecordEnemyDeath(first.Id, BossId, TimeSpan.FromTicks(-1)), Is.False);
            Assert.That(first.Boss.TryRecordEnemyDeath(first.Id, BossId, TimeSpan.FromSeconds(10)), Is.False);
            first.DefeatBoss();
            other.DefeatBoss();
            differentZone.DefeatBoss();

            RunResult result = first.Finish(RunEndReason.Extracted);
            RunSettlementCommit commit = first.Commit(result);
            RunSettlementCommit otherCommit = other.Commit(other.Finish(RunEndReason.Extracted));
            Assert.That(progression.TryUnlockNext(result, otherCommit, first.Boss.Evidence), Is.Null);
            Assert.That(progression.TryUnlockNext(result, commit, other.Boss.Evidence), Is.Null);
            Assert.That(progression.TryUnlockNext(result, commit, differentZone.Boss.Evidence), Is.Null);
        }

        [Test]
        public void DefeatAfterEffectiveExtractionTimeAndMissingSaveCannotUnlock()
        {
            var progression = new ZoneProgression(catalog);
            Sortie late = Begin(1);
            Assert.That(late.Boss.TryRecordEnemyDeath(late.Id, BossId, TimeSpan.FromSeconds(5)), Is.True);
            RunResult extracted = late.Finish(RunEndReason.Extracted);
            Assert.That(progression.TryUnlockNext(extracted, late.Commit(extracted), late.Boss.Evidence), Is.Null);

            Sortie noSave = Begin(1, seedSave: false);
            noSave.DefeatBoss();
            RunResult result = noSave.Finish(RunEndReason.Extracted);
            RunSettlementCommit uncommitted = noSave.Commit(result);
            Assert.That(uncommitted.Status, Is.EqualTo(RunSettlementStatus.MissingSave));
            Assert.That(progression.TryUnlockNext(result, uncommitted, noSave.Boss.Evidence), Is.Null);
            Assert.That(progression.HighestUnlockedZoneId, Is.EqualTo(1));
        }

        [TestCase("legacy-opaque-status", false)]
        [TestCase("Extracted", true)]
        [TestCase("Death", false)]
        public void LegacyReceiptMustCarryAnExactSuccessfulOutcome(string legacyResult, bool unlocks)
        {
            var progression = new ZoneProgression(catalog);
            Sortie sortie = Begin(1, legacyResult: legacyResult);
            sortie.DefeatBoss();
            RunResult result = sortie.Finish(RunEndReason.Extracted);
            Assert.That(progression.TryUnlockNext(result, sortie.Commit(result), sortie.Boss.Evidence),
                Is.EqualTo(unlocks ? (int?)2 : null));
        }

        [Test]
        public void LockedOrFinalZoneCannotOpenAnUnlistedOrSkippedZone()
        {
            var fresh = new ZoneProgression(catalog);
            Sortie locked = Begin(2);
            locked.DefeatBoss();
            RunResult lockedResult = locked.Finish(RunEndReason.Extracted);
            Assert.That(fresh.TryUnlockNext(lockedResult, locked.Commit(lockedResult), locked.Boss.Evidence), Is.Null);
            Assert.That(fresh.IsUnlocked(3), Is.False);

            var final = new ZoneProgression(catalog, highestUnlockedZoneId: 5);
            Sortie last = Begin(5);
            last.DefeatBoss();
            RunResult lastResult = last.Finish(RunEndReason.Extracted);
            Assert.That(final.TryUnlockNext(lastResult, last.Commit(lastResult), last.Boss.Evidence), Is.Null);
            Assert.That(final.HighestUnlockedZoneId, Is.EqualTo(5));
            Assert.That(final.IsUnlocked(6), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ZoneProgression(catalog, 6));
        }

        [Test]
        public void UsesTheNextRegisteredZoneIdRatherThanAssumingConsecutiveIds()
        {
            var custom = Object.Instantiate(catalog);
            try
            {
                using var serialized = new SerializedObject(custom);
                SerializedProperty zones = serialized.FindProperty("zones");
                zones.arraySize = 2;
                zones.GetArrayElementAtIndex(1).FindPropertyRelative("zoneId").intValue = 3;
                zones.MoveArrayElement(1, 0);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var progression = new ZoneProgression(custom);
                Sortie first = Begin(1);
                first.DefeatBoss();
                RunResult result = first.Finish(RunEndReason.Extracted);
                Assert.That(progression.TryUnlockNext(result, first.Commit(result), first.Boss.Evidence), Is.EqualTo(3));
                Assert.That(progression.IsUnlocked(2), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(custom);
            }
        }

        private Sortie Begin(int zone, bool seedSave = true, string? legacyResult = null, RunId? id = null)
        {
            string folder = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN86-" + Guid.NewGuid().ToString("N"));
            saveFolders.Add(folder);
            return new Sortie(zone, folder, seedSave, legacyResult, id ?? RunId.New());
        }

        private sealed class Sortie
        {
            private readonly RunState run;
            private readonly SortieManifest manifest;
            private readonly RunSettlementCoordinator coordinator;
            private readonly WeaponInstance carried;
            private TimeSpan now;

            public Sortie(int zone, string folder, bool seedSave, string? legacyResult, RunId id)
            {
                Id = id;
                run = new RunState(() => now);
                carried = WeaponInstance.Create(new WeaponDefinitionId("old_sword"), WeaponProvenance.Personal,
                    WeaponLocation.Equipped(0));
                var town = new InventoryService(new[] { carried });
                SortieStartResult started = town.TryStartSortie(Id.Value, () => run.TryStartRun(Id, zone, TimeSpan.FromSeconds(10)));
                Assert.That(started.Status, Is.EqualTo(SortieStartStatus.Started));
                manifest = started.Manifest!;
                Boss = new BossDefeatTracker(run, BossId);
                var store = new AtomicSnapshotStore(Path.Combine(folder, "snapshot.json"));
                coordinator = new RunSettlementCoordinator(store);
                if (seedSave)
                {
                    var receipts = legacyResult == null ? Array.Empty<SettlementSaveRecord>()
                        : new[] { new SettlementSaveRecord(Id.Value, "legacy-run-receipt", legacyResult) };
                    var snapshot = new SaveSnapshot(1, 0, town.Snapshot(), Array.Empty<CatalogSaveRecord>(), receipts);
                    Assert.That(store.Commit(0, snapshot).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
                }
            }

            public RunId Id { get; }
            public BossDefeatTracker Boss { get; }
            public void DefeatBoss() =>
                Assert.That(Boss.TryRecordEnemyDeath(Id, BossId, TimeSpan.FromSeconds(1)), Is.True);

            public RunResult Finish(RunEndReason reason)
            {
                if (reason == RunEndReason.Extracted)
                {
                    ExtractionAttempt attempt = run.TryBeginExtraction(Id, isInRange: true)!;
                    now = attempt.CompletesAt;
                    run.ProcessSignals(new[] { RunSignal.CompleteExtraction(Id, attempt.Id, now) });
                }
                else if (reason == RunEndReason.Death)
                {
                    now = TimeSpan.FromSeconds(2);
                    run.ProcessSignals(new[] { RunSignal.PlayerDied(Id, now) });
                }
                else
                {
                    now = TimeSpan.FromSeconds(10);
                    run.ProcessSignals(Array.Empty<RunSignal>());
                }
                return run.Result!;
            }

            public RunSettlementCommit Commit(RunResult result)
                => coordinator.TrySettle(result, manifest, new[] { carried }, new RunCoinAccumulator(Id));
        }
    }
}
