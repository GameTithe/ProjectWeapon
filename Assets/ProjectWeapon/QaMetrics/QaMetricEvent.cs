#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.QaMetrics
{
    public enum QaEventKind
    {
        RunStarted = 1,
        RunEnded,
        RunLootChanged,
        ExtractionStateChanged,
        RunSettlementCommitted,
        WalletChanged,
        CatalogChanged,
        AdAttemptStarted,
        AdCompletionVerified,
        AdRewardCommitted,
        AdAttemptEnded
    }

    public enum QaWalletTransaction
    {
        Settlement = 1,
        CoinDraw,
        AdReward,
        OtherConfirmed
    }

    public enum QaAdEnd
    {
        CancelledBeforeDisplay = 1,
        FillFailed,
        Interrupted,
        VerificationFailed
    }

    /// <summary>A versioned synthetic event. Opaque IDs and a monotonic fixture clock are supplied by the QA harness.</summary>
    [Serializable]
    public sealed class QaMetricEvent
    {
        public const int ContractVersion = 1;

        [SerializeField] private int version;
        [SerializeField] private QaEventKind kind;
        [SerializeField] private string scope = string.Empty;
        [SerializeField] private string key = string.Empty;
        [SerializeField] private string related = string.Empty;
        [SerializeField] private string subject = string.Empty;
        [SerializeField] private long ticks;
        [SerializeField] private long value;
        [SerializeField] private int category;
        [SerializeField] private int attempt;
        [SerializeField] private string[] definitions = Array.Empty<string>();

        public QaEventKind Kind => kind;
        public Guid Scope => Guid.ParseExact(scope, "N");
        public Guid Key => Guid.ParseExact(key, "N");
        public Guid Related => related.Length == 0 ? Guid.Empty : Guid.ParseExact(related, "N");
        public TimeSpan At => TimeSpan.FromTicks(ticks);
        public long Value => value;
        public int Category => category;
        public int Attempt => attempt;
        public IReadOnlyList<string> Definitions => Array.AsReadOnly(definitions);

        private QaMetricEvent() { }

        public static QaMetricEvent RunStarted(Guid scope, RunId run, TimeSpan time, int highestOwnedGrade)
            => Make(QaEventKind.RunStarted, scope, run.Value, time, value: highestOwnedGrade);

        public static QaMetricEvent RunEnded(Guid scope, RunId run, TimeSpan time, RunEndReason reason)
            => Make(QaEventKind.RunEnded, scope, run.Value, time, category: (int)reason);

        public static QaMetricEvent RunLootChanged(Guid scope, RunId run, Guid changeId, Guid instanceId, TimeSpan time)
            => Make(QaEventKind.RunLootChanged, scope, run.Value, time, related: changeId, subject: instanceId);

        public static QaMetricEvent ExtractionChanged(Guid scope, RunId run, int attemptId, bool started, TimeSpan time)
            => Make(QaEventKind.ExtractionStateChanged, scope, run.Value, time,
                category: started ? 1 : 2, attempt: attemptId);

        public static QaMetricEvent SettlementCommitted(Guid scope, RunId run, TimeSpan time, RunEndReason reason,
            int lostOwnedPackedWeapons)
            => Make(QaEventKind.RunSettlementCommitted, scope, run.Value, time,
                value: lostOwnedPackedWeapons, category: (int)reason);

        public static QaMetricEvent WalletCommitted(Guid scope, Guid transaction, TimeSpan time, long delta,
            QaWalletTransaction transactionKind)
            => Make(QaEventKind.WalletChanged, scope, transaction, time, value: delta, category: (int)transactionKind);

        public static QaMetricEvent CatalogCommitted(Guid scope, Guid transaction, TimeSpan time,
            IEnumerable<string> firstRecoveries)
            => Make(QaEventKind.CatalogChanged, scope, transaction, time, definitions: firstRecoveries);

        public static QaMetricEvent AdAttemptStarted(Guid scope, Guid attemptId, TimeSpan time)
            => Make(QaEventKind.AdAttemptStarted, scope, attemptId, time);

        public static QaMetricEvent AdCompletionVerified(Guid scope, Guid attemptId, TimeSpan time)
            => Make(QaEventKind.AdCompletionVerified, scope, attemptId, time);

        public static QaMetricEvent AdRewardCommitted(Guid scope, Guid rewardTransaction, Guid attemptId, TimeSpan time)
            => Make(QaEventKind.AdRewardCommitted, scope, rewardTransaction, time, related: attemptId);

        public static QaMetricEvent AdAttemptEnded(Guid scope, Guid attemptId, TimeSpan time, QaAdEnd reason)
            => Make(QaEventKind.AdAttemptEnded, scope, attemptId, time, category: (int)reason);

        internal static QaMetricEvent Decode(string json)
        {
            QaMetricEvent result = JsonUtility.FromJson<QaMetricEvent>(json);
            if (result == null || !result.IsValid())
                throw new FormatException("Invalid synthetic QA event.");
            result.definitions = Canonical(result.definitions);
            return result;
        }

        internal string Encode() => JsonUtility.ToJson(this);

        internal (QaEventKind Kind, Guid Key, Guid Related, int Attempt, int State) ObservationKey
            => (Kind, Key, Kind == QaEventKind.RunLootChanged ? Related : Guid.Empty, Attempt,
                Kind == QaEventKind.ExtractionStateChanged ? Category : 0);

        private static QaMetricEvent Make(QaEventKind kind, Guid scope, Guid key, TimeSpan at,
            Guid related = default, Guid subject = default, long value = 0, int category = 0, int attempt = 0,
            IEnumerable<string>? definitions = null)
        {
            var item = new QaMetricEvent
            {
                version = ContractVersion,
                kind = kind,
                scope = scope.ToString("N"),
                key = key.ToString("N"),
                related = related == Guid.Empty ? string.Empty : related.ToString("N"),
                subject = subject == Guid.Empty ? string.Empty : subject.ToString("N"),
                ticks = at.Ticks,
                value = value,
                category = category,
                attempt = attempt,
                definitions = Canonical(definitions ?? Array.Empty<string>())
            };
            if (!item.IsValid())
                throw new ArgumentException("Invalid synthetic QA event.");
            return item;
        }

        private bool IsValid()
        {
            if (version != ContractVersion || ticks < 0 || !IsKey(scope) || !IsKey(key)
                || related == null || subject == null || definitions == null)
                return false;
            if (kind != QaEventKind.ExtractionStateChanged && attempt != 0)
                return false;
            if (kind != QaEventKind.CatalogChanged && definitions.Length != 0)
                return false;

            bool linked = kind == QaEventKind.RunLootChanged || kind == QaEventKind.AdRewardCommitted;
            if (linked ? !IsKey(related) : related.Length != 0)
                return false;
            if (kind == QaEventKind.RunLootChanged ? !IsKey(subject) : subject.Length != 0)
                return false;

            return kind switch
            {
                QaEventKind.RunStarted => value is >= 0 and <= 5 && category == 0,
                QaEventKind.RunEnded => value == 0 && Enum.IsDefined(typeof(RunEndReason), category),
                QaEventKind.RunSettlementCommitted => value is >= 0 and <= int.MaxValue
                    && Enum.IsDefined(typeof(RunEndReason), category),
                QaEventKind.ExtractionStateChanged => value == 0 && attempt > 0 && category is 1 or 2,
                QaEventKind.WalletChanged => value != 0 && Enum.IsDefined(typeof(QaWalletTransaction), category),
                QaEventKind.AdAttemptEnded => value == 0 && Enum.IsDefined(typeof(QaAdEnd), category),
                QaEventKind.CatalogChanged => value == 0 && category == 0 && definitions.Length != 0
                    && definitions.All(IsDefinitionKey),
                QaEventKind.RunLootChanged or QaEventKind.AdAttemptStarted or QaEventKind.AdCompletionVerified
                    or QaEventKind.AdRewardCommitted => value == 0 && category == 0,
                _ => false
            };
        }

        private static bool IsKey(string candidate)
            => Guid.TryParseExact(candidate, "N", out Guid id) && id != Guid.Empty
                && StringComparer.Ordinal.Equals(candidate, id.ToString("N"));

        private static bool IsDefinitionKey(string candidate)
            => candidate != null && candidate.Length is > 0 and <= 64
                && candidate.All(ch => ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z'
                    or >= '0' and <= '9' or '_' or '-');

        private static string[] Canonical(IEnumerable<string> keys)
            => keys.Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray();
    }
}
