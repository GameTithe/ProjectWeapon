#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Save;
using ProjectWeapon.SortieLoop;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ProjectWeapon.MaceSkill.Tests
{
    public sealed class MaceCombatHudTests
    {
        private const string LobbyPath = "Assets/Scenes/SortieLoop/SortieLoop.unity";
        private const string DefinitionPath = "Assets/ProjectWeapon/Weapons/PrototypeStarter/Mace.asset";
        private Scene lobby;
        private SortieLoopHost host = null!;
        private Touchscreen screen = null!;
        private string directory = null!;
        private InputSettings.UpdateMode oldUpdate;
        private InputSettings.BackgroundBehavior oldBackground;
        private InputSettings.EditorInputBehaviorInPlayMode oldRouting;
        private WeaponInstance mace = null!;
        private double clock;

        [UnitySetUp]
        public IEnumerator OpenLobby()
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

            WeaponDefinition definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(DefinitionPath);
            Assert.That(definition.Grade, Is.EqualTo(1));
            Assert.That(definition.Id.Value, Is.EqualTo("mace"));
            mace = WeaponInstance.Create(definition.Id, WeaponProvenance.Personal, WeaponLocation.Warehouse());
            directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN136-" + Guid.NewGuid().ToString("N"));
            string profile = Path.Combine(directory, "snapshot.json");
            var snapshot = new SaveSnapshot(1, 0, new[] { mace },
                Array.Empty<CatalogSaveRecord>(), Array.Empty<SettlementSaveRecord>());
            Assert.That(new AtomicSnapshotStore(profile).Commit(0, snapshot).Status, Is.EqualTo(SnapshotCommitStatus.Committed));

            void Configure(Scene loaded, LoadSceneMode _)
            {
                if (loaded.path != LobbyPath)
                    return;
                lobby = loaded;
                host = loaded.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SortieLoopHost>(true)).Single();
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
        public IEnumerator Clean()
        {
            Scene room = SceneManager.GetSceneByName("ZoneOneGreybox");
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
        public IEnumerator ActualSelectedMaceSelfTapDamagesTheBossOutsideBasicAttackRange()
        {
            yield return Tap(1, ButtonNamed("Open Equipment"));
            yield return Tap(2, ButtonNamed("WarehouseWeapon"));
            yield return Tap(3, ButtonNamed("Confirm"));
            double timeout = Time.realtimeSinceStartupAsDouble + 8;
            while ((!host.ActiveRoom || !host.ActiveHud) && Time.realtimeSinceStartupAsDouble < timeout)
                yield return null;

            Assert.That(host.ActiveRoom, Is.Not.Null);
            Assert.That(host.ActiveHud, Is.Not.Null);
            Assert.That(host.ActiveManifest!.Equipped.Single().Id, Is.EqualTo(mace.Id));
            var room = host.ActiveRoom!;
            var boss = room.Encounter.Boss.Boss!;
            room.Player.position = new Vector2(3.2f, 6.5f);
            Physics2D.SyncTransforms();
            clock = .10;
            yield return null;
            Assert.That(boss.Health, Is.EqualTo(90f), "The test must be beyond the actual mace basic attack range.");

            Button skill = ButtonNamed("Skill");
            Assert.That(skill.interactable, Is.True);
            yield return Tap(4, skill);
            Assert.That(boss.Health, Is.EqualTo(60f));
            Assert.That(skill.interactable, Is.False);

            Vector2 center = ScreenCenter(skill);
            yield return Touch(5, center, TouchPhase.Began);
            yield return Touch(5, center, TouchPhase.Ended);
            Assert.That(boss.Health, Is.EqualTo(60f));
        }

        private Button ButtonNamed(string name)
            => lobby.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Button>())
                .Single(button => button.name == name);

        private IEnumerator Tap(int id, Button button)
        {
            Assert.That(button.IsActive() && button.interactable, Is.True, button.name);
            Canvas.ForceUpdateCanvases();
            Vector2 center = ScreenCenter(button);
            yield return Touch(id, center, TouchPhase.Began);
            yield return Touch(id, center, TouchPhase.Ended);
        }

        private IEnumerator Touch(int id, Vector2 point, TouchPhase phase)
        {
            InputSystem.QueueStateEvent(screen, new TouchState { touchId = id, position = point, phase = phase });
            InputSystem.Update();
            yield return null;
        }

        private static Vector2 ScreenCenter(Button button)
        {
            var rect = (RectTransform)button.transform;
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        }
    }
}
#endif
