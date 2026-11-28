#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectWeapon.Accounts
{
    public enum AccountMode
    {
        LocalGuest,
        SigningIn,
        Online,
        Expired,
        Closed
    }

    public enum AccountSignInStatus
    {
        Failed,
        SignedIn,
        Cancelled,
        Unavailable,
        InProgress,
        Superseded
    }

    public enum OnlineRequestStatus
    {
        Applied,
        Unavailable,
        Stale,
        Failed
    }

    public readonly struct GuestProfileId : IEquatable<GuestProfileId>
    {
        public GuestProfileId(Guid value)
        {
            if (value == Guid.Empty)
                throw new ArgumentException("A local guest profile ID is required.", nameof(value));
            Value = value;
        }

        public Guid Value { get; }
        public bool IsValid => Value != Guid.Empty;
        public bool Equals(GuestProfileId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is GuestProfileId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
    }

    public readonly struct ProviderSignInResult
    {
        private ProviderSignInResult(AccountSignInStatus status, string? playerId)
        {
            Status = status;
            PlayerId = playerId;
        }

        public AccountSignInStatus Status { get; }
        public string? PlayerId { get; }

        public static ProviderSignInResult Verified(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId) || playerId != playerId.Trim())
                throw new ArgumentException("A verified player ID is required.", nameof(playerId));
            return new ProviderSignInResult(AccountSignInStatus.SignedIn, playerId);
        }

        public static ProviderSignInResult Cancelled => new ProviderSignInResult(AccountSignInStatus.Cancelled, null);
        public static ProviderSignInResult Unavailable => new ProviderSignInResult(AccountSignInStatus.Unavailable, null);
    }

    /// <summary>Trusted provider boundary. Results carry an authorized player ID, never a credential.</summary>
    public interface IAccountProvider
    {
        event Action? SessionInvalidated;
        Task<ProviderSignInResult> SignInAsync(CancellationToken cancellation);
        bool IsAuthorized(string playerId);
        void SignOut();
    }

    /// <summary>An online identity scoped to exactly one accepted sign-in, even if the same player signs in again.</summary>
    public sealed class OnlineAccountSession
    {
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        internal OnlineAccountSession(string playerId)
        {
            PlayerId = playerId;
            Cancellation = lifetime.Token;
        }

        public string PlayerId { get; }
        public CancellationToken Cancellation { get; }

        internal void Revoke()
        {
            AccountSessionBoundary.Cancel(lifetime);
            lifetime.Dispose();
        }
    }
}
