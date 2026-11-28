#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Inventory;
using ProjectWeapon.Loot;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Settlement;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Room = ProjectWeapon.ZoneOneGreybox.ZoneOneGreybox;

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
