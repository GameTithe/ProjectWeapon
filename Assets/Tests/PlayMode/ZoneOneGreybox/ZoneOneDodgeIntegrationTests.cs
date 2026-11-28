#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectWeapon.ZoneOneGreybox.Tests
{
    public sealed class ZoneOneDodgeIntegrationTests
    {
        private const string ScenePath = "Assets/Scenes/ZoneOneGreybox/ZoneOneGreybox.unity";
        private Scene scene;
        private ZoneOneGreybox room = null!;
        private RunState run = null!;
        private TimeSpan now;

        [UnityTearDown]
        public IEnumerator Unload()
        {
            SceneManager.sceneLoaded -= BindRoom;
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator ExternalDodgeAtCompletionCancelsEvenWhenNextRoomFrameIsLater()
        {
            yield return LoadRoom();

            now = TimeSpan.FromMilliseconds(100);
            room.Player.position = room.ExtractionCenter;
            Assert.That(room.TryStartExtraction(), Is.True);
            Assert.That(run.ActiveExtraction, Is.Not.Null);
            TimeSpan completesAt = run.ActiveExtraction!.CompletesAt;

            now = completesAt;
            Assert.That(room.Movement!.TryDodge(run, now), Is.True);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Extracting));
            now += TimeSpan.FromMilliseconds(20);
            yield return null;

            Assert.That(run.ActiveExtraction, Is.Null);
            Assert.That(run.Result, Is.Null);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Exploring));
        }

        [UnityTest]
        public IEnumerator SuspensionCanDrainTheExactDodgeSignalBeforeTheRoomFrame()
        {
            yield return LoadRoom();
            now = TimeSpan.FromMilliseconds(200);
            room.Player.position = room.ExtractionCenter;
            Assert.That(room.TryStartExtraction(), Is.True);
            now += TimeSpan.FromMilliseconds(100);
            Assert.That(room.Movement!.TryDodge(run, now), Is.True);

            RunSignal accepted = room.DrainAcceptedDodgeSignals().Single();
            Assert.That(accepted.EffectiveAt, Is.EqualTo(now));
            Assert.That(accepted.RunId, Is.EqualTo(run.CurrentRun!.Id));
            Assert.That(room.DrainAcceptedDodgeSignals(), Is.Empty);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Extracting));
        }

        private IEnumerator LoadRoom()
        {
            now = TimeSpan.Zero;
            SceneManager.sceneLoaded += BindRoom;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics2D));
            SceneManager.sceneLoaded -= BindRoom;
            Assert.That(room, Is.Not.Null);
        }

        private void BindRoom(Scene loaded, LoadSceneMode mode)
        {
            if (loaded.path != ScenePath)
                return;

            scene = loaded;
            room = scene.GetRootGameObjects().Select(root => root.GetComponent<ZoneOneGreybox>())
                .Single(candidate => candidate != null)!;
            ZoneCatalog catalog = AssetDatabase.LoadAssetAtPath<ZoneCatalog>(
                "Assets/ProjectWeapon/Dungeon/PrototypeZoneCatalog.asset");
            run = new RunState(() => now);
            var countdown = new ZoneRunCountdown(catalog, run);
            Assert.That(countdown.TryStartRun(RunId.New(), room.Zone.ZoneId), Is.True);
            room.BindRun(run, countdown, () => now, new InventoryService(Array.Empty<WeaponInstance>()), null);
        }
    }
}
#endif
