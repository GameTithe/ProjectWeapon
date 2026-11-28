#nullable enable

using System;
using System.Collections.Generic;

namespace ProjectWeapon.Inventory
{
    public enum SortieStartStatus
    {
        InvalidRequest,
        Started,
        InventoryAlreadyReserved,
        LoadoutNotReady,
        RunRejected
    }

    public readonly struct SortieStartResult
    {
        internal SortieStartResult(SortieStartStatus status, SortieManifest? manifest = null)
        {
            Status = status;
            Manifest = manifest;
        }

        public SortieStartStatus Status { get; }
        public SortieManifest? Manifest { get; }
    }

    /// <summary>The immutable, equipped loadout captured at sortie start.</summary>
    public sealed class SortieManifest
    {
        internal SortieManifest(Guid runId, WeaponInstance[] equipped)
        {
            RunId = runId;
            Equipped = Array.AsReadOnly(equipped);
        }

        public Guid RunId { get; }
        public IReadOnlyList<WeaponInstance> Equipped { get; }
    }
}
