#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.CombatHud;
using ProjectWeapon.Inventory;
using ProjectWeapon.Loot;
using ProjectWeapon.Run;
using ProjectWeapon.RunRecovery;
using ProjectWeapon.Save;
using ProjectWeapon.Settlement;
using ProjectWeapon.WeaponSale;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Room = ProjectWeapon.ZoneOneGreybox.ZoneOneGreybox;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ProjectWeapon.SortieLoop.Tests
{
    public sealed class SortieLoopTests
    {
        private const string LobbyPath = "Assets/Scenes/SortieLoop/SortieLoop.unity";
        private const string RoomName = "ZoneOneGreybox";
        private const long InitialCoins = 90;

        private Scene lobby;
        private SortieLoopHost host = null!;
        private Keyboard keyboard = null!;
        private Touchscreen? touchscreen;
        private InputSettings.UpdateMode originalUpdateMode;
        private InputSettings.BackgroundBehavior originalBackgroundBehavior;
        private InputSettings.EditorInputBehaviorInPlayMode originalEditorInput;
        private bool inputConfigured;
        private string? profileDirectory;
        private AtomicSnapshotStore store = null!;
        private WeaponInstance sword = null!;
        private WeaponInstance warehouseSpare = null!;
        private WeaponInstance? pickedUp;
        private double clock;
        private double sortieOrigin;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            clock = 0;
            pickedUp = null;
            RegisterKeyboard();

            profileDirectory = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN193-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(profileDirectory, "snapshot.json");
            store = new AtomicSnapshotStore(path);
            sword = PersonalWeapon("old_sword");
            warehouseSpare = PersonalWeapon("wooden_spear");
            var seed = new SaveSnapshot(1, InitialCoins, new[] { sword, warehouseSpare },
                Array.Empty<CatalogSaveRecord>(), Array.Empty<SettlementSaveRecord>());
            Assert.That(store.Commit(0, seed).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            yield return LoadLobby(path);
        }

        private IEnumerator LoadLobby(string path)
        {
            void ConfigureLobby(Scene scene, LoadSceneMode mode)
            {
                if (scene.path != LobbyPath)
                    return;

                lobby = scene;
                host = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SortieLoopHost>(true))
                    .Single();
                host.Configure(path, () => clock);
            }

            SceneManager.sceneLoaded += ConfigureLobby;
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(LobbyPath,
                    new LoadSceneParameters(LoadSceneMode.Additive));
            }
            finally
            {
                SceneManager.sceneLoaded -= ConfigureLobby;
            }
            yield return null;
            Assert.That(lobby.isLoaded, Is.True);
            Assert.That(host.IsInLobby, Is.True);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene roomScene = SceneManager.GetSceneByName(RoomName);
            if (roomScene.IsValid() && roomScene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(roomScene);
            if (lobby.IsValid() && lobby.isLoaded)
                yield return SceneManager.UnloadSceneAsync(lobby);

            if (inputConfigured)
            {
                if (keyboard != null && keyboard.added)
                    InputSystem.RemoveDevice(keyboard);
                if (touchscreen != null && touchscreen.added)
                    InputSystem.RemoveDevice(touchscreen);
                touchscreen = null;
                InputSystem.settings.updateMode = originalUpdateMode;
                InputSystem.settings.backgroundBehavior = originalBackgroundBehavior;
                InputSystem.settings.editorInputBehaviorInPlayMode = originalEditorInput;
                inputConfigured = false;
            }

            if (profileDirectory != null && Directory.Exists(profileDirectory))
            {
                foreach (string file in Directory.EnumerateFiles(profileDirectory))
                    File.Delete(file);
                Directory.Delete(profileDirectory);
            }
        }

        [UnityTest]
        public IEnumerator SuccessfulReturnSavesTheExactChestWeaponAndLetsAnEmptyLoadoutSortieAgain()
        {
            yield return StartSortie(chooseSword: true);
            Room room = host.ActiveRoom!;
            RunId firstRun = room.Run.CurrentRun!.Id;
            AssertReserved(sword, firstRun, revision: 2);
            AssertWarehouse(store.Load()!, warehouseSpare);

            room.Player.position = new Vector2(3.5f, 5.5f);
            float meleeHealth = room.Encounter.Melee.Brain.CurrentHealth;
            yield return WaitUntilOrFail(() => room.Encounter.Melee.Brain.CurrentHealth < meleeHealth,
                "The selected Old Sword did not damage the real melee enemy.");
            yield return OpenChestAndPickUp(room);
            WeaponInstance loot = pickedUp!;
            Assert.That(store.Load()!.Weapons.Any(item => item.Id == loot.Id), Is.False,
                "Field loot should not be in the durable warehouse before return.");

            yield return WaitForRangedWindupAtReturn(room);
            var rangedImpact = room.Encounter.Ranged.Brain.ActiveAttack!.Value.HitsAt;
            int health = room.Encounter.PlayerHealth;

            yield return PressInteract();
            ExtractionAttempt interruptedByMovement = ActiveAttempt(room);
            Assert.That(interruptedByMovement.CompletesAt - interruptedByMovement.StartedAt,
                Is.EqualTo(TimeSpan.FromSeconds(3)));
            SetElapsed(interruptedByMovement.StartedAt + TimeSpan.FromMilliseconds(50));
            SendKeyboard(Key.D);
            yield return null;
            SendKeyboard();
            AssertCancelled(room, firstRun);
            Assert.That(room.Encounter.PlayerHealth, Is.EqualTo(health));

            room.Player.position = room.ExtractionCenter;
            yield return PressInteract();
            ExtractionAttempt interruptedByHit = ActiveAttempt(room);
            Assert.That(interruptedByHit.Id, Is.Not.EqualTo(interruptedByMovement.Id));
            SetElapsed(rangedImpact);
            yield return null;
            AssertCancelled(room, firstRun);
            Assert.That(room.Encounter.PlayerHealth, Is.LessThan(health));

            yield return DefeatRangedEnemy(room);
            room.Player.position = room.ExtractionCenter;
            yield return PressInteract();
            ExtractionAttempt completed = ActiveAttempt(room);
            SetElapsed(completed.CompletesAt - TimeSpan.FromMilliseconds(1));
            yield return null;
            Assert.That(room.Phase, Is.EqualTo(RunPhase.Extracting));
            Assert.That(store.Load()!.Settlements, Is.Empty);

            SetElapsed(completed.CompletesAt);
            yield return null;
            Assert.That(room.Phase, Is.EqualTo(RunPhase.Succeeded));
            Assert.That(room.Run.Result!.EffectiveAt, Is.EqualTo(completed.CompletesAt));
            SaveSnapshot written = store.Load()!;
            RunSettlementDetails details = ReadReceipt(written, firstRun, RunEndReason.Extracted);
            AssertWarehouse(written, sword);
            AssertWarehouse(written, warehouseSpare);
            AssertWarehouse(written, loot);
            CollectionAssert.AreEquivalent(new[] { sword.Id, warehouseSpare.Id, loot.Id },
                written.Weapons.Select(item => item.Id));
            Assert.That(details.ReturnedWeapons.Single().InstanceId, Is.EqualTo(sword.Id.ToString()));
            Assert.That(details.NewWeapons.Single().InstanceId, Is.EqualTo(loot.Id.ToString()));
            Assert.That(written.ConfirmedCoins, Is.EqualTo(InitialCoins));
            Assert.That(written.Revision, Is.EqualTo(3));

            yield return ReturnToLobby();
            yield return StartSortie(chooseSword: false);
            Room next = host.ActiveRoom!;
            RunId nextRun = next.Run.CurrentRun!.Id;
            Assert.That(nextRun, Is.Not.EqualTo(firstRun));
            WeaponInstance loan = host.ActiveManifest!.Equipped.Single();
            Assert.That(loan.Provenance, Is.EqualTo(WeaponProvenance.Loan));
            Assert.That(loan.DefinitionId.Value, Is.EqualTo("old_sword"));
            Assert.That(loan.Id, Is.Not.EqualTo(sword.Id));
            AssertReserved(loan, nextRun, revision: 4);
            AssertWarehouse(store.Load()!, sword);
            AssertWarehouse(store.Load()!, loot);
        }

        [UnityTest]
        public IEnumerator ExactDeadlineTakesPriorityAndCommitsOnlyOneLossSettlement()
        {
            yield return StartSortie(chooseSword: true);
            Room room = host.ActiveRoom!;
            RunId run = room.Run.CurrentRun!.Id;
            yield return OpenChestAndPickUp(room);
            WeaponInstance loot = pickedUp!;

            yield return DefeatRangedEnemy(room);

            room.Player.position = room.ExtractionCenter;
            TimeSpan deadline = room.Run.CurrentRun!.Deadline;
            SetElapsed(deadline - TimeSpan.FromSeconds(3));
            yield return PressInteract();
            Assert.That(ActiveAttempt(room).CompletesAt, Is.EqualTo(deadline));
            SetElapsed(deadline - TimeSpan.FromMilliseconds(1));
            yield return null;
            Assert.That(room.Phase, Is.EqualTo(RunPhase.Extracting));
            Assert.That(store.Load()!.Settlements, Is.Empty);

            SetElapsed(deadline);
            yield return null;
            Assert.That(room.Phase, Is.EqualTo(RunPhase.Failed));
            Assert.That(room.Run.Result!.EffectiveAt, Is.EqualTo(deadline));
            Assert.That(room.Run.Result.Remaining, Is.EqualTo(TimeSpan.Zero));
            SaveSnapshot written = store.Load()!;
            RunSettlementDetails details = ReadReceipt(written, run, RunEndReason.TimeExpired);
            CollectionAssert.AreEqual(new[] { warehouseSpare.Id }, written.Weapons.Select(item => item.Id));
            CollectionAssert.AreEquivalent(new[] { sword.Id.ToString(), loot.Id.ToString() },
                details.LostWeapons.Select(item => item.InstanceId));
            Assert.That(details.NewWeapons, Is.Empty);
            Assert.That(written.Weapons.Any(item => item.Id == loot.Id), Is.False);
            AssertWarehouse(written, warehouseSpare);
            Assert.That(written.ConfirmedCoins, Is.EqualTo(InitialCoins));
            Assert.That(written.Revision, Is.EqualTo(3));

            clock += 3;
            yield return null;
            yield return null;
            Assert.That(store.Load()!.Revision, Is.EqualTo(written.Revision));
            ReadReceipt(store.Load()!, run, RunEndReason.TimeExpired);

            yield return ReturnToLobby();
            Assert.That(store.Load()!.Revision, Is.EqualTo(written.Revision));
        }

        [UnityTest]
        public IEnumerator ConflictingSaveRefreshesTheLobbyAndAllowsJustOneNewSortie()
        {
            Click(ButtonNamed("Open Equipment"));
            Click(WarehouseSwordButton());
            Button staleConfirm = ButtonNamed("Confirm");
            string path = Path.Combine(profileDirectory!, "snapshot.json");
            var otherWriter = new AtomicSnapshotStore(path);
            SaveSnapshot previous = otherWriter.Load()!;
            var updated = new SaveSnapshot(2, InitialCoins + 5, previous.Weapons, previous.Catalog,
                previous.Settlements, previous.CoinWeaponDraws);
            Assert.That(otherWriter.Commit(previous.Revision, updated).Status,
                Is.EqualTo(SnapshotCommitStatus.Committed));

            Click(staleConfirm);
            Assert.That(host.ActiveManifest, Is.Null);
            yield return WaitUntilOrFail(() => host.IsInLobby, "A stale save did not refresh the lobby.");
            Assert.That(host.ActiveRoom, Is.Null);
            Assert.That(SceneManager.GetSceneByName(RoomName).isLoaded, Is.False);
            Assert.That(store.Load()!.Revision, Is.EqualTo(2));
            AssertWarehouse(store.Load()!, sword);
            AssertWarehouse(store.Load()!, warehouseSpare);

            yield return StartSortie(chooseSword: true);
            RunId retry = host.ActiveRoom!.Run.CurrentRun!.Id;
            AssertReserved(sword, retry, revision: 3);
            SaveSnapshot saved = store.Load()!;
            Assert.That(saved.Weapons.Count(item => item.Location.Kind == WeaponLocationKind.RunReserved), Is.EqualTo(1));
            Assert.That(saved.ConfirmedCoins, Is.EqualTo(InitialCoins + 5));
            Assert.That(saved.Settlements, Is.Empty);
            AssertWarehouse(saved, warehouseSpare);
        }

        [UnityTest]
        public IEnumerator ExistingSaleReceiptSurvivesTheLobbyReservationAndReplaysWithoutAnotherCredit()
        {
            yield return SceneManager.UnloadSceneAsync(lobby);
            string path = Path.Combine(profileDirectory!, "sale-snapshot.json");
            store = new AtomicSnapshotStore(path);
            WeaponInstance offered = PersonalWeapon("old_sword");
            var seed = new SaveSnapshot(1, InitialCoins, new[] { sword, warehouseSpare, offered },
                Array.Empty<CatalogSaveRecord>(), Array.Empty<SettlementSaveRecord>());
            Assert.That(store.Commit(0, seed).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(
                "Assets/ProjectWeapon/Weapons/PrototypeStarter/PrototypeStarterWeaponCatalog.asset");
            var prices = AssetDatabase.LoadAssetAtPath<WeaponSalePrices>(
                "Assets/ProjectWeapon/WeaponSale/PrototypeWeaponSalePrices.asset");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(prices, Is.Not.Null);
            Guid request = Guid.NewGuid();
            var town = new RunState(() => TimeSpan.Zero);
            WeaponSaleResult sold = new WeaponSaleService(store, town, catalog, prices).TrySell(request, offered.Id);
            Assert.That(sold.Status, Is.EqualTo(WeaponSaleStatus.Sold));
            long fundedBalance = InitialCoins + 30;
            Assert.That(sold.ActiveSnapshot!.ConfirmedCoins, Is.EqualTo(fundedBalance));

            yield return LoadLobby(path);
            yield return StartSortie(chooseSword: true);

            RunState activeRun = host.ActiveRoom!.Run;
            AssertReserved(sword, activeRun.CurrentRun!.Id, revision: 3);
            var reopened = new AtomicSnapshotStore(path);
            SaveSnapshot reserved = reopened.Load()!;
            AssertWarehouse(reserved, warehouseSpare);
            Assert.That(reserved.Weapons.Any(item => item.Id == offered.Id), Is.False);
            Assert.That(reserved.ConfirmedCoins, Is.EqualTo(fundedBalance));
            WeaponSaleSaveRecord receipt = reserved.WeaponSales.Single();
            Assert.That(receipt.RequestId, Is.EqualTo(request));
            Assert.That(receipt.InstanceId, Is.EqualTo(offered.Id));
            Assert.That(receipt.Price, Is.EqualTo(30));
            Assert.That(receipt.BalanceAfterSale, Is.EqualTo(fundedBalance));

            byte[] beforeReplay = File.ReadAllBytes(path);
            var retry = new WeaponSaleService(reopened, activeRun, catalog, prices);
            WeaponSaleResult replay = retry.TrySell(request, offered.Id);
            Assert.That(replay.Status, Is.EqualTo(WeaponSaleStatus.AlreadySold));
            Assert.That(replay.Receipt!.RequestId, Is.EqualTo(request));
            Assert.That(replay.Receipt.InstanceId, Is.EqualTo(offered.Id));
            Assert.That(replay.ActiveSnapshot!.ConfirmedCoins, Is.EqualTo(fundedBalance));
            Assert.That(replay.ActiveSnapshot.Revision, Is.EqualTo(3));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(beforeReplay));
        }

        [UnityTest]
        public IEnumerator RefreshedLobbyPreservesAllTransactionHistoriesThroughReservationAndTimeout()
        {
            Click(ButtonNamed("Open Equipment"));
            Click(WarehouseSwordButton());
            Button staleConfirm = ButtonNamed("Confirm");
            string path = Path.Combine(profileDirectory!, "snapshot.json");
            var otherWriter = new AtomicSnapshotStore(path);
            SaveSnapshot previous = otherWriter.Load()!;

            Guid originalRunId = Guid.NewGuid();
            var originalSettlement = new SettlementSaveRecord(originalRunId, "RunSettlement:" + originalRunId,
                nameof(RunEndReason.Extracted));
            var draw = new CoinWeaponDrawSaveRecord(Guid.NewGuid(), 500, InitialCoins, warehouseSpare.Id,
                warehouseSpare.DefinitionId, grade: 1);
            long balance = InitialCoins + 30;
            var sale = new WeaponSaleSaveRecord(Guid.NewGuid(), WeaponInstanceId.New(), sword.DefinitionId,
                grade: 1, price: 30, balanceAfterSale: balance);
            var milestone = new CatalogMilestoneSaveRecord(5, 500, originalRunId);
            var recovered = new[] { "old_sword", "wooden_spear", "hunting_bow", "mace", "dagger" }.Select(
                id => new CatalogSaveRecord(new WeaponDefinitionId(id), "recovered"));
            var history = new SaveSnapshot(previous.Revision + 1, balance, previous.Weapons, recovered,
                new[] { originalSettlement }, new[] { draw }, new[] { sale }, new[] { milestone });
            Assert.That(otherWriter.Commit(previous.Revision, history).Status, Is.EqualTo(SnapshotCommitStatus.Committed));

            Click(staleConfirm);
            Assert.That(host.ActiveManifest, Is.Null);
            yield return WaitUntilOrFail(() => host.IsInLobby, "The lobby did not reload the newer saved history.");
            Assert.That(host.ActiveRoom, Is.Null);
            Assert.That(otherWriter.Load()!.Revision, Is.EqualTo(history.Revision));

            yield return StartSortie(chooseSword: true);
            Room room = host.ActiveRoom!;
            RunSession session = room.Run.CurrentRun!;
            Assert.That(session.Id.Value, Is.Not.EqualTo(originalRunId));
            AssertReserved(sword, session.Id, revision: history.Revision + 1);
            SaveSnapshot reserved = otherWriter.Load()!;
            Assert.That(reserved.ConfirmedCoins, Is.EqualTo(balance));
            Assert.That(reserved.Settlements.Select(item => item.RunId), Is.EqualTo(new[] { originalRunId }));
            AssertHistory(reserved);

            SetElapsed(session.Deadline);
            yield return WaitUntilOrFail(() => room.Run.Result != null, "The reservation did not finalize at its deadline.");
            Assert.That(room.Run.Result!.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            yield return ReturnToLobby();

            SaveSnapshot settled = new AtomicSnapshotStore(path).Load()!;
            Assert.That(settled.Revision, Is.EqualTo(history.Revision + 2));
            Assert.That(settled.ConfirmedCoins, Is.EqualTo(balance));
            CollectionAssert.AreEquivalent(new[] { originalRunId, session.Id.Value },
                settled.Settlements.Select(item => item.RunId));
            SettlementSaveRecord latest = settled.Settlements.Single(item => item.RunId == session.Id.Value);
            Assert.That(latest.TransactionId, Is.EqualTo("RunSettlement:" + session.Id));
            Assert.That(RunSettlementDetails.Read(latest)!.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            AssertWarehouse(settled, warehouseSpare);
            Assert.That(settled.Weapons.Any(item => item.Id == sword.Id || item.Id == sale.InstanceId), Is.False);
            AssertHistory(settled);

            void AssertHistory(SaveSnapshot snapshot)
            {
                SettlementSaveRecord original = snapshot.Settlements.Single(item => item.RunId == originalRunId);
                Assert.That(JsonUtility.ToJson(original), Is.EqualTo(JsonUtility.ToJson(originalSettlement)));
                Assert.That(JsonUtility.ToJson(snapshot.CoinWeaponDraws.Single()), Is.EqualTo(JsonUtility.ToJson(draw)));
                Assert.That(JsonUtility.ToJson(snapshot.WeaponSales.Single()), Is.EqualTo(JsonUtility.ToJson(sale)));
                Assert.That(JsonUtility.ToJson(snapshot.CatalogMilestones.Single()), Is.EqualTo(JsonUtility.ToJson(milestone)));
            }
        }

        [UnityTest]
        public IEnumerator OverlappingAppInterruptionsKeepTheSameWorldAndRestartExtraction()
        {
            yield return StartSortie(chooseSword: true);
            Room room = host.ActiveRoom!;
            RunSession session = room.Run.CurrentRun!;
            yield return DefeatRangedEnemy(room);

            room.Player.position = room.ExtractionCenter;
            SetElapsed(TimeSpan.FromSeconds(10));
            Assert.That(room.TryStartExtraction(), Is.True);
            RunLifecycleRelay relay = host.GetComponent<RunLifecycleRelay>();
            relay.SendMessage("OnApplicationPause", true);
            relay.SendMessage("OnApplicationFocus", false);
            relay.SetConnectionAvailable(false);
            Assert.That(room.Run.ActiveExtraction, Is.Null);
            Assert.That(room.TryStartExtraction(), Is.False);
            Assert.That(room.Player.linearVelocity, Is.EqualTo(Vector2.zero));

            SetElapsed(TimeSpan.FromSeconds(12));
            relay.SendMessage("OnApplicationPause", false);
            relay.SendMessage("OnApplicationFocus", true);
            Assert.That(relay.IsGameplayBlocked, Is.True);
            Assert.That(room.TryStartExtraction(), Is.False);
            yield return null;
            Assert.That(store.Load()!.Revision, Is.EqualTo(2));

            relay.SetConnectionAvailable(true);
            Assert.That(relay.IsGameplayBlocked, Is.False);
            Assert.That(host.ActiveRoom, Is.SameAs(room));
            Assert.That(room.Run.CurrentRun, Is.SameAs(session));
            Assert.That(room.TryStartExtraction(), Is.True);
            Assert.That(ActiveAttempt(room).CompletesAt, Is.EqualTo(TimeSpan.FromSeconds(15)));
            SetElapsed(TimeSpan.FromSeconds(14.99));
            yield return null;
            Assert.That(room.Run.Result, Is.Null);
            SetElapsed(TimeSpan.FromSeconds(15));
            yield return WaitUntilOrFail(() => store.Load()!.Settlements.Count == 1, "Resumed run did not settle.");
            ReadReceipt(store.Load()!, session.Id, RunEndReason.Extracted);
        }

        [UnityTest]
        public IEnumerator ResumingAtDeadlineAndRepeatedReconnectWriteOnlyOneTimeout()
        {
            yield return StartSortie(chooseSword: false);
            Room room = host.ActiveRoom!;
            RunSession session = room.Run.CurrentRun!;
            RunLifecycleRelay relay = host.GetComponent<RunLifecycleRelay>();
            SetElapsed(session.Deadline - TimeSpan.FromSeconds(1));
            relay.SendMessage("OnApplicationPause", true);
            SetElapsed(session.Deadline);
            yield return null;
            Assert.That(room.Run.Result, Is.Null);
            Assert.That(store.Load()!.Revision, Is.EqualTo(2));

            relay.SendMessage("OnApplicationPause", false);
            Assert.That(room.Run.Result!.EffectiveAt, Is.EqualTo(session.Deadline));
            SaveSnapshot saved = store.Load()!;
            ReadReceipt(saved, session.Id, RunEndReason.TimeExpired);
            string snapshotPath = Path.Combine(profileDirectory!, "snapshot.json");
            byte[] committed = File.ReadAllBytes(snapshotPath);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                relay.SendMessage("OnApplicationPause", true);
                relay.SendMessage("OnApplicationPause", false);
            }
            CollectionAssert.AreEqual(committed, File.ReadAllBytes(snapshotPath));
            Assert.That(store.Load()!.Revision, Is.EqualTo(3));
            yield return ReturnToLobby();
        }

        [UnityTest]
        public IEnumerator LiveRoomRemainsBlockedWhenDurableReservationNoLongerMatches()
        {
            yield return StartSortie(chooseSword: true);
            Room room = host.ActiveRoom!;
            RunSession session = room.Run.CurrentRun!;
            RunLifecycleRelay relay = host.GetComponent<RunLifecycleRelay>();
            relay.SendMessage("OnApplicationPause", true);

            SaveSnapshot previous = store.Load()!;
            var items = previous.Weapons.Select(item => item.Id == sword.Id
                ? WeaponInstance.Rehydrate(sword.Id, sword.DefinitionId, sword.Provenance, WeaponLocation.Warehouse())
                : item);
            SaveSnapshot changed = previous.Next(previous.ConfirmedCoins, weapons: items.ToArray());
            Assert.That(store.Commit(previous.Revision, changed).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            SetElapsed(session.Deadline);
            relay.SendMessage("OnApplicationPause", false);
            room.Player.position = room.ExtractionCenter;
            yield return null;
            Assert.That(relay.IsGameplayBlocked, Is.True);
            Assert.That(room.TryStartExtraction(), Is.False);
            Assert.That(room.Run.Result, Is.Null);
            Assert.That(store.Load()!.Revision, Is.EqualTo(changed.Revision));
            Assert.That(store.Load()!.Settlements, Is.Empty);
        }

        [UnityTest]
        public IEnumerator OrdinaryTimeoutCannotSettleAgainstAnUnreservedEquippedWeapon()
        {
            yield return StartSortie(chooseSword: true);
            Room room = host.ActiveRoom!;
            RunSession session = room.Run.CurrentRun!;
            SaveSnapshot reserved = store.Load()!;
            SaveSnapshot changed = reserved.Next(reserved.ConfirmedCoins, weapons: reserved.Weapons.Select(item =>
                item.Id == sword.Id ? WeaponInstance.Rehydrate(sword.Id, sword.DefinitionId, sword.Provenance,
                    WeaponLocation.Equipped(0)) : item));
            Assert.That(store.Commit(reserved.Revision, changed).Status, Is.EqualTo(SnapshotCommitStatus.Committed));

            SetElapsed(session.Deadline);
            yield return WaitUntilOrFail(() => room.Run.Result != null, "The run did not finalize.");
            Assert.That(store.Load()!.Revision, Is.EqualTo(changed.Revision));
            Assert.That(store.Load()!.Settlements, Is.Empty);
            Assert.That(store.Load()!.Weapons.Single(item => item.Id == sword.Id).Location.Kind,
                Is.EqualTo(WeaponLocationKind.Equipped));
        }

        [UnityTest]
        public IEnumerator ExistingReceiptDuringRoomLoadPreventsEnteringTheSettledRun()
        {
            Click(ButtonNamed("Open Equipment"));
            Click(WarehouseSwordButton());
            Click(ButtonNamed("Confirm"));
            Guid runId = host.ActiveManifest!.RunId;
            Assert.That(host.ActiveRoom, Is.Null);

            RunLifecycleRelay relay = host.GetComponent<RunLifecycleRelay>();
            relay.SendMessage("OnApplicationPause", true);
            SaveSnapshot reserved = store.Load()!;
            var receipt = new SettlementSaveRecord(runId, "RunSettlement:" + runId,
                nameof(RunEndReason.TimeExpired));
            SaveSnapshot settled = reserved.Next(reserved.ConfirmedCoins,
                weapons: reserved.Weapons.Where(item => item.Location.ReservedRunId != runId),
                settlements: reserved.Settlements.Append(receipt));
            Assert.That(store.Commit(reserved.Revision, settled).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            relay.SendMessage("OnApplicationPause", false);

            yield return null;
            Assert.That(host.ActiveRoom, Is.Null);
            yield return ReturnToLobby();
            Assert.That(store.Load()!.Revision, Is.EqualTo(settled.Revision));
            Assert.That(store.Load()!.Settlements.Single().RunId, Is.EqualTo(runId));
        }

        [UnityTest]
        public IEnumerator ColdLobbyKeepsUnverifiableReservationAndDisablesSortie()
        {
            yield return StartSortie(chooseSword: true);
            string snapshotPath = Path.Combine(profileDirectory!, "snapshot.json");
            byte[] reserved = File.ReadAllBytes(snapshotPath);
            Guid runId = host.ActiveManifest!.RunId;

            yield return SceneManager.UnloadSceneAsync(lobby);
            yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneByName(RoomName));
            yield return LoadLobby(snapshotPath);
            Button open = ButtonNamed("Open Equipment");
            Assert.That(open.interactable, Is.False);
            open.onClick.Invoke();
            yield return null;
            Assert.That(host.IsInLobby, Is.True);
            Assert.That(host.ActiveManifest, Is.Null);
            Assert.That(store.Load()!.Weapons.Single(item => item.Id == sword.Id).Location.ReservedRunId,
                Is.EqualTo(runId));
            CollectionAssert.AreEqual(reserved, File.ReadAllBytes(snapshotPath));
        }

        [UnityTest]
        public IEnumerator RealWarehouseButtonCommitsEquipmentAndSaleThenOpensTheSameEquipmentInSortie()
        {
            SaveSnapshot previous = store.Load()!;
            WeaponInstance duplicate = PersonalWeapon("old_sword");
            Guid priorRun = Guid.NewGuid();
            var priorSettlement = new SettlementSaveRecord(priorRun, "RunSettlement:" + priorRun, "Extracted");
            var priorDraw = new CoinWeaponDrawSaveRecord(Guid.NewGuid(), 500, 180, warehouseSpare.Id,
                warehouseSpare.DefinitionId, 1);
            var priorSale = new WeaponSaleSaveRecord(Guid.NewGuid(), WeaponInstanceId.New(), sword.DefinitionId, 1, 30, 210);
            var priorMilestone = new CatalogMilestoneSaveRecord(5, 500, priorRun);
            var recovered = new[] { "old_sword", "wooden_spear", "hunting_bow", "mace", "dagger" }.Select(
                id => new CatalogSaveRecord(new WeaponDefinitionId(id), "recovered"));
            SaveSnapshot history = previous.Next(210, weapons: previous.Weapons.Append(duplicate), catalog: recovered,
                settlements: new[] { priorSettlement }, coinWeaponDraws: new[] { priorDraw },
                weaponSales: new[] { priorSale }, catalogMilestones: new[] { priorMilestone });
            Assert.That(store.Commit(previous.Revision, history).Status, Is.EqualTo(SnapshotCommitStatus.Committed));

            WeaponInstance equipped = new[] { sword, duplicate }.OrderBy(item => item.Id.ToString(), StringComparer.Ordinal).First();
            WeaponInstance offered = equipped.Id == sword.Id ? duplicate : sword;
            Button OldSwordRow() => VisibleButtons("WarehouseWeapon").First(
                button => button.GetComponentInChildren<TMP_Text>().text == "낡은 검");

            ClickByRaycast(ButtonNamed("Open Warehouse"));
            yield return null;
            Assert.That(host.IsInLobby, Is.False);
            Assert.That(VisibleButtons("Open Equipment"), Is.Empty);
            ClickByRaycast(OldSwordRow());
            ClickByRaycast(ButtonNamed("EquipSlot0"));
            SaveSnapshot afterMove = store.Load()!;
            Assert.That(afterMove.Revision, Is.EqualTo(history.Revision + 1));
            WeaponInstance savedEquipment = afterMove.Weapons.Single(item => item.Id == equipped.Id);
            Assert.That(savedEquipment.Location.Kind, Is.EqualTo(WeaponLocationKind.Equipped));
            Assert.That(savedEquipment.Location.SlotIndex, Is.Zero);
            Assert.That(afterMove.Weapons.Single(item => item.Id == offered.Id).Location.Kind,
                Is.EqualTo(WeaponLocationKind.Warehouse));

            ClickByRaycast(OldSwordRow());
            ClickByRaycast(ButtonNamed("PrepareSale"));
            yield return null;
            Assert.That(lobby.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TMP_Text>())
                .Single(text => text.name == "SaleQuote").text, Is.EqualTo("낡은 검 · 30 코인"));
            Button confirm = ButtonNamed("ConfirmSale");
            ClickByRaycast(confirm);
            confirm.onClick.Invoke();
            SaveSnapshot afterSale = store.Load()!;
            Assert.That(afterSale.Revision, Is.EqualTo(history.Revision + 2));
            Assert.That(afterSale.ConfirmedCoins, Is.EqualTo(240));
            Assert.That(afterSale.Weapons.Any(item => item.Id == offered.Id), Is.False);
            Assert.That(afterSale.WeaponSales.Single(item => item.InstanceId == offered.Id).Price, Is.EqualTo(30));
            Assert.That(afterSale.WeaponSales, Has.Count.EqualTo(2));

            ClickByRaycast(ButtonNamed("Close"));
            yield return null;
            Assert.That(host.IsInLobby, Is.True);
            ClickByRaycast(ButtonNamed("Open Equipment"));
            yield return null;
            Assert.That(ButtonNamed("Equipped0").GetComponentInChildren<TMP_Text>().text, Does.Contain("낡은 검"));
            ClickByRaycast(ButtonNamed("Confirm"));
            yield return WaitUntilOrFail(() => host.ActiveRoom != null, "The equipped warehouse weapon was not used to sortie.");
            Assert.That(host.ActiveManifest!.Equipped.Single().Id, Is.EqualTo(equipped.Id));

            AssertReserved(equipped, host.ActiveRoom!.Run.CurrentRun!.Id, history.Revision + 3);
            SaveSnapshot active = store.Load()!;
            AssertWarehouse(active, warehouseSpare);
            Assert.That(active.Weapons.Any(item => item.Id == offered.Id), Is.False);
            Assert.That(active.ConfirmedCoins, Is.EqualTo(afterSale.ConfirmedCoins));
            Assert.That(active.Settlements.Single().RunId, Is.EqualTo(priorRun));
            Assert.That(active.CoinWeaponDraws.Single().RequestId, Is.EqualTo(priorDraw.RequestId));
            Assert.That(active.CatalogMilestones.Single().SourceRunId, Is.EqualTo(priorMilestone.SourceRunId));
            CollectionAssert.AreEquivalent(afterSale.WeaponSales.Select(item => item.RequestId),
                active.WeaponSales.Select(item => item.RequestId));
        }

        private void ClickByRaycast(Button button)
        {
            Assert.That(button.IsActive() && button.interactable, Is.True, button.name);
            EventSystem eventSystem = lobby.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<EventSystem>())
                .Single();
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center))
            };
            var hits = new System.Collections.Generic.List<RaycastResult>();
            eventSystem.RaycastAll(pointer, hits);
            Assert.That(hits.FirstOrDefault().gameObject, Is.EqualTo(button.gameObject), button.name);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        [UnityTest]
        public IEnumerator RealCatalogButtonReadsTheSavedRosterAndReturnsWithoutWriting()
        {
            SaveSnapshot previous = store.Load()!;
            var catalog = new[]
            {
                new CatalogSaveRecord(new WeaponDefinitionId("old_sword"), "discovered"),
                new CatalogSaveRecord(new WeaponDefinitionId("wooden_spear"), "recovered"),
                new CatalogSaveRecord(new WeaponDefinitionId("genesis_bow"), "mastered")
            };
            SaveSnapshot newer = previous.Next(previous.ConfirmedCoins, catalog: catalog);
            Assert.That(store.Commit(previous.Revision, newer).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            string snapshotPath = Path.Combine(profileDirectory!, "snapshot.json");
            byte[] expectedBytes = File.ReadAllBytes(snapshotPath);

            ClickByRaycast(ButtonNamed("Open Catalog"));
            yield return null;
            Assert.That(host.IsInLobby, Is.False);
            ScrollRect list = lobby.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ScrollRect>())
                .Single(candidate => candidate.name == "Catalog List");
            Assert.That(list.content.childCount, Is.EqualTo(21));
            Assert.That(list.content.Find("old_sword/Catalog Stage").GetComponent<TMP_Text>().text, Is.EqualTo("발견"));
            Assert.That(list.content.Find("wooden_spear/Catalog Stage").GetComponent<TMP_Text>().text, Is.EqualTo("회수"));
            Assert.That(list.content.Find("genesis_bow/Catalog Stage").GetComponent<TMP_Text>().text, Is.EqualTo("숙련"));
            Assert.That(list.content.Find("genesis_bow/Weapon Name").GetComponent<TMP_Text>().text, Is.EqualTo("창세의 활"));

            ClickByRaycast(ButtonNamed("Close Catalog"));
            yield return null;
            Assert.That(host.IsInLobby, Is.True);
            Assert.That(host.ActiveManifest, Is.Null);
            CollectionAssert.AreEqual(expectedBytes, File.ReadAllBytes(snapshotPath));
            AssertWarehouse(store.Load()!, sword);
            AssertWarehouse(store.Load()!, warehouseSpare);
        }

        [UnityTest]
        public IEnumerator ActualCombatHudCastsTheStarterLoanAndReturnsThroughThePortal()
        {
            Assert.That(host.ActiveHud, Is.Null);
            yield return EnterWithTouch();

            Room room = host.ActiveRoom!;
            CombatActionHud hud = host.ActiveHud!;
            Assert.That(hud, Is.Not.Null);
            Assert.That(host.ActiveManifest!.Equipped.Single().IsLoaned, Is.True);
            Assert.That(host.ActiveManifest.Equipped.Single().DefinitionId.Value, Is.EqualTo("old_sword"));
            Button skillButton = ButtonNamed("Skill");
            Assert.That(skillButton.interactable, Is.True);

            Vector2 skill = Center(skillButton);
            Vector2 release = skill + Vector2.right * 24f * hud.GetComponentInParent<Canvas>().scaleFactor;
            room.Player.position = new Vector2(5.5f, 2.5f);
            Rigidbody2D meleeBody = room.Encounter.Melee.GetComponent<Rigidbody2D>();
            meleeBody.position = new Vector2(7.25f, 2.5f);
            Physics2D.SyncTransforms();
            float meleeBefore = room.Encounter.Melee.Brain.CurrentHealth;

            yield return Touch(3, skill, TouchPhase.Began);
            yield return Touch(3, release, TouchPhase.Moved);
            yield return Touch(3, release, TouchPhase.Ended);
            Assert.That(room.Encounter.Melee.Brain.CurrentHealth, Is.EqualTo(meleeBefore),
                "The sword reached an enemy through a castle wall.");
            Assert.That(skillButton.interactable, Is.False);

            SetElapsed(TimeSpan.FromSeconds(5));
            room.Player.position = new Vector2(9.6f, 6.4f);
            Physics2D.SyncTransforms();
            hud.Refresh();
            float rangedBefore = room.Encounter.Ranged.Brain.CurrentHealth;
            Assert.That(rangedBefore, Is.GreaterThan(0f));
            Vector2 stick = ScreenCenter((RectTransform)hud.Joystick.transform);
            float scale = hud.GetComponentInParent<Canvas>().scaleFactor;
            yield return Touch(4, stick, TouchPhase.Began);
            yield return Touch(4, stick + Vector2.up * 35f * scale, TouchPhase.Moved);
            yield return Touch(5, skill, TouchPhase.Began);
            yield return Touch(5, release, TouchPhase.Moved);
            Assert.That(hud.IsAiming && hud.Joystick.IsHeld, Is.True);
            Vector2 beforeMovement = room.Player.position;
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(room.Player.position.y, Is.GreaterThan(beforeMovement.y));
            Assert.That(room.Encounter.Ranged.Brain.CurrentHealth, Is.EqualTo(rangedBefore));
            yield return Touch(5, release, TouchPhase.Ended);
            Assert.That(room.Encounter.Ranged.Brain.CurrentHealth, Is.Zero);
            Assert.That(hud.Joystick.IsHeld, Is.True);
            yield return Touch(4, stick, TouchPhase.Ended);

            RunLifecycleRelay relay = host.GetComponent<RunLifecycleRelay>();
            hud.SendMessage("OnApplicationPause", true);
            relay.SendMessage("OnApplicationPause", true);
            Assert.That(hud.GetComponent<CanvasGroup>().alpha, Is.Zero);
            Assert.That(hud.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
            hud.SendMessage("OnApplicationPause", false);
            relay.SendMessage("OnApplicationPause", false);
            Assert.That(host.ActiveHud, Is.SameAs(hud));
            Assert.That(hud.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f));

            SetElapsed(TimeSpan.FromSeconds(7));
            room.Player.position = room.ExtractionCenter;
            hud.Refresh();
            yield return Tap(6, ButtonNamed("Extract"));
            Assert.That(room.Phase, Is.EqualTo(RunPhase.Extracting));
            SetElapsed(TimeSpan.FromSeconds(7.1));
            yield return Touch(7, Center(ButtonNamed("Dodge")), TouchPhase.Began);
            Assert.That(room.Phase, Is.EqualTo(RunPhase.Exploring));
            Assert.That(room.Movement!.IsDodging, Is.True);
            yield return Touch(7, Center(ButtonNamed("Dodge")), TouchPhase.Ended);
            yield return WaitUntilOrFail(() => !room.Movement.IsDodging, "The real dodge did not finish.");

            SetElapsed(TimeSpan.FromSeconds(9));
            room.Player.position = room.ExtractionCenter;
            hud.Refresh();
            yield return Tap(8, ButtonNamed("Extract"));
            RunId id = room.Run.CurrentRun!.Id;
            SetElapsed(ActiveAttempt(room).CompletesAt);
            yield return WaitUntilOrFail(() => store.Load()!.Settlements.Count == 1, "The touch return did not settle.");
            Assert.That(host.ActiveHud, Is.Null);
            ReadReceipt(store.Load()!, id, RunEndReason.Extracted);
            yield return Tap(9, ButtonNamed("Return to Lobby"));
            yield return WaitUntilOrFail(() => host.IsInLobby, "The result touch did not return to the lobby.");
            Assert.That(host.ActiveHud, Is.Null);
        }

        [UnityTest]
        public IEnumerator ActualWeaponButtonsSwitchTheFieldDefinitionAndItsSkillAvailability()
        {
            yield return EnterWithTouch(chooseTwo: true);
            Room room = host.ActiveRoom!;
            Assert.That(host.ActiveManifest!.Equipped.Select(item => item.Id),
                Is.EqualTo(new[] { sword.Id, warehouseSpare.Id }));
            Assert.That(ButtonNamed("Skill").interactable, Is.True);

            room.Player.position = new Vector2(9.65f, 6.4f);
            Physics2D.SyncTransforms();
            float before = room.Encounter.Ranged.Brain.CurrentHealth;
            yield return null;
            Assert.That(room.Encounter.Ranged.Brain.CurrentHealth, Is.EqualTo(before));
            yield return Tap(5, ButtonNamed("Weapon Slot 2"));
            Assert.That(ButtonNamed("Weapon Slot 2").interactable, Is.False);
            Assert.That(ButtonNamed("Weapon Slot 1").interactable, Is.True);
            Assert.That(ButtonNamed("Skill").interactable, Is.False);
            Assert.That(room.Encounter.Ranged.Brain.CurrentHealth, Is.EqualTo(before - 10f));

            yield return Tap(6, ButtonNamed("Weapon Slot 1"));
            Assert.That(ButtonNamed("Weapon Slot 1").interactable, Is.False);
            Assert.That(ButtonNamed("Skill").interactable, Is.True);
        }

        private IEnumerator EnterWithTouch(bool chooseTwo = false)
        {
            touchscreen = InputSystem.AddDevice<Touchscreen>();
            yield return Tap(1, ButtonNamed("Open Equipment"));
            if (chooseTwo)
            {
                yield return Tap(2, WarehouseSwordButton());
                yield return Tap(3, VisibleButtons("WarehouseWeapon").Single());
            }

            sortieOrigin = clock;
            yield return Tap(chooseTwo ? 4 : 2, ButtonNamed("Confirm"));
            yield return WaitUntilOrFail(() => host.ActiveRoom != null && host.ActiveHud != null, "The combat HUD did not bind.");
            yield return null;
        }

        private IEnumerator Tap(int id, Button button)
        {
            Assert.That(button.IsActive() && button.interactable, Is.True, button.name);
            Canvas.ForceUpdateCanvases();
            Vector2 point = Center(button);
            yield return Touch(id, point, TouchPhase.Began);
            yield return Touch(id, point, TouchPhase.Ended);
        }

        private IEnumerator Touch(int id, Vector2 point, TouchPhase phase)
        {
            InputSystem.QueueStateEvent(touchscreen!, new TouchState { touchId = id, position = point, phase = phase });
            InputSystem.Update();
            yield return null;
        }

        private static Vector2 Center(Button button) => ScreenCenter((RectTransform)button.transform);

        private static Vector2 ScreenCenter(RectTransform rect)
            => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));

        private IEnumerator StartSortie(bool chooseSword)
        {
            Click(ButtonNamed("Open Equipment"));
            if (chooseSword)
                Click(WarehouseSwordButton());
            Button confirm = ButtonNamed("Confirm");
            sortieOrigin = clock;
            Click(confirm);
            confirm.onClick.Invoke();
            yield return WaitUntilOrFail(() => host.ActiveRoom != null, "The real dungeon scene did not load.");
            Assert.That(host.ActiveRoom!.Run.Phase, Is.EqualTo(RunPhase.Exploring));
            Assert.That(host.ActiveRoom.Run.CurrentRun!.Id.Value, Is.EqualTo(host.ActiveManifest!.RunId));
            Assert.That(host.ActiveManifest.Equipped.Single().Id == sword.Id, Is.EqualTo(chooseSword));
            yield return null;
        }

        private IEnumerator ReturnToLobby()
        {
            Click(ButtonNamed("Return to Lobby"));
            yield return WaitUntilOrFail(() => host.IsInLobby, "The result screen did not return to the lobby.");
            Assert.That(SceneManager.GetSceneByName(RoomName).isLoaded, Is.False);
            Assert.That(host.ActiveManifest, Is.Null);
        }

        private IEnumerator OpenChestAndPickUp(Room room)
        {
            var chest = room.Encounter.Chest;
            room.Player.position = chest.transform.position;
            yield return PressInteract();
            Assert.That(chest.IsOpen, Is.True);

            Scene field = room.gameObject.scene;
            var drop = field.GetRootGameObjects().Select(root => root.GetComponent<WorldLoot>())
                .Single(item => item != null)!;
            Assert.That(drop.Item, Is.Not.Null);
            room.Player.position = drop.transform.position;
            Physics2D.SyncTransforms();
            for (int ticks = 0; ticks < 6 && room.Encounter.Collected.Count == 0; ticks++)
            {
                yield return new WaitForFixedUpdate();
                yield return null;
            }

            pickedUp = room.Encounter.Collected.Single();
            Assert.That(pickedUp.DefinitionId.Value, Is.EqualTo("wooden_spear"));
            Assert.That(pickedUp.Id, Is.EqualTo(drop.Item!.Id));
            Assert.That(pickedUp.Location.Kind, Is.EqualTo(WeaponLocationKind.Carried));
            Assert.That(drop.gameObject.activeSelf, Is.False);
        }

        private IEnumerator DefeatRangedEnemy(Room room)
        {
            room.Player.position = new Vector2(10.55f, 6.4f);
            for (int step = 0; step < 4 && room.Encounter.Ranged.Brain.CurrentHealth > 0; step++)
            {
                clock += 1.25;
                yield return null;
            }
            Assert.That(room.Encounter.Ranged.Brain.CurrentHealth, Is.Zero,
                "The equipped Old Sword did not defeat the ranged enemy before the final channel.");
        }

        private IEnumerator WaitForRangedWindupAtReturn(Room room)
        {
            Vector2 center = room.ExtractionCenter;
            room.Player.position = center;
            for (int step = 0; step < 5; step++)
            {
                yield return null;
                if (room.Encounter.Ranged.Brain.ActiveAttack is { } attack
                    && attack.Area.Contains(new WorldPoint2D(center.x, center.y)))
                    yield break;
                clock += 1.5;
            }
            Assert.Fail("The real ranged enemy did not aim at the extraction mark.");
        }

        private void AssertCancelled(Room room, RunId stillActive)
        {
            Assert.That(room.Phase, Is.EqualTo(RunPhase.Exploring));
            Assert.That(room.Run.CurrentRun!.Id, Is.EqualTo(stillActive));
            Assert.That(room.Run.ActiveExtraction, Is.Null);
            Assert.That(store.Load()!.Settlements, Is.Empty);
        }

        private static ExtractionAttempt ActiveAttempt(Room room)
        {
            Assert.That(room.Phase, Is.EqualTo(RunPhase.Extracting));
            Assert.That(room.Run.ActiveExtraction, Is.Not.Null);
            return room.Run.ActiveExtraction!;
        }

        private void AssertReserved(WeaponInstance expected, RunId run, long revision)
        {
            SaveSnapshot saved = store.Load()!;
            Assert.That(saved.Revision, Is.EqualTo(revision));
            WeaponInstance savedWeapon = saved.Weapons.Single(item => item.Id == expected.Id);
            Assert.That(savedWeapon.Location.Kind, Is.EqualTo(WeaponLocationKind.RunReserved));
            Assert.That(savedWeapon.Location.ReservedRunId, Is.EqualTo(run.Value));
        }

        private static void AssertWarehouse(SaveSnapshot saved, WeaponInstance expected)
        {
            WeaponInstance actual = saved.Weapons.Single(item => item.Id == expected.Id);
            Assert.That(actual.DefinitionId, Is.EqualTo(expected.DefinitionId));
            Assert.That(actual.Location.Kind, Is.EqualTo(WeaponLocationKind.Warehouse));
        }

        private static RunSettlementDetails ReadReceipt(SaveSnapshot snapshot, RunId run, RunEndReason reason)
        {
            SettlementSaveRecord receipt = snapshot.Settlements.Single();
            Assert.That(receipt.RunId, Is.EqualTo(run.Value));
            Assert.That(receipt.TransactionId, Is.EqualTo("RunSettlement:" + run));
            RunSettlementDetails? details = RunSettlementDetails.Read(receipt);
            Assert.That(details, Is.Not.Null);
            Assert.That(details!.Reason, Is.EqualTo(reason));
            return details!;
        }

        private static WeaponInstance PersonalWeapon(string id)
            => WeaponInstance.Create(new WeaponDefinitionId(id), WeaponProvenance.Personal, WeaponLocation.Warehouse());

        private void SetElapsed(TimeSpan elapsed) => clock = sortieOrigin + elapsed.TotalSeconds;

        private Button WarehouseSwordButton()
            => VisibleButtons("WarehouseWeapon").Single(item => item.GetComponentInChildren<TMP_Text>().text == "낡은 검");

        private Button ButtonNamed(string name) => VisibleButtons(name).Single();

        private Button[] VisibleButtons(string name)
            => lobby.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Button>())
                .Where(item => item.name == name).ToArray();

        private static void Click(Button button)
        {
            Assert.That(button.IsActive() && button.interactable, Is.True, button.name);
            var eventSystem = EventSystem.current;
            Assert.That(eventSystem, Is.Not.Null);
            button.OnPointerClick(new PointerEventData(eventSystem) { button = PointerEventData.InputButton.Left });
        }

        private static IEnumerator WaitUntilOrFail(Func<bool> ready, string message)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 8;
            while (!ready() && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            Assert.That(ready(), Is.True, message);
        }

        private IEnumerator PressInteract()
        {
            SendKeyboard(Key.E);
            yield return null;
            SendKeyboard();
        }

        private void RegisterKeyboard()
        {
            originalUpdateMode = InputSystem.settings.updateMode;
            originalBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            originalEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            inputConfigured = true;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
        }

        private void SendKeyboard(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }
    }
}
#endif
