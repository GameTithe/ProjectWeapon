#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace ProjectWeapon.AdRewards.Tests
{
    internal static class AdRewardCrashWriter
    {
        private static void Main()
        {
            string path = Path.GetFullPath(ReadEnvironment("LEDGER"));
            string profile = Path.GetDirectoryName(path)!;
            string root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (Path.GetDirectoryName(profile) != root || Path.GetFileName(path) != "authority.bin"
                || !Path.GetFileName(profile).StartsWith("ProjectWeapon-KAN97-", StringComparison.Ordinal)
                || !Guid.TryParseExact(Path.GetFileName(profile).Substring("ProjectWeapon-KAN97-".Length), "N", out _)
                || !File.Exists(path))
                throw new ArgumentException("A seeded isolated KAN-97 profile is required.");

            var store = new AdRewardFileStore(path, new object());
            AdRewardAuthoritySnapshot snapshot = store.Read();
            string token = ReadEnvironment("TOKEN");
            AdRewardReservation reservation = snapshot.Reservations.Single(item => item.Token == token);
            string proof = ReadEnvironment("PROOF");
            var verifier = new ProofVerifierDouble();
            verifier.Add(proof, new VerifiedAdRewardCompletion(snapshot.AuthorityScope, reservation.AccountId, token,
                ReadEnvironment("EVENT")));

            var pauseBeforeReply = new CommitDouble(store, (revision, grant) =>
            {
                if (!store.TryCommit(revision, grant))
                    throw new InvalidOperationException("The isolated writer could not commit the reward.");
                Console.WriteLine("READY " + grant.Weapon.Id);
                Console.Out.Flush();
                Thread.Sleep(TimeSpan.FromSeconds(30));
                throw new TimeoutException("The test parent did not terminate the writer.");
            });
            var service = new AdRewardCompletionService(snapshot.AuthorityScope, verifier, pauseBeforeReply,
                AdRewardDrawFixture.Create(), _ => 0, () => reservation.CreatedAtUtc.AddMinutes(2));
            AdRewardClaimResult reply = service.ProcessCallback(AdRewardCompletionService.LevelPlayProviderId, proof);
            throw new InvalidOperationException("The writer returned before the kill checkpoint: " + reply.Status);
        }

        private static string ReadEnvironment(string key)
            => Environment.GetEnvironmentVariable("PROJECTWEAPON_KAN97_" + key)
                ?? throw new ArgumentException("Missing child verification input: " + key);
    }
}
