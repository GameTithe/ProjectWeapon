using System;
using System.Threading.Tasks;

namespace ProjectWeapon.WeeklyChallenge;

internal sealed record WeekDocument(WeeklyConditions Candidate, WeeklyConditions? Frozen);
internal sealed record LockedWeekDocument(string WriteLock, WeekDocument Document);

internal interface IWeeklyConditionStore
{
    Task<LockedWeekDocument?> ReadWeekAsync(DateTimeOffset utcMonday);
    Task<bool> TryFreezeAsync(DateTimeOffset utcMonday, LockedWeekDocument observed);
}

internal sealed class FrozenWeeklyConditions(IWeeklyConditionStore store) : IFrozenWeeklyConditions
{
    public async Task<WeeklyConditions?> GetOrFreezeAsync(DateTimeOffset weekStartUtc)
    {
        for (int attempt = 0; attempt < WeeklyChallengeIssuer.MaxTries; attempt++)
        {
            LockedWeekDocument? observed = await store.ReadWeekAsync(weekStartUtc);
            if (observed is null || string.IsNullOrWhiteSpace(observed.WriteLock))
                return null;

            WeeklyConditions? frozen = observed.Document.Frozen;
            if (frozen is not null)
                return frozen.IsValidFor(weekStartUtc) ? frozen : null;

            WeeklyConditions candidate = observed.Document.Candidate;
            if (candidate is null || !candidate.IsValidFor(weekStartUtc))
                return null;
            if (await store.TryFreezeAsync(weekStartUtc, observed))
                return candidate;
        }

        return null;
    }
}
