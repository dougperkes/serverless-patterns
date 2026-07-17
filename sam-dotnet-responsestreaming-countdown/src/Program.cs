using System.Net;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Core.ResponseStreaming;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;

#pragma warning disable CA2252 // Opt in to preview features (response streaming)

// The function handler that will be called for each Lambda event.
// Streams a countdown timer through API Gateway response streaming.
var handler = async (APIGatewayProxyRequest request, ILambdaContext context) =>
{
    // Parse optional query parameters
    var durationSeconds = 60;  // default: 1 minute countdown
    var intervalSeconds = 5;   // default: tick every 5 seconds

    if (request.QueryStringParameters is not null)
    {
        if (request.QueryStringParameters.TryGetValue("duration", out var d) &&
            int.TryParse(d, out var parsedDuration) &&
            parsedDuration > 0 && parsedDuration <= 600)
        {
            durationSeconds = parsedDuration;
        }

        if (request.QueryStringParameters.TryGetValue("interval", out var i) &&
            int.TryParse(i, out var parsedInterval) &&
            parsedInterval > 0 && parsedInterval <= 60)
        {
            intervalSeconds = parsedInterval;
        }
    }

    // Set up the HTTP response stream with status code and headers.
    // API Gateway response streaming expects: JSON metadata + 8-null-byte delimiter + body.
    // CreateHttpStream handles this format automatically.
    var prelude = new HttpResponseStreamPrelude
    {
        StatusCode = HttpStatusCode.OK,
        Headers =
        {
            { "Content-Type", "text/plain; charset=utf-8" },
            { "Cache-Control", "no-cache" },
            { "X-Content-Type-Options", "nosniff" }
        }
    };

    using var responseStream = LambdaResponseStreamFactory.CreateHttpStream(prelude);
    using var writer = new StreamWriter(responseStream) { AutoFlush = false };

    var totalTicks = durationSeconds / intervalSeconds;
    var remaining = durationSeconds;

    await writer.WriteLineAsync($"🚀 Countdown started! Duration: {durationSeconds}s, Interval: {intervalSeconds}s");
    await writer.WriteLineAsync($"   Total ticks: {totalTicks}");
    await writer.WriteLineAsync(new string('─', 50));
    await writer.FlushAsync();

    for (var tick = 1; tick <= totalTicks; tick++)
    {
        await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));

        remaining -= intervalSeconds;
        var timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");

        var message = remaining > 0
            ? $"[{timestamp}] ⏱️  Tick {tick}/{totalTicks} — {remaining}s remaining"
            : $"[{timestamp}] 🏁 Tick {tick}/{totalTicks} — Countdown complete!";

        await writer.WriteLineAsync(message);
        await writer.FlushAsync();

        // Stop early if approaching Lambda timeout
        if (context.RemainingTime < TimeSpan.FromSeconds(10))
        {
            await writer.WriteLineAsync($"[{DateTime.UtcNow:HH:mm:ss.fff}] ⚠️  Approaching Lambda timeout, stopping.");
            await writer.FlushAsync();
            break;
        }
    }

    await writer.WriteLineAsync(new string('─', 50));
    await writer.WriteLineAsync($"✅ Done. Streamed {totalTicks} ticks over {durationSeconds} seconds.");
    await writer.FlushAsync();
};

// Build and run the Lambda runtime
await LambdaBootstrapBuilder.Create(handler, new DefaultLambdaJsonSerializer())
    .Build()
    .RunAsync();
