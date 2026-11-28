#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Enemies;
using ProjectWeapon.Inventory;
using ProjectWeapon.Loot;
using ProjectWeapon.Movement;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectWeapon.OneRoomDemo.Tests
{
    public sealed class OneRoomDemoTests
    {
        private const string ScenePath = "Assets/Scenes/OneRoomDemo/OneRoomDemo.unity";
        private Scene scene;
        private PhysicsScene2D physics;
        private Keyboard keyboard = null!;
        private OneRoomDemo demo = null!;
        private Rigidbody2D player = null!;
        private Transform canvas = null!;
        private EventSystem events = null!;
        private InputSettings.UpdateMode updateMode;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;

        [UnitySetUp]
        public IEnumerator Prepare()
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
            demo = Find<OneRoomDemo>();
            player = Find<PlayerMovement>().GetComponent<Rigidbody2D>();
            canvas = Find<Canvas>().transform;
            events = Find<EventSystem>();
            EventSystem.current = events;
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Dispose()
        {
            if (keyboard != null && keyboard.added)
                InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.updateMode = updateMode;
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator MoveReleaseAndFullBagRejectKeepTheWorldItemAndInventory()
        {
            yield return WalkTo(Key.S, Vector2.down, -2.65f);
            SetMovement();
            yield return SimulateStep();
            Vector2 stopped = player.position;
            yield return SimulateStep();
            Assert.That(player.position, Is.EqualTo(stopped));
            Assert.That(WorldSprite("Wanderer Visual").sprite.name, Does.StartWith("wanderer_idle_south"));

            yield return ApproachOverflow();
            WeaponInstance[] before = Carried();
            Assert.That(before.Select(x => x.DefinitionId.Value),
                Is.EquivalentTo(new[] { "dagger", "hunting_bow", "mace" }));
            Assert.That(Loot().Single().Item!.DefinitionId.Value, Is.EqualTo("wooden_spear"));
            Vector2 duringChoice = player.position;
            yield return SimulateStep();
            yield return SimulateStep();
            Assert.That(player.position, Is.EqualTo(duringChoice));
            SetMovement();
            Click("Cancel");
            yield return null;
            Assert.That(demo.IsChoiceOpen, Is.False);
            Assert.That(Carried().Select(x => x.Id), Is.EquivalentTo(before.Select(x => x.Id)));
            Assert.That(Loot().Single().Item!.DefinitionId.Value, Is.EqualTo("wooden_spear"));
        }

        [UnityTest]
        public IEnumerator ConfirmReplacesTheSelectedInstanceAndDropsTheDisplacedItem()
        {
            yield return WalkTo(Key.S, Vector2.down, -2.65f);
            yield return ApproachOverflow();
            SetMovement();
            WeaponInstance old = Carried().Single(x => x.Location.SlotIndex == 0);
            WeaponInstance incoming = Loot().Single().Item!;
            Canvas.ForceUpdateCanvases();
            var title = canvas.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Single(text => text.name == "Title");
            Assert.That(title.GetParsedText(), Is.EqualTo(title.text));
            Button("Candidate").onClick.Invoke();
            Assert.That(Carried().Any(x => x.Id == old.Id), Is.True);
            Click("Confirm");
            yield return null;
            Assert.That(demo.IsChoiceOpen, Is.False);
            Assert.That(Carried().Single(x => x.Location.SlotIndex == 0).Id, Is.EqualTo(incoming.Id));
            Assert.That(Loot().Single().Item!.Id, Is.EqualTo(old.Id));
        }

        [UnityTest]
        public IEnumerator GuardWarnsHitsAndDiesOnTheAuthoredSwordImpactFrame()
        {
            SetMovement(Key.D);
            bool warned = false;
            bool hitThePlayer = false;
            int strikes = 0;
            float previousHealth = demo.EnemyHealth;
            var cue = Find<LineRenderer>();
            var actor = WorldSprite("Wanderer Visual");
            var weapon = WorldSprite("Equipped Worn Sword");
            for (int step = 0; step < 480 && demo.EnemyPhase != MeleeEnemyPhase.Dead; step++)
            {
                yield return SimulateStep();
                if (!warned && cue.enabled && cue.positionCount > 2)
                {
                    warned = true;
                    SetMovement();
                }
                hitThePlayer |= demo.PlayerHealth < 100;
                if (demo.EnemyHealth < previousHealth)
                {
                    Assert.That(actor.sprite.name, Does.StartWith("wanderer_attack_").And.EndWith("_3"));
                    Assert.That(weapon.sprite.name, Is.EqualTo("WornSword"));
                    previousHealth = demo.EnemyHealth;
                    strikes++;
                }
            }

            SetMovement();
            Assert.That(warned, Is.True);
            Assert.That(hitThePlayer, Is.True);
            Assert.That(strikes, Is.EqualTo(3));
            Assert.That(demo.EnemyPhase, Is.EqualTo(MeleeEnemyPhase.Dead));
            Assert.That(cue.enabled, Is.False);
        }

        private IEnumerator ApproachOverflow()
        {
            SetMovement(Key.D);
            for (int step = 0; step < 160 && !demo.IsChoiceOpen; step++)
                yield return SimulateStep();
            Assert.That(demo.IsChoiceOpen, Is.True);
        }

        private IEnumerator WalkTo(Key key, Vector2 direction, float goal)
        {
            SetMovement(key);
            for (int step = 0; step < 100 && Vector2.Dot(player.position, direction) < goal; step++)
                yield return SimulateStep();
            Assert.That(Vector2.Dot(player.position, direction), Is.GreaterThanOrEqualTo(goal));
        }

        private IEnumerator SimulateStep()
        {
            yield return new WaitForFixedUpdate();
            Assert.That(physics.Simulate(Time.fixedDeltaTime), Is.True);
            yield return null;
        }

        private void SetMovement(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }

        private WeaponInstance[] Carried()
            => demo.Inventory.Snapshot().Where(x => x.Location.Kind == WeaponLocationKind.Carried).ToArray();

        private WorldLoot[] Loot() => demo.GetComponentsInChildren<WorldLoot>();

        private T Find<T>() where T : Component
            => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>()).Single();

        private SpriteRenderer WorldSprite(string objectName)
            => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SpriteRenderer>())
                .Single(sprite => sprite.name == objectName);

        private UnityEngine.UI.Button Button(string name)
            => canvas.GetComponentsInChildren<UnityEngine.UI.Button>().First(button => button.name == name);

        private void Click(string name)
        {
            Canvas.ForceUpdateCanvases();
            UnityEngine.UI.Button button = Button(name);
            var transform = (RectTransform)button.transform;
            var pointer = new PointerEventData(events)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, transform.TransformPoint(transform.rect.center))
            };
            var hits = new List<RaycastResult>();
            events.RaycastAll(pointer, hits);
            Assert.That(hits[0].gameObject, Is.EqualTo(button.gameObject));
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
    }
}
#endif
