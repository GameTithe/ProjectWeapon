#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Weapons;

[assembly: InternalsVisibleTo("ProjectWeapon.WeaponSale.Tests")]

namespace ProjectWeapon.WeaponSale
{
    public enum WeaponSaleStatus
    {
        InvalidRequest,
        Sold,
        AlreadySold,
        RequestConflict,
        NotInTown,
        MissingInstance,
        ProvenanceRestricted,
        InvalidLocation,
        NoOwnedDuplicate,
        InvalidDefinition,
        InvalidPrice,
        StorageLimit,
        RevisionConflict
    }

    public readonly struct WeaponSaleResult
    {
        internal WeaponSaleResult(WeaponSaleStatus status, SaveSnapshot? snapshot, WeaponSaleSaveRecord? receipt = null)
        {
            Status = status;
            ActiveSnapshot = snapshot;
            Receipt = receipt;
        }

        public WeaponSaleStatus Status { get; }
        public SaveSnapshot? ActiveSnapshot { get; }
        public WeaponSaleSaveRecord? Receipt { get; }
    }

    /// <summary>Commits a warehouse sale on the thread that owns the run state.</summary>
    public sealed class WeaponSaleService
    {
        private readonly AtomicSnapshotStore store;
        private readonly RunState run;
        private readonly IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition> definitions;
        private readonly WeaponSalePrices prices;
        private readonly Func<long, SaveSnapshot, SnapshotCommitResult> commit;

        public WeaponSaleService(AtomicSnapshotStore store, RunState run, WeaponCatalog catalog, WeaponSalePrices prices)
            : this(store, run, catalog, prices, (store ?? throw new ArgumentNullException(nameof(store))).Commit)
        {
        }

        internal WeaponSaleService(AtomicSnapshotStore store, RunState run, WeaponCatalog catalog, WeaponSalePrices prices,
            Func<long, SaveSnapshot, SnapshotCommitResult> commit)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (prices == null)
                throw new ArgumentNullException(nameof(prices));

            definitions = catalog.CreateLookup();
            this.prices = prices;
            this.commit = commit ?? throw new ArgumentNullException(nameof(commit));
        }

        public WeaponSaleResult TrySell(Guid requestId, WeaponInstanceId instanceId)
        {
            if (requestId == Guid.Empty || !instanceId.IsValid)
                return new WeaponSaleResult(WeaponSaleStatus.InvalidRequest, null);

            SaveSnapshot? current = store.Load();
            if (current == null)
                return new WeaponSaleResult(WeaponSaleStatus.MissingInstance, null);

            if (current.TryGetWeaponSale(requestId, out WeaponSaleSaveRecord? existing))
                return Replay(current, existing!, instanceId);

            if (run.Phase != RunPhase.Town || current.Weapons.Any(weapon => weapon.Location.Kind == WeaponLocationKind.RunReserved))
                return new WeaponSaleResult(WeaponSaleStatus.NotInTown, current);

            WeaponInstance? sold = current.Weapons.FirstOrDefault(weapon => weapon.Id == instanceId);
            if (sold == null)
                return new WeaponSaleResult(WeaponSaleStatus.MissingInstance, current);
            if (sold.IsLoaned)
                return new WeaponSaleResult(WeaponSaleStatus.ProvenanceRestricted, current);
            if (sold.Location.Kind != WeaponLocationKind.Warehouse)
                return new WeaponSaleResult(WeaponSaleStatus.InvalidLocation, current);

            bool hasOwnedDuplicate = current.Weapons.Any(weapon => weapon.Id != instanceId && !weapon.IsLoaned
                && weapon.DefinitionId.Equals(sold.DefinitionId)
                && (weapon.Location.Kind == WeaponLocationKind.Warehouse || weapon.Location.Kind == WeaponLocationKind.Equipped));
            if (!hasOwnedDuplicate)
                return new WeaponSaleResult(WeaponSaleStatus.NoOwnedDuplicate, current);

            if (!definitions.TryGetValue(sold.DefinitionId, out WeaponDefinition definition)
                || definition == null || definition.Grade < 1 || definition.Grade > 5)
                return new WeaponSaleResult(WeaponSaleStatus.InvalidDefinition, current);

            int grade = definition.Grade;
            long price = prices.GetPrice(grade);
            if (price <= 0)
                return new WeaponSaleResult(WeaponSaleStatus.InvalidPrice, current);
            if (current.Revision == long.MaxValue || current.ConfirmedCoins > long.MaxValue - price)
                return new WeaponSaleResult(WeaponSaleStatus.StorageLimit, current);

            long balance = current.ConfirmedCoins + price;
            var receipt = new WeaponSaleSaveRecord(requestId, instanceId, sold.DefinitionId, grade, price, balance);
            var candidate = new SaveSnapshot(current.Revision + 1, balance,
                current.Weapons.Where(weapon => weapon.Id != instanceId), current.Catalog, current.Settlements,
                current.CoinWeaponDraws, current.WeaponSales.Append(receipt));

            SnapshotCommitResult result = commit(current.Revision, candidate);
            SaveSnapshot? active = result.ActiveSnapshot;
            if (active != null && active.TryGetWeaponSale(requestId, out WeaponSaleSaveRecord? committed))
            {
                if (result.Status == SnapshotCommitStatus.Committed)
                    return new WeaponSaleResult(WeaponSaleStatus.Sold, active, committed);

                return Replay(active, committed!, instanceId);
            }

            if (result.Status == SnapshotCommitStatus.RevisionConflict)
                return new WeaponSaleResult(WeaponSaleStatus.RevisionConflict, active);

            throw new InvalidDataException("The committed sale snapshot is missing its receipt.");
        }

        private static WeaponSaleResult Replay(
            SaveSnapshot snapshot, WeaponSaleSaveRecord receipt, WeaponInstanceId requestedInstance)
            => receipt.InstanceId == requestedInstance
                ? new WeaponSaleResult(WeaponSaleStatus.AlreadySold, snapshot, receipt)
                : new WeaponSaleResult(WeaponSaleStatus.RequestConflict, snapshot);
    }
}
