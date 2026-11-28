using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ProjectWeapon.WeeklyChallenge.Tests;

[TestFixture]
public sealed class WeeklyChallengeIssuerTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 23, 0, 0, 0, TimeSpan.Zero);
    private static readonly WeeklyConditions Week = new(Monday, "condition-1", "map-3", -701, "enemies-2",
        "chests-4", "rules-1", "equipment-2", "old-sword", "hunting-bow", 600);
    private static readonly WeeklyConditions Following = Week with { WeekStartUtc = Monday.AddDays(7), ConditionsVersion = "condition-2" };

    private MemoryAuthority authority = null!;
    private DateTimeOffset now;
    private WeeklyChallengeIssuer Issuer => new(authority, new FrozenWeeklyConditions(authority), () => now);

    [SetUp]
    public void SetUp()
    {
        authority = new();
        authority.AddPlayer("alice");
        authority.AddPlayer("bob");
        authority.AddWeek(Week);
        authority.AddWeek(Following);
        now = Monday;
    }

    [Test]
    public async Task SameWeekUsesFrozenExactSettingsForAllPlayersAndIndependentAttemptIds()
    {
        Guid request = Guid.NewGuid();
        WeeklyIssueResult alice = await Issuer.IssueAsync("alice", request);
        // Editing the editable candidate does not replace the frozen record.
        authority.EditCandidate(Week with { MapSeed = 23, EquipmentVersion = "new-template" });
        WeeklyIssueResult bob = await Issuer.IssueAsync("bob", request);

        Assert.That(alice.Status, Is.EqualTo(WeeklyIssueStatus.Available));
        Assert.That(bob.Status, Is.EqualTo(WeeklyIssueStatus.Available));
        Assert.That(bob.Attempt!.Conditions, Is.EqualTo(Week));
        Assert.That(alice.Attempt!.Conditions, Is.EqualTo(bob.Attempt.Conditions));
        Assert.That(alice.Attempt.AttemptId, Is.Not.EqualTo(bob.Attempt.AttemptId));
        Assert.That(alice.Attempt.AccountId, Is.EqualTo("alice"));
        Assert.That(bob.Attempt.AccountId, Is.EqualTo("bob"));
        Assert.That(authority.WeekCommits, Is.EqualTo(1));
    }

    [Test]
    public async Task HittingWeeklyLimitDoesNotChargeAnotherAttemptOrDisplaceTheOriginalReceipt()
    {
        Guid[] requests = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        WeeklyIssueResult[] results = new WeeklyIssueResult[5];
        for (int i = 0; i < requests.Length; i++)
            results[i] = await Issuer.IssueAsync("alice", requests[i]);

        WeeklyIssueResult replay = await Issuer.IssueAsync("alice", requests[4]);
        WeeklyIssueResult denied = await Issuer.IssueAsync("alice", Guid.NewGuid());
        Assert.That(results.Select(result => result.Attempt!.AttemptNumber), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        Assert.That(replay, Is.EqualTo(results[4]));
        Assert.That(denied.Status, Is.EqualTo(WeeklyIssueStatus.WeeklyLimitReached));
        Assert.That(denied.Attempt, Is.Null);
        Assert.That(authority.AttemptCommits, Is.EqualTo(5));
    }

    [Test]
    public async Task DurationAndMondayUseUtcAndExactExpiryIsStrict()
    {
        DateTimeOffset start = Monday.AddHours(5);
        now = start.ToOffset(TimeSpan.FromHours(9));
        Guid durationRequest = Guid.NewGuid();
        WeeklyIssueResult first = await Issuer.IssueAsync("alice", durationRequest);
        Assert.That(first.Attempt!.IssuedAtUtc.Offset, Is.EqualTo(TimeSpan.Zero));
        Assert.That(first.Attempt.ExpiresAtUtc, Is.EqualTo(start.AddSeconds(600)));
        now = first.Attempt.ExpiresAtUtc.AddTicks(-1);
        Assert.That(await Issuer.IssueAsync("alice", durationRequest), Is.EqualTo(first));
        now = first.Attempt.ExpiresAtUtc;
        Assert.That(await Issuer.IssueAsync("alice", durationRequest),
            Is.EqualTo(new WeeklyIssueResult(WeeklyIssueStatus.Expired, first.Attempt)));

        now = Following.WeekStartUtc.AddTicks(-1);
        Guid nearEndRequest = Guid.NewGuid();
        WeeklyIssueResult nearEnd = await Issuer.IssueAsync("alice", nearEndRequest);
        Assert.That(nearEnd.Attempt!.ExpiresAtUtc, Is.EqualTo(Following.WeekStartUtc));
        now = Following.WeekStartUtc;
        Assert.That(await Issuer.IssueAsync("alice", nearEndRequest),
            Is.EqualTo(new WeeklyIssueResult(WeeklyIssueStatus.Expired, nearEnd.Attempt)));
        WeeklyIssueResult nextWeek = await Issuer.IssueAsync("alice", Guid.NewGuid());
        Assert.That(nextWeek.Attempt!.Conditions, Is.EqualTo(Following));
        Assert.That(nextWeek.Attempt.AttemptNumber, Is.EqualTo(1));
        Assert.That(authority.AttemptCommits, Is.EqualTo(3));
    }

    [Test]
    public async Task AdvancesToServerWeekWhenMondayArrivesDuringConditionRead()
    {
        now = Following.WeekStartUtc.AddTicks(-1);
        authority.AfterFirstWeekRead = () => now = Following.WeekStartUtc;
        WeeklyIssueResult result = await Issuer.IssueAsync("alice", Guid.NewGuid());
        Assert.That(result.Attempt!.Conditions, Is.EqualTo(Following));
        Assert.That(result.Attempt.IssuedAtUtc, Is.EqualTo(Following.WeekStartUtc));
        Assert.That(authority.AttemptCommits, Is.EqualTo(1));
    }

    [Test]
    public async Task RestrictsSubmissionLookupToAuthenticatedOwnerAndRealIssuedIds()
    {
        Guid request = Guid.NewGuid();
        IssuedAttempt actual = (await Issuer.IssueAsync("alice", request)).Attempt!;
        Assert.That(await Issuer.FindIssuedAttemptAsync("bob", actual.AttemptId), Is.Null);
        Assert.That(await Issuer.FindIssuedAttemptAsync(null, actual.AttemptId), Is.Null);
        Assert.That(await Issuer.FindIssuedAttemptAsync("alice", request), Is.Null);
        now = Following.WeekStartUtc.AddDays(1);
        Assert.That(await Issuer.FindIssuedAttemptAsync("alice", actual.AttemptId), Is.EqualTo(actual));
    }

    [TestCase(null, false, WeeklyIssueStatus.Unauthorized)]
    [TestCase(" alice ", false, WeeklyIssueStatus.Unauthorized)]
    [TestCase("alice", true, WeeklyIssueStatus.InvalidRequest)]
    [TestCase("unprovisioned", false, WeeklyIssueStatus.AuthorityUnavailable)]
    public async Task RejectsUnauthenticatedInvalidAndMissingLedgerBeforeAnyWrite(string? account, bool empty, WeeklyIssueStatus status)
    {
        Assert.That((await Issuer.IssueAsync(account, empty ? Guid.Empty : Guid.NewGuid())).Status, Is.EqualTo(status));
        Assert.That(authority.CombinedCommits, Is.Zero);
    }

    [Test]
    public async Task MissingLockMissingScheduleAndInvalidCandidateAreUnavailableWithoutWrites()
    {
        authority.RemoveLedgerLock = true;
        Assert.That((await Issuer.IssueAsync("alice", Guid.NewGuid())).Status, Is.EqualTo(WeeklyIssueStatus.AuthorityUnavailable));
        authority.RemoveLedgerLock = false;
        authority.RemoveWeekLock = true;
        Assert.That((await Issuer.IssueAsync("alice", Guid.NewGuid())).Status, Is.EqualTo(WeeklyIssueStatus.AuthorityUnavailable));
        authority.RemoveWeekLock = false;
        authority.EditCandidate(Week with { DurationSeconds = 0 });
        Assert.That((await Issuer.IssueAsync("alice", Guid.NewGuid())).Status, Is.EqualTo(WeeklyIssueStatus.AuthorityUnavailable));
        now = Following.WeekStartUtc.AddDays(14);
        Assert.That((await Issuer.IssueAsync("alice", Guid.NewGuid())).Status, Is.EqualTo(WeeklyIssueStatus.AuthorityUnavailable));
        Assert.That(authority.CombinedCommits, Is.Zero);
    }

    [Test]
    public async Task RaceToFreezeReturnsTheOneCommittedWinnerAfterCandidateChange()
    {
        authority.WeekWriteGate = new(2);
        WeeklyConditions newer = Week with { MapSeed = 15 };
        authority.BeforeContestedFreeze = () => authority.EditCandidate(newer);
        Task<WeeklyIssueResult> alice = Issuer.IssueAsync("alice", Guid.NewGuid());
        Task<WeeklyIssueResult> bob = Issuer.IssueAsync("bob", Guid.NewGuid());
        WeeklyIssueResult[] results = await Task.WhenAll(alice, bob);

        Assert.That(results.Select(result => result.Attempt!.Conditions), Is.All.EqualTo(newer));
        Assert.That(authority.WeekCommits, Is.EqualTo(1));
        Assert.That(authority.FailedWeekWrites, Is.EqualTo(2));
    }

    [Test]
    public async Task ConcurrentRetriesOfSameRequestReuseCommittedAttempt()
    {
        authority.PlayerWriteGate = new(2);
        Guid request = Guid.NewGuid();
        var results = await Task.WhenAll(Issuer.IssueAsync("alice", request), Issuer.IssueAsync("alice", request));
        Assert.That(results[0].Status, Is.EqualTo(WeeklyIssueStatus.Available));
        Assert.That(results[0], Is.EqualTo(results[1]));
        Assert.That(authority.AttemptCommits, Is.EqualTo(1));
        Assert.That(authority.FailedPlayerWrites, Is.EqualTo(1));
    }

    [Test]
    public async Task ConcurrentDistinctRequestsCannotBothReserveFifthPlace()
    {
        for (int i = 0; i < 4; i++)
            await Issuer.IssueAsync("alice", Guid.NewGuid());
        authority.PlayerWriteGate = new(2);

        var results = await Task.WhenAll(Issuer.IssueAsync("alice", Guid.NewGuid()), Issuer.IssueAsync("alice", Guid.NewGuid()));
        Assert.That(results.Select(result => result.Status), Is.EquivalentTo(new[]
        {
            WeeklyIssueStatus.Available, WeeklyIssueStatus.WeeklyLimitReached
        }));
        Assert.That(results.Single(result => result.Attempt is not null).Attempt!.AttemptNumber, Is.EqualTo(5));
        Assert.That(authority.AttemptCommits, Is.EqualTo(5));
    }

    [Test]
    public async Task BoundedContentionReturnsRetryableAndSameRequestCanRecover()
    {
        Guid request = Guid.NewGuid();
        authority.RefusePlayerWrites = true;
        Assert.That((await Issuer.IssueAsync("alice", request)).Status, Is.EqualTo(WeeklyIssueStatus.Retryable));
        Assert.That(authority.AttemptCommits, Is.Zero);
        Assert.That(authority.FailedPlayerWrites, Is.EqualTo(4));
        authority.RefusePlayerWrites = false;
        Assert.That((await Issuer.IssueAsync("alice", request)).Attempt!.AttemptNumber, Is.EqualTo(1));
    }

    [Test]
    public async Task LostResponseAfterCommitReplaysAcrossWeekEvenWithoutPublishedConfig()
    {
        Guid request = Guid.NewGuid();
        authority.AfterPlayerCommit = () => throw new TimeoutException();
        Assert.ThrowsAsync<TimeoutException>(async () => await Issuer.IssueAsync("alice", request));
        now = Following.WeekStartUtc;
        authority.RemoveWeekLock = true;
        WeeklyIssueResult replay = await Issuer.IssueAsync("alice", request);
        Assert.That(replay.Status, Is.EqualTo(WeeklyIssueStatus.Expired));
        Assert.That(replay.Attempt, Is.EqualTo(authority.LastCommitted));
        Assert.That(replay.Attempt!.Conditions, Is.EqualTo(Week));
        Assert.That(authority.AttemptCommits, Is.EqualTo(1));
    }

    private sealed class MemoryAuthority : IWeeklyAttemptStore, IWeeklyConditionStore
    {
        private readonly object sync = new();
        private readonly Dictionary<string, LockedLedger> ledgers = new();
        private readonly Dictionary<DateTimeOffset, LockedWeekDocument> weeks = new();
        internal int WeekCommits { get; private set; }
        internal int AttemptCommits { get; private set; }
        internal int FailedWeekWrites { get; private set; }
        internal int FailedPlayerWrites { get; private set; }
        internal int CombinedCommits => WeekCommits + AttemptCommits;
        internal AsyncGate? WeekWriteGate { get; set; }
        internal AsyncGate? PlayerWriteGate { get; set; }
        internal bool RemoveLedgerLock { get; set; }
        internal bool RemoveWeekLock { get; set; }
        internal bool RefusePlayerWrites { get; set; }
        internal Action? AfterFirstWeekRead { get; set; }
        internal Action? BeforeContestedFreeze { get; set; }
        internal Action? AfterPlayerCommit { get; set; }
        internal IssuedAttempt? LastCommitted { get; private set; }

        internal void AddPlayer(string id) => ledgers.Add(id, new LockedLedger(NewLock(), new PlayerLedger(id, [])));
        internal void AddWeek(WeeklyConditions candidate)
            => weeks.Add(candidate.WeekStartUtc, new(NewLock(), new WeekDocument(candidate, null)));

        internal void EditCandidate(WeeklyConditions next)
        {
            lock (sync)
            {
                LockedWeekDocument prior = weeks[next.WeekStartUtc];
                weeks[next.WeekStartUtc] = new(NewLock(), prior.Document with { Candidate = next });
            }
        }

        public Task<LockedLedger?> ReadAsync(string accountId)
        {
            lock (sync)
            {
                if (!ledgers.TryGetValue(accountId, out LockedLedger? observed))
                    return Task.FromResult<LockedLedger?>(null);
                return Task.FromResult<LockedLedger?>(RemoveLedgerLock ? observed with { WriteLock = "" } : observed);
            }
        }

        public async Task<bool> TryReplaceAsync(string accountId, LockedLedger observed, PlayerLedger next)
        {
            if (PlayerWriteGate is { } gate)
                await gate.EnterAsync();
            lock (sync)
            {
                if (RefusePlayerWrites || ledgers[accountId].WriteLock != observed.WriteLock)
                {
                    FailedPlayerWrites++;
                    return false;
                }
                ledgers[accountId] = new(NewLock(), next);
                LastCommitted = next.Attempts.Last();
                AttemptCommits++;
                AfterPlayerCommit?.Invoke();
                return true;
            }
        }

        public Task<LockedWeekDocument?> ReadWeekAsync(DateTimeOffset utcMonday)
        {
            lock (sync)
            {
                Action? callback = AfterFirstWeekRead;
                AfterFirstWeekRead = null;
                callback?.Invoke();
                if (!weeks.TryGetValue(utcMonday, out LockedWeekDocument? observed))
                    return Task.FromResult<LockedWeekDocument?>(null);
                return Task.FromResult<LockedWeekDocument?>(RemoveWeekLock ? observed with { WriteLock = "" } : observed);
            }
        }

        public async Task<bool> TryFreezeAsync(DateTimeOffset utcMonday, LockedWeekDocument observed)
        {
            if (WeekWriteGate is { } gate)
                await gate.EnterAsync();
            lock (sync)
            {
                Action? callback = BeforeContestedFreeze;
                BeforeContestedFreeze = null;
                callback?.Invoke();
                if (weeks[utcMonday].WriteLock != observed.WriteLock)
                {
                    FailedWeekWrites++;
                    return false;
                }
                weeks[utcMonday] = new(NewLock(), observed.Document with { Frozen = observed.Document.Candidate });
                WeekCommits++;
                return true;
            }
        }

        private static string NewLock() => Guid.NewGuid().ToString("N");
    }

    private sealed class AsyncGate(int participants)
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        internal Task EnterAsync()
        {
            if (Interlocked.Increment(ref arrivals) >= participants)
                release.TrySetResult();
            return release.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
