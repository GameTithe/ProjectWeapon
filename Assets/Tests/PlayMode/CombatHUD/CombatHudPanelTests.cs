#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Inventory;
using ProjectWeapon.Player;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectWeapon.CombatHUD.Tests
{
    public sealed class CombatHudPanelTests : IPrebuildSetup, IPostBuildCleanup
    {
        private const string TmpFolder = "Assets/TextMesh Pro";
        private const string TmpSetupFlag = "ProjectWeapon.KAN174.ImportedTmpForTest";
        private const string ZoneCatalogPath = "Assets/ProjectWeapon/Dungeon/PrototypeZoneCatalog.asset";
        private const string WeaponCatalogPath =
            "Assets/ProjectWeapon/Weapons/PrototypeStarter/PrototypeStarterWeaponCatalog.asset";
        private Scene scene;
        private TMP_FontAsset? font;
        private CombatHudPanel panel = null!;
        private RunState run = null!;
        private ZoneRunCountdown countdown = null!;
        private InventoryService inventory = null!;
        private WeaponCatalog weaponCatalog = null!;
        private PlayerHealth health = null!;
        private WeaponInstance sword = null!;
        private WeaponInstance bow = null!;
        private WeaponInstance unknown = null!;
        private RunId runId;
        private TimeSpan now;

        public void Setup()
        {
            if (AssetDatabase.IsValidFolder(TmpFolder))
                return;

            SessionState.SetBool(TmpSetupFlag, true);
            TMP_PackageResourceImporter.ImportResources(true, false, false);
            AssetDatabase.Refresh();
        }

        public void Cleanup()
        {
            if (!SessionState.GetBool(TmpSetupFlag, false))
                return;

            AssetDatabase.DeleteAsset(TmpFolder);
            SessionState.EraseBool(TmpSetupFlag);
        }

        [UnitySetUp]
        public IEnumerator Prepare()
        {
            string? family = Font.GetOSInstalledFontNames().FirstOrDefault(candidate =>
                candidate == "Malgun Gothic" || candidate == "Noto Sans CJK KR" || candidate == "Apple SD Gothic Neo");
            if (family == null)
                Assert.Ignore("A Hangul-capable system font is required for the HUD rendering tests.");
            font = TMP_FontAsset.CreateFontAsset(family!, "Regular");
            Assert.That(font, Is.Not.Null);
            Assert.That(font!.TryAddCharacters("낡은 검 사냥 활 비어 있음 미확인 무기 HP0123456789/:—-"), Is.True);

            now = TimeSpan.Zero;
            runId = RunId.New();
            run = new RunState(() => now);
            countdown = new ZoneRunCountdown(AssetDatabase.LoadAssetAtPath<ZoneCatalog>(ZoneCatalogPath), run);
            Assert.That(countdown.TryStartRun(runId, 1), Is.True);
            health = new PlayerHealth(100);
            weaponCatalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(WeaponCatalogPath);
            Assert.That(weaponCatalog, Is.Not.Null);
            sword = Weapon("old_sword", WeaponLocation.Equipped(0));
            bow = Weapon("hunting_bow", WeaponLocation.Equipped(1));
            unknown = Weapon("unknown_weapon", WeaponLocation.Warehouse());
            inventory = new InventoryService(new[] { bow, sword, unknown });

            scene = SceneManager.CreateScene($"CombatHud-{Guid.NewGuid():N}");
            var canvasObject = new GameObject("HUD Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960, 540);
            scaler.matchWidthOrHeight = 0.5f;
            panel = canvasObject.AddComponent<CombatHudPanel>();
            panel.Initialize(font);
            panel.Bind(health, countdown, inventory, weaponCatalog, now, activeSlot: 0);
            yield return null;
            Canvas.ForceUpdateCanvases();
        }

        [UnityTearDown]
        public IEnumerator Dispose()
        {
            if (scene.IsValid())
                yield return SceneManager.UnloadSceneAsync(scene);
            if (font)
                UnityEngine.Object.Destroy(font);
        }

        [UnityTest]
        public IEnumerator HitAndZeroCountdownRefreshTheVisibleLabelsAndFill()
        {
            RunSession session = run.CurrentRun!;
            TimeSpan deadline = session.Deadline;
            Assert.That(Label("HealthValue").text, Is.EqualTo("HP 100 / 100"));
            string fullTime = string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}",
                (int)deadline.TotalMinutes, deadline.Seconds);
            Assert.That(Label("CountdownValue").text, Is.EqualTo(fullTime));
            Assert.That(panel.transform.Find("CombatHUD").GetComponent<RectTransform>().rect.height, Is.Positive);
            Assert.That(Find("Health").GetComponent<RectTransform>().rect.width, Is.Positive);
            Assert.That(Find("Countdown").GetComponent<RectTransform>().rect.width, Is.Positive);
            Assert.That(Find("EquipmentSlot1").GetComponent<RectTransform>().rect.width, Is.Positive);
            Assert.That(Find("EquipmentSlot2").GetComponent<RectTransform>().rect.width, Is.Positive);
            Assert.That(panel.GetComponentsInChildren<UnityEngine.UI.Graphic>().All(graphic => !graphic.raycastTarget),
                Is.True);

            now = TimeSpan.FromSeconds(1);
            Assert.That(health.ApplyDamage(27, now), Is.True);
            Assert.That(Label("HealthValue").text, Is.EqualTo("HP 73 / 100"));
            Assert.That(Find("HealthFill").GetComponent<RectTransform>().anchorMax.x, Is.EqualTo(0.73f).Within(0.0001f));

            now = deadline - TimeSpan.FromTicks(1);
            Assert.That(countdown.ProcessFrame(Array.Empty<RunSignal>()), Is.Null);
            panel.RefreshTime(now);
            Assert.That(Label("CountdownValue").text, Is.EqualTo("00:01"));

            now = deadline;
            Assert.That(countdown.ProcessFrame(Array.Empty<RunSignal>())?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            panel.RefreshTime(now);
            Assert.That(Label("CountdownValue").text, Is.EqualTo("00:00"));
            panel.RefreshTime(now.Add(TimeSpan.FromSeconds(5)));
            Assert.That(Label("CountdownValue").text, Is.EqualTo("00:00"));
            Assert.That(run.TryReturnToTown(runId), Is.True);
            panel.RefreshTime(now);
            Assert.That(Label("CountdownValue").text, Is.EqualTo("--:--"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator EquipmentUsesTheCommittedSlotIndexIncludingEmptyOrUnknownItems()
        {
            Assert.That(SlotLabel(1), Is.EqualTo("1  낡은 검"));
            Assert.That(SlotLabel(2), Is.EqualTo("2  사냥 활"));
            Assert.That(HasActiveMarker(1), Is.True);
            Assert.That(HasActiveMarker(2), Is.False);

            Move(sword, WeaponLocation.Equipped(0), WeaponLocation.Warehouse());
            Move(bow, WeaponLocation.Equipped(1), WeaponLocation.Equipped(0));
            Move(sword, WeaponLocation.Warehouse(), WeaponLocation.Equipped(1));
            panel.RefreshEquipment(activeSlot: 1);
            Assert.That(SlotLabel(1), Is.EqualTo("1  사냥 활"));
            Assert.That(SlotLabel(2), Is.EqualTo("2  낡은 검"));
            Assert.That(HasActiveMarker(1), Is.False);
            Assert.That(HasActiveMarker(2), Is.True);

            Move(sword, WeaponLocation.Equipped(1), WeaponLocation.Carried(0));
            panel.RefreshEquipment(activeSlot: 1);
            Assert.That(SlotLabel(2), Is.EqualTo("2  비어 있음"));
            Assert.That(HasActiveMarker(2), Is.False);
            Move(unknown, WeaponLocation.Warehouse(), WeaponLocation.Equipped(1));
            panel.RefreshEquipment(activeSlot: null);
            Assert.That(SlotLabel(2), Is.EqualTo("2  미확인 무기"));
            Assert.That(HasActiveMarker(2), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RebindingAndReenablingNeverReadThePreviousHealthSource()
        {
            var nextHealth = new PlayerHealth(40);
            panel.Bind(nextHealth, countdown, inventory, weaponCatalog, now);
            health.ApplyDamage(99, now);
            Assert.That(Label("HealthValue").text, Is.EqualTo("HP 40 / 40"));

            panel.enabled = false;
            nextHealth.ApplyDamage(15, now);
            Assert.That(Label("HealthValue").text, Is.EqualTo("HP 40 / 40"));
            panel.enabled = true;
            Assert.That(Label("HealthValue").text, Is.EqualTo("HP 25 / 40"));
            nextHealth.ApplyDamage(40, now);
            Assert.That(Label("HealthValue").text, Is.EqualTo("HP 0 / 40"));
            Assert.That(Find("HealthFill").GetComponent<RectTransform>().anchorMax.x, Is.Zero);
            yield return null;
        }

        private void Move(WeaponInstance instance, WeaponLocation source, WeaponLocation destination)
            => Assert.That(inventory.Move(instance.Id, source, destination), Is.EqualTo(InventoryMoveResult.Moved));

        private Transform Find(string name) => panel.GetComponentsInChildren<Transform>().Single(t => t.name == name);

        private TextMeshProUGUI Label(string name) => Find(name).GetComponent<TextMeshProUGUI>();

        private string SlotLabel(int number) => Find($"EquipmentSlot{number}").Find("SlotValue").GetComponent<TextMeshProUGUI>().text;

        private bool HasActiveMarker(int number)
            => Find($"EquipmentSlot{number}").Find("ActiveMarker").GetComponent<UnityEngine.UI.Image>().enabled;

        private static WeaponInstance Weapon(string id, WeaponLocation location)
            => WeaponInstance.Create(new WeaponDefinitionId(id), WeaponProvenance.Personal, location);
    }
}
#endif
