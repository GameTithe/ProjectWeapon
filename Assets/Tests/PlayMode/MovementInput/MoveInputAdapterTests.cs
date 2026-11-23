using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ProjectWeapon.MovementInput.Tests
{
    public sealed class MoveInputAdapterTests
    {
        private GameObject player;
        private InputActionAsset asset;
        private InputActionReference reference;
        private Keyboard keyboard;
        private MoveInputAdapter adapter;
        private InputAction move;
        private InputSettings.UpdateMode originalUpdateMode;
        private InputSettings.BackgroundBehavior originalBackgroundBehavior;
        private InputSettings.EditorInputBehaviorInPlayMode originalEditorBehavior;

        [SetUp]
        public void SetUp()
        {
            originalUpdateMode = InputSystem.settings.updateMode;
            originalBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            originalEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            keyboard = InputSystem.AddDevice<Keyboard>();
            InputActionMap map = new InputActionMap("Player");
            move = map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.AddActionMap(map);
            reference = InputActionReference.Create(move);

            player = new GameObject("Player");
            player.SetActive(false);
            adapter = player.AddComponent<MoveInputAdapter>();
            AssignActionReference(reference);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(reference);
            Object.DestroyImmediate(asset);
            if (keyboard.added)
            {
                InputSystem.RemoveDevice(keyboard);
            }

            InputSystem.settings.updateMode = originalUpdateMode;
            InputSystem.settings.backgroundBehavior = originalBackgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = originalEditorBehavior;
        }

        [Test]
        public void ReadsMovementAndReturnsToNeutralOnRelease()
        {
            AddKeyboardBinding("2DVector");
            move.Enable();
            player.SetActive(true);
            Assert.That(adapter.Direction, Is.EqualTo(Vector2.zero));

            SendKeys(Key.F6, Key.F7);
            Assert.That(adapter.Direction.x, Is.EqualTo(Mathf.Sqrt(0.5f)).Within(0.0001f));
            Assert.That(adapter.Direction.y, Is.EqualTo(Mathf.Sqrt(0.5f)).Within(0.0001f));

            SendKeys();
            Assert.That(adapter.Direction, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void ClampsLargeVectorsAndPreservesSmallVectors()
        {
            AddKeyboardBinding("2DVector(mode=1)");
            move.AddCompositeBinding("2DVector", processors: "scaleVector2(x=0.5,y=0.5)")
                .With("Up", "<Keyboard>/f8");
            move.Enable();
            player.SetActive(true);

            SendKeys(Key.F6, Key.F7);
            Assert.That(adapter.Direction.magnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(adapter.Direction.x, Is.EqualTo(adapter.Direction.y).Within(0.0001f));

            SendKeys(Key.F8);
            Assert.That(adapter.Direction, Is.EqualTo(Vector2.up * 0.5f));
        }

        [Test]
        public void DisablingAdapterLeavesTheSharedSourceEnabled()
        {
            AddKeyboardBinding("2DVector");
            move.Enable();
            player.SetActive(true);
            SendKeys(Key.F7);

            adapter.enabled = false;
            Assert.That(adapter.Direction, Is.EqualTo(Vector2.zero));
            Assert.That(move.enabled, Is.True);
            Assert.That(move.ReadValue<Vector2>(), Is.EqualTo(Vector2.right));

            SendKeys();
            adapter.enabled = true;
            SendKeys(Key.F6);
            Assert.That(adapter.Direction, Is.EqualTo(Vector2.up));
        }

        [Test]
        public void RemovingTheActiveDeviceReturnsToNeutral()
        {
            AddKeyboardBinding("2DVector");
            move.Enable();
            player.SetActive(true);
            SendKeys(Key.F7);
            Assert.That(adapter.Direction, Is.EqualTo(Vector2.right));

            InputSystem.RemoveDevice(keyboard);
            InputSystem.Update();
            Assert.That(adapter.Direction, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void DisabledInputActionReturnsNeutral()
        {
            AddKeyboardBinding("2DVector");
            move.Enable();
            player.SetActive(true);
            SendKeys(Key.F7);
            Assert.That(adapter.Direction, Is.EqualTo(Vector2.right));

            move.Disable();
            Assert.That(adapter.Direction, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void ReadsTheProjectWidePlayerMoveAction()
        {
            InputAction action = InputSystem.actions.FindAction("Player/Move", throwIfNotFound: true);
            Assert.That(action.enabled, Is.True);
            Object.DestroyImmediate(reference);
            reference = InputActionReference.Create(action);
            AssignActionReference(reference);
            player.SetActive(true);

            SendKeys(Key.D);
            Assert.That(adapter.Direction, Is.EqualTo(Vector2.right));
            SendKeys();
            Assert.That(adapter.Direction, Is.EqualTo(Vector2.zero));
        }

        private void AddKeyboardBinding(string composite)
        {
            move.AddCompositeBinding(composite)
                .With("Up", "<Keyboard>/f6")
                .With("Right", "<Keyboard>/f7");
        }

        private void SendKeys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
            Assert.That(InputState.currentUpdateType, Is.EqualTo(InputUpdateType.Manual));
            foreach (Key key in keys)
            {
                Assert.That(keyboard[key].isPressed, Is.True, $"Virtual keyboard did not record {key}.");
            }
        }

        private void AssignActionReference(InputActionReference source)
        {
            typeof(MoveInputAdapter).GetField("moveAction", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(adapter, source);
        }
    }
}
