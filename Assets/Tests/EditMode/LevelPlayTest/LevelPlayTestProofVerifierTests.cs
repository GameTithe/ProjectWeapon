#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.AdRewards;
using ProjectWeapon.LevelPlayTestAuthority;
using ProjectWeapon.WeaponDraw;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.LevelPlayTest.Tests
{
    public sealed class LevelPlayTestProofVerifierTests
    {
        private const string Scope = "dedicated-test";
        private const string Token = "DemoReservation";
        private const string UserId = "testUser1";
        private const string TestKey = "ProjectWeaponTestSecret";
        private const string Query = "?timestamp=1700000000&eventId=evt1&userId=testUser1&rewards=1"
            + "&dynamicUserId=DemoReservation&appKey=testApp&signature=1c68a505b3640da5ac707773414d1fd1";

        [Test]
        public void AuthorizedProviderProofUsesTheExistingSingleGrantService()
        {
            DateTime now = new DateTime(2023, 11, 14, 23, 0, 0, DateTimeKind.Utc);
            var store = new TestStore(new AdRewardAuthoritySnapshot(Scope, 0, new[]
            {
                new AdRewardReservation(Token, UserId, now.AddHours(-1), now.AddDays(9), true)
            }, Array.Empty<AdRewardGrant>()));
            var verifier = new LevelPlayTestProofVerifier(Scope, "testApp", TestKey, 1,
                binding => binding.ReservationToken == Token && binding.ProviderEventId == "evt1"
                    && binding.SignedUserId == UserId && binding.SignedTimestamp == "1700000000");
            var service = new AdRewardCompletionService(Scope, verifier, store, CreateDraw(), _ => 0, () => now);
            const string provider = AdRewardCompletionService.LevelPlayProviderId;

            foreach (string bad in new[]
            {
                Query.Replace("eventId=evt1", "eventId=evt2"),
                Query.Replace("userId=testUser1", "userId=otherAccount"),
                Query.Replace("&rewards=1", "&rewards=2"),
                Query.Replace("dynamicUserId=DemoReservation", "dynamicUserId=OtherReservation"),
                Query.Replace("dynamicUserId=DemoReservation", "dynamicUserId=SecondReservationOfSameUser"),
                Query.Replace("appKey=testApp", "appKey=productionApp"),
                Query.Replace("signature=", "unknown="),
                Query.Replace("timestamp=1700000000", "timestamp=bad"),
                Query + "&eventId=evt1",
                Query + "&dynamicUserId=OtherReservation"
            })
            {
                AdRewardClaimResult denied = service.ProcessCallback(provider, bad);
                Assert.That(denied.Status, Is.EqualTo(AdRewardClaimStatus.UnverifiedCompletion), bad);
                Assert.That(denied.Receipt, Is.Null, bad);
            }
            Assert.That(store.Read().Revision, Is.Zero);
            Assert.That(store.Read().Grants, Is.Empty);

            AdRewardClaimResult accepted = service.ProcessCallback(provider, Query);
            Assert.That(accepted.Status, Is.EqualTo(AdRewardClaimStatus.Granted));
            Assert.That(accepted.Receipt!.Completion.ProviderEventId, Is.EqualTo("evt1"));
            AdRewardClaimResult replay = service.ProcessCallback(provider, Query);
            Assert.That(replay.Status, Is.EqualTo(AdRewardClaimStatus.AlreadyGranted));
            Assert.That(replay.Receipt!.Weapon.Id, Is.EqualTo(accepted.Receipt.Weapon.Id));
            Assert.That(store.Read().ConfirmedUses(UserId, now.Date), Is.EqualTo(1));
            Assert.That(store.Read().Grants, Has.Count.EqualTo(1));
        }

        private static WeightedWeaponDraw CreateDraw()
        {
            int[] sizes = { 5, 5, 5, 3, 3 };
            var grades = new Dictionary<WeaponDefinitionId, int>();
            var pools = new IReadOnlyList<WeaponDefinitionId>[sizes.Length];
            for (int i = 0; i < sizes.Length; i++)
            {
                int grade = i + 1;
                WeaponDefinitionId[] pool = Enumerable.Range(0, sizes[i])
                    .Select(number => new WeaponDefinitionId($"test_{grade}_{number}")).ToArray();
                foreach (WeaponDefinitionId id in pool)
                    grades.Add(id, grade);
                pools[i] = pool;
            }

            return new WeightedWeaponDraw(WeightedWeaponDraw.DraftGradeWeightsBasisPoints, pools, grades);
        }

        private sealed class TestStore : IAdRewardAuthorityStore
        {
            private AdRewardAuthoritySnapshot snapshot;
            public TestStore(AdRewardAuthoritySnapshot initial) => snapshot = initial;
            public AdRewardAuthoritySnapshot Read() => snapshot;
            public bool TryCommit(long expectedRevision, AdRewardGrant grant)
            {
                if (snapshot.Revision != expectedRevision)
                    return false;
                snapshot = new AdRewardAuthoritySnapshot(Scope, expectedRevision + 1, snapshot.Reservations,
                    snapshot.Grants.Append(grant));
                return true;
            }
        }
    }
}
