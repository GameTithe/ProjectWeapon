#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.WeaponSale;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ProjectWeapon.WarehouseUI.Tests
{
    public sealed class WarehousePanelTests
    {
        private const string PathPrefix = "Assets/ProjectWeapon/";
        private static readonly WeaponDefinitionId Sword = new WeaponDefinitionId("old_sword");
        private static readonly WeaponDefinitionId Spear = new WeaponDefinitionId("wooden_spear");
        private string directory = null!;
        private string path = null!;
        private AtomicSnapshotStore store = null!;
        private GameObject? canvas;
        private GameObject? eventRoot;
        private WarehousePanel panel = null!;
        private WeaponSalePrices? prices;
        private EventSystem raycaster = null!;
        private readonly List<(Guid Request, WeaponInstanceId Item)> submitted = new List<(Guid, WeaponInstanceId)>();
        private Func<WeaponSaleResult, WeaponSaleResult> afterSale = result => result;
        private WeaponSaleResult latestSale;
        private int moves;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN175-" + Guid.NewGuid().ToString("N"));
            path = Path.Combine(directory, "snapshot.json");
            store = new AtomicSnapshotStore(path);
            submitted.Clear();
            afterSale = result => result;
            latestSale = default;
            moves = 0;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (canvas != null)
                Object.Destroy(canvas);
            if (eventRoot != null)
                Object.Destroy(eventRoot);
            if (prices != null)
                Object.Destroy(prices);
            yield return null;

            if (!Directory.Exists(directory))
                yield break;
            foreach (string file in Directory.EnumerateFiles(directory))
                File.Delete(file);
            Directory.Delete(directory);
        }

        [UnityTest]
        public IEnumerator RealButtonsMoveOnlyTheSelectedWarehouseWeaponAndShowLatestSaveAfterConflict()
        {
            WeaponInstance sword = Item(Sword);
            WeaponInstance spare = Item(Spear);
            Seed(sword, spare);
            OpenPanel();
            yield return null;

            Assert.That(Buttons("WarehouseWeapon"), Has.Length.EqualTo(2));
            Click(WarehouseWeapon("낡은 검"));
            Click(Button("EquipSlot1"));
            Assert.That(moves, Is.EqualTo(1));
            Assert.That(Stored(sword).Location.SlotIndex, Is.EqualTo(1));
            Assert.That(Stored(spare).Location.Kind, Is.EqualTo(WeaponLocationKind.Warehouse));
            Assert.That(Buttons("WarehouseWeapon"), Has.Length.EqualTo(1));
            Assert.That(Text("SelectedItem"), Is.EqualTo("낡은 검"));

            SaveSnapshot previous = store.Load()!;
            var otherWriter = new AtomicSnapshotStore(path);
            Assert.That(otherWriter.Commit(previous.Revision,
                Copy(previous, previous.ConfirmedCoins + 17, previous.Weapons)).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            Click(Button("Store"));
            Assert.That(moves, Is.EqualTo(2));
            Assert.That(Stored(sword).Location.Kind, Is.EqualTo(WeaponLocationKind.Equipped));
            Assert.That(panel.ActiveSnapshot!.Revision, Is.EqualTo(previous.Revision + 1));
            Assert.That(Text("Coins"), Does.Contain("107"));
            Assert.That(Text("Status"), Does.Contain("최신"));

            Click(Button("Store"));
            Assert.That(moves, Is.EqualTo(3));
            Assert.That(Stored(sword).Location.Kind, Is.EqualTo(WeaponLocationKind.Warehouse));
            Assert.That(Buttons("WarehouseWeapon"), Has.Length.EqualTo(2));
            Assert.That(store.Load()!.ConfirmedCoins, Is.EqualTo(107));
        }

        [UnityTest]
        public IEnumerator CancelLeavesSaveUnchangedAndDuplicateConfirmationPaysOnlyOnce()
        {
            WeaponInstance first = Item(Sword);
            WeaponInstance second = Item(Sword);
            Seed(first, second, Item(Spear));
            OpenPanel();
            yield return null;

            Click(WarehouseWeapon("나무 창"));
            Assert.That(Button("PrepareSale").interactable, Is.False);
            Click(FirstSwordRow());
            Assert.That(Button("PrepareSale").interactable, Is.True);
            byte[] untouched = File.ReadAllBytes(path);
            Click(Button("PrepareSale"));
            yield return null;
            Assert.That(Text("SaleQuote"), Is.EqualTo("낡은 검 · 30 코인"));
            Assert.That(TopHit(Button("Close")), Is.Not.EqualTo(Button("Close").gameObject));
            Click(Button("CancelSale"));
            Assert.That(submitted, Is.Empty);
            CollectionAssert.AreEqual(untouched, File.ReadAllBytes(path));

            Click(Button("PrepareSale"));
            yield return null;
            UnityEngine.UI.Button confirm = Button("ConfirmSale");
            Click(confirm);
            confirm.onClick.Invoke();
            Assert.That(submitted, Has.Count.EqualTo(1));
            Assert.That(latestSale.Status, Is.EqualTo(WeaponSaleStatus.Sold));
            Assert.That(panel.ActiveSnapshot, Is.SameAs(latestSale.ActiveSnapshot));
            SaveSnapshot saved = store.Load()!;
            Assert.That(saved.ConfirmedCoins, Is.EqualTo(120));
            Assert.That(saved.WeaponSales.Single().RequestId, Is.EqualTo(submitted[0].Request));
            Assert.That(saved.Weapons.Any(item => item.Id == submitted[0].Item), Is.False);
            Assert.That(saved.Weapons.Count(item => item.DefinitionId.Equals(Sword)), Is.EqualTo(1));
            Assert.That(Text("Status"), Does.Contain("30 코인"));
        }

        [UnityTest]
        public IEnumerator LoanIsShownInEquipmentAndRealButtonsCannotStoreOrSellIt()
        {
            var town = new InventoryService(new[] { Item(Spear) });
            StarterLoanResult prepared = town.PrepareStarterLoan(Guid.NewGuid());
            Assert.That(prepared.Status, Is.EqualTo(StarterLoanStatus.Issued));
            Seed(town.Snapshot().ToArray());
            OpenPanel();
            yield return null;

            Assert.That(Buttons("WarehouseWeapon"), Has.Length.EqualTo(1));
            Assert.That(Label(Button("EquipSlot0")), Does.Contain("대여품"));
            Click(Button("EquipSlot0"));
            Assert.That(Text("SelectedItem"), Does.Contain("대여품"));
            Assert.That(Text("LoanNotice"), Does.Contain("보관").And.Contain("판매"));
            Assert.That(Button("Store").interactable, Is.False);
            Assert.That(Button("PrepareSale").interactable, Is.False);
            Button("Store").onClick.Invoke();
            Button("PrepareSale").onClick.Invoke();
            Assert.That(moves, Is.Zero);
            Assert.That(submitted, Is.Empty);
            Assert.That(Stored(prepared.Loan!).Location.Kind, Is.EqualTo(WeaponLocationKind.Equipped));
            Assert.That(store.Load()!.Revision, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ChangedQuoteOrItemLocationRequiresANewConfirmationWithoutSendingASale()
        {
            WeaponInstance first = Item(Sword);
            WeaponInstance second = Item(Sword);
            Seed(first, second);
            OpenPanel();
            yield return null;

            Click(FirstSwordRow());
            Click(Button("PrepareSale"));
            yield return null;
            SetPrice(45);
            Click(Button("ConfirmSale"));
            Assert.That(submitted, Is.Empty);
            Assert.That(Text("Status"), Does.Contain("가격"));
            Assert.That(Label(Button("PrepareSale")), Does.Contain("45"));
            Click(Button("PrepareSale"));
            yield return null;
            Assert.That(Text("SaleQuote"), Does.Contain("45"));

            SaveSnapshot latest = store.Load()!;
            var external = new InventoryService(latest.Weapons);
            WeaponInstance moved = new[] { first, second }.OrderBy(item => item.Id.ToString(), StringComparer.Ordinal).First();
            Assert.That(external.Move(moved.Id, WeaponLocation.Warehouse(), WeaponLocation.Equipped(0)),
                Is.EqualTo(InventoryMoveResult.Moved));
            Assert.That(store.Commit(latest.Revision, Copy(latest, latest.ConfirmedCoins, external.Snapshot())).Status,
                Is.EqualTo(SnapshotCommitStatus.Committed));

            Click(Button("ConfirmSale"));
            Assert.That(submitted, Is.Empty);
            Assert.That(store.Load()!.WeaponSales, Is.Empty);
            Assert.That(Stored(moved).Location.Kind, Is.EqualTo(WeaponLocationKind.Equipped));
            Assert.That(Button("PrepareSale").interactable, Is.False);
        }

        [UnityTest]
        public IEnumerator LostSaleResponseReplaysOriginalReceiptUsingSameGuidAndShowsLatestBalance()
        {
            Seed(Item(Sword), Item(Sword));
            afterSale = result => submitted.Count == 1 ? throw new IOException("The sale reply was lost.") : result;
            OpenPanel();
            yield return null;

            Click(FirstSwordRow());
            Click(Button("PrepareSale"));
            yield return null;
            Click(Button("ConfirmSale"));
            Assert.That(latestSale.Status, Is.EqualTo(WeaponSaleStatus.Sold));
            Assert.That(submitted, Has.Count.EqualTo(1));
            Assert.That(Text("SaleStatus"), Does.Contain("다시 시도"));
            SaveSnapshot written = store.Load()!;
            Assert.That(written.WeaponSales.Single().Price, Is.EqualTo(30));

            // An already committed sale replays its original receipt even when tuning and the balance have changed.
            SetPrice(77);
            Assert.That(store.Commit(written.Revision,
                Copy(written, written.ConfirmedCoins + 5, written.Weapons)).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            byte[] beforeRetry = File.ReadAllBytes(path);
            Click(Button("ConfirmSale"));
            Assert.That(submitted.Count, Is.EqualTo(2));
            Assert.That(submitted[1], Is.EqualTo(submitted[0]));
            Assert.That(latestSale.Status, Is.EqualTo(WeaponSaleStatus.AlreadySold));
            Assert.That(Text("Status"), Does.Contain("30 코인"));
            Assert.That(Text("Coins"), Does.Contain("125"));
            Assert.That(panel.ActiveSnapshot!.WeaponSales, Has.Count.EqualTo(1));
            CollectionAssert.AreEqual(beforeRetry, File.ReadAllBytes(path));
        }

        private void OpenPanel()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(PathPrefix +
                "Weapons/PrototypeStarter/PrototypeStarterWeaponCatalog.asset");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PathPrefix + "WarehouseUI/WarehouseHangul.asset");
            var sourcePrices = AssetDatabase.LoadAssetAtPath<WeaponSalePrices>(PathPrefix +
                "WeaponSale/PrototypeWeaponSalePrices.asset");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(font, Is.Not.Null);
            Assert.That(sourcePrices, Is.Not.Null);
            prices = Object.Instantiate(sourcePrices);
            var run = new RunState(() => TimeSpan.Zero);
            var sale = new WeaponSaleService(store, run, catalog, prices);
            canvas = new GameObject("Warehouse Test Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960, 540);
            scaler.matchWidthOrHeight = .5f;

            var anchor = new GameObject("Warehouse Host", typeof(RectTransform), typeof(WarehousePanel));
            var rect = anchor.GetComponent<RectTransform>();
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            eventRoot = new GameObject("Warehouse Test EventSystem", typeof(EventSystem));
            raycaster = eventRoot.GetComponent<EventSystem>();
            panel = anchor.GetComponent<WarehousePanel>();
            panel.Initialize(catalog, font, prices, run, store.Load, (current, positions) =>
            {
                moves++;
                var saved = store.Commit(current.Revision, Copy(current, current.ConfirmedCoins, positions));
                return saved.Status == SnapshotCommitStatus.Committed ? saved.ActiveSnapshot : null;
            }, (request, weapon) =>
            {
                submitted.Add((request, weapon));
                latestSale = sale.TrySell(request, weapon);
                return afterSale(latestSale);
            });
            panel.Open();
        }

        private void Click(UnityEngine.UI.Button button)
        {
            Assert.That(button.interactable, Is.True, button.name);
            GameObject? target = TopHit(button);
            Assert.That(target, Is.EqualTo(button.gameObject), button.name);
            ExecuteEvents.Execute(button.gameObject, PointerFor(button), ExecuteEvents.pointerClickHandler);
        }

        private GameObject? TopHit(UnityEngine.UI.Button button)
        {
            Canvas.ForceUpdateCanvases();
            var hits = new List<RaycastResult>();
            raycaster.RaycastAll(PointerFor(button), hits);
            return hits.Count == 0 ? null : hits[0].gameObject;
        }

        private PointerEventData PointerFor(UnityEngine.UI.Button button)
        {
            var rect = (RectTransform)button.transform;
            return new PointerEventData(raycaster)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center))
            };
        }

        private UnityEngine.UI.Button Button(string name) => Buttons(name).Single();
        private UnityEngine.UI.Button FirstSwordRow() => Buttons("WarehouseWeapon").First(row => Label(row) == "낡은 검");
        private UnityEngine.UI.Button WarehouseWeapon(string name) => Buttons("WarehouseWeapon").Single(row => Label(row) == name);
        private UnityEngine.UI.Button[] Buttons(string name)
            => canvas!.GetComponentsInChildren<UnityEngine.UI.Button>().Where(button => button.name == name).ToArray();
        private string Text(string name)
            => canvas!.GetComponentsInChildren<TMP_Text>().Single(label => label.name == name).text;
        private static string Label(UnityEngine.UI.Button button) => button.GetComponentInChildren<TMP_Text>().text;

        private void Seed(params WeaponInstance[] items)
        {
            var snapshot = new SaveSnapshot(1, 90, items, Array.Empty<CatalogSaveRecord>(), Array.Empty<SettlementSaveRecord>());
            Assert.That(store.Commit(0, snapshot).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
        }

        private void SetPrice(int value)
        {
            var data = new SerializedObject(prices!);
            data.FindProperty("grade1").intValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private WeaponInstance Stored(WeaponInstance item) => store.Load()!.Weapons.Single(weapon => weapon.Id == item.Id);
        private static WeaponInstance Item(WeaponDefinitionId id)
            => WeaponInstance.Create(id, WeaponProvenance.Personal, WeaponLocation.Warehouse());
        private static SaveSnapshot Copy(SaveSnapshot before, long coins, IReadOnlyList<WeaponInstance> weapons)
            => new SaveSnapshot(before.Revision + 1, coins, weapons, before.Catalog, before.Settlements,
                before.CoinWeaponDraws, before.WeaponSales);
    }
}
#endif
