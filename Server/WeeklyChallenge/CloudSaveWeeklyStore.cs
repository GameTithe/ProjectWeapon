using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.CloudSave.Model;

namespace ProjectWeapon.WeeklyChallenge;

/// <summary>Uses only the authenticated server context and existing, scoped Cloud Save records.</summary>
internal sealed class CloudSaveWeeklyStore : IWeeklyAttemptStore, IWeeklyConditionStore
{
    private readonly IExecutionContext context;
    private readonly IGameApiClient api;
    private readonly string key;

    private CloudSaveWeeklyStore(IExecutionContext context, IGameApiClient api, Guid environment)
    {
        this.context = context;
        this.api = api;
        key = "weekly_challenge_v1_" + environment.ToString("N");
    }

    internal static bool TryBind(IExecutionContext context, IGameApiClient api, [NotNullWhen(true)] out CloudSaveWeeklyStore? store)
    {
        store = null;
        if (!WeeklyConditions.IsKey(context.PlayerId) || string.IsNullOrWhiteSpace(context.ProjectId)
            || !Guid.TryParse(context.EnvironmentId, out Guid environment) || environment == Guid.Empty
            || string.IsNullOrWhiteSpace(context.AccessToken) || string.IsNullOrWhiteSpace(context.ServiceToken))
            return false;

        store = new(context, api, environment);
        return true;
    }

    public async Task<LockedLedger?> ReadAsync(string accountId)
    {
        if (accountId != context.PlayerId)
            return null;

        var response = await api.CloudSaveData.GetProtectedItemsAsync(context, context.AccessToken, context.ProjectId,
            context.PlayerId, new List<string> { key });
        var item = response.Data.Results.SingleOrDefault(item => item.Key == key);
        if (item is null || string.IsNullOrWhiteSpace(item.WriteLock) || Read<PlayerLedger>(item.Value) is not { } ledger)
            return null;
        return new(item.WriteLock, ledger);
    }

    public async Task<bool> TryReplaceAsync(string accountId, LockedLedger observed, PlayerLedger next)
    {
        if (accountId != context.PlayerId || next.AccountId != accountId || observed.Ledger.AccountId != accountId
            || string.IsNullOrWhiteSpace(observed.WriteLock))
            return false;

        var body = new SetItemBody(key, JsonSerializer.Serialize(next), observed.WriteLock);
        try
        {
            await api.CloudSaveData.SetProtectedItemAsync(context, context.ServiceToken, context.ProjectId,
                context.PlayerId, body);
            return true;
        }
        catch (ApiException ex) when (IsConflict(ex))
        {
            return false;
        }
    }

    public async Task<LockedWeekDocument?> ReadWeekAsync(DateTimeOffset utcMonday)
    {
        string weekKey = WeekKey(utcMonday);
        var response = await api.CloudSaveData.GetPrivateCustomItemsAsync(context, context.ServiceToken, context.ProjectId,
            key, new List<string> { weekKey });
        var item = response.Data.Results.SingleOrDefault(item => item.Key == weekKey);
        if (item is null || string.IsNullOrWhiteSpace(item.WriteLock) || Read<WeekDocument>(item.Value) is not { } document)
            return null;
        return new(item.WriteLock, document);
    }

    public async Task<bool> TryFreezeAsync(DateTimeOffset utcMonday, LockedWeekDocument observed)
    {
        if (observed.Document.Frozen is not null || string.IsNullOrWhiteSpace(observed.WriteLock))
            return false;

        WeekDocument frozen = observed.Document with { Frozen = observed.Document.Candidate };
        var body = new SetItemBody(WeekKey(utcMonday), JsonSerializer.Serialize(frozen), observed.WriteLock);
        try
        {
            await api.CloudSaveData.SetPrivateCustomItemAsync(context, context.ServiceToken, context.ProjectId, key, body);
            return true;
        }
        catch (ApiException ex) when (IsConflict(ex))
        {
            return false;
        }
    }

    private static T? Read<T>(object value)
    {
        // The module writes and expects a JSON string as the Cloud Save value, whose encoding is stable across SDK versions.
        string? json = value as string ?? (value as JValue)?.Value as string;
        return json is null ? default : JsonSerializer.Deserialize<T>(json);
    }

    private static string WeekKey(DateTimeOffset utcMonday)
        => utcMonday.ToString("yyyy_MM_dd", CultureInfo.InvariantCulture);

    private static bool IsConflict(ApiException exception)
        => exception.Response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed;
}
