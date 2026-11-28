#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProjectWeapon.Inventory;

namespace ProjectWeapon.AdRewards
{
    /// <summary>Binding produced only by the host verifier after validating a LevelPlay completion.</summary>
    public readonly struct VerifiedAdRewardCompletion
    {
        public VerifiedAdRewardCompletion(string authorityScope, string accountId, string reservationToken, string providerEventId)
        {
            AuthorityScope = authorityScope;
            AccountId = accountId;
            ReservationToken = reservationToken;
            ProviderEventId = providerEventId;
        }

        public string AuthorityScope { get; }
        public string AccountId { get; }
        public string ReservationToken { get; }
        public string ProviderEventId { get; }

        internal bool IsValid => !string.IsNullOrWhiteSpace(AuthorityScope) && !string.IsNullOrWhiteSpace(AccountId)
            && !string.IsNullOrWhiteSpace(ReservationToken) && !string.IsNullOrWhiteSpace(ProviderEventId);
    }

    public interface IRewardedAdProofVerifier
    {
        /// <summary>
        /// Verifies the provider request against the server configuration, including signature, signed user and reward,
        /// and the account's authoritative association with the exact reservation.
        /// </summary>
        bool TryVerifyLevelPlayCompletion(string signedCallback, out VerifiedAdRewardCompletion completion);
    }

    public enum AdRewardReservationState
    {
        Viewing = 1,
        AwaitingProof,
        Released
    }

    /// <summary>Authoritative reservation data. Expiry is derived from the UTC acceptance interval.</summary>
    public sealed class AdRewardReservation
    {
        public AdRewardReservation(string token, string accountId, DateTime createdAtUtc, DateTime acceptUntilUtc,
            bool acceptsCompletion)
            : this(token, accountId, createdAtUtc, acceptUntilUtc, Guid.Empty,
                acceptsCompletion ? AdRewardReservationState.AwaitingProof : AdRewardReservationState.Released)
        {
        }

        public AdRewardReservation(string token, string accountId, DateTime createdAtUtc, DateTime acceptUntilUtc,
            Guid requestId, AdRewardReservationState state)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(accountId))
                throw new ArgumentException("A reservation token and account ID are required.");
            if (createdAtUtc.Kind != DateTimeKind.Utc || acceptUntilUtc.Kind != DateTimeKind.Utc || acceptUntilUtc <= createdAtUtc)
                throw new ArgumentException("A reservation needs an increasing UTC acceptance interval.");
            if (!Enum.IsDefined(typeof(AdRewardReservationState), state) ||
                (requestId == Guid.Empty && state == AdRewardReservationState.Viewing))
                throw new ArgumentException("A viewing reservation needs a stable request ID and a valid state.");

