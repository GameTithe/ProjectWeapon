#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

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

        public bool MatchesReservations(IEnumerable<WeaponInstance> savedWeapons)
        {
            WeaponInstance[] reserved = savedWeapons.Where(item => item.Location.ReservedRunId == RunId).ToArray();
            return reserved.Length == Equipped.Count && Equipped.All(item =>
                reserved.Any(saved => saved.Id == item.Id && saved.DefinitionId.Equals(item.DefinitionId)
                    && saved.Provenance == item.Provenance));
        }
    }
}
