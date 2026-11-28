#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace ProjectWeapon.SortieUI.Tests
{
    public sealed class SortieEquipmentPanelTests
    {
        private const string Feature = "Assets/ProjectWeapon/";
        private GameObject? root;
        private GameObject? eventsRoot;
        private EventSystem eventSystem = null!;
        private SortieEquipmentPanel panel = null!;
        private InventoryService inventory = null!;
        private ZoneCatalog zones = null!;
        private SortieManifest? accepted;
        private bool available;
        private int startCalls;

        [UnityTearDown]
        public IEnumerator RemoveFixture()
        {
            if (root)
                UnityEngine.Object.Destroy(root);
            if (eventsRoot)
                UnityEngine.Object.Destroy(eventsRoot);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ChooseTwoInstancesAndReserveExactlyThoseWeaponsOnce()
        {
            WeaponInstance first = Item("old_sword", WeaponLocation.Warehouse());
            WeaponInstance second = Item("dagger", WeaponLocation.Warehouse());
            WeaponInstance spare = Item("mace", WeaponLocation.Warehouse());
            var run = NewRun();
            Setup(new[] { first, second, spare }, () => Starter(run).TryStart(1));
            yield return null;

            Assert.That(Text("LossNotice"), Does.Contain("광고·코인").And.Contain("보관할 수 없습니다"));
            for (int index = 0; index < 3; index++)
                Assert.That(Text($"Carried{index}"), Is.EqualTo("전리품 공간"));

            Click(Warehouse("낡은 검"));
            Click(Warehouse("단검"));
            Assert.That(Warehouse("철퇴").interactable, Is.False);
            Click(Button("Equipped1"));
            Click(Button("Remove"));
            Click(Warehouse("단검"));
            var confirm = Button("Confirm");
            Click(confirm);
            confirm.onClick.Invoke();

            Assert.That(startCalls, Is.EqualTo(1));
            Assert.That(panel.IsOpen, Is.False);
            Assert.That(accepted!.Equipped.Select(item => item.Id), Is.EqualTo(new[] { first.Id, second.Id }));
            Assert.That(accepted.RunId, Is.EqualTo(run.CurrentRun!.Id.Value));
            Assert.That(Location(spare), Is.EqualTo(WeaponLocationKind.Warehouse));
            Assert.That(Location(first), Is.EqualTo(WeaponLocationKind.RunReserved));
        }

        [UnityTest]
        public IEnumerator AvailabilityAndStaleCarryBlockBeforeHostAndRealRunRejectionKeepsSelection()
        {
            WeaponInstance equipped = Item("old_sword", WeaponLocation.Equipped(0));
            WeaponInstance carried = Item("mace", WeaponLocation.Warehouse());
            var run = NewRun();
            Setup(new[] { equipped, carried }, () => Starter(run).TryStart(1));
            yield return null;

            available = false;
            panel.Refresh();
            var confirm = Button("Confirm");
            Assert.That(confirm.interactable, Is.False);
            confirm.onClick.Invoke();
            Assert.That(startCalls, Is.Zero);

            available = true;
            panel.Refresh();
            Assert.That(confirm.interactable, Is.True);
            Assert.That(inventory.Move(carried.Id, WeaponLocation.Warehouse(), WeaponLocation.Carried(0)),
                Is.EqualTo(InventoryMoveResult.Moved));
            Click(confirm);
            Assert.That(startCalls, Is.Zero);
            Assert.That(Text("Carried0"), Is.EqualTo("비워야 함"));

            Assert.That(inventory.Move(carried.Id, WeaponLocation.Carried(0), WeaponLocation.Warehouse()),
                Is.EqualTo(InventoryMoveResult.Moved));
            panel.Refresh();
            Assert.That(run.TryStartRun(RunId.New(), 1, TimeSpan.FromMinutes(1)), Is.True);
            Click(confirm);

            Assert.That(startCalls, Is.EqualTo(1));
            Assert.That(panel.IsOpen, Is.True);
            Assert.That(accepted, Is.Null);
            Assert.That(Text("Status"), Does.Contain("출격을 시작하지 못했습니다"));
            Assert.That(Location(equipped), Is.EqualTo(WeaponLocationKind.Equipped));
            Assert.That(Location(carried), Is.EqualTo(WeaponLocationKind.Warehouse));
        }

        [UnityTest]
        public IEnumerator ClosingEmptySelectionDoesNotIssueLoanAndHostConfirmsSameRunLoanOnce()
        {
            var run = NewRun();
            RunId runId = RunId.New();
            Setup(Array.Empty<WeaponInstance>(), () =>
            {
                StarterLoanResult prepared = inventory.PrepareStarterLoan(runId.Value);
                Assert.That(prepared.Status, Is.EqualTo(StarterLoanStatus.Issued));
                Assert.That(inventory.Move(prepared.Loan!.Id, WeaponLocation.Equipped(0), WeaponLocation.Warehouse()),
                    Is.EqualTo(InventoryMoveResult.ProvenanceRestricted));
                Button("Confirm").onClick.Invoke();
                return Starter(run).TryStart(runId, 1);
            });
            yield return null;

            Assert.That(Button("Equipped0").GetComponentInChildren<TextMeshProUGUI>().text, Does.Contain("무료 대여 예정"));
            Assert.That(Button("Remove").interactable, Is.False);
            Assert.That(inventory.Snapshot(), Is.Empty);
            Click(Button("Cancel"));
            Assert.That(startCalls, Is.Zero);
            Assert.That(inventory.Snapshot(), Is.Empty);

            panel.Open();
            Click(Button("Confirm"));
            Assert.That(startCalls, Is.EqualTo(1));
            WeaponInstance loan = accepted!.Equipped.Single();
            Assert.That(loan.IsLoaned, Is.True);
            Assert.That(loan.DefinitionId.Value, Is.EqualTo("old_sword"));
            Assert.That(loan.Location.SlotIndex, Is.Zero);
            Assert.That(inventory.CheckSaleProvenance(loan.Id), Is.EqualTo(InventorySaleProvenance.ProvenanceRestricted));
        }

        private void Setup(IReadOnlyList<WeaponInstance> items, Func<SortieStartResult> start)
        {
            if (!Resources.Load<TMP_Settings>("TMP Settings"))
                Assert.Ignore("Import TMP Essential Resources before running this Canvas fixture.");

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Feature + "SortieUI/SortieHangul.asset");
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Feature +
                "Weapons/PrototypeStarter/PrototypeStarterWeaponCatalog.asset");
            zones = AssetDatabase.LoadAssetAtPath<ZoneCatalog>(Feature + "Dungeon/PrototypeZoneCatalog.asset");
            Assert.That(font, Is.Not.Null);
            Assert.That(zones, Is.Not.Null);
            Assert.That(catalog, Is.Not.Null);

            inventory = new InventoryService(items);
            accepted = null;
            available = true;
            startCalls = 0;
            root = new GameObject("Sortie Canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960, 540);
            scaler.matchWidthOrHeight = .5f;
            var host = new GameObject("Equipment Panel", typeof(RectTransform), typeof(SortieEquipmentPanel));
            var rect = host.GetComponent<RectTransform>();
            rect.SetParent(root.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            eventsRoot = new GameObject("Sortie Test EventSystem", typeof(EventSystem));
            eventSystem = eventsRoot.GetComponent<EventSystem>();
            panel = host.GetComponent<SortieEquipmentPanel>();
            panel.Initialize(inventory, catalog, font, () => available, () =>
            {
                startCalls++;
                return start();
            });
            panel.Confirmed += manifest => accepted = manifest;
            panel.Open();
        }

        private void Click(UnityEngine.UI.Button button)
        {
            Assert.That(button.interactable, Is.True);
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center))
            };
            var hits = new List<RaycastResult>();
            eventSystem.RaycastAll(pointer, hits);
            Assert.That(hits[0].gameObject, Is.EqualTo(button.gameObject));
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        private UnityEngine.UI.Button Button(string name)
            => root!.GetComponentsInChildren<UnityEngine.UI.Button>().Single(button => button.name == name);

        private UnityEngine.UI.Button Warehouse(string caption)
            => root!.GetComponentsInChildren<UnityEngine.UI.Button>().Single(button => button.name == "WarehouseWeapon"
                && button.GetComponentInChildren<TextMeshProUGUI>().text == caption);

        private string Text(string name)
            => root!.GetComponentsInChildren<TextMeshProUGUI>().Single(label => label.name == name).text;

        private SortieStarter Starter(RunState run) => new SortieStarter(inventory, new ZoneRunCountdown(zones, run));

        private WeaponLocationKind Location(WeaponInstance item)
            => inventory.Snapshot().Single(candidate => candidate.Id == item.Id).Location.Kind;

        private static RunState NewRun() => new RunState(() => TimeSpan.Zero);

        private static WeaponInstance Item(string definition, WeaponLocation location)
            => WeaponInstance.Create(new WeaponDefinitionId(definition), WeaponProvenance.Personal, location);
    }
}
#endif
