#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Inventory;
using ProjectWeapon.Loot;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.LootUI
{
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class LootOverflowPanel : MonoBehaviour
    {
        [SerializeField] private LootPickupTrigger? pickup;
        [SerializeField] private WeaponCatalog? weaponCatalog;
        [SerializeField] private TMP_FontAsset? textFont;

        private readonly Queue<(WorldLoot? Source, InventoryPickupChoice Choice)> waiting =
            new Queue<(WorldLoot?, InventoryPickupChoice)>();
        private IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition> definitions =
            new Dictionary<WeaponDefinitionId, WeaponDefinition>();

        private LootOverflowView? view;
        private InventoryPickupChoice? active;
        private WorldLoot? source;
        private WeaponInstanceId? selected;
        private bool hasIncomingDetails;
        private bool dismissedByInventory;
        private bool listening;

        public bool IsOpen => active != null;

        public void Initialize(LootPickupTrigger trigger, WeaponCatalog? catalog, TMP_FontAsset font)
        {
            if (!trigger)
                throw new ArgumentNullException(nameof(trigger));
            if (!font)
                throw new ArgumentNullException(nameof(font));
            if (view != null)
                throw new InvalidOperationException("This overflow panel is already initialized.");
            if (!Resources.Load<TMP_Settings>("TMP Settings"))
                throw new InvalidOperationException("Import TMP Essential Resources before initializing the overflow panel.");

            pickup = trigger;
            definitions = ReadCatalog(catalog);
            view = new LootOverflowView((RectTransform)transform, font, Select, Confirm, Cancel);
            Listen();
        }

        private void Awake()
        {
            if (pickup && textFont && Resources.Load<TMP_Settings>("TMP Settings"))
                Initialize(pickup!, weaponCatalog, textFont!);
        }

        private void OnEnable() => Listen();

        private void OnDisable()
        {
            if (!listening || !pickup)
                return;

            pickup!.OutcomeReported -= Report;
            listening = false;

            if (active != null && !dismissedByInventory)
                pickup.TryResolveChoice(null, active, null);

            while (waiting.Count > 0)
                pickup.TryResolveChoice(null, waiting.Dequeue().Choice, null);

            Hide();
        }

        private void Listen()
        {
            if (listening || !pickup || view == null || !isActiveAndEnabled)
                return;

            pickup!.OutcomeReported += Report;
            listening = true;
        }

        private void Report(WorldLoot? worldLoot, WeaponInstanceId id, InventoryPickupResult result)
        {
            if (result.Status == InventoryPickupStatus.ChoiceRequired && result.Choice is InventoryPickupChoice choice)
            {
                if (active == null)
                    Show(worldLoot, choice);
                else
                    waiting.Enqueue((worldLoot, choice));
                return;
            }

            if (active == null || active.Loot.Id != id)
                return;

            switch (result.Status)
            {
                case InventoryPickupStatus.PickedUp:
                case InventoryPickupStatus.Cancelled:
                    Advance();
                    break;
                case InventoryPickupStatus.StaleChoice:
                case InventoryPickupStatus.DuplicateInstance:
                    dismissedByInventory = true;
                    ResetSelection();
                    view!.SetStatus("가방 상태가 바뀌었습니다. 창을 닫고 다시 시도하세요.");
                    break;
                case InventoryPickupStatus.InvalidSelection:
                    ResetSelection();
                    view!.SetStatus("교체할 무기를 다시 선택하세요.");
                    break;
            }
        }

        private void Show(WorldLoot? worldLoot, InventoryPickupChoice choice)
        {
            active = choice;
            source = worldLoot;
            selected = null;
            dismissedByInventory = false;
            string incoming = Describe(choice.Loot, out hasIncomingDetails);
            view!.Open(incoming, hasIncomingDetails ? string.Empty : "새 무기의 비교 정보가 없습니다.");

            foreach (WeaponInstance item in choice.Candidates)
            {
                string description = Describe(item, out bool hasDetails);
                view.AddCandidate(item.Id, $"{item.Location.SlotIndex.GetValueOrDefault() + 1}번 · {description}",
                    hasDetails && hasIncomingDetails);
            }
        }

        private void Select(WeaponInstanceId id)
        {
            if (active == null || dismissedByInventory || !hasIncomingDetails)
                return;

            foreach (WeaponInstance candidate in active.Candidates)
            {
                if (candidate.Id != id || !HasDetails(candidate))
                    continue;

                selected = id;
                view!.Highlight(id);
                view.SetStatus("선택한 무기를 이번 획득으로 교체합니다.");
                return;
            }
        }

        private void Confirm()
        {
            if (active == null || !selected.HasValue || dismissedByInventory || !pickup)
                return;

            if (!pickup!.TryResolveChoice(source, active, selected))
                view!.SetStatus("현재 전리품을 획득할 수 없습니다. 범위를 확인하거나 취소하세요.");
        }

        private void Cancel()
        {
            if (active == null)
                return;

            if (!dismissedByInventory && pickup && pickup!.TryResolveChoice(null, active, null))
                return;

            Advance();
        }

        private void ResetSelection()
        {
            selected = null;
            view!.Highlight(null);
        }

        private void Advance()
        {
            Hide();
            if (waiting.Count == 0)
                return;

            var next = waiting.Dequeue();
            Show(next.Source, next.Choice);
        }

        private void Hide()
        {
            active = null;
            source = null;
            selected = null;
            view?.Close();
        }

        private bool HasDetails(WeaponInstance item) => definitions.ContainsKey(item.DefinitionId);

        private string Describe(WeaponInstance item, out bool hasDetails)
        {
            hasDetails = definitions.TryGetValue(item.DefinitionId, out WeaponDefinition definition) && definition;
            if (!hasDetails)
                return $"{item.DefinitionId} · 비교 정보 없음";

            return $"{definition!.DisplayName} · 등급 {definition.Grade} · {KindLabel(definition.Kind)}";
        }

        private static string KindLabel(WeaponKind kind) => kind switch
        {
            WeaponKind.Sword => "검",
            WeaponKind.Spear => "창",
            WeaponKind.Bow => "활",
            WeaponKind.Mace => "철퇴",
            WeaponKind.Dagger => "단검",
            _ => "분류 미확인"
        };

        private static IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition> ReadCatalog(WeaponCatalog? catalog)
        {
            if (catalog)
            {
                try { return catalog!.CreateLookup(); }
                catch (InvalidOperationException) { }
            }

            return new Dictionary<WeaponDefinitionId, WeaponDefinition>();
        }
    }
}
