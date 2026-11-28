#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectWeapon.Accounts;
using ProjectWeapon.Inventory;
using ProjectWeapon.Save;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.AccountSync.Tests
{
    public sealed class AccountSaveSyncTests
    {
        private static readonly WeaponDefinitionId Sword = new WeaponDefinitionId("old_sword");
        private static readonly WeaponDefinitionId Bow = new WeaponDefinitionId("hunting_bow");
        private static readonly AccountSaveOperation Award = new AccountSaveOperation(
            Guid.Parse("12345678-1234-1234-1234-123456789abc"), AccountSaveOperationKind.Reward, "reward-1");

        [Test]
        public async Task SameVersionSameWeaponSaleCanCommitOnlyOnce()
        {
            var store = Store();
            var rules = new Rules();
            var authority = new AccountSaveAuthority(store, rules);
            var firstDevice = await Connect(new TestEndpoint(authority));
            var secondDevice = await Connect(new TestEndpoint(authority));
            AccountSaveSnapshot loaded = (await firstDevice.Client.ReadAsync()).Snapshot!;
            var sale = new AccountSaveOperation(Guid.NewGuid(), AccountSaveOperationKind.WeaponSale,
                loaded.Progress.Weapons[0].Id.ToString());
            var competing = new AccountSaveOperation(Guid.NewGuid(), sale.Kind, sale.Subject);

            store.PauseFirstReplace();
            Task<AccountSaveResult> first = firstDevice.Client.ApplyAsync(loaded.Revision, sale);
            Assert.That(store.HasPausedWriter, Is.True);
            AccountSaveResult second;
            try
            {
                second = await secondDevice.Client.ApplyAsync(loaded.Revision, competing);
            }
            finally
            {
                store.ReleaseWriter();
            }
            AccountSaveResult loser = await first;

            Assert.That(second.Status, Is.EqualTo(AccountSaveStatus.Applied));
            Assert.That(loser.Status, Is.EqualTo(AccountSaveStatus.VersionConflict));
            Assert.That(loser.Snapshot!.Revision, Is.EqualTo(2));
            Assert.That(loser.Snapshot.Progress.ConfirmedCoins, Is.EqualTo(130));
            Assert.That(loser.Snapshot.Progress.Weapons.Select(item => item.Id), Is.EqualTo(new[] { loaded.Progress.Weapons[1].Id }));
            Assert.That(loser.Snapshot.Progress.Catalog.Single().DefinitionId, Is.EqualTo(Sword));
            Assert.That(loser.Snapshot.Receipts.Keys, Is.EqualTo(new[] { competing.Id }));
            Assert.That((await firstDevice.Client.ApplyAsync(2, sale)).Status, Is.EqualTo(AccountSaveStatus.Rejected));
            Assert.That(store.Commits, Is.EqualTo(1));
        }

        [Test]
        public async Task SameOperationReplaysEvenWhenAnotherDeviceWinsTheWriteLock()
        {
            var store = Store();
            var rules = new Rules();
            var authority = new AccountSaveAuthority(store, rules);
            var first = await Connect(new TestEndpoint(authority));
            var second = await Connect(new TestEndpoint(authority));

            store.PauseFirstReplace();
            Task<AccountSaveResult> delayed = first.Client.ApplyAsync(1, Award);
            Assert.That(store.HasPausedWriter, Is.True);
            AccountSaveResult won;
            try
            {
                won = await second.Client.ApplyAsync(1, Award);
            }
            finally
            {
                store.ReleaseWriter();
            }

            AccountSaveResult replayed = await delayed;
            Assert.That(won.Status, Is.EqualTo(AccountSaveStatus.Applied));
            Assert.That(replayed.Status, Is.EqualTo(AccountSaveStatus.AlreadyApplied));
            Assert.That(replayed.Receipt!.ResultId, Is.EqualTo(won.Receipt!.ResultId));
            Assert.That(replayed.Snapshot!.Progress.ConfirmedCoins, Is.EqualTo(140));
            Assert.That(replayed.Snapshot.Progress.Weapons.Count, Is.EqualTo(3));
            Assert.That(replayed.Snapshot.Progress.Catalog.Select(item => item.DefinitionId), Is.EqualTo(new[] { Sword, Bow }));
            Assert.That(store.Commits, Is.EqualTo(1));
        }

        [Test]
        public async Task DeviceChangeAfterLostReplyReturnsOriginalGrantAlongsideLatestRevision()
        {
            AccountSaveSnapshot seed = Store().Snapshot("first-player");
            var legacy = new SettlementSaveRecord(Guid.NewGuid(), "legacy-id", "historical-result");
            var historical = new AccountSaveSnapshot(new SaveSnapshot(seed.Revision, seed.Progress.ConfirmedCoins,
                seed.Progress.Weapons, seed.Progress.Catalog, new[] { legacy }), seed.Receipts.Values);
            using var profile = new TemporaryProfile(historical);
            var rules = new Rules();
            var authority = new AccountSaveAuthority(profile.Open(), rules);
            var failingEndpoint = new TestEndpoint(authority) { LoseReply = true };
            var first = await Connect(failingEndpoint);

            Assert.That((await first.Client.ApplyAsync(1, Award)).Status, Is.EqualTo(AccountSaveStatus.Unavailable));
            var newDevice = await Connect(new TestEndpoint(new AccountSaveAuthority(profile.Open(), rules)));
            AccountSaveSnapshot committed = (await newDevice.Client.ReadAsync()).Snapshot!;
            var sale = new AccountSaveOperation(Guid.NewGuid(), AccountSaveOperationKind.WeaponSale,
                committed.Progress.Weapons[0].Id.ToString());
            Assert.That((await newDevice.Client.ApplyAsync(2, sale)).Status, Is.EqualTo(AccountSaveStatus.Applied));

            for (int i = 0; i < 3; i++)
            {
                AccountSaveResult reply = await newDevice.Client.ApplyAsync(1, Award);
                Assert.That(reply.Status, Is.EqualTo(AccountSaveStatus.AlreadyApplied));
                Assert.That(reply.Receipt!.AppliedRevision, Is.EqualTo(2));
                Assert.That(reply.Receipt.ResultId, Is.EqualTo(committed.Progress.Weapons[2].Id.ToString()));
                Assert.That(reply.Snapshot!.Revision, Is.EqualTo(3));
                Assert.That(reply.Snapshot.Progress.ConfirmedCoins, Is.EqualTo(170));
                Assert.That(reply.Snapshot.Progress.Weapons.Count, Is.EqualTo(2));
                Assert.That(reply.Snapshot.Progress.Settlements.Single().TransactionId, Is.EqualTo(legacy.TransactionId));
                Assert.That(reply.Snapshot.Progress.Settlements.Single().Result, Is.EqualTo(legacy.Result));
                Assert.That(reply.Snapshot.Receipts.Count, Is.EqualTo(2));
            }
            var changedArgument = new AccountSaveOperation(Award.Id, Award.Kind, "different-claim");
            Assert.That((await newDevice.Client.ApplyAsync(1, changedArgument)).Status,
                Is.EqualTo(AccountSaveStatus.OperationMismatch));
            Assert.That(rules.Resolutions, Is.EqualTo(2));
            Assert.That((await profile.Open().ReadAsync("first-player", default))!.Snapshot.Revision, Is.EqualTo(3));
        }

        [Test]
        public async Task OlderResponseCannotRegressTheCurrentSessionCache()
        {
            var store = Store();
            var endpoint = new TestEndpoint(new AccountSaveAuthority(store, new Rules()));
            var device = await Connect(endpoint);
            var delayed = new TaskCompletionSource<AccountSaveResult>();
            endpoint.ReadOverride = delayed.Task;
            Task<AccountSaveResult> oldRead = device.Client.ReadAsync();
            AccountSaveSnapshot prior = store.Snapshot("first-player");
            Assert.That((await device.Client.ApplyAsync(1, Award)).Status, Is.EqualTo(AccountSaveStatus.Applied));
            delayed.SetResult(new AccountSaveResult(AccountSaveStatus.Read, prior));
            Assert.That((await oldRead).Snapshot!.Revision, Is.EqualTo(2));
            device.Connected = false;
            SaveSnapshot cache = (await device.Client.ReadAsync()).Snapshot!.Progress;
            Assert.That(cache.Catalog.Select(record => record.DefinitionId), Is.EqualTo(new[] { Sword, Bow }));
            Assert.That(cache.ConfirmedCoins, Is.EqualTo(140));
        }

        [Test]
        public async Task UnavailableReplyFallsBackToTheCurrentSessionCacheOnlyWhenDisconnected()
        {
            var store = Store();
            var endpoint = new TestEndpoint(new AccountSaveAuthority(store, new Rules()));
            var device = await Connect(endpoint);
            AccountSaveSnapshot initial = (await device.Client.ReadAsync()).Snapshot!;

            var delayed = new TaskCompletionSource<AccountSaveResult>();
            endpoint.ReadOverride = delayed.Task;
            Task<AccountSaveResult> pending = device.Client.ReadAsync();
            Assert.That(pending.IsCompleted, Is.False);
            device.Connected = false;
            delayed.SetResult(new AccountSaveResult(AccountSaveStatus.Unavailable));

            AccountSaveResult offline = await pending;
            Assert.That(offline.Status, Is.EqualTo(AccountSaveStatus.OfflineReadOnly));
            Assert.That(offline.Snapshot, Is.SameAs(initial));
            Assert.That(offline.Receipt, Is.Null);

            device.Connected = true;
            AccountSaveResult serverUnavailable = await device.Client.ReadAsync();
            Assert.That(serverUnavailable.Status, Is.EqualTo(AccountSaveStatus.Unavailable));
            Assert.That(serverUnavailable.Snapshot, Is.Null);
        }

        [Test]
        public async Task VerifiedReceiptStillSurvivesConnectionLossBeforeReply()
        {
            var store = Store();
            var delayed = new TaskCompletionSource<bool>();
            var endpoint = new TestEndpoint(new AccountSaveAuthority(store, new Rules())) { ApplyReplyBarrier = delayed.Task };
            var device = await Connect(endpoint);
            Task<AccountSaveResult> pending = device.Client.ApplyAsync(1, Award);
            Assert.That(pending.IsCompleted, Is.False);
            Assert.That(store.Commits, Is.EqualTo(1));
            device.Connected = false;
            delayed.SetResult(true);

            AccountSaveResult reply = await pending;
            Assert.That(reply.Status, Is.EqualTo(AccountSaveStatus.Applied));
            Assert.That(reply.Receipt!.Operation.Id, Is.EqualTo(Award.Id));
            Assert.That(reply.Snapshot!.Revision, Is.EqualTo(2));
            Assert.That((await device.Client.ReadAsync()).Snapshot, Is.SameAs(reply.Snapshot));
        }

        [Test]
        public async Task OfflineMutationStopsBeforeTransportAndOnlyCurrentSessionCanReadCache()
        {
            var store = Store();
            var endpoint = new TestEndpoint(new AccountSaveAuthority(store, new Rules()));
            var device = await Connect(endpoint);
            Assert.That((await device.Client.ReadAsync()).Status, Is.EqualTo(AccountSaveStatus.Read));

            device.Connected = false;
            foreach (AccountSaveOperationKind kind in Enum.GetValues(typeof(AccountSaveOperationKind)))
            {
                var operation = new AccountSaveOperation(Guid.NewGuid(), kind, "online-command");
                Assert.That((await device.Client.ApplyAsync(1, operation)).Status, Is.EqualTo(AccountSaveStatus.OfflineReadOnly));
            }
            AccountSaveResult cached = await device.Client.ReadAsync();
            Assert.That(cached.Status, Is.EqualTo(AccountSaveStatus.OfflineReadOnly));
            Assert.That(cached.Snapshot!.Progress.ConfirmedCoins, Is.EqualTo(100));
            Assert.That(endpoint.Calls, Is.EqualTo(1));
            Assert.That(store.Commits, Is.Zero);

            await device.Accounts.UseGuestAsync();
            Assert.That((await device.Client.ReadAsync()).Snapshot, Is.Null);
            device.Provider.NextPlayer = "second-player";
            Assert.That(await device.Accounts.SignInAsync(), Is.EqualTo(AccountSignInStatus.SignedIn));
            Assert.That((await device.Client.ReadAsync()).Snapshot, Is.Null);
            device.Connected = true;
            Assert.That((await device.Client.ReadAsync()).Snapshot!.Progress.ConfirmedCoins, Is.EqualTo(200));
            device.Provider.Expire();
            Assert.That((await device.Client.ReadAsync()).Snapshot, Is.Null);
        }

        [Test]
        public async Task ResponseFromRevokedSessionCannotRefreshCacheAfterRelogin()
        {
            var store = Store();
            var delayed = new TaskCompletionSource<AccountSaveResult>();
            var endpoint = new TestEndpoint(new AccountSaveAuthority(store, new Rules())) { ReadOverride = delayed.Task };
            var device = await Connect(endpoint);
            Task<AccountSaveResult> pending = device.Client.ReadAsync();
            await device.Accounts.UseGuestAsync();
            Assert.That(await device.Accounts.SignInAsync(), Is.EqualTo(AccountSignInStatus.SignedIn));
            delayed.SetResult(new AccountSaveResult(AccountSaveStatus.Read, store.Snapshot("first-player")));
            Assert.That((await pending).Status, Is.EqualTo(AccountSaveStatus.StaleSession));

            device.Connected = false;
            Assert.That((await device.Client.ReadAsync()).Snapshot, Is.Null);
        }

        [Test]
        public async Task MissingProfileOrIdentityCannotCreateOrOverwriteProtectedData()
        {
            var store = Store();
            var rules = new Rules();
            var authority = new AccountSaveAuthority(store, rules);

            Assert.That((await authority.ApplyAsync(new Identity(null), 1, Award, default)).Status,
                Is.EqualTo(AccountSaveStatus.Unavailable));
            Assert.That((await authority.ApplyAsync(new Identity("unprovisioned-player"), 1, Award, default)).Status,
                Is.EqualTo(AccountSaveStatus.Unavailable));
            Assert.Throws<ArgumentException>(() => new ProtectedAccountSave(store.Snapshot("first-player"), ""));
            Assert.That(rules.Resolutions, Is.Zero);
            Assert.That(store.Commits, Is.Zero);
        }

        private static CasStore Store()
        {
            WeaponInstance NewSword() => WeaponInstance.Create(Sword, WeaponProvenance.Personal, WeaponLocation.Warehouse());
            AccountSaveSnapshot Seed(long coins, params WeaponInstance[] weapons) => new AccountSaveSnapshot(
                new SaveSnapshot(1, coins, weapons, new[] { new CatalogSaveRecord(Sword, "recovered") },
                    Array.Empty<SettlementSaveRecord>()), Array.Empty<AccountSaveReceipt>());
            return new CasStore(new Dictionary<string, AccountSaveSnapshot>
            {
                ["first-player"] = Seed(100, NewSword(), NewSword()),
                ["second-player"] = Seed(200, NewSword())
            });
        }

        private static async Task<Device> Connect(TestEndpoint endpoint)
        {
            var device = new Device(endpoint);
            Assert.That(await device.Accounts.SignInAsync(), Is.EqualTo(AccountSignInStatus.SignedIn));
            return device;
        }

        private sealed class Device
        {
            public Device(IAccountSaveEndpoint endpoint)
            {
                Accounts = new AccountSessionBoundary(new GuestProfileId(Guid.NewGuid()), Provider);
                Client = new AccountSaveClient(Accounts, endpoint, () => Connected);
            }

            public TestProvider Provider { get; } = new TestProvider();
            public AccountSessionBoundary Accounts { get; }
            public AccountSaveClient Client { get; }
            public bool Connected { get; set; } = true;
        }

        private sealed class TestProvider : IAccountProvider
        {
            private string? authorized;
            public string NextPlayer { get; set; } = "first-player";
            public event Action? SessionInvalidated;
            public Task<ProviderSignInResult> SignInAsync(CancellationToken cancellation)
            {
                authorized = NextPlayer;
                return Task.FromResult(ProviderSignInResult.Verified(NextPlayer));
            }

            public bool IsAuthorized(string playerId) => authorized == playerId;
            public void SignOut() => authorized = null;
            public void Expire()
            {
                SignOut();
                SessionInvalidated?.Invoke();
            }
        }

        private sealed class Identity : IAccountSaveIdentity
        {
            public Identity(string? playerId) => VerifiedPlayerId = playerId;
            public string? VerifiedPlayerId { get; }
        }

        private sealed class TestEndpoint : IAccountSaveEndpoint
        {
            private readonly AccountSaveAuthority authority;
            public TestEndpoint(AccountSaveAuthority authority) => this.authority = authority;
            public int Calls { get; private set; }
            public bool LoseReply { get; set; }
            public Task<AccountSaveResult>? ReadOverride { get; set; }
            public Task? ApplyReplyBarrier { get; set; }

            public Task<AccountSaveResult> ReadAsync(OnlineAccountSession session)
            {
                Calls++;
                return ReadOverride ?? authority.ReadAsync(new Identity(session.PlayerId), session.Cancellation);
            }

            public async Task<AccountSaveResult> ApplyAsync(OnlineAccountSession session, long revision,
                AccountSaveOperation operation)
            {
                Calls++;
                AccountSaveResult applied = await authority.ApplyAsync(new Identity(session.PlayerId), revision,
                    operation, session.Cancellation);
                if (ApplyReplyBarrier != null)
                    await ApplyReplyBarrier;
                if (LoseReply)
                    throw new InvalidOperationException("Simulated lost response.");
                return applied;
            }
        }

        private sealed class Rules : IAccountSaveOperations
        {
            public int Resolutions { get; private set; }

            public ResolvedAccountSaveOperation? Resolve(string playerId, SaveSnapshot current, AccountSaveOperation operation)
            {
                Resolutions++;
                if (operation.Kind == AccountSaveOperationKind.Reward && operation.Subject == Award.Subject)
                {
                    WeaponInstance weapon = WeaponInstance.Create(Bow, WeaponProvenance.Personal, WeaponLocation.Warehouse());
                    SaveSnapshot next = current.Next(current.ConfirmedCoins + 40, weapons: current.Weapons.Append(weapon),
                        catalog: current.Catalog.Append(new CatalogSaveRecord(Bow, "discovered")));
                    return new ResolvedAccountSaveOperation(next, weapon.Id.ToString());
                }

                if (operation.Kind != AccountSaveOperationKind.WeaponSale)
                    return null;
                WeaponInstance? sold = current.Weapons.SingleOrDefault(weapon => weapon.Id.ToString() == operation.Subject);
                if (sold == null)
                    return null;
                return new ResolvedAccountSaveOperation(current.Next(current.ConfirmedCoins + 30,
                    weapons: current.Weapons.Where(weapon => weapon.Id != sold.Id)), operation.Subject);
            }
        }

        private sealed class TemporaryProfile : IDisposable
        {
            private readonly string directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN117-" + Guid.NewGuid().ToString("N"));
            private string PathToSave => Path.Combine(directory, "protected.json");

            public TemporaryProfile(AccountSaveSnapshot initial)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(PathToSave, DurableCasStore.Encode(initial));
            }

            public IProtectedAccountSaveStore Open() => new DurableCasStore(PathToSave);

            public void Dispose()
            {
                File.Delete(PathToSave);
                File.Delete(PathToSave + ".staging");
                Directory.Delete(directory);
            }
        }

        // A test-only serialized Protected value. The restart scenario opens a fresh adapter against the same bytes.
        private sealed class DurableCasStore : IProtectedAccountSaveStore
        {
            private readonly string path;

            public DurableCasStore(string path) => this.path = path;

            public Task<ProtectedAccountSave?> ReadAsync(string playerId, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                if (playerId != "first-player")
                    return Task.FromResult<ProtectedAccountSave?>(null);
                var saved = JsonUtility.FromJson<PersistedValue>(File.ReadAllText(path));
                AccountSaveReceipt[] receipts = saved.operations.Select(record => new AccountSaveReceipt(
                    new AccountSaveOperation(Guid.Parse(record.id), record.kind, record.subject), record.revision,
                    record.resultId)).ToArray();
                return Task.FromResult<ProtectedAccountSave?>(
                    new ProtectedAccountSave(new AccountSaveSnapshot(saved.progress, receipts), saved.writeLock));
            }

            public async Task<bool> TryReplaceAsync(string playerId, ProtectedAccountSave current,
                AccountSaveSnapshot next, CancellationToken cancellation)
            {
                ProtectedAccountSave? loaded = await ReadAsync(playerId, cancellation);
                if (loaded == null || loaded.WriteLock != current.WriteLock)
                    return false;

                string staging = path + ".staging";
                using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(Encode(next));
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }
                File.Replace(staging, path, null);
                return true;
            }

            public static string Encode(AccountSaveSnapshot snapshot) => JsonUtility.ToJson(new PersistedValue
            {
                writeLock = Guid.NewGuid().ToString("N"),
                progress = snapshot.Progress,
                operations = snapshot.Receipts.Values.Select(receipt => new PersistedOperation
                {
                    id = receipt.Operation.Id.ToString("D"),
                    kind = receipt.Operation.Kind,
                    subject = receipt.Operation.Subject,
                    revision = receipt.AppliedRevision,
                    resultId = receipt.ResultId
                }).ToArray()
            });

            [Serializable]
            private sealed class PersistedValue
            {
                public string writeLock = "";
                public SaveSnapshot progress = null!;
                public PersistedOperation[] operations = Array.Empty<PersistedOperation>();
            }

            [Serializable]
            private sealed class PersistedOperation
            {
                public string id = "";
                public AccountSaveOperationKind kind;
                public string subject = "";
                public long revision;
                public string resultId = "";
            }
        }

        private sealed class CasStore : IProtectedAccountSaveStore
        {
            private readonly Dictionary<string, ProtectedAccountSave> profiles;
            private TaskCompletionSource<bool>? pausedWriter;
            private bool pauseNext;

            public CasStore(Dictionary<string, AccountSaveSnapshot> seeded) =>
                profiles = seeded.ToDictionary(pair => pair.Key, pair => WithNewLock(pair.Value));

            public int Commits { get; private set; }
            public bool HasPausedWriter => pausedWriter != null;
            public AccountSaveSnapshot Snapshot(string playerId) => profiles[playerId].Snapshot;
            public void PauseFirstReplace() => pauseNext = true;
            public void ReleaseWriter() => pausedWriter!.SetResult(true);

            public Task<ProtectedAccountSave?> ReadAsync(string playerId, CancellationToken cancellation)
                => Task.FromResult(profiles.TryGetValue(playerId, out ProtectedAccountSave value) ? value : null);

            public async Task<bool> TryReplaceAsync(string playerId, ProtectedAccountSave current, AccountSaveSnapshot next,
                CancellationToken cancellation)
            {
                if (pauseNext)
                {
                    pauseNext = false;
                    pausedWriter = new TaskCompletionSource<bool>();
                    await pausedWriter.Task;
                }

                if (profiles[playerId].WriteLock != current.WriteLock)
                    return false;
                profiles[playerId] = WithNewLock(next);
                Commits++;
                return true;
            }

            private static ProtectedAccountSave WithNewLock(AccountSaveSnapshot snapshot) =>
                new ProtectedAccountSave(snapshot, Guid.NewGuid().ToString("N"));
        }
    }
}
