#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Enemies.Boss;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.SortieLoop;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ProjectWeapon.WoodenSpearSkill.Tests
{
    public sealed class WoodenSpearCombatHudTests
    {
        private const string LobbyPath = "Assets/Scenes/SortieLoop/SortieLoop.unity";
        private const string RoomName = "ZoneOneGreybox";

        private Scene lobby;
        private SortieLoopHost host = null!;
        private Touchscreen screen = null!;
        private string directory = null!;
        private InputSettings.UpdateMode oldUpdate;
        private InputSettings.BackgroundBehavior oldBackground;
        private InputSettings.EditorInputBehaviorInPlayMode oldRouting;
        private WeaponInstance spear = null!;
        private double clock;
        private int nextTouchId;

        [UnitySetUp]
        public IEnumerator OpenActualLobby()
        {
            oldUpdate = InputSystem.settings.updateMode;
            oldBackground = InputSystem.settings.backgroundBehavior;
            oldRouting = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            screen = InputSystem.AddDevice<Touchscreen>();
            clock = 0;
            nextTouchId = 0;

            spear = WarehouseWeapon("wooden_spear");
            directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN133-" + Guid.NewGuid().ToString("N"));
            string profile = Path.Combine(directory, "snapshot.json");
            var snapshot = new SaveSnapshot(1, 0, new[] { spear, WarehouseWeapon("old_sword") },
                Array.Empty<CatalogSaveRecord>(), Array.Empty<SettlementSaveRecord>());
            Assert.That(new AtomicSnapshotStore(profile).Commit(0, snapshot).Status,
                Is.EqualTo(SnapshotCommitStatus.Committed));

            void Configure(Scene scene, LoadSceneMode mode)
            {
                if (scene.path != LobbyPath)
                    return;
                lobby = scene;
                host = lobby.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<SortieLoopHost>(true)).Single();
                host.Configure(profile, () => clock);
            }

            SceneManager.sceneLoaded += Configure;
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(LobbyPath,
                    new LoadSceneParameters(LoadSceneMode.Additive));
            }
            finally
            {
                SceneManager.sceneLoaded -= Configure;
            }
            yield return null;
            Assert.That(host.IsInLobby, Is.True);
        }

        [UnityTearDown]
        public IEnumerator CloseActualLobby()
        {
            Scene room = SceneManager.GetSceneByName(RoomName);
            if (room.IsValid() && room.isLoaded)
                yield return SceneManager.UnloadSceneAsync(room);
            if (lobby.IsValid() && lobby.isLoaded)
                yield return SceneManager.UnloadSceneAsync(lobby);
            if (screen != null && screen.added)
                InputSystem.RemoveDevice(screen);
            InputSystem.settings.updateMode = oldUpdate;
            InputSystem.settings.backgroundBehavior = oldBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = oldRouting;
            if (directory != null && Directory.Exists(directory))
            {
                foreach (string entry in Directory.EnumerateFiles(directory))
                    File.Delete(entry);
                Directory.Delete(directory);
            }
        }

        [UnityTest]
        public IEnumerator ActualDraggedSpearHitsTheBossAtZeroAndBothExactFollowUpTimes()
        {
            yield return StartEquippedSortie();
            BossShockwave boss = host.ActiveRoom!.Encounter.Boss.Boss!;
            clock = .100;
            yield return null;
            Assert.That(boss.Health, Is.EqualTo(90f), "The boss is beyond standard spear range.");
            yield return ReleaseSkillTowardBoss();
            Assert.That(boss.Health, Is.EqualTo(79f));
            Assert.That(ButtonNamed("Weapon Slot 2").interactable, Is.False);

            clock = .249;
            yield return null;
            Assert.That(boss.Health, Is.EqualTo(79f));
            clock = .250;
            yield return null;
            Assert.That(boss.Health, Is.EqualTo(68f));
            clock = .399;
            yield return null;
            Assert.That(boss.Health, Is.EqualTo(68f));
            clock = .400;
            yield return null;
            Assert.That(boss.Health, Is.EqualTo(57f));
            yield return null;
            Assert.That(ButtonNamed("Weapon Slot 2").interactable, Is.True);

            clock = 5.299;
            yield return null;
            Assert.That(ButtonNamed("Skill").interactable, Is.False);
            clock = 5.300;
            yield return null;
            Assert.That(ButtonNamed("Skill").interactable, Is.True);
            Assert.That(boss.Health, Is.EqualTo(57f));
        }

        [UnityTest]
        public IEnumerator LateFrameAppliesBothDueThrustsFromTheOriginalPosition()
        {
            yield return StartEquippedSortie();
            var room = host.ActiveRoom!;
            BossShockwave boss = room.Encounter.Boss.Boss!;
            clock = .100;
            yield return null;
            yield return ReleaseSkillTowardBoss();
            Assert.That(boss.Health, Is.EqualTo(79f));

            room.Player.position = new Vector2(4.45f, 6.5f);
            Physics2D.SyncTransforms();
            clock = .460;
            yield return null;
            Assert.That(boss.Health, Is.EqualTo(57f));
            yield return null;
            Assert.That(boss.Health, Is.EqualTo(57f), "The room must drain each scheduled thrust once.");
        }

        [UnityTest]
        public IEnumerator ExactDeadlineEndsTheActualSortieBeforeAnEqualTimeThrust()
        {
            yield return StartEquippedSortie();
            var room = host.ActiveRoom!;
            BossShockwave boss = room.Encounter.Boss.Boss!;
            double deadline = room.Run.CurrentRun!.Deadline.TotalSeconds;
            clock = deadline - .150;
            yield return null;
            yield return ReleaseSkillTowardBoss();
            Assert.That(boss.Health, Is.EqualTo(79f));
            clock = deadline;
            yield return null;
            Assert.That(room.Run.Result?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(boss.Health, Is.EqualTo(79f));
            Assert.That(room.Encounter.IsWeaponActionBusy, Is.False);
        }

        private IEnumerator StartEquippedSortie()
        {
            yield return Tap(ButtonNamed("Open Equipment"));
            yield return Tap(WarehouseButton("나무 창"));
            yield return Tap(WarehouseButton("낡은 검"));
            yield return Tap(ButtonNamed("Confirm"));
            double timeout = Time.realtimeSinceStartupAsDouble + 8;
            while ((!host.ActiveRoom || !host.ActiveHud) && Time.realtimeSinceStartupAsDouble < timeout)
                yield return null;

            Assert.That(host.ActiveRoom, Is.Not.Null);
            Assert.That(host.ActiveHud, Is.Not.Null);
            Assert.That(host.ActiveManifest!.Equipped[0].Id, Is.EqualTo(spear.Id));
            host.ActiveRoom!.Player.position = new Vector2(3.85f, 6.5f);
            Physics2D.SyncTransforms();
        }

        private IEnumerator ReleaseSkillTowardBoss()
        {
            Button skill = ButtonNamed("Skill");
            Assert.That(skill.interactable, Is.True);
            Canvas.ForceUpdateCanvases();
            Vector2 start = Center(skill);
            Vector2 release = start + Vector2.left * 48f * skill.GetComponentInParent<Canvas>().scaleFactor;
            int id = ++nextTouchId;
            yield return Touch(id, start, TouchPhase.Began);
            yield return Touch(id, release, TouchPhase.Moved);
            Assert.That(host.ActiveHud!.IsAiming, Is.True);
            yield return Touch(id, release, TouchPhase.Ended);
        }

        private Button ButtonNamed(string name)
            => lobby.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Button>())
                .Single(button => button.name == name);

        private Button WarehouseButton(string label)
            => lobby.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Button>())
                .Single(button => button.name == "WarehouseWeapon" && button.GetComponentInChildren<TMP_Text>().text == label);

        private IEnumerator Tap(Button button)
        {
            Assert.That(button.IsActive() && button.interactable, Is.True, button.name);
            Canvas.ForceUpdateCanvases();
            int id = ++nextTouchId;
            Vector2 point = Center(button);
            yield return Touch(id, point, TouchPhase.Began);
            yield return Touch(id, point, TouchPhase.Ended);
        }

        private IEnumerator Touch(int id, Vector2 point, TouchPhase phase)
        {
            InputSystem.QueueStateEvent(screen, new TouchState { touchId = id, position = point, phase = phase });
            InputSystem.Update();
            yield return null;
        }

        private static Vector2 Center(Button button)
        {
            var rect = (RectTransform)button.transform;
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        }

        private static WeaponInstance WarehouseWeapon(string id)
            => WeaponInstance.Create(new WeaponDefinitionId(id), WeaponProvenance.Personal, WeaponLocation.Warehouse());
    }
}
#endif
