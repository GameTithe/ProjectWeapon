#nullable enable

using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Save;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ProjectWeapon.CatalogUI.Tests
{
    public sealed class CatalogListPanelTests
    {
        private GameObject canvas = null!;
        private CatalogListPanel panel = null!;
        private EventSystem eventSystem = null!;
        private string directory = null!;
        private string path = null!;
        private AtomicSnapshotStore store = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN179-" + Guid.NewGuid().ToString("N"));
            path = Path.Combine(directory, "snapshot.json");
            store = new AtomicSnapshotStore(path);
            canvas = new GameObject("Catalog Test Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            eventSystem = new GameObject("Catalog Test EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            var root = (RectTransform)new GameObject("Catalog", typeof(RectTransform), typeof(CatalogListPanel)).transform;
            root.SetParent(canvas.transform, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            panel = root.GetComponent<CatalogListPanel>();
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/ProjectWeapon/CatalogUI/CatalogHangul.asset");
            Assert.That(font, Is.Not.Null);
            panel.Initialize(font, store.Load);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(canvas);
            UnityEngine.Object.DestroyImmediate(eventSystem.gameObject);
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }

        [UnityTest]
        public IEnumerator DisplaysTwentyOneEntriesAndPaintsFourStagesWithoutWritingInventory()
        {
            var records = new[]
            {
                Progress("old_sword", "discovered"), Progress("wooden_spear", "recovered"),
                Progress("hunting_bow", "mastered"), Progress("dagger", "future_state"),
                Progress("unlisted_dlc_weapon", "mastered")
            };
            var item = WeaponInstance.Create(new WeaponDefinitionId("old_sword"), WeaponProvenance.Personal,
                WeaponLocation.Warehouse());
            var snapshot = new SaveSnapshot(1, 90, new[] { item }, records, Array.Empty<SettlementSaveRecord>());
            Assert.That(store.Commit(0, snapshot).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            byte[] before = File.ReadAllBytes(path);

            panel.Open();
            yield return null;
            var list = panel.GetComponentInChildren<ScrollRect>();
            Assert.That(list.content.childCount, Is.EqualTo(21));
            Assert.That(list.verticalNormalizedPosition, Is.EqualTo(1f).Within(.001f));
            CollectionAssert.AreEqual(new[] { 5, 5, 5, 3, 3 }, list.content.Cast<Transform>()
                .GroupBy(row => Label(row, "Weapon Grade")).Select(group => group.Count()).ToArray());
            AssertRow("old_sword", "???", "발견", hasShape: true);
            AssertRow("wooden_spear", "나무 창", "회수", hasShape: true);
            AssertRow("hunting_bow", "사냥 활", "숙련", hasShape: true);
            AssertRow("mace", "???", "미발견", hasShape: false);
            AssertRow("dagger", "???", "기록 확인 필요", hasShape: false);
            Assert.That(Label(list.content.Find("wooden_spear"), "Acquisition"), Does.StartWith("폐성 1구역 상자"));
            Assert.That(Label(list.content.Find("genesis_bow"), "Acquisition"), Does.StartWith("상위 구역 탐험"));
            Assert.That(panel.GetComponentsInChildren<TMP_Text>().Single(label => label.name == "Recovery Count").text,
                Is.EqualTo("회수 2 / 21"));

            list.verticalScrollbar.value = 0;
            yield return null;
            Assert.That(list.content.anchoredPosition.y, Is.GreaterThan(0));
            bool closed = false;
            panel.Closed += () => closed = true;
            Button close = panel.GetComponentsInChildren<Button>().Single(button => button.name == "Close Catalog");
            close.OnPointerClick(new PointerEventData(eventSystem) { button = PointerEventData.InputButton.Left });
            Assert.That(closed, Is.True);
            Assert.That(panel.gameObject.activeSelf, Is.False);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            Assert.That(store.Load()!.Weapons.Single().Id, Is.EqualTo(item.Id));
        }

        [UnityTest]
        public IEnumerator ReopeningReadsCurrentSaveAndMissingSaveShowsRecovery()
        {
            panel.Open();
            yield return null;
            Assert.That(panel.GetComponentInChildren<ScrollRect>().viewport.gameObject.activeSelf, Is.False);
            Assert.That(panel.GetComponentsInChildren<TMP_Text>().Single(label => label.name == "Recovery Count").text,
                Is.EqualTo("저장 복구 대기"));
            panel.Close();

            var snapshot = new SaveSnapshot(1, 0, Array.Empty<WeaponInstance>(),
                new[] { Progress("dagger", "recovered") }, Array.Empty<SettlementSaveRecord>());
            Assert.That(store.Commit(0, snapshot).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            panel.Open();
            yield return null;
            AssertRow("dagger", "단검", "회수", hasShape: true);
            Assert.That(panel.GetComponentInChildren<ScrollRect>().viewport.gameObject.activeSelf, Is.True);
        }

        private void AssertRow(string id, string name, string stage, bool hasShape)
        {
            Transform row = panel.GetComponentInChildren<ScrollRect>().content.Find(id);
            Assert.That(Label(row, "Weapon Name"), Is.EqualTo(name));
            Assert.That(Label(row, "Catalog Stage"), Is.EqualTo(stage));
            Assert.That(row.Find("Weapon Tile/Weapon Silhouette").gameObject.activeSelf, Is.EqualTo(hasShape));
        }

        private static CatalogSaveRecord Progress(string id, string state) =>
            new CatalogSaveRecord(new WeaponDefinitionId(id), state);

        private static string Label(Transform owner, string name) => owner.Find(name).GetComponent<TMP_Text>().text;
    }
}
