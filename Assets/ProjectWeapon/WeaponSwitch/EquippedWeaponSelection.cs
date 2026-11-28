#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.WeaponSwitch
{
    public readonly struct SelectedWeapon
    {
        internal SelectedWeapon(int slotIndex, WeaponInstanceId instanceId, WeaponDefinition definition)
        {
            SlotIndex = slotIndex;
            InstanceId = instanceId;
            Definition = definition;
        }

        public int SlotIndex { get; }
        public WeaponInstanceId InstanceId { get; }
        public WeaponDefinition Definition { get; }
    }

    /// <summary>Selects which of the two already-equipped weapon instances the next action will use.</summary>
    public sealed class EquippedWeaponSelection
    {
        private const int SlotCount = 2;
        private readonly RunState run;
        private readonly InventoryService inventory;
        private readonly IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition> definitions;
        private readonly Func<bool> isAttackOrSkillBusy;
        private readonly WeaponActionLocks locks;
        private readonly RunId runId;

        public EquippedWeaponSelection(RunState run, InventoryService inventory, WeaponCatalog catalog,
            Func<bool> isAttackOrSkillBusy, WeaponActionLocks locks)
        {
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            runId = run.CurrentRun?.Id ?? throw new InvalidOperationException("Start the run before selecting equipment.");
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            if (!catalog)
                throw new ArgumentNullException(nameof(catalog));
            definitions = catalog.CreateLookup();
            this.isAttackOrSkillBusy = isAttackOrSkillBusy ?? throw new ArgumentNullException(nameof(isAttackOrSkillBusy));
            this.locks = locks ?? throw new ArgumentNullException(nameof(locks));

            IReadOnlyList<WeaponInstance> snapshot = inventory.Snapshot();
            ActiveSlotIndex = Find(snapshot, 0).HasValue ? 0 : Find(snapshot, 1).HasValue ? 1 : null;
        }

        public int? ActiveSlotIndex { get; private set; }
        public event Action<SelectedWeapon>? ActiveSlotChanged;

        public SelectedWeapon? ReadActiveWeapon()
            => ActiveSlotIndex is int index ? Find(inventory.Snapshot(), index) : null;

        public bool TrySwitch(int slotIndex, TimeSpan observedAt)
        {
            if ((uint)slotIndex >= SlotCount || ActiveSlotIndex == slotIndex
                || !CanAct(observedAt) || isAttackOrSkillBusy())
                return false;

            if (Find(inventory.Snapshot(), slotIndex) is not SelectedWeapon target)
                return false;

            ActiveSlotIndex = slotIndex;
            ActiveSlotChanged?.Invoke(target);
            return true;
        }

        public bool TryGetReadyWeapon(CombatWeaponAction action, TimeSpan observedAt, out SelectedWeapon weapon)
        {
            weapon = default;
            if (!CanAct(observedAt) || ReadActiveWeapon() is not SelectedWeapon candidate)
                return false;

            ScriptableObject? definition = action switch
            {
                CombatWeaponAction.BasicAttack => candidate.Definition.BasicAttack,
                CombatWeaponAction.Skill => candidate.Definition.Skill,
                _ => null
            };
            if (!definition || observedAt < locks.ReadyAt(action, candidate.InstanceId))
                return false;

            weapon = candidate;
            return true;
        }

        private bool CanAct(TimeSpan observedAt)
            => run.Phase == RunPhase.Exploring && run.CurrentRun is { } session && session.Id == runId
                && observedAt >= session.StartedAt && observedAt < session.Deadline;

        private SelectedWeapon? Find(IReadOnlyList<WeaponInstance> snapshot, int slotIndex)
        {
            foreach (WeaponInstance item in snapshot)
            {
                if (item.Location.Kind == WeaponLocationKind.Equipped && item.Location.SlotIndex == slotIndex
                    && definitions.TryGetValue(item.DefinitionId, out WeaponDefinition definition) && definition)
                    return new SelectedWeapon(slotIndex, item.Id, definition);
            }
            return null;
        }
    }
}
