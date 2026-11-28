#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Enemies;
using ProjectWeapon.Extraction;
using ProjectWeapon.Inventory;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.ManualSkillUI;
using ProjectWeapon.Movement;
using ProjectWeapon.Run;
using ProjectWeapon.WeaponSwitch;
using ProjectWeapon.Weapons;
using TMPro;
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

namespace ProjectWeapon.CombatHud.Tests
{
    public sealed class CombatActionHudTests
    {
        private const string MovementPath = "Assets/ProjectWeapon/Movement/";
        private readonly List<Object> temporary = new List<Object>();
        private readonly List<RunSignal> dodgeSignals = new List<RunSignal>();
        private Scene scene;
        private Canvas canvas = null!;
        private EventSystem? oldEvents;
        private Touchscreen screen = null!;
        private CombatActionHud hud = null!;
        private Rigidbody2D body = null!;
        private RunState run = null!;
        private RunId runId;
        private ExtractionChannel channel = null!;
        private EquippedWeaponSelection equipment = null!;
        private InventoryService field = null!;
        private WeaponInstance directional = null!;
        private WeaponInstance self = null!;
        private SkillDamageFixture effect = null!;
        private TimeSpan now;
        private bool enabled;
        private Rect safeArea;
        private int dodgeCount;
        private InputSettings.UpdateMode updateMode;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInputs;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            now = TimeSpan.Zero;
            enabled = true;
            dodgeCount = 0;
            dodgeSignals.Clear();
            safeArea = new Rect(30f, 16f, Screen.width - 80f, Screen.height - 24f);
            oldEvents = EventSystem.current;
            updateMode = InputSystem.settings.updateMode;
            background = InputSystem.settings.backgroundBehavior;
            editorInputs = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            screen = InputSystem.AddDevice<Touchscreen>();
            scene = SceneManager.CreateScene($"CombatHud-{Guid.NewGuid():N}", new CreateSceneParameters(LocalPhysicsMode.Physics2D));

            GameObject eventRoot = NewRoot("HUD Event System");
            eventRoot.SetActive(false);
            EventSystem eventSystem = eventRoot.AddComponent<EventSystem>();
            var module = eventRoot.AddComponent<InputSystemUIInputModule>();
            InputActionAsset actions = InputSystem.actions;
            var point = InputActionReference.Create(actions.FindAction("UI/Point", throwIfNotFound: true));
            var click = InputActionReference.Create(actions.FindAction("UI/Click", throwIfNotFound: true));
            temporary.Add(point);
            temporary.Add(click);
            module.actionsAsset = actions;
            module.pointerBehavior = UIPointerBehavior.SingleMouseOrPenButMultiTouchAndTrack;
            module.point = point;
            module.leftClick = click;
            eventRoot.SetActive(true);
            EventSystem.current = eventSystem;

            canvas = NewRoot("Combat Canvas").AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var scaler = canvas.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960f, 540f);
            scaler.matchWidthOrHeight = .5f;
            var joystick = Asset<GameObject>(MovementPath + "VirtualJoystick.prefab").GetComponent<VirtualJoystick>();
            var font = Asset<TMP_FontAsset>("Assets/Scenes/SortieLoop/SortieLoopHangul.asset");
            hud = CombatActionHud.Create(canvas, joystick, font, () => safeArea);

