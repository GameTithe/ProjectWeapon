using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.CloudSave.Model;

namespace ProjectWeapon.WeeklyChallenge.Tests;

public sealed class WeeklyChallengeModuleTests
{
    private const string Account = "verified-player";
    private const string Project = "test-project";
    private const string Environment = "bd45c189-f983-4b04-8a30-d1b9f5d6c4b3";
    private const string Scope = "weekly_challenge_v1_bd45c189f9834b048a30d1b9f5d6c4b3";

    [Test]
    public async Task RealModuleBindsOnlyRequestIdAndTrustedCloudSaveScopeAndLocks()
    {
        MethodInfo handler = typeof(WeeklyChallengeModule).GetMethods().Single(method =>
            method.GetCustomAttribute<CloudCodeFunctionAttribute>() is not null);
        Assert.That(handler.GetParameters().Select(parameter => parameter.ParameterType),
            Is.EqualTo(new[] { typeof(IExecutionContext), typeof(IGameApiClient), typeof(string) }));

        var api = new CloudSaveFixture();
        var module = api.NewModule();
        Guid request = Guid.NewGuid();
        WeeklyIssueResult result = await module.IssueWeeklyAttemptAsync(api.Context, api.Client, request.ToString("D"));

        Assert.That(result.Status, Is.EqualTo(WeeklyIssueStatus.Available));
        Assert.That(result.Attempt!.AccountId, Is.EqualTo(Account));
        Assert.That(result.Attempt.RequestId, Is.EqualTo(request));
        Assert.That(result.Attempt.Conditions, Is.EqualTo(api.Settings));
        Assert.That(api.Writes, Is.EqualTo(new[] { "SetPrivateCustomItemAsync", "SetProtectedItemAsync" }));
        Assert.That(api.Week.Frozen, Is.EqualTo(api.Settings));
        Assert.That(api.Ledger.Attempts, Is.EqualTo(new[] { result.Attempt }));

        var secondProcess = api.NewModule();
        var replay = await secondProcess.IssueWeeklyAttemptAsync(api.Context, api.Client, request.ToString("D"));
        Assert.That(replay, Is.EqualTo(result));
        Assert.That(api.Writes, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task MissingDataAndInvalidAuthenticationNeverIssueOrWrite()
    {
        var api = new CloudSaveFixture { OmitPlayer = true };
        var module = api.NewModule();
        var denied = await module.IssueWeeklyAttemptAsync(MakeContext(null), api.Client, Guid.NewGuid().ToString());
        Assert.That(denied.Status, Is.EqualTo(WeeklyIssueStatus.Unauthorized));
        var invalid = await module.IssueWeeklyAttemptAsync(api.Context, api.Client, "forged: other player");
        Assert.That(invalid.Status, Is.EqualTo(WeeklyIssueStatus.InvalidRequest));
        Assert.That(api.Reads, Is.Zero);

        var missingPlayer = await module.IssueWeeklyAttemptAsync(api.Context, api.Client, Guid.NewGuid().ToString());
        Assert.That(missingPlayer.Status, Is.EqualTo(WeeklyIssueStatus.AuthorityUnavailable));
        api.OmitPlayer = false;
        api.OmitWeek = true;
        var missingWeek = await module.IssueWeeklyAttemptAsync(api.Context, api.Client, Guid.NewGuid().ToString());
        Assert.That(missingWeek.Status, Is.EqualTo(WeeklyIssueStatus.AuthorityUnavailable));
        Assert.That(api.Writes, Is.Empty);
    }

    [Test]
    public async Task WriteLockConflictRereadsBeforeIssuingAndServerErrorAfterCommitReplays()
    {
        var api = new CloudSaveFixture { LoseFirstFreeze = true, LoseFirstReply = true };
        var module = api.NewModule();
        string request = Guid.NewGuid().ToString();
        WeeklyIssueResult unavailable = await module.IssueWeeklyAttemptAsync(api.Context, api.Client, request);
        Assert.That(unavailable.Status, Is.EqualTo(WeeklyIssueStatus.Retryable));
        Assert.That(unavailable.Attempt, Is.Null);

        IssuedAttempt committed = api.Ledger.Attempts.Single();
        Assert.That(committed.Conditions, Is.EqualTo(api.Settings with { MapSeed = 15 }));
        WeeklyIssueResult retry = await module.IssueWeeklyAttemptAsync(api.Context, api.Client, request);
        Assert.That(retry.Status, Is.EqualTo(WeeklyIssueStatus.Available));
        Assert.That(retry.Attempt, Is.EqualTo(committed));
        Assert.That(api.Writes.Count(method => method == "SetProtectedItemAsync"), Is.EqualTo(1));
    }

    [Test]
    public async Task CorruptStoredJsonAndMissingRevisionFailClosed()
    {
        var api = new CloudSaveFixture { OverridePlayerValue = new JValue("{invalid") };
        var module = api.NewModule();
        var malformed = await module.IssueWeeklyAttemptAsync(api.Context, api.Client, Guid.NewGuid().ToString());
        Assert.That(malformed.Status, Is.EqualTo(WeeklyIssueStatus.AuthorityUnavailable));
        api.OverridePlayerValue = null;
        api.PlayerRevision = "";
        var missing = await module.IssueWeeklyAttemptAsync(api.Context, api.Client, Guid.NewGuid().ToString());
        Assert.That(missing.Status, Is.EqualTo(WeeklyIssueStatus.AuthorityUnavailable));
        Assert.That(api.Writes, Is.Empty);
    }

    [Test]
    public async Task ParseableLedgerWithUnrepresentableWeekReturnsUnavailableWithoutWrites()
    {
        var api = new CloudSaveFixture();
        IssuedAttempt normal = SeedAttempt(api.Settings);
        JsonObject stored = JsonSerializer.SerializeToNode(new PlayerLedger(Account, [normal]))!.AsObject();
        JsonObject recorded = stored["Attempts"]![0]!.AsObject();

        DateTimeOffset nearMax = DateTimeOffset.MaxValue.AddSeconds(-1);
        recorded["Conditions"]!["WeekStartUtc"] = JsonValue.Create(WeeklyConditions.Monday(nearMax));
        recorded["IssuedAtUtc"] = JsonValue.Create(nearMax);
        recorded["ExpiresAtUtc"] = JsonValue.Create(DateTimeOffset.MaxValue);
        api.OverridePlayerValue = new JValue(stored.ToJsonString());

        WeeklyIssueResult result = await api.NewModule().IssueWeeklyAttemptAsync(api.Context, api.Client,
            normal.RequestId.ToString("D"));
        Assert.That(result.Status, Is.EqualTo(WeeklyIssueStatus.AuthorityUnavailable));
        Assert.That(result.Attempt, Is.Null);
        Assert.That(api.Writes, Is.Empty);
    }

    [Test]
    public async Task ParseableLedgerWithDivergentSameWeekConditionsReturnsUnavailable()
    {
        var api = new CloudSaveFixture();
        IssuedAttempt first = SeedAttempt(api.Settings);
        IssuedAttempt second = SeedAttempt(api.Settings with { MapSeed = 99 }) with { AttemptNumber = 2 };
        api.OverridePlayerValue = new JValue(JsonSerializer.Serialize(new PlayerLedger(Account, [first, second])));

        WeeklyIssueResult result = await api.NewModule().IssueWeeklyAttemptAsync(api.Context, api.Client,
            first.RequestId.ToString("D"));
        Assert.That(result.Status, Is.EqualTo(WeeklyIssueStatus.AuthorityUnavailable));
        Assert.That(result.Attempt, Is.Null);
        Assert.That(api.Writes, Is.Empty);
    }

    [Test]
    public async Task AlternateFrozenConditionsCannotExtendExistingAccountLedger()
    {
        var api = new CloudSaveFixture();
        IssuedAttempt issued = SeedAttempt(api.Settings with { MapSeed = 99 });
        api.OverridePlayerValue = new JValue(JsonSerializer.Serialize(new PlayerLedger(Account, [issued])));
        api.UseFrozenSettings();

        WeeklyIssueResult result = await api.NewModule().IssueWeeklyAttemptAsync(api.Context, api.Client,
            Guid.NewGuid().ToString("D"));
        Assert.That(result.Status, Is.EqualTo(WeeklyIssueStatus.AuthorityUnavailable));
        Assert.That(result.Attempt, Is.Null);
        Assert.That(api.Writes, Is.Empty);
    }

    private static IssuedAttempt SeedAttempt(WeeklyConditions conditions)
        => new(Guid.NewGuid(), Guid.NewGuid(), Account, conditions, 1, conditions.WeekStartUtc,
            conditions.WeekStartUtc.AddSeconds(conditions.DurationSeconds));

    private static IExecutionContext MakeContext(string? player)
        => CreateProxy<IExecutionContext>((method, _) => method switch
        {
            "get_ProjectId" => Project,
            "get_EnvironmentId" => Environment,
            "get_PlayerId" => player,
            "get_AccessToken" => "player-test-token",
            "get_ServiceToken" => "service-test-token",
            _ => throw new NotSupportedException(method)
        });

    private sealed class CloudSaveFixture
    {
        public CloudSaveFixture()
        {
            Week = new(Settings, null);
            Type dataApiInterface = typeof(IGameApiClient).GetProperty(nameof(IGameApiClient.CloudSaveData))!.PropertyType;
            object cloudSave = DispatchProxy.Create(dataApiInterface, typeof(SdkProxy));
            ((SdkProxy)cloudSave).InvokeSdk = Call;
            Client = CreateProxy<IGameApiClient>((method, _) => method == "get_CloudSaveData"
                ? cloudSave
                : throw new NotSupportedException(method));
        }

        public WeeklyConditions Settings { get; } = new(new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero),
            "conditions-1", "map-1", 9, "enemies-1", "chests-1", "rules-1", "equipment-1", "worn-sword", null, 600);
        public IExecutionContext Context { get; } = MakeContext(Account);
        public IGameApiClient Client { get; }
        public PlayerLedger Ledger { get; private set; } = new(Account, []);
        public WeekDocument Week { get; private set; }
        public string PlayerRevision { get; set; } = "player-initial";
        public string WeekRevision { get; set; } = "week-initial";
        public List<string> Writes { get; } = new();
        public int Reads { get; private set; }
        public bool OmitPlayer { get; set; }
        public bool OmitWeek { get; set; }
        public bool LoseFirstFreeze { get; set; }
        public bool LoseFirstReply { get; set; }
        public JValue? OverridePlayerValue { get; set; }

        public WeeklyChallengeModule NewModule() => new(() => Settings.WeekStartUtc.AddHours(1));
        public void UseFrozenSettings() => Week = Week with { Frozen = Settings };

        private object Call(string method, object?[] arguments)
        {
            Assert.That(arguments[0], Is.SameAs(Context));
            Assert.That(arguments[2], Is.EqualTo(Project));
            string weekKey = Settings.WeekStartUtc.ToString("yyyy_MM_dd", CultureInfo.InvariantCulture);

            switch (method)
            {
                case "GetProtectedItemsAsync":
                    Reads++;
                    Assert.That(arguments[1], Is.EqualTo("player-test-token"));
                    Assert.That(arguments[3], Is.EqualTo(Account));
                    Assert.That(arguments[4], Is.EqualTo(new[] { Scope }));
                    return Items(OmitPlayer ? null : Item(Scope, OverridePlayerValue ?? new JValue(JsonSerializer.Serialize(Ledger)), PlayerRevision));
                case "GetPrivateCustomItemsAsync":
                    Reads++;
                    Assert.That(arguments[1], Is.EqualTo("service-test-token"));
                    Assert.That(arguments[3], Is.EqualTo(Scope));
                    Assert.That(arguments[4], Is.EqualTo(new[] { weekKey }));
                    return Items(OmitWeek ? null : Item(weekKey, JsonSerializer.Serialize(Week), WeekRevision));
                case "SetPrivateCustomItemAsync":
                    var weeklyBody = VerifyWrite(method, arguments, Scope, weekKey, WeekRevision);
                    if (LoseFirstFreeze)
                    {
                        LoseFirstFreeze = false;
                        WeekRevision = "week-winner";
                        Week = Week with { Frozen = Settings with { MapSeed = 15 } };
                        throw Failure(HttpStatusCode.Conflict);
                    }
                    Week = JsonSerializer.Deserialize<WeekDocument>((string)weeklyBody.Value)!;
                    WeekRevision = "week-next";
                    return Written(WeekRevision);
                case "SetProtectedItemAsync":
                    var playerBody = VerifyWrite(method, arguments, Account, Scope, PlayerRevision);
                    Ledger = JsonSerializer.Deserialize<PlayerLedger>((string)playerBody.Value)!;
                    PlayerRevision = "player-next";
                    if (LoseFirstReply)
                    {
                        LoseFirstReply = false;
                        throw Failure(HttpStatusCode.ServiceUnavailable);
                    }
                    return Written(PlayerRevision);
            }

            throw new NotSupportedException(method);
        }

        private SetItemBody VerifyWrite(string method, object?[] arguments, string target, string key, string revision)
        {
            Assert.That(arguments[1], Is.EqualTo("service-test-token"));
            Assert.That(arguments[3], Is.EqualTo(target));
            var body = (SetItemBody)arguments[4]!;
            Assert.That(body.Key, Is.EqualTo(key));
            Assert.That(body.WriteLock, Is.EqualTo(revision).And.Not.Empty);
            Writes.Add(method);
            return body;
        }

        private static Item Item(string key, object value, string writeLock)
            => new(key, value, writeLock, new ModifiedMetadata(DateTime.UnixEpoch), new ModifiedMetadata(DateTime.UnixEpoch));

        private static Task<ApiResponse<GetItemsResponse>> Items(Item? item)
            => Result(new GetItemsResponse(item is null ? [] : [item], new GetItemsResponseLinks("")));

        private static Task<ApiResponse<SetItemResponse>> Written(string revision)
            => Result(new SetItemResponse(revision));

        private static Task<ApiResponse<T>> Result<T>(T value)
        {
            // The SDK deserializer normally fills this internal setter.
            var response = new ApiResponse<T>();
            typeof(ApiResponse<T>).GetProperty(nameof(ApiResponse<T>.Data))!.SetValue(response, value);
            return Task.FromResult(response);
        }

        private static ApiException Failure(HttpStatusCode status)
        {
            var response = new ApiResponse();
            typeof(ApiResponse).GetProperty(nameof(ApiResponse.StatusCode))!.SetValue(response, status);
            return new ApiException(default, "synthetic Cloud Save failure", response);
        }
    }

    private static T CreateProxy<T>(Func<string, object?[], object?> invoke) where T : class
    {
        T proxy = DispatchProxy.Create<T, SdkProxy>();
        ((SdkProxy)(object)proxy).InvokeSdk = invoke;
        return proxy;
    }

    public class SdkProxy : DispatchProxy
    {
        public Func<string, object?[], object?> InvokeSdk { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => InvokeSdk(targetMethod!.Name, args ?? []);
    }
}
