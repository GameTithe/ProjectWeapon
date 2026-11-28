using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProjectWeapon.WeeklyChallenge;

/// <summary>The complete, shared settings for one UTC week. Equipment is temporary and belongs to the challenge.</summary>
public sealed record WeeklyConditions(
    DateTimeOffset WeekStartUtc,
    string ConditionsVersion,
    string MapVersion,
    int MapSeed,
    string EnemiesVersion,
    string ChestsVersion,
    string RulesVersion,
    string EquipmentVersion,
    string FirstWeaponDefinitionId,
    string? SecondWeaponDefinitionId,
    int DurationSeconds)
{
    public DateTimeOffset WeekEndUtc => WeekStartUtc.AddDays(7);

    internal bool IsValidFor(DateTimeOffset week)
        => WeekStartUtc.Offset == TimeSpan.Zero
            && WeekStartUtc == week
            && DurationSeconds is > 0 and <= 7 * 24 * 60 * 60
            && IsKey(ConditionsVersion) && IsKey(MapVersion) && IsKey(EnemiesVersion) && IsKey(ChestsVersion)
            && IsKey(RulesVersion) && IsKey(EquipmentVersion) && IsKey(FirstWeaponDefinitionId)
            && (SecondWeaponDefinitionId is null || IsKey(SecondWeaponDefinitionId));

    internal static DateTimeOffset Monday(DateTimeOffset instant)
    {
        DateTimeOffset utc = instant.ToUniversalTime();
        return new DateTimeOffset(utc.Date.AddDays(-((int)utc.DayOfWeek + 6) % 7), TimeSpan.Zero);
    }

    internal static bool IsKey(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value == value.Trim();
}

public sealed record IssuedAttempt(
    Guid AttemptId,
    Guid RequestId,
    string AccountId,
    WeeklyConditions Conditions,
    int AttemptNumber,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public enum WeeklyIssueStatus
{
    Available,
    Expired,
    Unauthorized,
    InvalidRequest,
    WeeklyLimitReached,
    AuthorityUnavailable,
    Retryable
}

public sealed record WeeklyIssueResult(WeeklyIssueStatus Status, IssuedAttempt? Attempt = null);

public sealed record PlayerLedger(string AccountId, IssuedAttempt[] Attempts);
public sealed record LockedLedger(string WriteLock, PlayerLedger Ledger);

/// <summary>Returns only a frozen, persistently committed definition for the requested week.</summary>
public interface IFrozenWeeklyConditions
{
    Task<WeeklyConditions?> GetOrFreezeAsync(DateTimeOffset weekStartUtc);
}

/// <summary>Per authenticated account. Replace requires a revision from the same document, including first issuance.</summary>
public interface IWeeklyAttemptStore
{
    Task<LockedLedger?> ReadAsync(string accountId);
    Task<bool> TryReplaceAsync(string accountId, LockedLedger observed, PlayerLedger next);
}

public sealed class WeeklyChallengeIssuer
{
    public const int WeeklyLimit = 5;
    internal const int MaxTries = 4;

    private readonly IWeeklyAttemptStore attempts;
    private readonly IFrozenWeeklyConditions conditions;
    private readonly Func<DateTimeOffset> utcNow;

    public WeeklyChallengeIssuer(IWeeklyAttemptStore attempts, IFrozenWeeklyConditions conditions, Func<DateTimeOffset> utcNow)
    {
        this.attempts = attempts;
        this.conditions = conditions;
        this.utcNow = utcNow;
    }

    /// <summary>Only an authenticated server caller may provide the account ID. The client supplies just the request ID.</summary>
    public async Task<WeeklyIssueResult> IssueAsync(string? authenticatedAccountId, Guid requestId)
    {
        if (!WeeklyConditions.IsKey(authenticatedAccountId))
            return new(WeeklyIssueStatus.Unauthorized);
        if (requestId == Guid.Empty)
            return new(WeeklyIssueStatus.InvalidRequest);

        string accountId = authenticatedAccountId!;
        for (int attempt = 0; attempt < MaxTries; attempt++)
        {
            LockedLedger? observed = await ReadLedgerAsync(accountId);
            if (observed is null)
                return new(WeeklyIssueStatus.AuthorityUnavailable);

            IssuedAttempt[] issued = observed.Ledger.Attempts;
            DateTimeOffset now = utcNow().ToUniversalTime();
            IssuedAttempt? existing = issued.SingleOrDefault(item => item.RequestId == requestId);
            if (existing is not null)
                return View(existing, now);

            DateTimeOffset week = WeeklyConditions.Monday(now);
            int count = issued.Count(item => item.Conditions.WeekStartUtc == week);
            if (count >= WeeklyLimit)
                return new(WeeklyIssueStatus.WeeklyLimitReached);

            WeeklyConditions? frozen = await conditions.GetOrFreezeAsync(week);
            now = utcNow().ToUniversalTime();
            if (WeeklyConditions.Monday(now) != week)
                continue;
            if (frozen is null || !frozen.IsValidFor(week))
                return new(WeeklyIssueStatus.AuthorityUnavailable);

            Guid attemptId = Guid.NewGuid();
            if (issued.Any(item => item.AttemptId == attemptId))
                continue;

            IssuedAttempt created = new(attemptId, requestId, accountId, frozen, count + 1, now, Expiry(frozen, now));
            PlayerLedger next = new(accountId, [.. issued, created]);
            if (await attempts.TryReplaceAsync(accountId, observed, next))
                return View(created, utcNow().ToUniversalTime());
        }

        return new(WeeklyIssueStatus.Retryable);
    }

    /// <summary>Server-only KAN-126 lookup. Includes expired records for submission-time validation.</summary>
    public async Task<IssuedAttempt?> FindIssuedAttemptAsync(string? authenticatedAccountId, Guid attemptId)
    {
        if (!WeeklyConditions.IsKey(authenticatedAccountId) || attemptId == Guid.Empty)
            return null;

        LockedLedger? observed = await ReadLedgerAsync(authenticatedAccountId!);
        return observed?.Ledger.Attempts.SingleOrDefault(item => item.AttemptId == attemptId);
    }

    private async Task<LockedLedger?> ReadLedgerAsync(string accountId)
    {
        LockedLedger? observed = await attempts.ReadAsync(accountId);
        return observed is not null && !string.IsNullOrWhiteSpace(observed.WriteLock) && IsValidLedger(accountId, observed.Ledger)
            ? observed
            : null;
    }

    private static bool IsValidLedger(string accountId, PlayerLedger ledger)
    {
        if (ledger.AccountId != accountId || ledger.Attempts is null)
            return false;

        var attempts = new HashSet<Guid>();
        var requests = new HashSet<Guid>();
        var counts = new Dictionary<DateTimeOffset, int>();
        foreach (IssuedAttempt item in ledger.Attempts)
        {
            if (item is null || item.AccountId != accountId || item.Conditions is null
                || item.IssuedAtUtc.Offset != TimeSpan.Zero || item.ExpiresAtUtc.Offset != TimeSpan.Zero
                || item.AttemptId == Guid.Empty || !attempts.Add(item.AttemptId)
                || item.RequestId == Guid.Empty || !requests.Add(item.RequestId))
                return false;

            DateTimeOffset week = WeeklyConditions.Monday(item.IssuedAtUtc);
            int number = counts.GetValueOrDefault(week) + 1;
            if (number > WeeklyLimit || item.AttemptNumber != number || !item.Conditions.IsValidFor(week)
                || item.ExpiresAtUtc != Expiry(item.Conditions, item.IssuedAtUtc))
                return false;
            counts[week] = number;
        }

        return true;
    }

    private static DateTimeOffset Expiry(WeeklyConditions conditions, DateTimeOffset issuedAt)
    {
        DateTimeOffset durationEnd = issuedAt.AddSeconds(conditions.DurationSeconds);
        return durationEnd < conditions.WeekEndUtc ? durationEnd : conditions.WeekEndUtc;
    }

    private static WeeklyIssueResult View(IssuedAttempt attempt, DateTimeOffset now)
        => new(now < attempt.ExpiresAtUtc ? WeeklyIssueStatus.Available : WeeklyIssueStatus.Expired, attempt);
}