            Token = token;
            AccountId = accountId;
            CreatedAtUtc = createdAtUtc;
            AcceptUntilUtc = acceptUntilUtc;
            RequestId = requestId;
            State = state;
        }

        public string Token { get; }
        public string AccountId { get; }
        public DateTime CreatedAtUtc { get; }
        public DateTime AcceptUntilUtc { get; }
        public Guid RequestId { get; }
        public AdRewardReservationState State { get; }
        public bool AcceptsCompletion => State != AdRewardReservationState.Released;

        internal AdRewardReservation WithState(AdRewardReservationState state)
            => new AdRewardReservation(Token, AccountId, CreatedAtUtc, AcceptUntilUtc, RequestId, state);
    }

    /// <summary>One immutable durable grant contains the receipt, owned weapon, and one confirmed use on the pinned day.</summary>
    public sealed class AdRewardGrant
    {
        public AdRewardGrant(VerifiedAdRewardCompletion completion, DateTime reservationDayUtc, WeaponInstance weapon)
        {
            if (!completion.IsValid)
                throw new ArgumentException("A complete verified provider binding is required.", nameof(completion));
            if (reservationDayUtc.Kind != DateTimeKind.Utc || reservationDayUtc != reservationDayUtc.Date)
                throw new ArgumentException("The reservation day must be UTC midnight.", nameof(reservationDayUtc));
            if (weapon == null || !weapon.IsValid || weapon.Provenance != WeaponProvenance.Personal
                || weapon.Location.Kind != WeaponLocationKind.Warehouse)
                throw new ArgumentException("A grant needs a personal warehouse weapon.", nameof(weapon));

            Completion = completion;
            ReservationDayUtc = reservationDayUtc;
            Weapon = weapon;
        }

        public VerifiedAdRewardCompletion Completion { get; }
        public DateTime ReservationDayUtc { get; }
        public WeaponInstance Weapon { get; }
    }

    public sealed class AdRewardAuthoritySnapshot
    {
        public AdRewardAuthoritySnapshot(string authorityScope, long revision, IEnumerable<AdRewardReservation> reservations,
            IEnumerable<AdRewardGrant> grants)
        {
            if (string.IsNullOrWhiteSpace(authorityScope))
                throw new ArgumentException("An authority scope is required.", nameof(authorityScope));
            if (revision < 0)
                throw new ArgumentOutOfRangeException(nameof(revision));

            AuthorityScope = authorityScope;
            Revision = revision;
            Reservations = Array.AsReadOnly(reservations.ToArray());
            Grants = Array.AsReadOnly(grants.ToArray());
            ValidateIdentities();
        }

        public string AuthorityScope { get; }
        public long Revision { get; }
        public IReadOnlyList<AdRewardReservation> Reservations { get; }
        public IReadOnlyList<AdRewardGrant> Grants { get; }

        public int ConfirmedUses(string accountId, DateTime reservationDayUtc)
            => Grants.Count(grant => grant.Completion.AccountId == accountId && grant.ReservationDayUtc == reservationDayUtc);

        private void ValidateIdentities()
        {
            var reservationsByToken = Reservations.ToDictionary(reservation => reservation.Token, StringComparer.Ordinal);
            var requests = new HashSet<(string AccountId, Guid RequestId)>();
            foreach (AdRewardReservation reservation in Reservations)
            {
                if (reservation.RequestId != Guid.Empty && !requests.Add((reservation.AccountId, reservation.RequestId)))
                    throw new ArgumentException("The authority snapshot contains a duplicate account reservation request.");
            }
            var events = new HashSet<string>(StringComparer.Ordinal);
            var grantedTokens = new HashSet<string>(StringComparer.Ordinal);
            var weapons = new HashSet<WeaponInstanceId>();

            foreach (AdRewardGrant grant in Grants)
            {
                VerifiedAdRewardCompletion completion = grant.Completion;
                if (completion.AuthorityScope != AuthorityScope || !events.Add(completion.ProviderEventId)
                    || !grantedTokens.Add(completion.ReservationToken) || !weapons.Add(grant.Weapon.Id))
                    throw new ArgumentException("The authority snapshot contains an invalid or duplicated grant identity.");
                if (reservationsByToken.TryGetValue(completion.ReservationToken, out AdRewardReservation current)
                    && (current.AccountId != completion.AccountId || current.CreatedAtUtc.Date != grant.ReservationDayUtc))
                    throw new ArgumentException("The grant does not match its reservation.");
            }
        }
    }

    public interface IAdRewardAuthorityStore
    {
        AdRewardAuthoritySnapshot Read();

        /// <summary>
        /// Compare the revision and commit the entire grant as one durable operation. Preserve event, reservation and
        /// weapon-instance uniqueness within the authority scope. Return false when the revision has changed.
        /// </summary>
        bool TryCommit(long expectedRevision, AdRewardGrant grant);
    }

    public interface IAdRewardReservationStore : IAdRewardAuthorityStore
    {
        /// <summary>
        /// Adds or changes one reservation and preserves all grants. Compare and advance the same revision as TryCommit.
        /// </summary>
        bool TryWriteReservation(long expectedRevision, AdRewardReservation reservation);
    }
}
