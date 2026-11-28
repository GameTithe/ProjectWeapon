#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Settlement;
using ProjectWeapon.SettlementUI;
using ProjectWeapon.SortieUI;
using ProjectWeapon.Wallet;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Room = ProjectWeapon.ZoneOneGreybox.ZoneOneGreybox;

namespace ProjectWeapon.SortieLoop
{
    [DisallowMultipleComponent]
    public sealed class SortieLoopHost : MonoBehaviour
    {
        private const string RoomScene = "ZoneOneGreybox";
        private const string ProfileDirectory = "ZoneOnePrototype";

        [SerializeField] private ZoneCatalog zones = null!;
        [SerializeField] private WeaponCatalog weapons = null!;
        [SerializeField] private TMP_FontAsset hangul = null!;
        [SerializeField] private GameObject lobby = null!;
        [SerializeField] private RectTransform equipmentAnchor = null!;
        [SerializeField] private SettlementResultsPanel results = null!;
        [SerializeField] private Button openEquipment = null!;
        [SerializeField] private Button retry = null!;
        [SerializeField] private TMP_Text lobbyStatus = null!;
        [SerializeField] private TMP_Text loading = null!;

        private AtomicSnapshotStore store = null!;
        private IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition> definitions = null!;
        private SaveSnapshot? profile;
        private InventoryService? town;
        private SortieEquipmentPanel? equipment;
        private ActiveSortie? active;
        private Func<double> clock = () => Time.realtimeSinceStartupAsDouble;
        private string? overriddenPath;
        private bool initialized;
        private bool returning;
        private bool refreshLobby;
        private bool cleaningFailedLoad;
        private Scene roomScene;
        private AsyncOperation? roomLoad;
        private TMP_Text retryLabel = null!;

        public Room? ActiveRoom { get; private set; }
        public SortieManifest? ActiveManifest => active?.Manifest;
        public bool IsInLobby => active == null && lobby.activeSelf;

        /// <summary>Optional isolated profile and clock, configured before this scene starts.</summary>
        public void Configure(string profilePath, Func<double> readTime)
        {
            if (initialized)
                throw new InvalidOperationException("Configure the profile before starting the lobby.");
            overriddenPath = profilePath ?? throw new ArgumentNullException(nameof(profilePath));
            clock = readTime ?? throw new ArgumentNullException(nameof(readTime));
        }

        private void Start()
        {
            initialized = true;
            definitions = weapons.CreateLookup();
            store = new AtomicSnapshotStore(overriddenPath ?? Path.Combine(Application.persistentDataPath,
                ProfileDirectory, "snapshot.json"));
            results.Initialize(weapons, hangul);
            openEquipment.onClick.AddListener(OpenEquipment);
            retryLabel = retry.GetComponentInChildren<TMP_Text>(true);
            retry.onClick.AddListener(RetryActive);
            results.ReturnToLobbyRequested += ReturnToLobby;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            ShowLobby();
        }

        private void LateUpdate()
        {
            if (!refreshLobby)
                return;

            refreshLobby = false;
            ShowLobby();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            if (active != null)
                active.Run.RunResultFinalized -= FinishSortie;
        }

        private void ShowLobby()
        {
            lobby.SetActive(true);
            retry.gameObject.SetActive(false);
            loading.gameObject.SetActive(false);
            openEquipment.interactable = false;
            if (equipment != null) equipment.gameObject.SetActive(false);
            try
            {
                SnapshotLoadResult loaded = store.LoadWithRecovery(definitions.ContainsKey);
                if (loaded.Status == SnapshotLoadStatus.Missing)
                    profile = CreateProfile();
                else
                    profile = loaded.Status == SnapshotLoadStatus.Healthy ? loaded.Snapshot : null;

                if (profile == null || profile.Weapons.Any(item => item.Location.Kind == WeaponLocationKind.RunReserved))
                {
                    lobbyStatus.text = "저장 복구 대기";
                    return;
                }
            }
            catch (IOException)
            {
                lobbyStatus.text = "저장 복구 대기";
                return;
            }
            catch (UnauthorizedAccessException)
            {
                lobbyStatus.text = "저장 복구 대기";
                return;
            }

            town = new InventoryService(profile.Weapons);
            int warehouseCount = profile.Weapons.Count(item => item.Location.Kind == WeaponLocationKind.Warehouse);
            lobbyStatus.text = $"창고 무기 {warehouseCount}개";
            BuildEquipmentPanel();
            openEquipment.interactable = true;
        }

