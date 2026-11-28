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
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectWeapon.ZoneOneGreybox.Tests
{
    public sealed class DaggerFlurryEncounterTests
    {
        private const string ScenePath = "Assets/Scenes/ZoneOneGreybox/ZoneOneGreybox.unity";
        private const string ZoneCatalogPath = "Assets/ProjectWeapon/Dungeon/PrototypeZoneCatalog.asset";
        private const string DaggerPath = "Assets/ProjectWeapon/Weapons/PrototypeStarter/Dagger.asset";
        private static readonly TimeSpan Duration = TimeSpan.FromSeconds(1);
        private Scene scene;
        private ZoneOneGreybox room = null!;
        private TimeSpan now;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = default;
            room = null!;
            now = TimeSpan.Zero;
            SceneManager.sceneLoaded += BindLoadedRoom;
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                    new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics2D));
            }
            finally
            {
                SceneManager.sceneLoaded -= BindLoadedRoom;
            }
            Assert.That(room, Is.Not.Null);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator EquippedDaggerDealsBothImpactsEvenWhenTheSecondFrameArrivesLate()
        {
            float health = EnterDaggerRange();
            yield return Sample(100);
            Assert.That(room.Encounter.Melee.Brain.CurrentHealth, Is.EqualTo(health - 5));

            yield return Sample(219);
            Assert.That(room.Encounter.Melee.Brain.CurrentHealth, Is.EqualTo(health - 5));
            yield return Sample(270);
            Assert.That(room.Encounter.Melee.Brain.CurrentHealth, Is.EqualTo(health - 10));
            yield return Sample(270);
            Assert.That(room.Encounter.Melee.Brain.CurrentHealth, Is.EqualTo(health - 10));

            yield return Sample(749);
            Assert.That(room.Encounter.Melee.Brain.CurrentHealth, Is.EqualTo(health - 10));
            yield return Sample(750);
            Assert.That(room.Encounter.Melee.Brain.CurrentHealth, Is.EqualTo(health - 15));
        }

        [UnityTest]
        public IEnumerator EnteringAndCancelingAnExtractionDoesNotResumeAFlurry()
        {
            float health = EnterDaggerRange();
            yield return Sample(100);
            Assert.That(room.Encounter.Melee.Brain.CurrentHealth, Is.EqualTo(health - 5));

            RunId id = room.Run.CurrentRun!.Id;
            now = Ms(150);
            ExtractionAttempt attempt = room.Run.TryBeginExtraction(id, isInRange: true)!;
            Assert.That(attempt, Is.Not.Null);
            yield return null;
            Assert.That(room.Run.Phase, Is.EqualTo(RunPhase.Extracting));

            now = Ms(160);
            room.Run.ProcessSignals(new[] { RunSignal.CancelExtraction(id, attempt.Id, now) });
            yield return Sample(270);
            Assert.That(room.Run.Phase, Is.EqualTo(RunPhase.Exploring));
            Assert.That(room.Encounter.Melee.Brain.CurrentHealth, Is.EqualTo(health - 5));
        }

        [UnityTest]
        public IEnumerator ExactRunDeadlineSuppressesThePendingSecondImpact()
        {
            float health = EnterDaggerRange();
            yield return Sample(880);
            Assert.That(room.Encounter.Melee.Brain.CurrentHealth, Is.EqualTo(health - 5));

            yield return Sample(1000);
            Assert.That(room.Run.Result?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(room.Encounter.Melee.Brain.CurrentHealth, Is.EqualTo(health - 5));
            yield return Sample(1100);
            Assert.That(room.Encounter.Melee.Brain.CurrentHealth, Is.EqualTo(health - 5));
        }

        private void BindLoadedRoom(Scene loaded, LoadSceneMode _)
        {
            if (loaded.path != ScenePath)
                return;

            scene = loaded;
            room = scene.GetRootGameObjects().Select(root => root.GetComponent<ZoneOneGreybox>())
                .Single(component => component != null)!;
            var catalog = AssetDatabase.LoadAssetAtPath<ZoneCatalog>(ZoneCatalogPath);
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(DaggerPath);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(weapon, Is.Not.Null);
            var equipped = WeaponInstance.Create(weapon.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            TimeSpan Clock() => now;
            var run = new RunState(Clock);
            Assert.That(run.TryStartRun(RunId.New(), room.Zone.ZoneId, Duration), Is.True);
            var field = new InventoryService(new[] { equipped });
            room.BindRun(run, new ZoneRunCountdown(catalog, run), Clock, field, weapon);
        }

        private float EnterDaggerRange()
        {
            room.Player.position = new Vector2(3.5f, 5.5f);
            return room.Encounter.Melee.Brain.CurrentHealth;
        }

        private IEnumerator Sample(int milliseconds)
        {
            now = Ms(milliseconds);
            yield return null;
        }

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
#endif