            GameObject player = Object.Instantiate(Asset<GameObject>(MovementPath + "PrototypeMovementPlayer.prefab"));
            SceneManager.MoveGameObjectToScene(player, scene);
            body = player.GetComponent<Rigidbody2D>();
            Bind(player.GetComponent<PlayerMovement>());
            InputSystem.Update();
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return null;
            Assert.That(eventSystem.currentInputModule, Is.SameAs(module));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
            if (oldEvents)
                EventSystem.current = oldEvents;
            foreach (Object item in temporary)
                Object.DestroyImmediate(item);
            temporary.Clear();
            if (screen != null && screen.added)
                InputSystem.RemoveDevice(screen);
            InputSystem.settings.updateMode = updateMode;
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInputs;
        }

        [UnityTest]
        public IEnumerator TwoTouchesMoveTheRealPlayerAndAimAndApplyOnlyOneSkill()
        {
            Vector2 stick = ScreenCenter((RectTransform)hud.Joystick.transform);
            Vector2 skill = Center("Skill");
            yield return Touch(1, stick, TouchPhase.Began);
            yield return Touch(1, stick + Offset(44, 0), TouchPhase.Moved);
            yield return Touch(2, skill, TouchPhase.Began);
            yield return Touch(2, skill + Offset(24, 0), TouchPhase.Moved);
            Assert.That(hud.IsAiming, Is.True);
            Assert.That(hud.Joystick.IsHeld, Is.True);

            yield return new WaitForFixedUpdate();
            scene.GetPhysicsScene2D().Simulate(Time.fixedDeltaTime);
            Assert.That(body.position.x, Is.GreaterThan(0f));
            Assert.That(effect.Applied, Is.Empty);
            yield return Touch(2, skill + Offset(24, 0), TouchPhase.Ended);
            Assert.That(hud.IsAiming, Is.False);
            Assert.That(hud.Joystick.IsHeld, Is.True);
            Assert.That(effect.Applied.Count, Is.EqualTo(1));
            Assert.That(effect.Applied[0].Weapon.InstanceId, Is.EqualTo(directional.Id));
            Assert.That(effect.Applied[0].Aim.Kind, Is.EqualTo(ManualSkillAimKind.Direction));
            Assert.That(effect.Receiver.CurrentHealth, Is.EqualTo(20f));
            yield return Touch(1, stick, TouchPhase.Ended);
            Assert.That(hud.Joystick.IsHeld, Is.False);
        }

        [UnityTest]
        public IEnumerator SlotChangesSelectSelfTapAndCanceledTouchDoesNotConsumeItsCooldown()
        {
            Assert.That(Button("Weapon Slot 1").interactable, Is.False);
            yield return Tap(1, Center("Weapon Slot 2"));
            Assert.That(equipment.ReadActiveWeapon()?.InstanceId, Is.EqualTo(self.Id));
            Assert.That(Button("Weapon Slot 2").interactable, Is.False);
            Vector2 skill = Center("Skill");
            yield return Touch(2, skill, TouchPhase.Began);
            yield return Touch(2, skill, TouchPhase.Canceled);
            Assert.That(effect.Applied, Is.Empty);

            yield return Touch(3, skill, TouchPhase.Began);
            yield return Tap(4, Center("Weapon Slot 1"));
            yield return Touch(3, skill, TouchPhase.Ended);
            Assert.That(effect.Applied, Is.Empty);
            Assert.That(equipment.ReadActiveWeapon()?.InstanceId, Is.EqualTo(directional.Id));

            yield return Tap(5, Center("Weapon Slot 2"));
            yield return Tap(6, skill);
            Assert.That(effect.Applied.Count, Is.EqualTo(1));
            Assert.That(effect.Applied[0].Weapon.InstanceId, Is.EqualTo(self.Id));
            Assert.That(effect.Applied[0].Aim.Kind, Is.EqualTo(ManualSkillAimKind.Self));
            Assert.That(effect.Receiver.CurrentHealth, Is.EqualTo(20f));
            Assert.That(Button("Skill").interactable, Is.False);
            yield return Tap(7, skill);
            Assert.That(effect.Applied.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator PortalRangeAndMoveGuardAndDodgePressUseTheLiveExtractionRules()
        {
            Vector2 exit = Center("Extract");
            Assert.That(Button("Extract").interactable, Is.False);
            yield return Tap(1, exit);
            Assert.That(run.ActiveExtraction, Is.Null);
            body.position = new Vector2(4f, 0f);
            hud.Refresh();
            Assert.That(Button("Extract").interactable, Is.True);

            Vector2 stick = ScreenCenter((RectTransform)hud.Joystick.transform);
            yield return Touch(2, stick + Offset(38, 0), TouchPhase.Began);
            Assert.That(Button("Extract").interactable, Is.False);
            yield return Tap(3, exit);
            Assert.That(run.ActiveExtraction, Is.Null);
            yield return Touch(2, stick, TouchPhase.Ended);

            yield return Tap(4, exit);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Extracting));
            Assert.That(Button("Skill").interactable, Is.False);
            Assert.That(Button("Extract").interactable, Is.False);

            Vector2 dodge = Center("Dodge");
            yield return Touch(5, dodge, TouchPhase.Began);
            Assert.That(dodgeCount, Is.EqualTo(1));
            Assert.That(Button("Dodge").interactable, Is.False);
            channel.ProcessFrame(dodgeSignals);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Exploring));
            yield return null;
            yield return Touch(5, dodge, TouchPhase.Ended);
            yield return Tap(6, dodge);
            Assert.That(dodgeCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SafeAreaDoesNotMaskTheWorldAndInterruptionClearsBothHands()
        {
            Canvas.ForceUpdateCanvases();
            foreach (string name in new[] { "Move", "Skill", "Dodge", "Extract", "Weapon Slot 1", "Weapon Slot 2" })
            {
                RectTransform rect = Find(name);
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                Assert.That(safeArea.Contains(RectTransformUtility.WorldToScreenPoint(null, corners[0])), Is.True, name);
                Assert.That(safeArea.Contains(RectTransformUtility.WorldToScreenPoint(null, corners[2])), Is.True, name);
            }

            var raycaster = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            var hits = new List<RaycastResult>();
            raycaster.Raycast(new PointerEventData(EventSystem.current) { position = safeArea.center }, hits);
            Assert.That(hits, Is.Empty);
            var glyph = Find("Skill").Find("Pixel Icon").GetComponent<UnityEngine.UI.Graphic>();
            Assert.That(glyph.raycastTarget, Is.False);
            Assert.That(glyph.canvasRenderer.GetMesh().vertexCount, Is.GreaterThan(0));

            Vector2 stick = ScreenCenter((RectTransform)hud.Joystick.transform);
            Vector2 skill = Center("Skill");
            yield return Touch(1, stick + Offset(40, 0), TouchPhase.Began);
            yield return Touch(2, skill, TouchPhase.Began);
            yield return Touch(2, skill + Offset(20, 0), TouchPhase.Moved);
            Assert.That(hud.Joystick.IsHeld && hud.IsAiming, Is.True);
            enabled = false;
            hud.Refresh();
            Assert.That(hud.Joystick.IsHeld || hud.IsAiming, Is.False);
            Assert.That(Button("Dodge").interactable, Is.False);
            Assert.That(Button("Weapon Slot 2").interactable, Is.False);

            enabled = true;
            hud.Refresh();
            yield return Touch(2, skill + Offset(20, 0), TouchPhase.Ended);
            yield return Touch(1, stick, TouchPhase.Ended);
            Assert.That(effect.Applied, Is.Empty);

            Assert.That(field.Move(self.Id, WeaponLocation.Equipped(1), WeaponLocation.Carried(1)),
                Is.EqualTo(InventoryMoveResult.Moved));
            hud.Refresh();
            Assert.That(Button("Weapon Slot 2").interactable, Is.False);

            now = TimeSpan.FromSeconds(10);
            hud.Refresh();
            Assert.That(Button("Skill").interactable, Is.False);
            Assert.That(Button("Dodge").interactable, Is.False);
            Assert.That(hud.Joystick.enabled, Is.False);
        }

        private void Bind(PlayerMovement movement)
        {
            var directionDefinition = Definition("hud_direction", ManualSkillAimKind.Direction);
            var selfDefinition = Definition("hud_self", ManualSkillAimKind.Self);
            var catalog = NewAsset<WeaponCatalog>();
            using (var source = new SerializedObject(catalog))
            {
                SerializedProperty definitions = source.FindProperty("definitions");
                definitions.arraySize = 2;
                definitions.GetArrayElementAtIndex(0).objectReferenceValue = directionDefinition;
                definitions.GetArrayElementAtIndex(1).objectReferenceValue = selfDefinition;
                source.ApplyModifiedPropertiesWithoutUndo();
            }

            directional = WeaponInstance.Create(directionDefinition.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            self = WeaponInstance.Create(selfDefinition.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(1));
            field = new InventoryService(new[] { directional, self });
            run = new RunState(() => now);
            runId = RunId.New();
            Assert.That(run.TryStartRun(runId, 1, TimeSpan.FromSeconds(10)), Is.True);
            channel = new ExtractionChannel(run);
            var access = new ExtractionAccess(run, 1, new WorldPoint2D(4f, 0f), 1f);
            movement.DodgeStarted += (id, at) =>
            {
                dodgeCount++;
                if (channel.ObserveAcceptedDodge(id, at) is { } signal)
                    dodgeSignals.Add(signal);
            };

            equipment = new EquippedWeaponSelection(run, field, catalog, () => movement.IsDodging, new WeaponActionLocks());
            effect = new SkillDamageFixture(NewAsset<MeleeEnemySettings>());
            var executor = new ManualSkillExecutor(run, equipment, effect, () => movement.IsDodging, _ => null, "player");
            hud.Bind(new CombatHudBindings(run, movement, Asset<MovementSettings>(MovementPath + "PrototypeMovementSettings.asset"),
                field, equipment, executor, () => now, () => enabled, () => access.CanStart(runId, Origin()),
                () => access.TryStart(runId, Origin()) != null, () => movement.IsDodging));
        }

        private WeaponDefinition Definition(string id, ManualSkillAimKind kind)
        {
            var pattern = NewAsset<ManualSkillPattern>();
            using (var source = new SerializedObject(pattern))
            {
                source.FindProperty("aimKind").intValue = (int)kind;
                source.ApplyModifiedPropertiesWithoutUndo();
            }
            var definition = NewAsset<WeaponDefinition>();
            using var data = new SerializedObject(definition);
            data.FindProperty("definitionId").stringValue = id;
            data.FindProperty("displayName").stringValue = id;
            data.FindProperty("skill").objectReferenceValue = pattern;
            data.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private T NewAsset<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            temporary.Add(asset);
            return asset;
        }

        private static T Asset<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path);
            return asset;
        }

        private GameObject NewRoot(string name)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        private RectTransform Find(string name)
            => (RectTransform)hud.transform.Find("Combat Safe Area/" + name);

        private UnityEngine.UI.Button Button(string name) => Find(name).GetComponent<UnityEngine.UI.Button>();
        private Vector2 Center(string name) => ScreenCenter(Find(name));
        private Vector2 Offset(float x, float y) => new Vector2(x, y) * canvas.scaleFactor;
        private WorldPoint2D Origin() => new WorldPoint2D(body.position.x, body.position.y);
        private static Vector2 ScreenCenter(RectTransform rect)
            => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));

        private IEnumerator Tap(int id, Vector2 point)
        {
            yield return Touch(id, point, TouchPhase.Began);
            yield return Touch(id, point, TouchPhase.Ended);
        }

        private IEnumerator Touch(int id, Vector2 point, TouchPhase phase)
        {
            InputSystem.QueueStateEvent(screen, new TouchState { touchId = id, position = point, phase = phase });
            InputSystem.Update();
            yield return null;
        }

        private sealed class SkillDamageFixture : IManualSkillEffect
        {
            internal SkillDamageFixture(MeleeEnemySettings settings) => Receiver = new MeleeEnemy("target", settings);
            internal List<ManualSkillActivationRequest> Applied { get; } = new();
            internal MeleeEnemy Receiver { get; }

            public bool TryStart(ManualSkillActivationRequest request)
            {
                Applied.Add(request);
                var target = new TargetCandidate("target", new WorldPoint2D(request.Origin.X + .8f, request.Origin.Y),
                    Receiver.CurrentHealth > 0f, isAttackable: true);
                AreaDamageRequest area = request.Aim.Kind == ManualSkillAimKind.Direction
                    ? AreaDamageRequest.Sector(request.GestureId, request.AttackerId, request.Origin, 1.2f, 4f, request.Aim.Vector, 90f)
                    : AreaDamageRequest.Circle(request.GestureId, request.AttackerId, request.Origin, 1.2f, 4f);
                foreach (DamageRequest damage in AreaDamageSelector.FindTargets(area, new[] { target }))
                    Receiver.ApplyDamage(damage, request.StartedAt);
                return true;
            }
        }
    }
}
#endif
