using System.Net;
using CohereInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace CohereInventoryAndTrend.Mcp.Adapters;

internal static class FoundryHttpClientExtensions
{
    public static IHttpClientBuilder AddFoundryResilience(
        this IHttpClientBuilder builder,
        AzureFoundryModelsOptions options)
    {
        if (!options.RetryEnabled)
        {
            return builder;
        }

        builder.AddResilienceHandler("foundry", (pipelineBuilder, context) =>
        {
            var logger = context.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("FoundryResilience");

            pipelineBuilder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(options.BaseDelaySeconds),
                MaxDelay = TimeSpan.FromSeconds(options.MaxDelaySeconds),
                ShouldHandle = static args =>
                {
                    if (args.Outcome.Exception is HttpRequestException)
                    {
                        return ValueTask.FromResult(true);
                    }

                    var response = args.Outcome.Result;
                    if (response is null)
                    {
                        return ValueTask.FromResult(false);
                    }

                    var statusCode = (int)response.StatusCode;
                    return ValueTask.FromResult(
                        response.StatusCode == HttpStatusCode.TooManyRequests
                        || response.StatusCode == HttpStatusCode.RequestTimeout
                        || statusCode >= 500);
                },
                DelayGenerator = args =>
                {
                    var retryAfterDelay = GetRetryAfterDelay(args.Outcome.Result);
                    return ValueTask.FromResult(retryAfterDelay);
                },
                OnRetry = args =>
                {
                    if (args.Outcome.Exception is not null)
                    {
                        logger.LogWarning(
                            args.Outcome.Exception,
                            "Foundry request failed on attempt {AttemptNumber}. Retrying in {RetryDelayMs} ms.",
                            args.AttemptNumber,
                            args.RetryDelay.TotalMilliseconds);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Foundry request returned {StatusCode} on attempt {AttemptNumber}. Retrying in {RetryDelayMs} ms.",
                            args.Outcome.Result?.StatusCode,
                            args.AttemptNumber,
                            args.RetryDelay.TotalMilliseconds);
                    }

                    return ValueTask.CompletedTask;
                }
            });
        });

        return builder;
    }

    private static TimeSpan? GetRetryAfterDelay(HttpResponseMessage? response)
    {
        if (response?.Headers.RetryAfter is not { } retryAfter)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter.Date is { } retryDate)
        {
            var delay = retryDate - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                return delay;
            }
        }

        return null;
    }
}
