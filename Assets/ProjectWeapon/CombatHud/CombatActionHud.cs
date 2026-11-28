#nullable enable

using System;
using ProjectWeapon.Combat;
using ProjectWeapon.Extraction;
using ProjectWeapon.Inventory;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.ManualSkillUI;
using ProjectWeapon.Movement;
using ProjectWeapon.Run;
using ProjectWeapon.WeaponSwitch;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.CombatHud
{
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class CombatActionHud : MonoBehaviour
    {
        private static readonly Color32 WarmBorder = new Color32(160, 106, 67, 255);
        private static readonly Color32 ActiveSlot = new Color32(242, 186, 107, 255);
        private static readonly Color32 DarkFace = new Color32(32, 23, 30, 241);
        private static readonly Color32 NeutralGlyph = new Color32(238, 216, 185, 255);
        private static readonly Color32 AimGlyph = new Color32(250, 156, 117, 255);
        private static readonly Color32 PortalGlyph = new Color32(129, 223, 255, 255);
        private static readonly Color32 DisabledTint = new Color32(112, 104, 118, 255);
        private static readonly Vector2 TapSize = new Vector2(52f, 52f);

        private Canvas canvas = null!;
        private Func<Rect> safeArea = null!;
        private RectTransform safeRoot = null!;
        private RectTransform aimArea = null!;
        private DirectionalSkillArrow arrow = null!;
        private DirectionalSkillDrag drag = null!;
        private SelfSkillTap tap = null!;
        private EquippedWeaponButtons equippedButtons = null!;
        private HudButton skill = null!;
        private HudButton dodge = null!;
        private HudButton extract = null!;
        private HudButton first = null!;
        private HudButton second = null!;
        private CombatHudPixelGlyph skillGlyph = null!;
        private UnityEngine.UI.Graphic joystickGraphic = null!;
        private CombatHudBindings? context;
        private Rigidbody2D body = null!;
        private RunId runId;
        private TimeSpan dodgeReadyAt;
        private bool paused;
        private bool lostFocus;

        public VirtualJoystick Joystick { get; private set; } = null!;
        public bool IsAiming => drag.IsAiming;

        public static CombatActionHud Create(Canvas canvas, VirtualJoystick joystickPrefab, TMP_FontAsset font,
            Func<Rect>? readSafeArea = null)
        {
            if (!canvas || canvas.rootCanvas.renderMode == RenderMode.WorldSpace)
                throw new ArgumentException("A screen-space Canvas is required.", nameof(canvas));
            if (!joystickPrefab || !joystickPrefab.GetComponent<UnityEngine.UI.Graphic>())
                throw new ArgumentException("Use a VirtualJoystick prefab with a raycast surface.", nameof(joystickPrefab));
            if (!font)
                throw new ArgumentNullException(nameof(font));

            var rootCanvas = canvas.rootCanvas;
            var root = ChildRect(rootCanvas.transform, "Combat Action HUD");
            var hud = root.gameObject.AddComponent<CombatActionHud>();
            hud.canvas = rootCanvas;
            hud.safeArea = readSafeArea ?? (() => Screen.safeArea);
            hud.Build(root, joystickPrefab, font);
            return hud;
        }

        public void Bind(CombatHudBindings bindings)
        {
            if (context != null)
                throw new InvalidOperationException("Create one combat HUD per room binding.");
            context = bindings ?? throw new ArgumentNullException(nameof(bindings));
            runId = context.Run.CurrentRun?.Id ?? throw new InvalidOperationException("Start the run before binding the HUD.");
            body = context.Movement.GetComponent<Rigidbody2D>();

            drag.Bind(context.Skills, context.Equipment, aimArea, arrow, ReadAvailableTime, ReadOrigin);
            tap.Bind(CaptureSelf, CompleteSelf, gesture => context.Skills.Cancel(gesture));
            equippedButtons.Bind(context.Equipment, first.Button, second.Button, ReadSwitchTime);
            context.Movement.DodgeStarted += OnDodgeStarted;
            if (isActiveAndEnabled)
                context.Movement.BindJoystick(Joystick);
            Refresh();
        }

        /// <summary>Updates controls after a host changes overlays, recovery, or current room state.</summary>
        public void Refresh()
        {
            if (context == null)
                return;

            RefreshSafeArea();
            TimeSpan now = context.Clock();
            bool live = AcceptsInput(now);
            bool exploring = live && context.Run.Phase == RunPhase.Exploring;
            bool skillsReady = exploring && !context.Movement.IsDodging && !context.WeaponActionBusy()
                && context.Equipment.TryGetReadyWeapon(CombatWeaponAction.Skill, now, out _);
            ManualSkillAimKind aimKind = (context.Equipment.ReadActiveWeapon()?.Definition.Skill as ManualSkillPattern)
                ?.AimKind ?? ManualSkillAimKind.Unspecified;
            bool direction = aimKind == ManualSkillAimKind.Direction;
            bool self = aimKind == ManualSkillAimKind.Self;
            drag.SetInputAvailable(skillsReady && direction);
            drag.enabled = direction;
            tap.enabled = skillsReady && self;
            skill.Set(skillsReady && (direction || self));
            skillGlyph.Show(direction ? CombatHudPixelGlyph.Shape.Aim : CombatHudPixelGlyph.Shape.Skill);
            dodge.Set(live && CanDodge(now));
            extract.Set(exploring && CanExtract());

            Joystick.enabled = live;
            joystickGraphic.raycastTarget = live;
            bool selectable = exploring && !context.WeaponActionBusy();
            int? activeSlot = context.Equipment.ActiveSlotIndex;
            bool hasFirst = false;
            bool hasSecond = false;
            foreach (WeaponInstance weapon in context.FieldInventory.Snapshot())
            {
                hasFirst |= weapon.Location.SlotIndex == 0;
                hasSecond |= weapon.Location.SlotIndex == 1;
            }
            first.Set(selectable && hasFirst && activeSlot != 0, selected: hasFirst && activeSlot == 0);
            second.Set(selectable && hasSecond && activeSlot != 1, selected: hasSecond && activeSlot == 1);
        }

        private void Update() => Refresh();

        private void OnEnable()
        {
            if (context != null && context.Movement)
                context.Movement.BindJoystick(Joystick);
            Refresh();
        }

        private void OnDisable()
        {
            if (context == null)
                return;

            drag.SetInputAvailable(false);
            tap.enabled = false;
            Joystick.Release();
            if (context.Movement)
                context.Movement.BindJoystick(null);
        }

        private void OnDestroy()
        {
            if (context != null && context.Movement)
                context.Movement.DodgeStarted -= OnDodgeStarted;
        }

        private void OnApplicationFocus(bool focused)
        {
            lostFocus = !focused;
            Refresh();
        }

        private void OnApplicationPause(bool isPaused)
        {
            paused = isPaused;
            Refresh();
        }

        private bool AcceptsInput(TimeSpan now) => isActiveAndEnabled && !paused && !lostFocus
            && context!.Movement && context.InputAvailable()
            && context.Run.CurrentRun is { } session && session.Id == runId && context.Run.Result == null
            && now >= session.StartedAt && now < session.Deadline;

        private TimeSpan ReadAvailableTime()
        {
            TimeSpan now = context!.Clock();
            return AcceptsInput(now) ? now : TimeSpan.MinValue;
        }

        private TimeSpan ReadSwitchTime() => context!.WeaponActionBusy() ? TimeSpan.MinValue : ReadAvailableTime();

        private WorldPoint2D ReadOrigin() => body
            ? new WorldPoint2D(body.position.x, body.position.y) : default;

        private WeaponInstanceId? CaptureSelf()
        {
            TimeSpan now = ReadAvailableTime();
            return !context!.Movement.IsDodging && !context.WeaponActionBusy()
                && context.Equipment.TryGetReadyWeapon(CombatWeaponAction.Skill, now, out SelectedWeapon weapon)
                && weapon.Definition.Skill is ManualSkillPattern { AimKind: ManualSkillAimKind.Self }
                ? weapon.InstanceId : null;
        }

        private void CompleteSelf(Guid gesture, WeaponInstanceId weapon)
        {
            TimeSpan now = ReadAvailableTime();
            if (now == TimeSpan.MinValue)
                context!.Skills.Cancel(gesture);
            else
                context!.Skills.TryActivate(gesture, weapon, now, ReadOrigin(), ManualSkillAim.AtSelf());

            Refresh();
        }

        private bool CanDodge(TimeSpan now)
            => context!.Movement.isActiveAndEnabled && body && body.simulated && body.bodyType == RigidbodyType2D.Dynamic
                && !context.Movement.IsDodging && now >= dodgeReadyAt;

        private void TryDodge()
        {
            TimeSpan now = ReadAvailableTime();
            if (now != TimeSpan.MinValue && CanDodge(now))
                context!.Movement.TryDodge(context.Run, now);
            Refresh();
        }

        private void OnDodgeStarted(RunId id, TimeSpan at)
        {
            if (id == runId)
                dodgeReadyAt = at + context!.MovementSettings.DodgeCooldown;
        }

        private bool CanExtract() => context!.Movement.SampleDirection().magnitude < ExtractionAccess.MovementCancelThreshold
            && !context.Movement.IsDodging && context.CanExtract();

        private void TryExtract()
        {
            if (ReadAvailableTime() != TimeSpan.MinValue && CanExtract())
                context!.TryExtract();
            Refresh();
        }

        private void Build(RectTransform root, VirtualJoystick joystickPrefab, TMP_FontAsset font)
        {
            safeRoot = ChildRect(root, "Combat Safe Area");
            aimArea = ChildRect(safeRoot, "Right Aim Region");
            aimArea.anchorMin = new Vector2(.5f, 0f);

            Joystick = Instantiate(joystickPrefab, safeRoot);
            Joystick.name = "Move";
            var stickRect = (RectTransform)Joystick.transform;
            Place(stickRect, Vector2.zero, new Vector2(80, 80), new Vector2(124, 124));
            joystickGraphic = Joystick.GetComponent<UnityEngine.UI.Graphic>();
            joystickGraphic.color = new Color32(68, 44, 47, 221);

            Vector2 right = Vector2.right;
            skill = BuildButton("Skill", right, new Vector2(-42, 108));
            skillGlyph = Glyph(skill, CombatHudPixelGlyph.Shape.Skill, AimGlyph, y: 0f, size: 25f);
            drag = skill.Button.gameObject.AddComponent<DirectionalSkillDrag>();
            tap = skill.Button.gameObject.AddComponent<SelfSkillTap>();
            dodge = BuildButton("Dodge", right, new Vector2(-100, 48));
            dodge.Button.gameObject.AddComponent<CombatHudDodgePress>().Begin = TryDodge;
            Glyph(dodge, CombatHudPixelGlyph.Shape.Dodge, NeutralGlyph, y: 0f, size: 23f);
            extract = BuildButton("Extract", right, new Vector2(-42, 48), TryExtract);
            Glyph(extract, CombatHudPixelGlyph.Shape.Exit, PortalGlyph, y: 6f, size: 21f);
            Label(extract, font, "귀환", 11, y: -15f);

            Vector2 center = new Vector2(.5f, 0f);
            first = BuildButton("Weapon Slot 1", center, new Vector2(-30, 44));
            second = BuildButton("Weapon Slot 2", center, new Vector2(30, 44));
            Label(first, font, "1", 22, y: 0f);
            Label(second, font, "2", 22, y: 0f);
            equippedButtons = gameObject.AddComponent<EquippedWeaponButtons>();

            arrow = ChildRect(safeRoot, "Skill Direction").gameObject.AddComponent<DirectionalSkillArrow>();
            arrow.raycastTarget = false;
            first.Set(false);
            second.Set(false);
            skill.Set(false);
            dodge.Set(false);
            extract.Set(false);
            Joystick.enabled = false;
            joystickGraphic.raycastTarget = false;
            RefreshSafeArea();
        }

        private HudButton BuildButton(string name, Vector2 anchor, Vector2 point, Action? handler = null)
        {
            RectTransform rect = ChildRect(safeRoot, name);
            Place(rect, anchor, point, TapSize);
            var background = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            background.color = WarmBorder;
            RectTransform inner = ChildRect(rect, "Inset");
            inner.offsetMin = Vector2.one * 2f;
            inner.offsetMax = -Vector2.one * 2f;
            var face = inner.gameObject.AddComponent<UnityEngine.UI.Image>();
            face.raycastTarget = false;
            face.color = DarkFace;
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = background;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color32(253, 220, 180, 255);
            colors.pressedColor = ActiveSlot;
            colors.disabledColor = DisabledTint;
            button.colors = colors;
            if (handler != null)
                button.onClick.AddListener(() => handler());
            return new HudButton(rect, background, button);
        }

        private static CombatHudPixelGlyph Glyph(HudButton button, CombatHudPixelGlyph.Shape shape,
            Color tint, float y, float size)
        {
            RectTransform icon = ChildRect(button.Rect, "Pixel Icon");
            Place(icon, new Vector2(.5f, .5f), new Vector2(0, y), new Vector2(size, size));
            var glyph = icon.gameObject.AddComponent<CombatHudPixelGlyph>();
            glyph.color = tint;
            glyph.raycastTarget = false;
            glyph.Show(shape);
            button.Content.Add(glyph);
            return glyph;
        }

        private static void Label(HudButton button, TMP_FontAsset font, string text, float size, float y)
        {
            RectTransform rect = ChildRect(button.Rect, "Label");
            Place(rect, new Vector2(.5f, .5f), new Vector2(0, y), new Vector2(46, size + 4f));
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = NeutralGlyph;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            button.Content.Add(label);
        }

        private void RefreshSafeArea()
        {
            Rect pixels = canvas.pixelRect;
            if (pixels.width <= 0f || pixels.height <= 0f)
                return;

            Rect safe = safeArea();
            Vector2 min = Normalize(safe.min, pixels);
            Vector2 max = Normalize(safe.max, pixels);
            if (max.x <= min.x || max.y <= min.y)
            {
                min = Vector2.zero;
                max = Vector2.one;
            }

            if (safeRoot.anchorMin == min && safeRoot.anchorMax == max)
                return;

            Joystick.Release();
            drag.CancelAim();
            tap.Cancel();
            safeRoot.anchorMin = min;
            safeRoot.anchorMax = max;
        }

        private static Vector2 Normalize(Vector2 point, Rect bounds)
            => new Vector2(Mathf.Clamp01((point.x - bounds.xMin) / bounds.width),
                Mathf.Clamp01((point.y - bounds.yMin) / bounds.height));

        private static RectTransform ChildRect(Transform parent, string name)
        {
            var child = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            child.SetParent(parent, false);
            child.anchorMin = Vector2.zero;
            child.anchorMax = Vector2.one;
            child.offsetMin = child.offsetMax = Vector2.zero;
            return child;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 point, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = point;
        }

        private sealed class HudButton
        {
            internal HudButton(RectTransform rect, UnityEngine.UI.Image background, UnityEngine.UI.Button button)
            {
                Rect = rect;
                Background = background;
                Button = button;
            }

            internal RectTransform Rect { get; }
            private UnityEngine.UI.Image Background { get; }
            internal UnityEngine.UI.Button Button { get; }
            internal System.Collections.Generic.List<UnityEngine.UI.Graphic> Content { get; } = new(2);

            internal void Set(bool enabled, bool selected = false)
            {
                Button.interactable = enabled;
                Background.color = selected ? ActiveSlot : WarmBorder;
                var colors = Button.colors;
                colors.disabledColor = selected ? Color.white : DisabledTint;
                Button.colors = colors;
                foreach (UnityEngine.UI.Graphic item in Content)
                {
                    Color color = item.color;
                    color.a = enabled || selected ? 1f : .45f;
                    item.color = color;
                }
            }
        }
    }
}
