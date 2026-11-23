#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ProjectWeapon.Movement.Tests
{
    public sealed class VirtualJoystickPlayModeTests
    {
        private const string Feature = "Assets/ProjectWeapon/Movement/";

        private Scene scene;
        private Canvas canvas;
        private EventSystem events;
        private Touchscreen touchscreen;
        private Keyboard keyboard;
        private VirtualJoystick stick;
        private Rigidbody2D body;
        private PlayerMovement movement;
        private Button unrelatedButton;
        private InputActionReference point;
        private InputActionReference click;
        private InputSettings.UpdateMode updateMode;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        private int buttonClicks;

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
            touchscreen = InputSystem.AddDevice<Touchscreen>();
            keyboard = InputSystem.AddDevice<Keyboard>();
            buttonClicks = 0;
            scene = SceneManager.CreateScene($"Joystick-{Guid.NewGuid():N}",
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            GameObject eventRoot = NewRoot("EventSystem");
            eventRoot.SetActive(false);
            events = eventRoot.AddComponent<EventSystem>();
            var inputModule = eventRoot.AddComponent<InputSystemUIInputModule>();
            InputActionAsset asset = InputSystem.actions;
            point = InputActionReference.Create(asset.FindAction("UI/Point", throwIfNotFound: true));
            click = InputActionReference.Create(asset.FindAction("UI/Click", throwIfNotFound: true));
            inputModule.actionsAsset = asset;
            inputModule.pointerBehavior = UIPointerBehavior.SingleMouseOrPenButMultiTouchAndTrack;
            inputModule.point = point;
            inputModule.leftClick = click;
            eventRoot.SetActive(true);
            EventSystem.current = events;

            canvas = NewRoot("Canvas").AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            stick = Object.Instantiate(LoadPrefab("VirtualJoystick.prefab"), canvas.transform)
                .GetComponent<VirtualJoystick>();
            var button = new GameObject("Other UI", typeof(RectTransform), typeof(Image), typeof(Button));
            button.transform.SetParent(canvas.transform, false);
            var buttonRect = (RectTransform)button.transform;
            buttonRect.anchorMin = Vector2.one;
            buttonRect.anchorMax = Vector2.one;
            buttonRect.anchoredPosition = new Vector2(-80f, -50f);
            buttonRect.sizeDelta = new Vector2(100f, 60f);
            unrelatedButton = button.GetComponent<Button>();
            unrelatedButton.onClick.AddListener(() => buttonClicks++);

            GameObject player = Object.Instantiate(LoadPrefab("PrototypeMovementPlayer.prefab"));
            SceneManager.MoveGameObjectToScene(player, scene);
            body = player.GetComponent<Rigidbody2D>();
            movement = player.GetComponent<PlayerMovement>();
            movement.BindJoystick(stick);
            InputSystem.Update();
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return null;
            Assert.That(events.currentInputModule, Is.SameAs(inputModule));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (scene.IsValid())
            {
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            Object.DestroyImmediate(point);
            Object.DestroyImmediate(click);
            if (touchscreen != null && touchscreen.added)
            {
                InputSystem.RemoveDevice(touchscreen);
            }
            if (keyboard != null && keyboard.added)
            {
                InputSystem.RemoveDevice(keyboard);
            }

            InputSystem.settings.updateMode = updateMode;
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
        }

        [UnityTest]
        public IEnumerator OtherUiCannotMovePlayerOrStealOrReleaseTheOwningFinger()
        {
            Vector2 other = ScreenPoint((RectTransform)unrelatedButton.transform);
            Vector2 center = ScreenPoint((RectTransform)stick.transform);
            SendTouch(1, other, TouchPhase.Began);
            yield return null;
            Assert.That(stick.IsHeld, Is.False);
            yield return new WaitForFixedUpdate();
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));

            SendTouch(2, center + Vector2.right * 55f, TouchPhase.Began);
            yield return null;
            Assert.That(stick.Direction.x, Is.GreaterThan(0.5f));
            SendTouch(1, center - Vector2.right * 45f, TouchPhase.Moved);
            yield return null;
            Assert.That(stick.Direction.x, Is.GreaterThan(0.5f));
            SendTouch(1, center - Vector2.right * 45f, TouchPhase.Ended);
            yield return null;
            Assert.That(stick.IsHeld, Is.True);

            SendTouch(3, center - Vector2.right * 45f, TouchPhase.Began);
            yield return null;
            Assert.That(stick.Direction.x, Is.GreaterThan(0.5f));
            SendTouch(2, center + Vector2.right * 55f, TouchPhase.Ended);
            yield return null;
            SendTouch(3, center - Vector2.right * 55f, TouchPhase.Moved);
            yield return null;
            Assert.That(stick.IsHeld, Is.False);
            Assert.That(stick.Direction, Is.EqualTo(Vector2.zero));
            SendTouch(3, center, TouchPhase.Ended);
            yield return null;

            SendTouch(4, other, TouchPhase.Began);
            yield return null;
            SendTouch(4, other, TouchPhase.Ended);
            yield return null;
            Assert.That(buttonClicks, Is.EqualTo(1));
            Assert.That(stick.IsHeld, Is.False);
        }

        [UnityTest]
        public IEnumerator RadialDeadzoneDisplacementAndReleaseUseTheRealPhysicsBody()
        {
            Vector2 center = ScreenPoint((RectTransform)stick.transform);
            Vector2 start = body.position;
            SendTouch(1, center + Vector2.right * 5f, TouchPhase.Began);
            yield return null;
            yield return new WaitForFixedUpdate();
            Assert.That(stick.IsHeld, Is.True);
            Assert.That(movement.Direction, Is.EqualTo(Vector2.zero));
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));

            Vector2 outside = center + Vector2.right * 120f;
            SendTouch(1, outside, TouchPhase.Moved);
            yield return null;
            yield return new WaitForFixedUpdate();
            Assert.That(body.linearVelocity.x, Is.EqualTo(4f).Within(0.001f));
            PhysicsScene2D physics = scene.GetPhysicsScene2D();
            Assert.That(physics.Simulate(Time.fixedDeltaTime), Is.True);
            Assert.That(body.position.x, Is.GreaterThan(start.x));

            SendTouch(1, outside, TouchPhase.Ended);
            yield return null;
            yield return new WaitForFixedUpdate();
            Vector2 stopped = body.position;
            Assert.That(stick.IsHeld, Is.False);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(physics.Simulate(Time.fixedDeltaTime), Is.True);
            Assert.That(body.position, Is.EqualTo(stopped));
        }

        [UnityTest]
        public IEnumerator TouchCancelStopsAndDesktopFallbackStillUsesTheSharedMoveAction()
        {
            Vector2 center = ScreenPoint((RectTransform)stick.transform);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            InputSystem.Update();
            yield return new WaitForFixedUpdate();
            Assert.That(body.linearVelocity.x, Is.EqualTo(4f).Within(0.001f));

            SendTouch(1, center, TouchPhase.Began);
            yield return null;
            yield return new WaitForFixedUpdate();
            Assert.That(stick.IsHeld, Is.True);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));

            SendTouch(1, center - Vector2.right * 45f, TouchPhase.Moved);
            yield return null;
            yield return new WaitForFixedUpdate();
            Assert.That(body.linearVelocity.x, Is.LessThan(0f));

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            SendTouch(1, center, TouchPhase.Canceled);
            yield return null;
            yield return new WaitForFixedUpdate();
            Assert.That(stick.IsHeld, Is.False);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));

            SendTouch(2, center + Vector2.up * 45f, TouchPhase.Began);
            yield return null;
            Assert.That(stick.IsHeld, Is.True);
            stick.gameObject.SetActive(false);
            yield return new WaitForFixedUpdate();
            Assert.That(stick.IsHeld, Is.False);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
        }

        private GameObject NewRoot(string name)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        private static GameObject LoadPrefab(string fileName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Feature + fileName);
            Assert.That(prefab, Is.Not.Null, fileName);
            return prefab;
        }

        private static Vector2 ScreenPoint(RectTransform target) =>
            RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(target.rect.center));

        private void SendTouch(int id, Vector2 position, TouchPhase phase)
        {
            InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId = id, position = position, phase = phase });
            InputSystem.Update();
        }
    }
}
#endif
