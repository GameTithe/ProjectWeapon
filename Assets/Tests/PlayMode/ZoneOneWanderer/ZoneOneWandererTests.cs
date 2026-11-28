#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Art.Wanderer;
using ProjectWeapon.Inventory;
using ProjectWeapon.Save;
using ProjectWeapon.SortieLoop;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ProjectWeapon.ZoneOneGreybox.Tests
{
    public sealed class ZoneOneWandererTests
    {
        private const string LobbyPath = "Assets/Scenes/SortieLoop/SortieLoop.unity";
        private const string PosesPath = "Assets/ProjectWeapon/Art/Wanderer/WandererLocomotion.asset";
        private InputSettings.UpdateMode updateMode;
        private InputSettings.BackgroundBehavior backgroundBehavior;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        private Scene lobby;
        private Keyboard keyboard = null!;
        private Touchscreen screen = null!;
        private SortieLoopHost host = null!;
        private string directory = null!;
        private double clock;

        [UnitySetUp]
        public IEnumerator OpenLobby()
        {
            updateMode = InputSystem.settings.updateMode;
            backgroundBehavior = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
            screen = InputSystem.AddDevice<Touchscreen>();
            clock = 0;
            directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN198-" + Guid.NewGuid().ToString("N"));
            string profile = Path.Combine(directory, "snapshot.json");
            var empty = new SaveSnapshot(1, 0, Array.Empty<WeaponInstance>(),
                Array.Empty<CatalogSaveRecord>(), Array.Empty<SettlementSaveRecord>());
            Assert.That(new AtomicSnapshotStore(profile).Commit(0, empty).Status, Is.EqualTo(SnapshotCommitStatus.Committed));

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
            if (keyboard != null && keyboard.added)
                InputSystem.RemoveDevice(keyboard);
            if (screen != null && screen.added)
                InputSystem.RemoveDevice(screen);
            InputSystem.settings.updateMode = updateMode;
            InputSystem.settings.backgroundBehavior = backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            if (directory != null && Directory.Exists(directory))
            {
                foreach (string entry in Directory.EnumerateFiles(directory))
                    File.Delete(entry);
                Directory.Delete(directory);
            }
        }

        [UnityTest]
        public IEnumerator ActualSortieDisplaysWandererForIdleMovementAndDodge()
        {
            yield return Tap(1, "Open Equipment");
            yield return Tap(2, "Confirm");
            yield return WaitUntil(() => host.ActiveRoom && host.ActiveHud, "The combat room did not load.");

            var room = host.ActiveRoom!;
            var movement = room.Movement!;
            var body = room.Player;
            var view = body.GetComponentInChildren<WandererLocomotionView>();
            var renderer = view.GetComponent<SpriteRenderer>();
            var poses = AssetDatabase.LoadAssetAtPath<WandererLocomotionSet>(PosesPath);
            Assert.That(body.GetComponent<SpriteRenderer>().enabled, Is.False, "The placeholder must be hidden.");
            Assert.That(body.GetComponent<CircleCollider2D>().enabled && body.simulated, Is.True);
            Assert.That(renderer.enabled, Is.True);
            AssertPose(WandererMotion.Idle, WandererFacing.South);

            float initialX = body.position.x;
            SendKeys(Key.D);
            yield return WaitUntil(() => body.position.x > initialX + .05f, "The actual player did not move east.");
            AssertPose(WandererMotion.Walk, WandererFacing.East);

            SendKeys();
            yield return WaitUntil(() => view.Motion == WandererMotion.Idle, "The visual did not return to idle.");
            AssertPose(WandererMotion.Idle, WandererFacing.East);

            clock = 1;
            yield return Touch(3, "Dodge", TouchPhase.Began);
            yield return WaitUntil(() => view.Motion == WandererMotion.Dodge, "The accepted dodge pose did not appear.");
            Assert.That(movement.IsDodging, Is.True);
            AssertPose(WandererMotion.Dodge, WandererFacing.East);
            yield return Touch(3, "Dodge", TouchPhase.Ended);
            yield return WaitUntil(() => !movement.IsDodging && view.Motion == WandererMotion.Idle,
                "The real dodge and its visual did not settle.");
            AssertPose(WandererMotion.Idle, WandererFacing.East);

            void AssertPose(WandererMotion motion, WandererFacing facing)
            {
                Assert.That(view.Motion, Is.EqualTo(motion));
                Assert.That(view.Facing, Is.EqualTo(facing));
                int frames = WandererLocomotionSet.FramesPerDirection(motion);
                Assert.That(Enumerable.Range(0, frames).Select(frame => poses.GetFrame(motion, facing, frame)),
                    Does.Contain(renderer.sprite));
            }
        }

        private void SendKeys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }

        private IEnumerator Tap(int id, string buttonName)
        {
            yield return Touch(id, buttonName, TouchPhase.Began);
            yield return Touch(id, buttonName, TouchPhase.Ended);
        }

        private IEnumerator Touch(int id, string buttonName, TouchPhase phase)
        {
            Button button = lobby.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Button>())
                .Single(candidate => candidate.name == buttonName);
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)button.transform;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            InputSystem.QueueStateEvent(screen, new TouchState { touchId = id, position = point, phase = phase });
            InputSystem.Update();
            yield return null;
        }

        private static IEnumerator WaitUntil(Func<bool> predicate, string failure)
        {
            float end = Time.realtimeSinceStartup + 8;
            while (!predicate() && Time.realtimeSinceStartup < end)
                yield return null;
            Assert.That(predicate(), Is.True, failure);
        }
    }
}
#endif