        private SaveSnapshot? CreateProfile()
        {
            var sword = new WeaponDefinitionId("old_sword");
            var spare = new WeaponDefinitionId("mace");
            if (!definitions.ContainsKey(sword) || !definitions.ContainsKey(spare))
                throw new InvalidOperationException("The starter weapon definition is missing.");
            var items = new[]
            {
                WeaponInstance.Create(sword, WeaponProvenance.Personal, WeaponLocation.Warehouse()),
                WeaponInstance.Create(spare, WeaponProvenance.Personal, WeaponLocation.Warehouse())
            };
            var initial = new SaveSnapshot(1, 0, items, Array.Empty<CatalogSaveRecord>(),
                Array.Empty<SettlementSaveRecord>());
            SnapshotCommitResult creation = store.Commit(0, initial);
            return creation.Status == SnapshotCommitStatus.Committed ? initial : creation.ActiveSnapshot;
        }

        private void BuildEquipmentPanel()
        {
            if (equipment != null)
            {
                equipment.gameObject.SetActive(false);
                Destroy(equipment.gameObject);
            }
            var root = new GameObject("Sortie Equipment", typeof(RectTransform), typeof(SortieEquipmentPanel));
            var rect = (RectTransform)root.transform;
            rect.SetParent(equipmentAnchor, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            equipment = root.GetComponent<SortieEquipmentPanel>();
            equipment.Initialize(town!, weapons, hangul, IsReady, ConfirmSortie);
            equipment.Dismissed += () => lobby.SetActive(true);
            equipment.Confirmed += _ => LoadRoom();
        }

        private bool IsReady() => profile != null && active == null && !returning
            && Application.CanStreamedLevelBeLoaded(RoomScene) && !SceneManager.GetSceneByName(RoomScene).isLoaded;

        private void OpenEquipment()
        {
            if (!IsReady())
                return;
            lobby.SetActive(false);
            equipment!.Open();
        }

        private SortieStartResult ConfirmSortie()
        {
            if (!IsReady() || town == null || profile == null)
                return default;

            var proposedTown = new InventoryService(town.Snapshot());
            RunId id = RunId.New();
            proposedTown.PrepareStarterLoan(id.Value);
            double origin = clock();
            TimeSpan Elapsed() => TimeSpan.FromSeconds(clock() - origin);
            var run = new RunState(Elapsed);
            var countdown = new ZoneRunCountdown(zones, run);
            SortieStartResult start = new SortieStarter(proposedTown, countdown).TryStart(id, 1);
            if (start.Status != SortieStartStatus.Started || start.Manifest == null)
                return start;

            SaveSnapshot proposed = profile.Next(profile.ConfirmedCoins, weapons: proposedTown.Snapshot());
            try
            {
                SnapshotCommitResult commit = store.Commit(profile.Revision, proposed);
                if (commit.Status != SnapshotCommitStatus.Committed)
                {
                    refreshLobby = true;
                    return default;
                }
            }
            catch (IOException)
            {
                refreshLobby = true;
                return default;
            }
            catch (UnauthorizedAccessException)
            {
                refreshLobby = true;
                return default;
            }

            profile = proposed;
            active = new ActiveSortie(run, countdown, Elapsed, start.Manifest);
            run.RunResultFinalized += FinishSortie;
            return start;
        }

        private void LoadRoom()
        {
            if (active == null || active.Result != null || active.HasEntered || roomLoad != null || roomScene.isLoaded || cleaningFailedLoad)
                return;

            retry.gameObject.SetActive(false);
            loading.text = "폐성 1구역 입장 중";
            loading.gameObject.SetActive(true);
            try
            {
                roomLoad = SceneManager.LoadSceneAsync(RoomScene, LoadSceneMode.Additive);
                if (roomLoad != null)
                {
                    roomLoad.completed += _ =>
                    {
                        roomLoad = null;
                        if (ActiveRoom == null && !cleaningFailedLoad)
                            OfferRoomRetry();
                    };
                    return;
                }
            }
            catch (InvalidOperationException)
            {
                // The reservation is already durable. Retry this run without creating another.
            }
            OfferRoomRetry();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != RoomScene || mode != LoadSceneMode.Additive || active == null || roomLoad == null)
                return;

            roomScene = scene;
            Room? room = null;
            try
            {
                room = scene.GetRootGameObjects().Select(root => root.GetComponent<Room>())
                    .SingleOrDefault(candidate => candidate != null);
                if (room == null)
                    throw new InvalidOperationException("The loaded validation scene has no room host.");

                WeaponInstance equipped = active.Manifest.Equipped[0];
                definitions.TryGetValue(equipped.DefinitionId, out WeaponDefinition definition);
                room.BindRun(active.Run, active.Countdown, active.Clock, active.Field, definition);
                ActiveRoom = room;
                active.HasEntered = true;
                loading.gameObject.SetActive(false);
                return;
            }
            catch (InvalidOperationException)
            {
                // Keep the same confirmed reservation while releasing an unusable room instance.
            }
            catch (ArgumentException)
            {
                // A bind failure must never replace the previously saved run with a fresh one.
            }

            if (room != null)
                room.enabled = false;
            cleaningFailedLoad = true;
            SceneManager.UnloadSceneAsync(roomScene);
        }

