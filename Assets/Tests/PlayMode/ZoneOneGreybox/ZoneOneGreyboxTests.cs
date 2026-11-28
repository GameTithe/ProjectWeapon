#if UNITY_EDITOR
#nullable enable

using System.Collections;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Run;
using ProjectWeapon.Inventory;
using ProjectWeapon.Loot;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace ProjectWeapon.ZoneOneGreybox.Tests
{
    public sealed class ZoneOneGreyboxTests
    {
        private const string ScenePath = "Assets/Scenes/ZoneOneGreybox/ZoneOneGreybox.unity";
        private Scene scene;
        private PhysicsScene2D physics;
        private ZoneOneGreybox preview = null!;
        private Keyboard keyboard = null!;
        private InputSettings.UpdateMode updateMode;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            updateMode = InputSystem.settings.updateMode;
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics2D));
            scene = SceneManager.GetSceneByPath(ScenePath);
            Assert.That(scene.isLoaded, Is.True);
            physics = scene.GetPhysicsScene2D();
            preview = scene.GetRootGameObjects().Select(root => root.GetComponent<ZoneOneGreybox>())
                .Single(component => component != null)!;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);

            if (keyboard != null && keyboard.added)
                InputSystem.RemoveDevice(keyboard);

            InputSystem.settings.updateMode = updateMode;
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
        }

        [UnityTest]
        public IEnumerator PlayerMovesFromStartAroundBlockingWallToTheReturnPoint()
        {
            Assert.That(preview.Zone.RecommendedGrade, Is.EqualTo(1));
            Assert.That(preview.Zone.MinimumRewardGrade, Is.EqualTo(1));
            Assert.That(preview.Zone.MaximumRewardGrade, Is.EqualTo(2));
            Assert.That(preview.Zone.TimeLimitSeconds, Is.EqualTo(480));
            Assert.That(preview.Player.position, Is.EqualTo(new Vector2(3.5f, 2.5f)));
            Assert.That(preview.CanStartExtraction, Is.False);

            yield return Drive(Key.D, 50);
            Vector2 blocked = preview.Player.position;
            Assert.That(blocked.x, Is.InRange(5.50f, 5.75f));
            Assert.That(blocked.y, Is.EqualTo(2.5f).Within(0.05f));
            var wall = physics.OverlapPoint(new Vector2(6.5f, 2.5f));
            Assert.That(wall, Is.TypeOf<TilemapCollider2D>());
            Assert.That(preview.TryStartExtraction(), Is.False);

            yield return Drive(Key.S, 12);
            yield return Drive(Key.D, 48);
            yield return Drive(Key.W, 37);

            Assert.That(Vector2.Distance(preview.Player.position, preview.ExtractionCenter), Is.LessThan(0.25f));
            Assert.That(preview.CanStartExtraction, Is.True);
            SendKeyboard(Key.E);
            yield return null;
            Assert.That(preview.Phase, Is.EqualTo(RunPhase.Extracting));
            SendKeyboard();
        }

        [UnityTest]
        public IEnumerator ExactReturnRadiusIsAcceptedAndTheOutsidePointCannotStart()
        {
            Vector2 center = preview.ExtractionCenter;
            preview.Player.position = center + Vector2.right * (preview.ExtractionRadius + 0.01f);
            Assert.That(preview.CanStartExtraction, Is.False);
            Assert.That(preview.TryStartExtraction(), Is.False);
            Assert.That(preview.Phase, Is.EqualTo(RunPhase.Exploring));

            preview.Player.position = center + Vector2.right * preview.ExtractionRadius;
            Assert.That(preview.CanStartExtraction, Is.True);
            Assert.That(preview.TryStartExtraction(), Is.True);
            Assert.That(preview.Phase, Is.EqualTo(RunPhase.Extracting));
            Assert.That(preview.TryStartExtraction(), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DesignatedEncounterSupportsCombatPickupAndSuccessfulReturn()
        {
            ZoneOneEncounter encounter = preview.Encounter;
            Assert.That(encounter, Is.Not.Null);
            Assert.That(encounter.Melee.transform.position, Is.EqualTo(new Vector3(4.5f, 5.5f, 0f)));
            Assert.That(encounter.Ranged.transform.position, Is.EqualTo(new Vector3(11.5f, 6.4f, 0f)));
            Assert.That(encounter.Chest.transform.position, Is.EqualTo(new Vector3(10.5f, 2.5f, 0f)));
            Assert.That(preview.Zone.GetRewardProbabilityBasisPoints(1), Is.EqualTo(8500));
            Assert.That(preview.Zone.GetRewardProbabilityBasisPoints(2), Is.EqualTo(1500));
            Assert.That(encounter.TryOpenChest(), Is.False);
            Assert.That(encounter.Collected, Is.Empty);

            preview.Player.position = new Vector2(3.5f, 5.5f);
            var meleeWarning = encounter.Melee.GetComponentInChildren<LineRenderer>(true);
            bool sawMeleeWarning = false;
            float by = Time.time + 3f;
            while (encounter.Melee.Brain.CurrentHealth > 0 && Time.time < by)
            {
                yield return null;
                sawMeleeWarning |= meleeWarning.enabled;
            }
            Assert.That(sawMeleeWarning, Is.True);
            Assert.That(encounter.Melee.Brain.CurrentHealth, Is.Zero);

            preview.Player.position = new Vector2(9.4f, 6.4f);
            var rangedWarning = encounter.Ranged.GetComponentInChildren<LineRenderer>(true);
            by = Time.time + 1.5f;
            while (!rangedWarning.enabled && Time.time < by)
                yield return null;
            Assert.That(rangedWarning.enabled, Is.True);
            Assert.That(rangedWarning.positionCount, Is.EqualTo(4));
            int previousHealth = encounter.PlayerHealth;
            by = Time.time + 1.5f;
            while (encounter.PlayerHealth == previousHealth && Time.time < by)
                yield return null;
            Assert.That(encounter.PlayerHealth, Is.LessThan(previousHealth));

            preview.Player.position = new Vector2(10.55f, 6.4f);
            by = Time.time + 3f;
            while (encounter.Ranged.Brain.CurrentHealth > 0 && Time.time < by)
                yield return null;
            Assert.That(encounter.Ranged.Brain.CurrentHealth, Is.Zero);
            Assert.That(encounter.PlayerHealth, Is.GreaterThan(0));

            preview.Player.position = new Vector2(10.5f, 2.5f);
            SendKeyboard(Key.E);
            yield return null;
            SendKeyboard();
            Assert.That(encounter.Chest.IsOpen, Is.True);
            Assert.That(encounter.TryOpenChest(), Is.False);

            var drop = scene.GetRootGameObjects().Select(root => root.GetComponent<WorldLoot>())
                .Single(item => item != null)!;
            preview.Player.position = drop.transform.position;
            for (int ticks = 0; ticks < 4 && encounter.Collected.Count == 0; ticks++)
            {
                yield return new WaitForFixedUpdate();
                Assert.That(physics.Simulate(Time.fixedDeltaTime), Is.True);
            }
            Assert.That(encounter.Collected.Single().DefinitionId.Value, Is.EqualTo("wooden_spear"));
            Assert.That(encounter.Collected.Single().Id, Is.EqualTo(drop.Item!.Id));
            Assert.That(encounter.Collected.Single().Location.Kind, Is.EqualTo(WeaponLocationKind.Carried));
            Assert.That(drop.gameObject.activeSelf, Is.False);

            preview.Player.position = preview.ExtractionCenter;
            SendKeyboard(Key.E);
            yield return null;
            SendKeyboard();
            Assert.That(preview.Phase, Is.EqualTo(RunPhase.Extracting));
            by = Time.time + 4f;
            while (preview.Phase == RunPhase.Extracting && Time.time < by)
                yield return null;
            Assert.That(preview.Phase, Is.EqualTo(RunPhase.Succeeded));
        }

        [UnityTest]
        public IEnumerator RangedHitCancelsAnInProgressReturnBeforeThePlayerCanOpenTheChestAgain()
        {
            ZoneOneEncounter encounter = preview.Encounter;
            preview.Player.position = preview.ExtractionCenter;
            var warning = encounter.Ranged.GetComponentInChildren<LineRenderer>(true);
            float by = Time.time + 1.5f;
            while (!warning.enabled && Time.time < by)
                yield return null;
            Assert.That(warning.enabled, Is.True);
            Assert.That(preview.TryStartExtraction(), Is.True);
            Assert.That(preview.Phase, Is.EqualTo(RunPhase.Extracting));
            int hp = encounter.PlayerHealth;
            by = Time.time + 1.5f;
            while (encounter.PlayerHealth == hp && Time.time < by)
                yield return null;
            Assert.That(encounter.PlayerHealth, Is.LessThan(hp));
            Assert.That(preview.Phase, Is.EqualTo(RunPhase.Exploring));
            Assert.That(encounter.TryOpenChest(), Is.False);
        }

        private IEnumerator Drive(Key key, int steps)
        {
            SendKeyboard(key);
            for (int step = 0; step < steps; step++)
            {
                yield return new WaitForFixedUpdate();
                Assert.That(physics.Simulate(Time.fixedDeltaTime), Is.True);
            }
            SendKeyboard();
        }

        private void SendKeyboard(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }
    }
}
#endif
