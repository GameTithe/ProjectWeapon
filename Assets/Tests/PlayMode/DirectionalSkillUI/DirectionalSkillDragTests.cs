#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Inventory;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.Movement;
using ProjectWeapon.Run;
using ProjectWeapon.WeaponSwitch;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ProjectWeapon.ManualSkillUI.Tests
{
    public sealed class DirectionalSkillDragTests
    {
        private Scene scene;
        private Canvas canvas = null!;
        private EventSystem events = null!;
        private EventSystem? previousEvents;
        private Touchscreen touchscreen = null!;
        private InputActionReference point = null!;
        private InputActionReference click = null!;
        private DirectionalSkillDrag drag = null!;
        private DirectionalSkillArrow arrow = null!;
        private VirtualJoystick stick = null!;
        private Rigidbody2D body = null!;
        private EquippedWeaponSelection selection = null!;
        private ManualSkillExecutor executor = null!;
        private WeaponInstance first = null!;
        private WeaponInstance second = null!;
        private ManualSkillPattern pattern = null!;
        private WeaponDefinition definition = null!;
        private WeaponCatalog catalog = null!;
        private RectTransform aimArea = null!;
        private readonly RecordedEffect effect = new RecordedEffect();
        private readonly List<(Guid Key, ManualSkillActivationStatus Status)> outcomes = new();
        private InputSettings.UpdateMode updateMode;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        private TimeSpan now;
        private int extraClicks;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            now = TimeSpan.Zero;
            extraClicks = 0;
            effect.Requests.Clear();
            outcomes.Clear();
            previousEvents = EventSystem.current;
            updateMode = InputSystem.settings.updateMode;
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            touchscreen = InputSystem.AddDevice<Touchscreen>();

            scene = SceneManager.CreateScene($"DirectionalSkill-{Guid.NewGuid():N}", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            GameObject eventRoot = NewRoot("EventSystem");
            eventRoot.SetActive(false);
            events = eventRoot.AddComponent<EventSystem>();
            var module = eventRoot.AddComponent<InputSystemUIInputModule>();
            InputActionAsset actions = InputSystem.actions;
            point = InputActionReference.Create(actions.FindAction("UI/Point", throwIfNotFound: true));
            click = InputActionReference.Create(actions.FindAction("UI/Click", throwIfNotFound: true));
            module.actionsAsset = actions;
            module.pointerBehavior = UIPointerBehavior.SingleMouseOrPenButMultiTouchAndTrack;
            module.point = point;
            module.leftClick = click;
            eventRoot.SetActive(true);
            EventSystem.current = events;

            canvas = NewRoot("Drag Canvas").AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var scaler = canvas.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Screen.width * .5f, Screen.height * .5f);
            aimArea = InCanvas("Right Aim Area", canvas.transform);
            aimArea.anchorMin = new Vector2(.55f, 0f);
            aimArea.anchorMax = Vector2.one;
            aimArea.offsetMin = aimArea.offsetMax = Vector2.zero;
            RectTransform button = InCanvas("Skill Touch Surface", aimArea);
            button.anchorMin = button.anchorMax = new Vector2(.55f, .40f);
            button.anchoredPosition = Vector2.zero;
            button.sizeDelta = new Vector2(52f, 52f);
            var surface = button.gameObject.AddComponent<UnityEngine.UI.Image>();
            surface.color = new Color32(62, 48, 47, 255);
            var uiButton = button.gameObject.AddComponent<UnityEngine.UI.Button>();
            uiButton.targetGraphic = surface;
            uiButton.onClick.AddListener(() => extraClicks++);
            drag = button.gameObject.AddComponent<DirectionalSkillDrag>();
            RectTransform arrowRect = InCanvas("Aim Arrow", canvas.transform);
            arrowRect.anchorMin = Vector2.zero;
            arrowRect.anchorMax = Vector2.one;
            arrowRect.offsetMin = arrowRect.offsetMax = Vector2.zero;
            arrow = arrowRect.gameObject.AddComponent<DirectionalSkillArrow>();

            stick = Object.Instantiate(LoadPrefab("VirtualJoystick.prefab"), canvas.transform).GetComponent<VirtualJoystick>();
            GameObject player = Object.Instantiate(LoadPrefab("PrototypeMovementPlayer.prefab"));
            SceneManager.MoveGameObjectToScene(player, scene);
            body = player.GetComponent<Rigidbody2D>();
            player.GetComponent<PlayerMovement>().BindJoystick(stick);
            BindSkill();

            InputSystem.Update();
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return null;
            Assert.That(events.currentInputModule, Is.SameAs(module));
            Assert.That(canvas.scaleFactor, Is.EqualTo(2f).Within(0.001f));
            Assert.That(arrow.raycastTarget, Is.False);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
            if (previousEvents)
                EventSystem.current = previousEvents;
            Object.DestroyImmediate(point);
            Object.DestroyImmediate(click);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(definition);
            Object.DestroyImmediate(pattern);
            if (touchscreen != null && touchscreen.added)
                InputSystem.RemoveDevice(touchscreen);
            InputSystem.settings.updateMode = updateMode;
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
        }

        [UnityTest]
        public IEnumerator ActualTouchDistanceIsMeasuredInCanvasUnitsAndTheBoundaryActivatesOneTime()
        {
            Vector2 center = SkillCenter();
            yield return SendTouch(1, center, TouchPhase.Began);
            Assert.That(drag.IsAiming, Is.True);
            yield return SendTouch(1, center + Vector2.right * 31f, TouchPhase.Moved);
            Assert.That(arrow.IsVisible, Is.True);
            Assert.That(arrow.Direction.x, Is.EqualTo(1f).Within(.0001f));
            Canvas.ForceUpdateCanvases();
            Assert.That(arrow.canvasRenderer.GetMesh().vertexCount, Is.EqualTo(7));
            yield return SendTouch(1, center + Vector2.right * 31f, TouchPhase.Ended);
            Assert.That(outcomes[0].Status, Is.EqualTo(ManualSkillActivationStatus.Canceled));
            Assert.That(arrow.IsVisible, Is.False);

            yield return SendTouch(2, center, TouchPhase.Began);
            yield return SendTouch(2, center + Vector2.right * 32f, TouchPhase.Moved);
            yield return SendTouch(2, center + Vector2.right * 32f, TouchPhase.Ended);
            Assert.That(outcomes[1].Status, Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(outcomes[1].Key, Is.Not.EqualTo(outcomes[0].Key));
            Assert.That(effect.Requests, Has.Count.EqualTo(1));
            Assert.That(effect.Requests[0].GestureId, Is.EqualTo(outcomes[1].Key));
            Assert.That(effect.Requests[0].Aim.Vector.X, Is.EqualTo(1f));
            Assert.That(effect.Requests[0].Weapon.InstanceId, Is.EqualTo(first.Id));
            yield return SendTouch(2, center + Vector2.right * 32f, TouchPhase.Ended);
            Assert.That(executor.Cancel(outcomes[1].Key), Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
            Assert.That(outcomes, Has.Count.EqualTo(2));
            Assert.That(effect.Requests, Has.Count.EqualTo(1));
            Assert.That(extraClicks, Is.Zero);
        }

        [UnityTest]
        public IEnumerator IndependentJoystickAndSecondRightTouchNeverStealTheOwningAim()
        {
            Vector2 center = SkillCenter();
            Vector2 stickPoint = ScreenCenter((RectTransform)stick.transform) + Vector2.right * 76f;
            yield return SendTouch(1, stickPoint, TouchPhase.Began);
            yield return new WaitForFixedUpdate();
            Assert.That(stick.IsHeld, Is.True);
            Assert.That(body.linearVelocity.x, Is.GreaterThan(0f));

            yield return SendTouch(2, center, TouchPhase.Began);
            yield return SendTouch(2, center + Vector2.up * 50f, TouchPhase.Moved);
            Assert.That(drag.IsAiming, Is.True);
            Assert.That(arrow.Direction.y, Is.EqualTo(1f).Within(.0001f));
            yield return SendTouch(3, center, TouchPhase.Began);
            yield return SendTouch(3, center + Vector2.right * 61f, TouchPhase.Moved);
            yield return SendTouch(3, center + Vector2.right * 61f, TouchPhase.Ended);
            yield return SendTouch(4, center, TouchPhase.Began);
            yield return SendTouch(4, center, TouchPhase.Ended);
            Assert.That(arrow.Direction.y, Is.EqualTo(1f).Within(.0001f));
            Assert.That(outcomes, Is.Empty);
            Assert.That(extraClicks, Is.Zero);

            Vector2 before = body.position;
            yield return new WaitForFixedUpdate();
            Assert.That(scene.GetPhysicsScene2D().Simulate(Time.fixedDeltaTime), Is.True);
            Assert.That(body.position.x, Is.GreaterThan(before.x));
            yield return SendTouch(2, center + Vector2.right * 62f, TouchPhase.Ended);
            Assert.That(effect.Requests, Has.Count.EqualTo(1));
            Assert.That(effect.Requests[0].Aim.Vector.X, Is.EqualTo(1f));
            Assert.That(effect.Requests[0].Origin.X, Is.EqualTo(body.position.x));
            Assert.That(stick.IsHeld, Is.True);
            Assert.That(drag.IsAiming, Is.False);
            yield return SendTouch(1, stickPoint, TouchPhase.Ended);
            Assert.That(stick.IsHeld, Is.False);
        }

        [UnityTest]
        public IEnumerator ReleaseOutsideOrReturningInsideAndSystemCancellationConsumeEachGesture()
        {
            Vector2 center = SkillCenter();
            Vector2 aiming = center + Vector2.up * 55f;
            yield return SendTouch(1, center, TouchPhase.Began);
            yield return SendTouch(1, aiming, TouchPhase.Moved);
            yield return SendTouch(1, new Vector2(Screen.width * .50f, aiming.y), TouchPhase.Ended);
            yield return SendTouch(2, center, TouchPhase.Began);
            yield return SendTouch(2, aiming, TouchPhase.Moved);
            yield return SendTouch(2, center, TouchPhase.Ended);
            yield return SendTouch(3, center, TouchPhase.Began);
            yield return SendTouch(3, aiming, TouchPhase.Moved);
            yield return SendTouch(3, aiming, TouchPhase.Canceled);

            Assert.That(outcomes, Has.Count.EqualTo(3));
            foreach (var canceled in outcomes)
            {
                Assert.That(canceled.Status, Is.EqualTo(ManualSkillActivationStatus.Canceled));
                Assert.That(executor.Cancel(canceled.Key), Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
            }
            Assert.That(effect.Requests, Is.Empty);
            Assert.That(drag.IsAiming, Is.False);
            Assert.That(arrow.IsVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator InputSuspensionAndDisabledButtonDiscardOldReleaseAndAllowFreshTouch()
        {
            Vector2 center = SkillCenter();
            Vector2 aiming = center + Vector2.up * 55f;
            yield return SendTouch(1, center, TouchPhase.Began);
            yield return SendTouch(1, aiming, TouchPhase.Moved);
            drag.SetInputAvailable(false);
            Assert.That(drag.IsAiming, Is.False);
            Assert.That(arrow.IsVisible, Is.False);
            drag.SetInputAvailable(true);
            yield return SendTouch(1, aiming, TouchPhase.Ended);

            yield return SendTouch(2, center, TouchPhase.Began);
            yield return SendTouch(2, aiming, TouchPhase.Moved);
            drag.gameObject.SetActive(false);
            drag.gameObject.SetActive(true);
            yield return SendTouch(2, aiming, TouchPhase.Ended);
            Assert.That(outcomes, Has.Count.EqualTo(2));
            Assert.That(effect.Requests, Is.Empty);

            yield return SendTouch(3, center, TouchPhase.Began);
            yield return SendTouch(3, aiming, TouchPhase.Moved);
            yield return SendTouch(3, aiming, TouchPhase.Ended);
            Assert.That(outcomes[2].Status, Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(effect.Requests, Has.Count.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SwitchingBetweenEqualDefinitionsBeforeReleaseDoesNotUseTheNewInstance()
        {
            Vector2 center = SkillCenter();
            Vector2 aiming = center + Vector2.up * 55f;
            yield return SendTouch(1, center, TouchPhase.Began);
            Assert.That(drag.IsAiming, Is.True);
            Assert.That(selection.TrySwitch(1, now), Is.True);
            yield return SendTouch(1, aiming, TouchPhase.Moved);
            yield return SendTouch(1, aiming, TouchPhase.Ended);
            Assert.That(outcomes[0].Status, Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(effect.Requests, Is.Empty);

            yield return SendTouch(2, center, TouchPhase.Began);
            yield return SendTouch(2, aiming, TouchPhase.Moved);
            yield return SendTouch(2, aiming, TouchPhase.Ended);
            Assert.That(outcomes[1].Status, Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(effect.Requests, Has.Count.EqualTo(1));
            Assert.That(effect.Requests[0].Weapon.InstanceId, Is.EqualTo(second.Id));
        }

        private void BindSkill()
        {
            pattern = ScriptableObject.CreateInstance<ManualSkillPattern>();
            definition = ScriptableObject.CreateInstance<WeaponDefinition>();
            catalog = ScriptableObject.CreateInstance<WeaponCatalog>();
            using var patternData = new SerializedObject(pattern);
            patternData.FindProperty("aimKind").intValue = (int)ManualSkillAimKind.Direction;
            patternData.ApplyModifiedPropertiesWithoutUndo();
            using var definitionData = new SerializedObject(definition);
            definitionData.FindProperty("definitionId").stringValue = "skill_ui_test";
            definitionData.FindProperty("displayName").stringValue = "Direction fixture";
            definitionData.FindProperty("skill").objectReferenceValue = pattern;
            definitionData.ApplyModifiedPropertiesWithoutUndo();
            using var catalogData = new SerializedObject(catalog);
            SerializedProperty entries = catalogData.FindProperty("definitions");
            entries.arraySize = 1;
            entries.GetArrayElementAtIndex(0).objectReferenceValue = definition;
            catalogData.ApplyModifiedPropertiesWithoutUndo();

            first = WeaponInstance.Create(definition.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            second = WeaponInstance.Create(definition.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(1));
            var run = new RunState(() => now);
            Assert.That(run.TryStartRun(RunId.New(), 1, TimeSpan.FromSeconds(10)), Is.True);
            selection = new EquippedWeaponSelection(run, new InventoryService(new[] { first, second }), catalog,
                () => false, new WeaponActionLocks());
            executor = new ManualSkillExecutor(run, selection, effect, () => false, _ => null, "player");
            drag.Bind(executor, selection, aimArea, arrow, () => now, () => new WorldPoint2D(body.position.x, body.position.y));
            drag.Finished += (key, status) => outcomes.Add((key, status));
        }

        private GameObject NewRoot(string name)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        private static RectTransform InCanvas(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, worldPositionStays: false);
            return rect;
        }

        private static GameObject LoadPrefab(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectWeapon/Movement/" + name);
            Assert.That(prefab, Is.Not.Null);
            return prefab;
        }

        private Vector2 SkillCenter() => ScreenCenter((RectTransform)drag.transform);
        private static Vector2 ScreenCenter(RectTransform rect) =>
            RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));

        private IEnumerator SendTouch(int id, Vector2 position, TouchPhase phase)
        {
            InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId = id, position = position, phase = phase });
            InputSystem.Update();
            yield return null;
        }

        private sealed class RecordedEffect : IManualSkillEffect
        {
            internal readonly List<ManualSkillActivationRequest> Requests = new();
            public bool TryStart(ManualSkillActivationRequest request)
            {
                Requests.Add(request);
                return true;
            }
        }
    }
}
#endif