        private void OfferRoomRetry()
        {
            if (active == null || active.Result != null)
                return;
            if (active.HasEntered)
            {
                retry.gameObject.SetActive(false);
                loading.text = "저장 복구 대기";
                loading.gameObject.SetActive(true);
                return;
            }

            retryLabel.text = "같은 탐험에 다시 입장";
            retry.gameObject.SetActive(true);
            loading.text = "폐성 1구역 입장 대기";
            loading.gameObject.SetActive(true);
        }

        private void RetryActive()
        {
            if (active?.Result != null)
                SettleActive();
            else
                LoadRoom();
        }

        private void FinishSortie(RunResult result)
        {
            if (active == null || active.Result != null || result.RunId != active.Run.CurrentRun?.Id)
                return;

            active.Result = result;
            active.HeldAtEnd = active.Field.Snapshot();
            ActiveRoom?.StopMovement();
            SettleActive();
        }

        private void SettleActive()
        {
            if (active?.Result is not { } result || active.HeldAtEnd == null || returning)
                return;

            RunSettlementCommit? commit = null;
            try
            {
                commit = new RunSettlementCoordinator(store).TrySettle(result, active.Manifest, active.HeldAtEnd,
                    active.Wallet);
            }
            catch (IOException)
            {
                // The same result and held items stay available for an explicit retry.
            }
            catch (UnauthorizedAccessException)
            {
                // The pending run remains reserved until its settlement can be read back.
            }

            if (commit != null)
                results.ShowCommit(commit);
            bool saved = commit?.Receipt?.RunId == result.RunId.Value;
            active.Receipt = saved ? commit!.Receipt : null;
            retryLabel.text = "정산 다시 시도";
            retry.gameObject.SetActive(!saved);
            loading.text = "정산을 다시 시도하세요.";
            loading.gameObject.SetActive(commit == null);
        }

        private void ReturnToLobby(Guid id)
        {
            if (returning || active?.Receipt is not { } receipt || receipt.RunId != id)
                return;

            returning = true;
            loading.text = "로비로 이동 중";
            loading.gameObject.SetActive(true);
            if (!roomScene.isLoaded)
            {
                FinishReturning();
                return;
            }
            if (SceneManager.UnloadSceneAsync(roomScene) != null)
                return;

            returning = false;
            loading.gameObject.SetActive(false);
            results.ShowReceipt(receipt);
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (scene != roomScene)
                return;

            roomScene = default;
            ActiveRoom = null;
            cleaningFailedLoad = false;
            if (returning)
                FinishReturning();
            else
                OfferRoomRetry();
        }

        private void FinishReturning()
        {
            active!.Run.RunResultFinalized -= FinishSortie;
            active.Run.TryReturnToTown(active.Result!.RunId);
            active = null;
            ActiveRoom = null;
            returning = false;
            ShowLobby();
        }

        private sealed class ActiveSortie
        {
            internal ActiveSortie(RunState run, ZoneRunCountdown countdown, Func<TimeSpan> clock, SortieManifest manifest)
            {
                Run = run;
                Countdown = countdown;
                Clock = clock;
                Manifest = manifest;
                Field = new InventoryService(manifest.Equipped);
                Wallet = new RunCoinAccumulator(run.CurrentRun!.Id);
            }

            internal RunState Run { get; }
            internal ZoneRunCountdown Countdown { get; }
            internal Func<TimeSpan> Clock { get; }
            internal SortieManifest Manifest { get; }
            internal InventoryService Field { get; }
            internal RunCoinAccumulator Wallet { get; }
            internal RunResult? Result { get; set; }
            internal IReadOnlyList<WeaponInstance>? HeldAtEnd { get; set; }
            internal SettlementSaveRecord? Receipt { get; set; }
            internal bool HasEntered { get; set; }
        }
    }
}
