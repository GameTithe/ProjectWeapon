#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Settlement;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.QaMetrics
{
    /// <summary>Adapts a synthetic run fixture without subscribing to gameplay or sending data anywhere.</summary>
    public sealed class QaRunCapture
    {
        private readonly Guid scope;
        private readonly RunId run;
        private readonly HashSet<string> packedOwned;
        private readonly HashSet<WeaponInstanceId> seen;

        public QaRunCapture(Guid scope, RunSession session, SortieManifest manifest,
            IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition> definitions)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));
            if (definitions == null)
                throw new ArgumentNullException(nameof(definitions));
            if (manifest.RunId != session.Id.Value)
                throw new ArgumentException("The loadout must belong to the synthetic run.", nameof(manifest));

            this.scope = scope;
            run = session.Id;
            seen = new HashSet<WeaponInstanceId>(manifest.Equipped.Select(item => item.Id));
            packedOwned = new HashSet<string>(StringComparer.Ordinal);
            int grade = 0;
            foreach (WeaponInstance item in manifest.Equipped.Where(item => !item.IsLoaned))
            {
                if (!definitions.TryGetValue(item.DefinitionId, out WeaponDefinition weapon) || weapon == null)
                    throw new ArgumentException("An equipped definition is missing from the fixture.", nameof(definitions));
                grade = Math.Max(grade, weapon.Grade);
                packedOwned.Add(item.Id.ToString());
            }
            Started = QaMetricEvent.RunStarted(scope, run, session.StartedAt, grade);
        }

        public QaMetricEvent Started { get; }

        public QaMetricEvent Ended(RunResult validatedResult)
        {
            if (validatedResult == null)
                throw new ArgumentNullException(nameof(validatedResult));
            if (validatedResult.RunId != run)
                throw new ArgumentException("The result belongs to a different run.", nameof(validatedResult));
            return QaMetricEvent.RunEnded(scope, run, validatedResult.EffectiveAt, validatedResult.Reason);
        }

        public QaMetricEvent? LootChanged(InventoryPickupResult acceptedPickup, Guid changeId, TimeSpan at)
        {
            if (acceptedPickup.Status != InventoryPickupStatus.PickedUp || acceptedPickup.PickedUp is not WeaponInstance item
                || item.Provenance != WeaponProvenance.ExplorationLoot || seen.Contains(item.Id))
                return null;

            QaMetricEvent added = QaMetricEvent.RunLootChanged(scope, run, changeId, Guid.Parse(item.Id.ToString()), at);
            seen.Add(item.Id);
            return added;
        }

        public QaMetricEvent ExtractionStarted(ExtractionAttempt acceptedAttempt)
        {
            if (acceptedAttempt == null)
                throw new ArgumentNullException(nameof(acceptedAttempt));
            return QaMetricEvent.ExtractionChanged(scope, run, acceptedAttempt.Id, true, acceptedAttempt.StartedAt);
        }

        /// <summary>Supply the same attempt and time that the run actually accepted as cancelled.</summary>
        public QaMetricEvent ExtractionCancelled(int acceptedAttemptId, TimeSpan acceptedAt)
            => QaMetricEvent.ExtractionChanged(scope, run, acceptedAttemptId, false, acceptedAt);

        /// <summary>Supply the original synthetic commit time, including on receipt redelivery.</summary>
        public IReadOnlyList<QaMetricEvent> SettlementCommitted(RunSettlementCommit commit, TimeSpan originalCommitTime)
        {
            if (commit == null)
                throw new ArgumentNullException(nameof(commit));
            if (commit.Status is RunSettlementStatus.RetryRequired or RunSettlementStatus.MissingSave
                || commit.Receipt == null || commit.Receipt.RunId != run.Value)
                return Array.Empty<QaMetricEvent>();

            RunSettlementDetails? details = RunSettlementDetails.Read(commit.Receipt);
            if (details == null)
                return Array.Empty<QaMetricEvent>();

            var events = new List<QaMetricEvent>
            {
                QaMetricEvent.SettlementCommitted(scope, run, originalCommitTime, details.Reason,
                    details.LostInstanceIds.Count(packedOwned.Contains))
            };
            // A settlement has one stable transaction per run. Each event kind has its own key namespace.
            if (details.AwardedCoins > 0)
                events.Add(QaMetricEvent.WalletCommitted(scope, run.Value, originalCommitTime, details.AwardedCoins,
                    QaWalletTransaction.Settlement));
            if (details.HasItemizedDetails && details.NewCatalogRecoveries.Count != 0)
                events.Add(QaMetricEvent.CatalogCommitted(scope, run.Value, originalCommitTime, details.NewCatalogRecoveries));
            return events.AsReadOnly();
        }
    }
}
