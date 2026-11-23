#nullable enable

using System;
using UnityEngine;

namespace ProjectWeapon.Inventory
{
    public enum WeaponLocationKind
    {
        Unspecified = 0,
        Warehouse = 1,
        Equipped = 2,
        Carried = 3,
        RunReserved = 4
    }

    [Serializable]
    public struct WeaponLocation
    {
        [SerializeField] private WeaponLocationKind kind;
        [SerializeField] private int slotIndex;
        [SerializeField] private string? runId;

        private WeaponLocation(WeaponLocationKind kind, int slotIndex, string? runId)
        {
            this.kind = kind;
            this.slotIndex = slotIndex;
            this.runId = runId;
        }

        public WeaponLocationKind Kind => kind;
        public int? SlotIndex => IsSlotLocation ? slotIndex : (int?)null;
        public Guid? ReservedRunId => kind == WeaponLocationKind.RunReserved
            && runId?.Length == 36 && Guid.TryParseExact(runId, "D", out Guid parsed) && parsed != Guid.Empty ? parsed : (Guid?)null;

        public bool IsValid => kind switch
        {
            WeaponLocationKind.Warehouse => slotIndex == -1 && string.IsNullOrEmpty(runId),
            WeaponLocationKind.Equipped => slotIndex >= 0 && string.IsNullOrEmpty(runId),
            WeaponLocationKind.Carried => slotIndex >= 0 && string.IsNullOrEmpty(runId),
            WeaponLocationKind.RunReserved => slotIndex == -1 && ReservedRunId.HasValue,
            _ => false
        };

        private bool IsSlotLocation => kind == WeaponLocationKind.Equipped || kind == WeaponLocationKind.Carried;

        public static WeaponLocation Warehouse() => new WeaponLocation(WeaponLocationKind.Warehouse, -1, null);

        public static WeaponLocation Equipped(int slotIndex) => InSlot(WeaponLocationKind.Equipped, slotIndex);

        public static WeaponLocation Carried(int slotIndex) => InSlot(WeaponLocationKind.Carried, slotIndex);

        public static WeaponLocation RunReserved(Guid runId)
        {
            if (runId == Guid.Empty)
                throw new ArgumentException("A reserved weapon must have a run ID.", nameof(runId));

            return new WeaponLocation(WeaponLocationKind.RunReserved, -1, runId.ToString("D"));
        }

        private static WeaponLocation InSlot(WeaponLocationKind kind, int slotIndex)
        {
            if (slotIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));

            return new WeaponLocation(kind, slotIndex, null);
        }
    }
}
