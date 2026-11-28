#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProjectWeapon.Run;

namespace ProjectWeapon.QaMetrics
{
    public static class QaMetricsReplay
    {
        public static QaReplayReport Calculate(IEnumerable<QaMetricEvent> events, QaReplayWindow window)
        {
            if (events == null)
                throw new ArgumentNullException(nameof(events));
            if (window == null)
                throw new ArgumentNullException(nameof(window));

            var report = new QaReplayReport();
            QaMetricEvent[] accepted = Deduplicate(events, window, report);
            var byKind = accepted.ToLookup(item => item.Kind);
            var starts = byKind[QaEventKind.RunStarted].ToDictionary(item => item.Key);
            var ended = byKind[QaEventKind.RunEnded]
                .Where(item => starts.TryGetValue(item.Key, out QaMetricEvent start) && item.At >= start.At)
                .ToDictionary(item => item.Key);
            var completed = byKind[QaEventKind.RunSettlementCommitted]
                .Where(item => starts.TryGetValue(item.Key, out QaMetricEvent start) && item.At >= start.At
                    && (!ended.TryGetValue(item.Key, out QaMetricEvent end) || item.At >= end.At))
                .ToDictionary(item => item.Key);

            QaMetricEvent[] periodSettled = completed.Values.Where(item => window.Includes(item.At)).ToArray();
            report.ExtractionSuccess = new QaRatio(periodSettled.Count(item => item.Category == (int)RunEndReason.Extracted),
                periodSettled.Length);
            report.PendingSettlements = ended.Values.Count(item => window.Includes(item.At) && !completed.ContainsKey(item.Key));

            if (window.HighGrade is int highGrade)
            {
                QaMetricEvent[] periodStarts = starts.Values.Where(item => window.Includes(item.At)).ToArray();
                report.HighGradeSorties = new QaRatio(periodStarts.Count(item => item.Value >= highGrade), periodStarts.Length);
            }

            if (window.PostLootWindow is TimeSpan lootWindow)
                ReadExtractionChoices(byKind, starts, ended, window, lootWindow, report);
            if (window.RedeployWindow is TimeSpan redeployWindow)
                report.RedeployAfterLoss = ReadRedeploy(accepted, periodSettled, window, redeployWindow);

            ReadWallet(byKind[QaEventKind.WalletChanged], window, report);
            ReadAds(byKind, window, report);
            ReadCatalog(byKind[QaEventKind.CatalogChanged], starts, ended, window, report);
            return report;
        }

        private static QaMetricEvent[] Deduplicate(IEnumerable<QaMetricEvent> events, QaReplayWindow window,
            QaReplayReport report)
        {
            var selected = new List<QaMetricEvent>();
            foreach (var key in events.Where(item => item.Scope == window.Scope && item.At < window.ObservedUntil)
                .GroupBy(item => item.ObservationKey))
            {
                var payloads = key.GroupBy(item => item.Encode(), StringComparer.Ordinal).ToArray();
                report.DuplicateDeliveries += key.Count() - payloads.Length;
                if (payloads.Length == 1)
                    selected.Add(payloads[0].First());
                else
                    report.ConflictingKeys++;
            }
            report.AcceptedEvents = selected.Count;
            return selected.OrderBy(item => item.At).ThenBy(item => item.Key).ThenBy(item => item.Attempt).ToArray();
        }

        private static void ReadExtractionChoices(ILookup<QaEventKind, QaMetricEvent> byKind,
            IReadOnlyDictionary<Guid, QaMetricEvent> starts, IReadOnlyDictionary<Guid, QaMetricEvent> ended,
            QaReplayWindow window, TimeSpan horizon, QaReplayReport report)
        {
            var firstLoot = byKind[QaEventKind.RunLootChanged]
                .Where(item => IsDuringRun(item, starts, ended)).GroupBy(item => item.Key).Select(group => group.First());
            var extractions = byKind[QaEventKind.ExtractionStateChanged]
                .Where(item => IsDuringRun(item, starts, ended)).ToLookup(item => item.Key);
            int observed = 0;
            int chosen = 0;
            int cancelled = 0;
            foreach (QaMetricEvent loot in firstLoot)
            {
                if (!window.Includes(loot.At) || (!ended.ContainsKey(loot.Key) && !window.HasObserved(loot.At, horizon)))
                    continue;

                observed++;
                QaMetricEvent? choice = extractions[loot.Key].FirstOrDefault(item => item.Category == 1
                    && item.At >= loot.At && item.At - loot.At <= horizon);
                if (choice == null)
                    continue;

                chosen++;
                if (extractions[loot.Key].Any(item => item.Category == 2 && item.Attempt == choice.Attempt && item.At >= choice.At))
                    cancelled++;
            }
            report.PostLootExtraction = new QaRatio(chosen, observed);
            report.PostLootCancellation = new QaRatio(cancelled, chosen);
        }

