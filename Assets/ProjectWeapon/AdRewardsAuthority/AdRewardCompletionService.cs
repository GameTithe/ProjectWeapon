#nullable enable

using System;
using System.Linq;
using ProjectWeapon.Inventory;
using ProjectWeapon.WeaponDraw;

namespace ProjectWeapon.AdRewards
{
    public enum AdRewardClaimStatus
    {
        InvalidProvider,
        UnverifiedCompletion,
        ReservationMismatch,
        ReservationUnavailable,
        ExpiredReservation,
        ConflictingCompletion,
        RetryRequired,
        Granted,
        AlreadyGranted
    }

    public readonly struct AdRewardClaimResult
    {
        internal AdRewardClaimResult(AdRewardClaimStatus status, AdRewardGrant? receipt = null)
        {
            Status = status;
            Receipt = receipt;
        }

        public AdRewardClaimStatus Status { get; }
        public AdRewardGrant? Receipt { get; }
    }

    /// <summary>Server authority prototype for the verified provider callback path.</summary>
    public sealed class AdRewardCompletionService
    {
        public const string LevelPlayProviderId = "unity_levelplay";

        private readonly string authorityScope;
        private readonly IRewardedAdProofVerifier verifier;
        private readonly IAdRewardAuthorityStore store;
        private readonly WeightedWeaponDraw draw;
        private readonly Func<int, int> nextExclusive;
        private readonly Func<DateTime> utcNow;

        public AdRewardCompletionService(string authorityScope, IRewardedAdProofVerifier verifier,
            IAdRewardAuthorityStore store, WeightedWeaponDraw draw, Func<int, int> nextExclusive, Func<DateTime> utcNow)
        {
            if (string.IsNullOrWhiteSpace(authorityScope))
                throw new ArgumentException("An authority scope is required.", nameof(authorityScope));

            this.authorityScope = authorityScope;
            this.verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.draw = draw ?? throw new ArgumentNullException(nameof(draw));
            this.nextExclusive = nextExclusive ?? throw new ArgumentNullException(nameof(nextExclusive));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public AdRewardClaimResult ProcessCallback(string providerId, string signedCallback)
        {
            if (!StringComparer.Ordinal.Equals(providerId, LevelPlayProviderId))
                return new AdRewardClaimResult(AdRewardClaimStatus.InvalidProvider);
            if (string.IsNullOrWhiteSpace(signedCallback)
                || !verifier.TryVerifyLevelPlayCompletion(signedCallback, out VerifiedAdRewardCompletion completion)
                || !completion.IsValid)
                return new AdRewardClaimResult(AdRewardClaimStatus.UnverifiedCompletion);
            if (completion.AuthorityScope != authorityScope)
                return new AdRewardClaimResult(AdRewardClaimStatus.ReservationMismatch);

            AdRewardAuthoritySnapshot snapshot = ReadCurrentScope();
            if (CheckPriorGrant(snapshot, completion) is { } previous)
                return previous;

            AdRewardReservation? reservation = snapshot.Reservations.FirstOrDefault(item => item.Token == completion.ReservationToken);
            if (reservation == null || reservation.AccountId != completion.AccountId)
                return new AdRewardClaimResult(AdRewardClaimStatus.ReservationMismatch);
            if (!reservation.AcceptsCompletion)
                return new AdRewardClaimResult(AdRewardClaimStatus.ReservationUnavailable);

            DateTime now = utcNow();
            if (now.Kind != DateTimeKind.Utc || now < reservation.CreatedAtUtc)
                return new AdRewardClaimResult(AdRewardClaimStatus.RetryRequired);
            if (now >= reservation.AcceptUntilUtc)
                return new AdRewardClaimResult(AdRewardClaimStatus.ExpiredReservation);

            WeaponDrawResult reward = draw.Draw(nextExclusive);
            WeaponInstance weapon = WeaponInstance.Create(reward.DefinitionId, WeaponProvenance.Personal, WeaponLocation.Warehouse());
            var grant = new AdRewardGrant(completion, reservation.CreatedAtUtc.Date, weapon);
            if (store.TryCommit(snapshot.Revision, grant))
                return new AdRewardClaimResult(AdRewardClaimStatus.Granted, grant);

            return CheckPriorGrant(ReadCurrentScope(), completion) ?? new AdRewardClaimResult(AdRewardClaimStatus.RetryRequired);
        }

        private AdRewardAuthoritySnapshot ReadCurrentScope()
        {
            AdRewardAuthoritySnapshot snapshot = store.Read();
            if (snapshot.AuthorityScope != authorityScope)
                throw new InvalidOperationException("The store belongs to a different authority scope.");
            return snapshot;
        }

        private static AdRewardClaimResult? CheckPriorGrant(AdRewardAuthoritySnapshot snapshot, VerifiedAdRewardCompletion completion)
        {
            AdRewardGrant? recorded = snapshot.Grants.FirstOrDefault(grant =>
                grant.Completion.ProviderEventId == completion.ProviderEventId);
            if (recorded != null)
                return recorded.Completion.AccountId == completion.AccountId
                    && recorded.Completion.ReservationToken == completion.ReservationToken
                    ? new AdRewardClaimResult(AdRewardClaimStatus.AlreadyGranted, recorded)
                    : new AdRewardClaimResult(AdRewardClaimStatus.ConflictingCompletion);

            return snapshot.Grants.Any(grant => grant.Completion.ReservationToken == completion.ReservationToken)
                ? new AdRewardClaimResult(AdRewardClaimStatus.ConflictingCompletion)
                : null;
        }
    }
}
