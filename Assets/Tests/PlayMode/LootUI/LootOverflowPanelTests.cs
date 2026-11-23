#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Loot;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectWeapon.LootUI.PlayModeTests
{
    public sealed class LootOverflowPanelTests : IPrebuildSetup, IPostBuildCleanup
    {
        private const string TmpFolder = "Assets/TextMesh Pro";
        private const string TmpSetupFlag = "ProjectWeapon.KAN183.ImportedTmpForTest";

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

        [UnityTest]
        public IEnumerator SelectionShowsCatalogDetailsAndChangesOwnershipOnlyOnConfirm()
        {
            using var scenario = new Scenario("hunting_bow");
            yield return null;
            scenario.Step();
            yield return null;
            Canvas.ForceUpdateCanvases();

            Assert.That(scenario.Find("LootOverflowModal").gameObject.activeInHierarchy, Is.True);
            Assert.That(scenario.Find("Incoming").GetComponent<TextMeshProUGUI>().text,
                Is.EqualTo("사냥 활 · 등급 1 · 활"));

            UnityEngine.UI.Button confirm = scenario.Button("Confirm");
            var row = scenario.Find("Content").GetComponentInChildren<UnityEngine.UI.Button>();
            Assert.That(row.GetComponentInChildren<TextMeshProUGUI>().text, Does.Contain("낡은 검 · 등급 1 · 검"));
            Assert.That(scenario.Find("Card").GetComponent<RectTransform>().rect.width, Is.GreaterThan(0));
            Assert.That(row.GetComponent<RectTransform>().rect.height, Is.GreaterThan(0));
            Assert.That(confirm.interactable, Is.False);

            scenario.Click(confirm);
            Assert.That(scenario.Inventory.Snapshot().Single(), Is.SameAs(scenario.Existing));
            scenario.Click(row);
            Assert.That(confirm.interactable, Is.True);
            Assert.That(scenario.Inventory.Snapshot().Single(), Is.SameAs(scenario.Existing));

            scenario.Click(confirm);
            Assert.That(scenario.Inventory.Snapshot().Single().Id, Is.EqualTo(scenario.Incoming.Id));
            Assert.That(scenario.Results.Last().Displaced, Is.SameAs(scenario.Existing));
            Assert.That(scenario.Find("LootOverflowModal").gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator MissingComparisonDisablesConfirmAndCancelPreservesEquipment()
        {
            using var scenario = new Scenario("unregistered");
            yield return null;
            scenario.Step();
            yield return null;

            Assert.That(scenario.Find("Incoming").GetComponent<TextMeshProUGUI>().text,
                Does.Contain("비교 정보 없음"));
            Assert.That(scenario.Find("Content").GetComponentInChildren<UnityEngine.UI.Button>().interactable,
                Is.False);
            UnityEngine.UI.Button confirm = scenario.Button("Confirm");
            Assert.That(confirm.interactable, Is.False);
            scenario.Click(confirm);
            scenario.Click(scenario.Button("Cancel"));

            Assert.That(scenario.Results.Last().Status, Is.EqualTo(InventoryPickupStatus.Cancelled));
            Assert.That(scenario.Inventory.Snapshot().Single(), Is.SameAs(scenario.Existing));
            Assert.That(scenario.Find("LootOverflowModal").gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator ChangedBagInvalidatesTheDisplayedChoice()
        {
            using var scenario = new Scenario("hunting_bow");
            yield return null;
            scenario.Step();
            yield return null;
            scenario.Click(scenario.Find("Content").GetComponentInChildren<UnityEngine.UI.Button>());
            Assert.That(scenario.Inventory.Move(scenario.Existing.Id, WeaponLocation.Carried(0), WeaponLocation.Warehouse()),
                Is.EqualTo(InventoryMoveResult.Moved));

            UnityEngine.UI.Button confirm = scenario.Button("Confirm");
            scenario.Click(confirm);

            Assert.That(scenario.Results.Last().Status, Is.EqualTo(InventoryPickupStatus.StaleChoice));
            Assert.That(confirm.interactable, Is.False);
            Assert.That(scenario.Find("Status").GetComponent<TextMeshProUGUI>().text, Does.Contain("바뀌었습니다"));
            Assert.That(scenario.Inventory.Snapshot().Single().Location.Kind, Is.EqualTo(WeaponLocationKind.Warehouse));
            scenario.Click(scenario.Button("Cancel"));
            Assert.That(scenario.Find("LootOverflowModal").gameObject.activeSelf, Is.False);
        }

        private sealed class Scenario : IDisposable
        {
            private readonly Scene scene;
            private readonly TMP_FontAsset tmpFont;
            private readonly LootOverflowPanel panel;
            private readonly EventSystem events;

            public Scenario(string incomingDefinition)
            {
                Assert.That(Resources.Load<TMP_Settings>("TMP Settings"), Is.Not.Null);
                string? family = Font.GetOSInstalledFontNames().FirstOrDefault(candidate =>
                    candidate == "Malgun Gothic" || candidate == "Noto Sans CJK KR" || candidate == "Apple SD Gothic Neo");
                if (family == null)
                    Assert.Ignore("A Hangul-capable system font is required for this local UI rendering test.");

                tmpFont = TMP_FontAsset.CreateFontAsset(family!, "Regular");
                Assert.That(tmpFont, Is.Not.Null);
                Assert.That(tmpFont.TryAddCharacters("가방이 가득 찼습니다 낡은 검 사냥 활"), Is.True);
                var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(
                    "Assets/ProjectWeapon/Weapons/PrototypeStarter/PrototypeStarterWeaponCatalog.asset");
                Assert.That(catalog, Is.Not.Null);

                scene = SceneManager.CreateScene($"LootOverflow-{Guid.NewGuid():N}", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
                var picker = NewRoot("Picker");
                picker.AddComponent<Rigidbody2D>().gravityScale = 0;
                LootPickupTrigger trigger = picker.AddComponent<LootPickupTrigger>();
                var sensor = picker.GetComponent<CircleCollider2D>();
                sensor.isTrigger = true;
                sensor.radius = 1;

                Existing = WeaponInstance.Create(new WeaponDefinitionId("old_sword"), WeaponProvenance.Personal,
                    WeaponLocation.Carried(0));
                Incoming = WeaponInstance.Create(new WeaponDefinitionId(incomingDefinition), WeaponProvenance.ExplorationLoot,
                    WeaponLocation.Warehouse());
                Inventory = new InventoryService(new[] { Existing }, new InventorySlotCapacity(0, 1));
                trigger.Initialize(new LootPickupCoordinator(Inventory));
                trigger.OutcomeReported += (_, _, result) => Results.Add(result);

                var source = NewRoot("Loot");
                source.transform.position = new Vector3(0.7f, 0);
                source.AddComponent<CircleCollider2D>().radius = 0.1f;
                source.AddComponent<WorldLoot>().Initialize(Incoming);

                var canvasObject = NewRoot("UI Root", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler),
                    typeof(UnityEngine.UI.GraphicRaycaster));
                canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                events = NewRoot("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
                panel = canvasObject.AddComponent<LootOverflowPanel>();
                panel.Initialize(trigger, catalog, tmpFont);
            }

            public InventoryService Inventory { get; }
            public WeaponInstance Existing { get; }
            public WeaponInstance Incoming { get; }
            public List<InventoryPickupResult> Results { get; } = new List<InventoryPickupResult>();

            public Transform Find(string element) => panel.GetComponentsInChildren<Transform>(true)
                .First(child => child.name == element);

            public UnityEngine.UI.Button Button(string element) => Find(element).GetComponent<UnityEngine.UI.Button>();

            public void Click(UnityEngine.UI.Button button)
            {
                Canvas.ForceUpdateCanvases();
                RectTransform rect = (RectTransform)button.transform;
                var pointer = new PointerEventData(events)
                {
                    button = PointerEventData.InputButton.Left,
                    position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center))
                };
                var results = new List<RaycastResult>();
                events.RaycastAll(pointer, results);
                Assert.That(results, Is.Not.Empty,
                    $"Pointer {pointer.position}, screen {Screen.width}x{Screen.height}, depth {button.image.depth}, cull {button.image.canvasRenderer.cull}");
                Assert.That(results[0].gameObject, Is.SameAs(button.gameObject));
                ExecuteEvents.Execute(results[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
            }

            public void Step()
            {
                Physics2D.SyncTransforms();
                PhysicsScene2D physics = scene.GetPhysicsScene2D();
                Assert.That(physics.Simulate(0.02f), Is.True);
                Assert.That(physics.Simulate(0.02f), Is.True);
            }

            public void Dispose()
            {
                SceneManager.UnloadSceneAsync(scene);
                UnityEngine.Object.Destroy(tmpFont);
            }

            private GameObject NewRoot(string name, params Type[] components)
            {
                var root = new GameObject(name, components);
                SceneManager.MoveGameObjectToScene(root, scene);
                return root;
            }
        }
    }
}
#endif
