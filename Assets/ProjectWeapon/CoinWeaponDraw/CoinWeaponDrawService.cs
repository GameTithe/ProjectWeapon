#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ProjectWeapon.Inventory;
using ProjectWeapon.Save;
using ProjectWeapon.WeaponDraw;

[assembly: InternalsVisibleTo("ProjectWeapon.CoinWeaponDraw.Tests")]

namespace ProjectWeapon.CoinWeaponDraw
{
    public enum CoinWeaponDrawStatus
    {
        Purchased,
        AlreadyPurchased,
        InsufficientFunds,
        RequestConflict,
        RevisionConflict
    }

    public sealed class CoinWeaponDrawResult
    {
        internal CoinWeaponDrawResult(CoinWeaponDrawStatus status, SaveSnapshot? activeSnapshot,
            CoinWeaponDrawSaveRecord? receipt = null)
        {
            Status = status;
            ActiveSnapshot = activeSnapshot;
            Receipt = receipt;
        }

        public CoinWeaponDrawStatus Status { get; }
        public SaveSnapshot? ActiveSnapshot { get; }
        public CoinWeaponDrawSaveRecord? Receipt { get; }
    }

    public sealed class CoinWeaponDrawService
    {
        public const long PrototypeSingleDrawCost = 500;

        private readonly AtomicSnapshotStore store;
        private readonly WeightedWeaponDraw draw;
        private readonly Func<long, SaveSnapshot, SnapshotCommitResult> commit;

        public CoinWeaponDrawService(AtomicSnapshotStore store, WeightedWeaponDraw draw,
            long cost = PrototypeSingleDrawCost)
            : this(store, draw, cost, (store ?? throw new ArgumentNullException(nameof(store))).Commit)
        {
        }

        internal CoinWeaponDrawService(AtomicSnapshotStore store, WeightedWeaponDraw draw, long cost,
            Func<long, SaveSnapshot, SnapshotCommitResult> commit)
        {
            if (cost <= 0)
                throw new ArgumentOutOfRangeException(nameof(cost));

            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.draw = draw ?? throw new ArgumentNullException(nameof(draw));
            this.commit = commit ?? throw new ArgumentNullException(nameof(commit));
            Cost = cost;
        }

        public long Cost { get; }

        public CoinWeaponDrawResult Purchase(Guid requestId, Func<int, int> nextExclusive)
        {
            if (requestId == Guid.Empty)
                throw new ArgumentException("A coin draw requires a stable request ID.", nameof(requestId));

            SaveSnapshot? current = store.Load();
            if (current == null)
                return new CoinWeaponDrawResult(CoinWeaponDrawStatus.InsufficientFunds, null);

            if (current.TryGetCoinWeaponDraw(requestId, out CoinWeaponDrawSaveRecord? existing))
                return Replay(current, existing!);

            if (current.ConfirmedCoins < Cost)
                return new CoinWeaponDrawResult(CoinWeaponDrawStatus.InsufficientFunds, current);

            long nextRevision = checked(current.Revision + 1);
            WeaponDrawResult selected = draw.Draw(nextExclusive);
            WeaponInstance weapon = WeaponInstance.Create(selected.DefinitionId, WeaponProvenance.Personal,
                WeaponLocation.Warehouse());
            long remaining = current.ConfirmedCoins - Cost;
            var receipt = new CoinWeaponDrawSaveRecord(requestId, Cost, remaining, weapon.Id,
                selected.DefinitionId, selected.Grade);

            var candidate = new SaveSnapshot(nextRevision, remaining, current.Weapons.Append(weapon), current.Catalog,
                current.Settlements, current.CoinWeaponDraws.Append(receipt));
            SnapshotCommitResult result = commit(current.Revision, candidate);
            SaveSnapshot? active = result.ActiveSnapshot;
            if (active != null && active.TryGetCoinWeaponDraw(requestId, out CoinWeaponDrawSaveRecord? committed))
            {
                if (result.Status == SnapshotCommitStatus.Committed)
                    return new CoinWeaponDrawResult(CoinWeaponDrawStatus.Purchased, active, committed);

                return Replay(active, committed!);
            }

            if (result.Status == SnapshotCommitStatus.RevisionConflict)
                return new CoinWeaponDrawResult(CoinWeaponDrawStatus.RevisionConflict, active);

            throw new InvalidDataException("The committed coin draw snapshot is missing its purchase receipt.");
        }

        private CoinWeaponDrawResult Replay(SaveSnapshot snapshot, CoinWeaponDrawSaveRecord receipt)
        {
            if (receipt.Cost != Cost)
                return new CoinWeaponDrawResult(CoinWeaponDrawStatus.RequestConflict, snapshot);

            return new CoinWeaponDrawResult(CoinWeaponDrawStatus.AlreadyPurchased, snapshot, receipt);
        }
    }
}
