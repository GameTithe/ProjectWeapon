#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.CoinWeaponDraw;
using ProjectWeapon.Inventory;
using ProjectWeapon.Save;
using ProjectWeapon.WeaponDraw;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectWeapon.CoinDrawUI.Tests
{
    public sealed class CoinDrawResultPanelTests : IPrebuildSetup, IPostBuildCleanup
    {
        private const string TmpFolder = "Assets/TextMesh Pro";
        private const string TmpFlag = "ProjectWeapon.KAN180.ImportedTmpForTest";
        private const string CatalogPath =
            "Assets/ProjectWeapon/Weapons/PrototypeStarter/PrototypeStarterWeaponCatalog.asset";
        private Scene scene;
        private TMP_FontAsset? font;
        private string profileDirectory = string.Empty;
        private AtomicSnapshotStore store = null!;
        private CoinDrawResultPanel panel = null!;
        private EventSystem eventSystem = null!;
        private int samples;
        private int selection;
        private bool injectRevisionConflict;

        public void Setup()
        {
            if (AssetDatabase.IsValidFolder(TmpFolder))
                return;
            SessionState.SetBool(TmpFlag, true);
            TMP_PackageResourceImporter.ImportResources(true, false, false);
            AssetDatabase.Refresh();
        }

        public void Cleanup()
        {
            if (!SessionState.GetBool(TmpFlag, false))
                return;
            AssetDatabase.DeleteAsset(TmpFolder);
            SessionState.EraseBool(TmpFlag);
        }

        [UnitySetUp]
        public IEnumerator Prepare()
        {
            string? family = Font.GetOSInstalledFontNames().FirstOrDefault(candidate =>
                candidate == "Malgun Gothic" || candidate == "Noto Sans CJK KR" || candidate == "Apple SD Gothic Neo");
            if (family == null)
                Assert.Ignore("A Hangul-capable font is required for the coin draw UI tests.");
            font = TMP_FontAsset.CreateFontAsset(family!, "Regular");
            Assert.That(font, Is.Not.Null);
            Assert.That(font!.TryAddCharacters(
                "0123456789#· 무기뽑회거래확인중결과같은요청다시정지급개현재창고획득도감발견조건입니다수등록보유한채성공귀환해야합니다코부족이른에사용되었습저장상태가뀌못했단검사냥활철퇴낡나무점-."), Is.True);

            profileDirectory = Path.Combine(Path.GetTempPath(), "ProjectWeapon.KAN180", Guid.NewGuid().ToString("N"));
            store = new AtomicSnapshotStore(Path.Combine(profileDirectory, "profile.json"));
            var initial = new SaveSnapshot(1, 5000, Array.Empty<WeaponInstance>(),
                Array.Empty<CatalogSaveRecord>(), Array.Empty<SettlementSaveRecord>());
            Assert.That(store.Commit(0, initial).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            samples = 0;
            selection = 0;
            injectRevisionConflict = false;

            scene = SceneManager.CreateScene($"CoinDraw-{Guid.NewGuid():N}");
            var canvasRoot = new GameObject("Draw Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvasRoot, scene);
            canvasRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasRoot.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960, 540);
            scaler.matchWidthOrHeight = .5f;
            var events = new GameObject("EventSystem", typeof(EventSystem));
            SceneManager.MoveGameObjectToScene(events, scene);
            eventSystem = events.GetComponent<EventSystem>();
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(CatalogPath);
            Assert.That(catalog, Is.Not.Null);
            panel = canvasRoot.AddComponent<CoinDrawResultPanel>();
            panel.Initialize(new CoinWeaponDrawService(store, CreateDraw(catalog)), catalog, Sample, font);
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
            if (Directory.Exists(profileDirectory))
            {
                foreach (string file in Directory.EnumerateFiles(profileDirectory))
                    File.Delete(file);
                Directory.Delete(profileDirectory);
            }
        }

        [UnityTest]
        public IEnumerator SingleClickAndDuplicateCallbackKeepOneReceiptAndReplayFromCurrentWarehouse()
        {
            Click("DrawSingle");
            Guid firstId = panel.RequestId!.Value;
            Button("DrawSingle").onClick.Invoke();
            Button("DrawTen").onClick.Invoke();
            Assert.That(store.Load()!.CoinWeaponDraws.Count, Is.EqualTo(1));
            Assert.That(store.Load()!.ConfirmedCoins, Is.EqualTo(4500));
            Assert.That(Status, Is.EqualTo("확정 지급 1개  ·  현재 창고 1개"));
            Assert.That(VisibleAwards(), Is.EqualTo(new[] { "#01  단검  ·  1등급" }));
            Assert.That(Find("DiscoveryNotice").GetComponent<TextMeshProUGUI>().text, Does.Contain("성공 귀환"));
            Assert.That(store.Load()!.Catalog, Is.Empty);
            Click("ConfirmDrawResult");

            SaveSnapshot saved = store.Load()!;
            WeaponInstance weapon = saved.Weapons.Single();
            var equipped = WeaponInstance.Rehydrate(weapon.Id, weapon.DefinitionId, weapon.Provenance,
                WeaponLocation.Equipped(0));
            Assert.That(store.Commit(saved.Revision, new SaveSnapshot(saved.Revision + 1, saved.ConfirmedCoins,
                new[] { equipped }, saved.Catalog, saved.Settlements, saved.CoinWeaponDraws)).Status,
                Is.EqualTo(SnapshotCommitStatus.Committed));

            Assert.That(panel.TryResume(firstId, 1), Is.True);
            Assert.That(samples, Is.EqualTo(2));
            Assert.That(store.Load()!.Revision, Is.EqualTo(saved.Revision + 1));
            Assert.That(Status, Is.EqualTo("확정 지급 1개  ·  현재 창고 0개"));
            Assert.That(VisibleAwards(), Is.EqualTo(new[] { "#01  단검  ·  1등급" }));
            yield return null;
        }

        [UnityTest]
        public IEnumerator TenResultsKeepCommitOrderIncludingDuplicateDefinitions()
        {
            Click("DrawTen");
            Canvas.ForceUpdateCanvases();
            SaveSnapshot active = store.Load()!;
            CoinWeaponDrawSaveRecord receipt = active.CoinWeaponDraws.Single();
            Assert.That(active.ConfirmedCoins, Is.Zero);
            Assert.That(receipt.Awards.Select(x => x.InstanceId).Distinct().Count(), Is.EqualTo(10));
            Assert.That(Status, Is.EqualTo("확정 지급 10개  ·  현재 창고 10개"));
            string[] labels = VisibleAwards();
            Assert.That(labels, Has.Length.EqualTo(10));
            Assert.That(labels[0], Is.EqualTo("#01  단검  ·  1등급"));
            Assert.That(labels[1], Is.EqualTo("#02  사냥 활  ·  1등급"));
            Assert.That(labels[5], Is.EqualTo("#06  단검  ·  1등급"));
            Assert.That(labels[9], Does.StartWith("#10"));
            Assert.That(Find("Award10").GetComponent<RectTransform>().rect.height, Is.Positive);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RevisionConflictRetriesTheSameIdAndInsufficientFundsShowsNoAwards()
        {
            injectRevisionConflict = true;
            Click("DrawTen");
            Guid pending = panel.RequestId!.Value;
            Assert.That(panel.RequestCount, Is.EqualTo(10));
            Assert.That(Status, Does.Contain("저장 상태"));
            Assert.That(VisibleAwards(), Is.Empty);
            Assert.That(store.Load()!.Weapons, Is.Empty);
            Assert.That(panel.TryDraw(1), Is.False);
            panel.Acknowledge();
            Assert.That(panel.RequestId, Is.EqualTo(pending));

            Click("RetryDraw");
            Assert.That(store.Load()!.CoinWeaponDraws.Single().RequestId, Is.EqualTo(pending));
            Assert.That(store.Load()!.Weapons.Count, Is.EqualTo(10));
            Assert.That(store.Load()!.ConfirmedCoins, Is.Zero);
            Click("ConfirmDrawResult");
            Click("DrawSingle");
            Assert.That(Status, Is.EqualTo("코인이 부족합니다."));
            Assert.That(VisibleAwards(), Is.Empty);
            Assert.That(store.Load()!.CoinWeaponDraws.Count, Is.EqualTo(1));
            yield return null;
        }

        private int Sample(int limit)
        {
            samples++;
            if (injectRevisionConflict)
            {
                injectRevisionConflict = false;
                SaveSnapshot current = store.Load()!;
                Assert.That(store.Commit(current.Revision, new SaveSnapshot(current.Revision + 1,
                    current.ConfirmedCoins, current.Weapons, current.Catalog, current.Settlements,
                    current.CoinWeaponDraws)).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            }
            return limit == 10000 ? 0 : selection++ % limit;
        }

        private static WeightedWeaponDraw CreateDraw(WeaponCatalog catalog)
        {
            var grades = catalog.CreateLookup().ToDictionary(entry => entry.Key, entry => entry.Value.Grade);
            var pools = new List<IReadOnlyList<WeaponDefinitionId>>
            {
                grades.Keys.ToArray()
            };
            for (int grade = 2; grade <= 5; grade++)
            {
                var pool = new List<WeaponDefinitionId>();
                for (int index = 0; index < (grade <= 3 ? 5 : 3); index++)
                {
                    var id = new WeaponDefinitionId($"g{grade}-{index}");
                    pool.Add(id);
                    grades.Add(id, grade);
                }
                pools.Add(pool);
            }
            return new WeightedWeaponDraw(WeightedWeaponDraw.DraftGradeWeightsBasisPoints, pools, grades);
        }

        private Transform Find(string name)
            => panel.GetComponentsInChildren<Transform>(true).Single(item => item.name == name);

        private UnityEngine.UI.Button Button(string name) => Find(name).GetComponent<UnityEngine.UI.Button>();

        private void Click(string name)
            => Button(name).OnPointerClick(new PointerEventData(eventSystem) { button = PointerEventData.InputButton.Left });

        private string Status => Find("Status").GetComponent<TextMeshProUGUI>().text;

        private string[] VisibleAwards()
            => Find("Awards").GetComponentsInChildren<TextMeshProUGUI>().Select(item => item.text).ToArray();
    }
}
#endif
