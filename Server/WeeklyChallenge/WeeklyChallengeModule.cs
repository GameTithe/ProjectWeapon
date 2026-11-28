using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Apis.Extensions;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;

namespace ProjectWeapon.WeeklyChallenge;

public sealed class WeeklyChallengeModule
{
    private readonly Func<DateTimeOffset> utcNow;

    public WeeklyChallengeModule() : this(() => DateTimeOffset.UtcNow) { }

    internal WeeklyChallengeModule(Func<DateTimeOffset> utcNow) => this.utcNow = utcNow;

    [CloudCodeFunction("IssueWeeklyAttempt")]
    public async Task<WeeklyIssueResult> IssueWeeklyAttemptAsync(IExecutionContext context, IGameApiClient api, string? requestId)
    {
        if (!WeeklyConditions.IsKey(context.PlayerId))
            return new(WeeklyIssueStatus.Unauthorized);
        if (!Guid.TryParseExact(requestId, "D", out Guid request) || request == Guid.Empty)
            return new(WeeklyIssueStatus.InvalidRequest);
        if (!CloudSaveWeeklyStore.TryBind(context, api, out CloudSaveWeeklyStore? store))
            return new(WeeklyIssueStatus.AuthorityUnavailable);

        var issuer = new WeeklyChallengeIssuer(store, new FrozenWeeklyConditions(store), utcNow);
        try
        {
            return await issuer.IssueAsync(context.PlayerId, request);
        }
        catch (ApiException ex) when (ex.Response?.StatusCode is HttpStatusCode.TooManyRequests
            || (int?)ex.Response?.StatusCode >= 500)
        {
            // A write may already have committed. Retry with the original request ID.
            return new(WeeklyIssueStatus.Retryable);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            return new(WeeklyIssueStatus.Retryable);
        }
        catch (ApiException)
        {
            return new(WeeklyIssueStatus.AuthorityUnavailable);
        }
        catch (JsonException)
        {
            return new(WeeklyIssueStatus.AuthorityUnavailable);
        }
    }
}

public sealed class WeeklyChallengeModuleSetup : ICloudCodeSetup
{
    public void Setup(ICloudCodeConfig config) => config.AddGameApiClient();
}
