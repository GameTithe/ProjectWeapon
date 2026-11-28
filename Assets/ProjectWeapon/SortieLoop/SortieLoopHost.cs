#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectWeapon.CatalogUI;
using ProjectWeapon.CombatHud;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Inventory;
using ProjectWeapon.Movement;
using ProjectWeapon.Run;
using ProjectWeapon.RunRecovery;
using ProjectWeapon.Save;
using ProjectWeapon.Settlement;
using ProjectWeapon.SettlementUI;
using ProjectWeapon.SortieUI;
using ProjectWeapon.Wallet;
using ProjectWeapon.WarehouseUI;
using ProjectWeapon.WeaponSale;
using ProjectWeapon.WeaponSwitch;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Room = ProjectWeapon.ZoneOneGreybox.ZoneOneGreybox;
using Encounter = ProjectWeapon.ZoneOneGreybox.ZoneOneEncounter;

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
        [SerializeField] private Button openWarehouse = null!;
        [SerializeField] private WarehousePanel warehouse = null!;
        [SerializeField] private TMP_FontAsset warehouseHangul = null!;
        [SerializeField] private WeaponSalePrices warehousePrices = null!;
        [SerializeField] private Button openCatalog = null!;
        [SerializeField] private CatalogListPanel catalogList = null!;
        [SerializeField] private TMP_FontAsset catalogHangul = null!;
        [SerializeField] private Button retry = null!;
        [SerializeField] private TMP_Text lobbyStatus = null!;
        [SerializeField] private TMP_Text loading = null!;
        [SerializeField] private Canvas combatCanvas = null!;
        [SerializeField] private VirtualJoystick combatJoystick = null!;
        [SerializeField] private MovementSettings combatMovementSettings = null!;

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
        private bool recoveryPending;
        private RunLifecycleRelay lifecycle = null!;
        private Scene roomScene;
        private AsyncOperation? roomLoad;
        private TMP_Text retryLabel = null!;

        public Room? ActiveRoom { get; private set; }
        public CombatActionHud? ActiveHud { get; private set; }
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
            lifecycle = gameObject.AddComponent<RunLifecycleRelay>();
            lifecycle.RecoveryChanged += HandleRecovery;
            definitions = weapons.CreateLookup();
            store = new AtomicSnapshotStore(overriddenPath ?? Path.Combine(Application.persistentDataPath,
                ProfileDirectory, "snapshot.json"));
            results.Initialize(weapons, hangul);
            openEquipment.onClick.AddListener(OpenEquipment);
            openWarehouse.onClick.AddListener(OpenWarehouse);
            openCatalog.onClick.AddListener(OpenCatalog);
            catalogList.Initialize(catalogHangul, ReadCatalogSnapshot);
            catalogList.Closed += () => lobby.SetActive(true);
            var townRun = new RunState(() => TimeSpan.Zero);
            var sales = new WeaponSaleService(store, townRun, weapons, warehousePrices);
            warehouse.Initialize(weapons, warehouseHangul, warehousePrices, townRun, ReadWarehouse,
                CommitWarehouseMove, (request, weapon) => IsWarehouseOpen ? sales.TrySell(request, weapon) : default);
            warehouse.Closed += ShowLobby;
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
            if (lifecycle)
                lifecycle.RecoveryChanged -= HandleRecovery;
            if (active != null)
                active.Run.RunResultFinalized -= FinishSortie;
        }

        private void BindCombatHud(Room room, ActiveSortie sortie)
        {
            PlayerMovement movement = room.Movement
                ?? throw new InvalidOperationException("The combat room needs a movement controller.");
            var equipment = new EquippedWeaponSelection(sortie.Run, sortie.Field, weapons,
                () => movement.IsDodging, new WeaponActionLocks());
            Encounter encounter = room.Encounter
                ? room.Encounter : throw new InvalidOperationException("The combat room needs an encounter.");
            var skills = encounter.BindPlayerEquipment(equipment, () => movement.IsDodging);

            ActiveHud = CombatActionHud.Create(combatCanvas, combatJoystick, hangul);
            ActiveHud.Bind(new CombatHudBindings(sortie.Run, movement, combatMovementSettings, sortie.Field,
                equipment, skills, sortie.Clock, () => CanControlRoom(room), () => room.CanStartExtraction,
                room.TryStartExtraction, () => movement.IsDodging, Encounter.SupportsManualSkill));
        }

        private bool CanControlRoom(Room room) => room && room == ActiveRoom && !returning && !lifecycle.IsGameplayBlocked
            && active is { Result: null, Receipt: null };

        private void RefreshCombatHud()
        {
            if (!ActiveHud)
                return;

            ActiveHud!.SetVisible(ActiveRoom && CanControlRoom(ActiveRoom!));
        }

        private void RemoveCombatHud()
        {
            if (ActiveHud)
            {
                ActiveHud!.gameObject.SetActive(false);
                Destroy(ActiveHud.gameObject);
            }
            ActiveHud = null;
        }

        private void ShowLobby()
        {
            town = null;
            lobby.SetActive(true);
            retry.gameObject.SetActive(false);
            loading.gameObject.SetActive(false);
            openEquipment.interactable = false;
            openWarehouse.interactable = false;
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
            openWarehouse.interactable = true;
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

        private bool IsReady() => town != null && profile != null && active == null && !returning && lifecycle.IsAvailable
            && Application.CanStreamedLevelBeLoaded(RoomScene) && !SceneManager.GetSceneByName(RoomScene).isLoaded;

        private void OpenEquipment()
        {
            if (!lobby.activeSelf || !IsReady())
                return;
            lobby.SetActive(false);
            equipment!.Open();
        }

        private void OpenCatalog()
        {
            if (active != null || returning || !lobby.activeSelf || !lifecycle.IsAvailable)
                return;
            lobby.SetActive(false);
            catalogList.Open();
        }

        private SaveSnapshot? ReadCatalogSnapshot()
        {
            try
            {
                // The read-only roster can interpret saved entries beyond the loaded combat definitions.
                return store.Load();
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException
                || error is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private bool IsWarehouseOpen => active == null && !returning && warehouse.IsOpen;

        private void OpenWarehouse()
        {
            if (active != null || returning || !lobby.activeSelf || !openWarehouse.interactable)
                return;

            lobby.SetActive(false);
            warehouse.Open();
        }

        private SaveSnapshot? ReadWarehouse()
        {
            if (!IsWarehouseOpen)
                return null;

            SnapshotLoadResult loaded = store.LoadWithRecovery(definitions.ContainsKey);
            return loaded.Status == SnapshotLoadStatus.Healthy ? loaded.Snapshot : null;
        }

        private SaveSnapshot? CommitWarehouseMove(SaveSnapshot expected, IReadOnlyList<WeaponInstance> positions)
        {
            if (!IsWarehouseOpen || expected.Weapons.Any(item => item.Location.Kind == WeaponLocationKind.RunReserved))
                return null;

            SaveSnapshot proposal = expected.Next(expected.ConfirmedCoins, weapons: positions);
            SnapshotCommitResult result = store.Commit(expected.Revision, proposal);
            return result.Status is SnapshotCommitStatus.Committed or SnapshotCommitStatus.AlreadyCommitted
                ? result.ActiveSnapshot : null;
        }

        private SortieStartResult ConfirmSortie()
        {
            if (!IsReady() || warehouse.IsOpen || town == null || profile == null)
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
            var recovery = new RunRecoveryCoordinator(run, countdown.Extraction, store, definitions.ContainsKey,
                (snapshot, id) => id.Value == start.Manifest.RunId && start.Manifest.MatchesReservations(snapshot.Weapons));
            lifecycle.Initialize(recovery, () => ActiveRoom?.DrainAcceptedDodgeSignals() ?? Array.Empty<RunSignal>(), HasLiveWorld);
            return start;
        }

        private bool HasLiveWorld() => active is { } sortie && (!sortie.HasEntered || ActiveRoom != null);

        private void HandleRecovery(RunRecoveryResult recovery)
        {
            if (active == null || returning)
                return;

            ActiveRoom?.SetRecoveryBlocked(lifecycle.IsGameplayBlocked);
            recoveryPending = recovery.Status == RunRecoveryStatus.RecoveryPending;
            RefreshCombatHud();
            switch (recovery.Status)
            {
                case RunRecoveryStatus.Resumed:
                    if (ActiveRoom != null)
                    {
                        retry.gameObject.SetActive(false);
                        loading.gameObject.SetActive(false);
                    }
                    else if (roomLoad == null)
                    {
                        OfferRoomRetry();
                    }
                    break;
                case RunRecoveryStatus.Finalized when lifecycle.IsAvailable:
                    if (active.Result != null)
                        SettleActive();
                    break;
                case RunRecoveryStatus.Settled when recovery.Receipt is { } receipt:
                    active.Receipt = receipt;
                    RemoveCombatHud();
                    if (active.Result == null)
                        lifecycle.ClearRun();
                    retry.gameObject.SetActive(false);
                    loading.gameObject.SetActive(false);
                    results.ShowReceipt(receipt);
                    break;
                case RunRecoveryStatus.RecoveryPending:
                    retryLabel.text = "복구 다시 시도";
                    retry.gameObject.SetActive(lifecycle.IsAvailable);
                    loading.text = "저장 복구 대기";
                    loading.gameObject.SetActive(true);
                    break;
            }
        }

        private void LoadRoom()
        {
            if (active == null || lifecycle.IsGameplayBlocked || active.Result != null || active.Receipt != null || active.HasEntered
                || roomLoad != null || roomScene.isLoaded || cleaningFailedLoad)
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
                        if (returning && !roomScene.isLoaded && !cleaningFailedLoad)
                            FinishReturning();
                        else if (ActiveRoom == null && !cleaningFailedLoad)
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
                if (returning || active.Result != null || active.Receipt != null)
                    throw new InvalidOperationException("The reserved run ended before its room loaded.");

                WeaponInstance equipped = active.Manifest.Equipped[0];
                definitions.TryGetValue(equipped.DefinitionId, out WeaponDefinition definition);
                room.BindRun(active.Run, active.Countdown, active.Clock, active.Field, definition);
                BindCombatHud(room, active);
                ActiveRoom = room;
                active.HasEntered = true;
                room.SetRecoveryBlocked(lifecycle.IsGameplayBlocked);
                RefreshCombatHud();
                if (!recoveryPending)
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

            RemoveCombatHud();
            if (room != null)
                room.enabled = false;
            cleaningFailedLoad = true;
            SceneManager.UnloadSceneAsync(roomScene);
        }

        private void OfferRoomRetry()
        {
            if (active == null || active.Result != null || active.Receipt != null)
                return;
            if (lifecycle.IsGameplayBlocked)
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
            if (!lifecycle.IsAvailable)
                return;
            if (recoveryPending && active?.Run.CurrentRun is { } session)
                lifecycle.Reconnect(session.Id);
            else if (active?.Result != null)
                SettleActive();
            else
                LoadRoom();
        }

        private void FinishSortie(RunResult result)
        {
            if (active == null || active.Result != null || active.Receipt != null || result.RunId != active.Run.CurrentRun?.Id)
                return;

            active.Result = result;
            active.HeldAtEnd = active.Field.Snapshot();
            RemoveCombatHud();
            ActiveRoom?.StopMovement();
            if (!lifecycle.IsGameplayBlocked)
                SettleActive();
        }

        private void SettleActive()
        {
            if (active?.Result is not { } result || active.HeldAtEnd == null || returning || recoveryPending)
                return;

            RunSettlementCommit? commit = null;
            try
            {
                commit = new RunSettlementCoordinator(store).TrySettle(result, active.Manifest, active.HeldAtEnd,
                    active.Wallet, requireExactReservations: true);
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
            if (roomLoad != null || cleaningFailedLoad)
                return;
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

            RemoveCombatHud();
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
            lifecycle.ClearRun();
            recoveryPending = false;
            active!.Run.RunResultFinalized -= FinishSortie;
            if (active.Result is { } result)
                active.Run.TryReturnToTown(result.RunId);
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