        private static bool IsDuringRun(QaMetricEvent item, IReadOnlyDictionary<Guid, QaMetricEvent> starts,
            IReadOnlyDictionary<Guid, QaMetricEvent> ended)
            => starts.TryGetValue(item.Key, out QaMetricEvent start) && item.At >= start.At
                && (!ended.TryGetValue(item.Key, out QaMetricEvent end) || item.At <= end.At);

        private static QaRatio ReadRedeploy(QaMetricEvent[] accepted, IEnumerable<QaMetricEvent> settlements,
            QaReplayWindow window, TimeSpan horizon)
        {
            QaMetricEvent[] starts = accepted.Where(item => item.Kind == QaEventKind.RunStarted).ToArray();
            int returned = 0;
            int observed = 0;
            foreach (QaMetricEvent result in settlements)
            {
                if (result.Category == (int)RunEndReason.Extracted || result.Value == 0)
                    continue;

                bool redeployed = starts.Any(item => item.Key != result.Key
                    && item.At >= result.At && item.At - result.At <= horizon);
                if (redeployed)
                    returned++;
                if (redeployed || window.HasObserved(result.At, horizon))
                    observed++;
            }
            return new QaRatio(returned, observed);
        }

        private static void ReadWallet(IEnumerable<QaMetricEvent> changes, QaReplayWindow window, QaReplayReport report)
        {
            foreach (QaMetricEvent change in changes.Where(item => window.Includes(item.At)))
            {
                if (change.Value > 0)
                    report.CoinsEarned += change.Value;
                else
                    report.CoinsSpent -= (decimal)change.Value;
            }
        }

        private static void ReadAds(ILookup<QaEventKind, QaMetricEvent> byKind, QaReplayWindow window, QaReplayReport report)
        {
            var shown = byKind[QaEventKind.AdAttemptStarted].ToDictionary(item => item.Key);
            var verified = byKind[QaEventKind.AdCompletionVerified].ToDictionary(item => item.Key);
            var finished = byKind[QaEventKind.AdAttemptEnded].ToDictionary(item => item.Key);
            var rewards = byKind[QaEventKind.AdRewardCommitted].ToLookup(item => item.Related);
            int sufficientlyObserved = 0;
            int committed = 0;
            foreach (QaMetricEvent ad in shown.Values.Where(item => window.Includes(item.At)))
            {
                bool rewarded = verified.TryGetValue(ad.Key, out QaMetricEvent check) && check.At >= ad.At
                    && rewards[ad.Key].Any(item => item.At >= check.At);
                bool failed = finished.TryGetValue(ad.Key, out QaMetricEvent end) && end.At >= ad.At
                    && end.Category is (int)QaAdEnd.Interrupted or (int)QaAdEnd.VerificationFailed;
                if (rewarded && failed)
                {
                    report.InconsistentAdAttempts++;
                    continue;
                }
                if (rewarded || failed)
                    sufficientlyObserved++;
                else
                    report.PendingAdAttempts++;
                if (rewarded)
                    committed++;
            }

            foreach (QaMetricEvent terminal in finished.Values.Where(item => window.Includes(item.At) && !shown.ContainsKey(item.Key)))
            {
                if (terminal.Category == (int)QaAdEnd.CancelledBeforeDisplay)
                    report.AdCancelledBeforeDisplay++;
                if (terminal.Category == (int)QaAdEnd.FillFailed)
                    report.AdFillFailures++;
            }
            report.AdCompletion = new QaRatio(committed, sufficientlyObserved);
        }

        private static void ReadCatalog(IEnumerable<QaMetricEvent> changes,
            IReadOnlyDictionary<Guid, QaMetricEvent> starts, IReadOnlyDictionary<Guid, QaMetricEvent> ended,
            QaReplayWindow window, QaReplayReport report)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int created = 0;
            foreach (QaMetricEvent change in changes)
            {
                foreach (string definition in change.Definitions)
                {
                    if (seen.Add(definition) && window.Includes(change.At))
                        created++;
                }
            }

            decimal duration = 0;
            foreach (QaMetricEvent endpoint in ended.Values.Where(item => window.Includes(item.At)))
                duration += (endpoint.At - starts[endpoint.Key].At).Ticks;
            report.CatalogRecovery = new QaCatalogSpeed(created, duration);
        }
    }
}
