#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Settlement;
using ProjectWeapon.Wallet;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectWeapon.SettlementUI.Tests
{
    public sealed class SettlementResultsTests
    {
        private string directory = null!;
        private AtomicSnapshotStore store = null!;

        [SetUp]
        public void Prepare()
        {
            directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN186-" + Guid.NewGuid().ToString("N"));
            store = new AtomicSnapshotStore(Path.Combine(directory, "snapshot.json"));
        }

        [TearDown]
        public void RemoveStore()
        {
            if (!Directory.Exists(directory))
                return;
            foreach (string path in Directory.EnumerateFiles(directory))
                File.Delete(path);
            Directory.Delete(directory);
        }

        [Test]
        public void CommittedAndConflictingResultsReadTheSameDurableDeltasAfterLaterChanges()
        {
            RunId id = RunId.New();
            WeaponInstance worn = Equip("old_sword", 0);
            WeaponInstance abandoned = Equip("hunting_bow", 1);
            WeaponInstance protectedWeapon = Weapon("mace", WeaponProvenance.Personal, WeaponLocation.Warehouse());
            WeaponInstance loot = Carry("wooden_spear", 0);
            WeaponInstance otherLoot = Carry("wooden_spear", 1);
            var fixture = Finish(id, RunEndReason.Extracted, worn, abandoned, protectedWeapon);
            Save(fixture.Town, new CatalogSaveRecord(worn.DefinitionId, "recovered"));

            var coordinator = new RunSettlementCoordinator(store);
            RunSettlementCommit first = coordinator.TrySettle(fixture.Run, fixture.Manifest, new[] { worn, loot, otherLoot },
                Wallet(id, 35));
            Assert.That(first.Status, Is.EqualTo(RunSettlementStatus.Committed));
            SettlementResultsDisplay display = SettlementResultsDisplay.FromCommit(first, Name);
            Assert.That(display.Title, Is.EqualTo("귀환 성공"));
            Assert.That(Lines(display.Items, "반입품 회수"), Is.EqualTo(new[] { "old_sword ×1" }));
            Assert.That(Lines(display.Items, "신규 획득"), Is.EqualTo(new[] { "wooden_spear ×2" }));
            Assert.That(Lines(display.Items, "상실"), Is.EqualTo(new[] { "hunting_bow ×1" }));
            Assert.That(Lines(display.Records, "정산 코인"), Is.EqualTo(new[] { "확정 코인 +35", "소멸 코인 0" }));
            Assert.That(Lines(display.Records, "도감 신규 회수"), Is.EqualTo(new[] { "wooden_spear" }));
            Assert.That(Lines(display.Records, "기존 자산 보존"), Is.EqualTo(new[] { "미반입 창고 무기 1개", "기존 확정 코인 100" }));

            SaveSnapshot committed = store.Load()!;
            Assert.That(store.Commit(committed.Revision, new SaveSnapshot(committed.Revision + 1, 432,
                new[] { protectedWeapon }, Array.Empty<CatalogSaveRecord>(), committed.Settlements)).Status,
                Is.EqualTo(SnapshotCommitStatus.Committed));
            RunResult opposite = Finish(id, RunEndReason.Death, worn).Run;
            RunSettlementCommit competing = coordinator.TrySettle(opposite, fixture.Manifest, Array.Empty<WeaponInstance>(),
                Wallet(id, 999));
            Assert.That(competing.Status, Is.EqualTo(RunSettlementStatus.ConflictingOutcome));
            Assert.That(competing.Receipt!.Result, Is.EqualTo(first.Receipt!.Result));

            SettlementResultsDisplay conflict = SettlementResultsDisplay.FromCommit(competing, Name);
            Assert.That(conflict.Notice, Does.Contain("이미 확정된"));
            Assert.That(conflict.Title, Is.EqualTo(display.Title));
            Assert.That(conflict.RunId, Is.EqualTo(id.Value));
            CollectionAssert.AreEqual(display.Items.SelectMany(item => item.Lines), conflict.Items.SelectMany(item => item.Lines));
            CollectionAssert.AreEqual(display.Records.SelectMany(item => item.Lines), conflict.Records.SelectMany(item => item.Lines));
            SettlementResultsDisplay reopened = SettlementResultsDisplay.FromReceipt(committed.Settlements.Single(), Name);
            CollectionAssert.AreEqual(display.Items.SelectMany(item => item.Lines), reopened.Items.SelectMany(item => item.Lines));
            Assert.That(store.Load()!.Revision, Is.EqualTo(3));
        }

        [TestCase(RunEndReason.Death, "귀환 실패 · 사망")]
        [TestCase(RunEndReason.TimeExpired, "귀환 실패 · 시간 초과")]
        public void FailedReceiptKeepsLoanClosureSeparateFromLostItems(RunEndReason reason, string title)
        {
            RunId id = RunId.New();
            var fixture = Finish(id, reason, Weapon("mace", WeaponProvenance.Personal, WeaponLocation.Warehouse()));
            WeaponInstance loot = Carry("dagger", 0);
            Save(fixture.Town, new CatalogSaveRecord(loot.DefinitionId, "found"));

            RunSettlementCommit commit = new RunSettlementCoordinator(store).TrySettle(fixture.Run, fixture.Manifest,
                new[] { loot }, Wallet(id, 26));
            SettlementResultsDisplay display = SettlementResultsDisplay.FromCommit(commit, Name);
            Assert.That(display.Title, Is.EqualTo(title));
            Assert.That(Lines(display.Items, "반입품 회수"), Is.EqualTo(new[] { "없음" }));
            Assert.That(Lines(display.Items, "신규 획득"), Is.EqualTo(new[] { "없음" }));
            Assert.That(Lines(display.Items, "상실"), Is.EqualTo(new[] { "dagger ×1" }));
            Assert.That(Lines(display.Items, "대여 종료"), Is.EqualTo(new[] { "old_sword ×1" }));
            Assert.That(Lines(display.Records, "정산 코인"), Is.EqualTo(new[] { "확정 코인 +0", "소멸 코인 26" }));
            Assert.That(Lines(display.Records, "도감 신규 회수"), Is.EqualTo(new[] { "없음" }));
            Assert.That(Lines(display.Records, "기존 자산 보존"), Is.EqualTo(new[] { "미반입 창고 무기 1개", "기존 확정 코인 100" }));
        }

        [Test]
        public void LegacyAndOpaqueReceiptsNeverInventItemizedAmounts()
        {
            const string payload = @"{""version"":1,""outcome"":""Extracted"",""awardedCoins"":9,""forfeitedCoins"":0,""recovered"":[""old-id""],""lost"":[],""closedLoans"":[]}";
            var id = Guid.NewGuid();
            SettlementResultsDisplay old = SettlementResultsDisplay.FromReceipt(new SettlementSaveRecord(id, "v1", payload), Name);
            Assert.That(old.RunId, Is.EqualTo(id));
            Assert.That(Lines(old.Items, "회수·획득 합계"), Is.EqualTo(new[] { "1개" }));
            Assert.That(Lines(old.Records, "정산 코인"), Does.Contain("확정 코인 +9"));
            Assert.That(old.Items.Any(item => item.Title == "신규 획득"), Is.False);
            Assert.That(Lines(old.Records, "기존 자산 보존"), Is.EqualTo(new[] { "미반입 창고 무기", "기존 확정 코인" }));

            SettlementResultsDisplay opaque = SettlementResultsDisplay.FromStatus(RunSettlementStatus.ConflictingOutcome,
                new SettlementSaveRecord(id, "previous", "Death"), Name);
            Assert.That(opaque.Title, Is.EqualTo("귀환 실패 · 사망"));
            Assert.That(opaque.Notice, Does.Contain("상세 내역이 없습니다"));
            Assert.That(opaque.Items, Is.Empty);
            Assert.That(opaque.Records, Is.Empty);
            Assert.That(opaque.RunId, Is.EqualTo(id));
        }

        [Test]
        public void MissingSaveOrDeclinedCommitDoesNotOfferAnUnconfirmedResult()
        {
            RunId id = RunId.New();
            var fixture = Finish(id, RunEndReason.Extracted, Equip("old_sword", 0));
            RunSettlementCommit missing = new RunSettlementCoordinator(store).TrySettle(fixture.Run, fixture.Manifest,
                fixture.Manifest.Equipped, Wallet(id, 19));
            Assert.That(missing.Status, Is.EqualTo(RunSettlementStatus.MissingSave));
            SettlementResultsDisplay absent = SettlementResultsDisplay.FromCommit(missing, Name);
            SettlementResultsDisplay retry = SettlementResultsDisplay.FromStatus(RunSettlementStatus.RetryRequired, null, Name);
            Assert.That(absent.Notice, Does.Contain("저장 데이터를 찾을 수 없습니다"));
            Assert.That(retry.Notice, Does.Contain("저장을 완료하지 못했습니다"));
            foreach (SettlementResultsDisplay uncommitted in new[] { absent, retry })
            {
                Assert.That(uncommitted.RunId, Is.Null);
                Assert.That(uncommitted.Items, Is.Empty);
                Assert.That(uncommitted.Records, Is.Empty);
            }
            Assert.That(store.Load(), Is.Null);
        }

        [Test]
        public void ClosingAStoredResultNotifiesTheHostOnceAndDoesNotWriteAnything()
        {
            var fixture = Finish(RunId.New(), RunEndReason.Extracted, Equip("old_sword", 0));
            var receipt = new SettlementSaveRecord(fixture.Run.RunId.Value, "existing", "old-format");
            var oldest = new SaveSnapshot(1, 50, fixture.Town.Snapshot(), Array.Empty<CatalogSaveRecord>(), new[] { receipt });
            Assert.That(store.Commit(0, oldest).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            var font = Resources.Load<TMP_Settings>("TMP Settings") ? TMP_Settings.defaultFontAsset : null;
            Assert.That(font, Is.Not.Null);
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(
                "Assets/ProjectWeapon/Weapons/PrototypeStarter/PrototypeStarterWeaponCatalog.asset");
            var canvas = new GameObject("Settlement Canvas", typeof(RectTransform), typeof(Canvas), typeof(SettlementResultsPanel));
            try
            {
                var panel = canvas.GetComponent<SettlementResultsPanel>();
                panel.Initialize(catalog, font!);
                var dismissed = new List<Guid>();
                panel.ReturnToLobbyRequested += dismissed.Add;
                panel.ShowReceipt(store.Load()!.Settlements.Single());
                Assert.That(panel.IsOpen, Is.True);
                var button = canvas.GetComponentsInChildren<UnityEngine.UI.Button>().Single();
                var click = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
                button.OnPointerClick(click);
                button.OnPointerClick(click);
                Assert.That(dismissed, Is.EqualTo(new[] { receipt.RunId }));
                Assert.That(panel.IsOpen, Is.False);
                Assert.That(store.Load()!.Revision, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas);
            }
        }

        private void Save(InventoryService inventory, params CatalogSaveRecord[] catalog)
        {
            var snapshot = new SaveSnapshot(1, 100, inventory.Snapshot(), catalog, Array.Empty<SettlementSaveRecord>());
            Assert.That(store.Commit(0, snapshot).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
        }

        private static IReadOnlyList<string> Lines(IReadOnlyList<SettlementSection> sections, string title)
            => sections.Single(section => section.Title == title).Lines;

        private static string Name(WeaponDefinitionId id) => id.Value;

        private static WeaponInstance Equip(string definition, int slot)
            => Weapon(definition, WeaponProvenance.Personal, WeaponLocation.Equipped(slot));

        private static WeaponInstance Carry(string definition, int slot)
            => Weapon(definition, WeaponProvenance.ExplorationLoot, WeaponLocation.Carried(slot));

        private static WeaponInstance Weapon(string definition, WeaponProvenance provenance, WeaponLocation location)
            => WeaponInstance.Create(new WeaponDefinitionId(definition), provenance, location);

        private static RunCoinAccumulator Wallet(RunId id, int pending)
        {
            var wallet = new RunCoinAccumulator(id);
            wallet.AddReward(pending);
            return wallet;
        }

        private static (RunResult Run, SortieManifest Manifest, InventoryService Town) Finish(
            RunId id, RunEndReason reason, params WeaponInstance[] items)
        {
            var inventory = new InventoryService(items);
            inventory.PrepareStarterLoan(id.Value);
            TimeSpan now = TimeSpan.Zero;
            TimeSpan deadline = TimeSpan.FromSeconds(10);
            var run = new RunState(() => now);
            SortieManifest manifest = inventory.TryStartSortie(id.Value, () => run.TryStartRun(id, 1, deadline)).Manifest!;
            Assert.That(manifest, Is.Not.Null);
            if (reason == RunEndReason.Extracted)
            {
                ExtractionAttempt attempt = run.TryBeginExtraction(id, true)!;
                now = attempt.CompletesAt;
                run.ProcessSignals(new[] { RunSignal.CompleteExtraction(id, attempt.Id, now) });
            }
            else if (reason == RunEndReason.Death)
            {
                now = TimeSpan.FromSeconds(1);
                run.ProcessSignals(new[] { RunSignal.PlayerDied(id, now) });
            }
            else
            {
                now = deadline;
                run.ProcessSignals(Array.Empty<RunSignal>());
            }
            return (run.Result!, manifest, inventory);
        }
    }
}
