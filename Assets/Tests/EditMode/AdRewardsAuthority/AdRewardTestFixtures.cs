#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.WeaponDraw;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.AdRewards.Tests
{
    internal static class AdRewardDrawFixture
    {
        public static WeightedWeaponDraw Create()
        {
            var grades = new Dictionary<WeaponDefinitionId, int>();
            IReadOnlyList<WeaponDefinitionId>[] pools = new IReadOnlyList<WeaponDefinitionId>[5];
            for (int grade = 1; grade <= pools.Length; grade++)
            {
                var definitions = new WeaponDefinitionId[grade <= 3 ? 5 : 3];
                for (int index = 0; index < definitions.Length; index++)
                {
                    var id = new WeaponDefinitionId($"grade{grade}-{index}");
                    grades.Add(id, grade);
                    definitions[index] = id;
                }
                pools[grade - 1] = definitions;
            }
            return new WeightedWeaponDraw(WeightedWeaponDraw.DraftGradeWeightsBasisPoints, pools, grades);
        }
    }

    internal sealed class ProofVerifierDouble : IRewardedAdProofVerifier
    {
        private readonly Dictionary<string, VerifiedAdRewardCompletion> verified = new Dictionary<string, VerifiedAdRewardCompletion>();

        public void Add(string signedCallback, VerifiedAdRewardCompletion completion) => verified.Add(signedCallback, completion);

        public bool TryVerifyLevelPlayCompletion(string signedCallback, out VerifiedAdRewardCompletion completion)
            => verified.TryGetValue(signedCallback, out completion);
    }

    internal sealed class CommitDouble : IAdRewardAuthorityStore
    {
        private readonly IAdRewardAuthorityStore store;
        private readonly Func<long, AdRewardGrant, bool> commit;

        public CommitDouble(IAdRewardAuthorityStore store, Func<long, AdRewardGrant, bool> commit)
        {
            this.store = store;
            this.commit = commit;
        }

        public AdRewardAuthoritySnapshot Read() => store.Read();
        public bool TryCommit(long expectedRevision, AdRewardGrant grant) => commit(expectedRevision, grant);
    }
}
