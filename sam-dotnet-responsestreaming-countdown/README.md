# AWS Lambda Response Streaming via API Gateway — Countdown Timer (.NET)

This sample demonstrates **Lambda Response Streaming through Amazon API Gateway** for .NET using AWS SAM. A Lambda function streams a countdown timer, sending a partial response every N seconds through a REST API with response streaming enabled.

## Architecture

```
┌──────────────┐       ┌──────────────────┐       ┌────────────────────────────┐
│   Client     │       │   API Gateway    │       │   Lambda Function          │
│  (curl)      │──GET─▶│   REST API       │──────▶│   (Response Streaming)     │
│              │◀─stream│  ResponseTransfer │◀stream│                            │
│              │       │  Mode: STREAM    │       │  Tick 1... Tick 2... Done! │
└──────────────┘       └──────────────────┘       └────────────────────────────┘
```

## What It Demonstrates

- **API Gateway response streaming** — Uses `ResponseTransferMode: STREAM` on the API Gateway integration to stream Lambda responses directly to clients.
- **Time to first byte (TTFB)** — The client receives the first line immediately, then subsequent lines arrive every N seconds.
- **Long-running streamed responses** — The countdown can run for up to 10 minutes (600 seconds) while streaming progress. API Gateway supports up to 15 minutes with response streaming.
- **Graceful timeout handling** — If the Lambda is approaching its timeout, the stream stops cleanly.
- **.NET response streaming SDK** — Uses `LambdaResponseStreamFactory.CreateHttpStream()` from the `Amazon.Lambda.Core` preview package to produce the streaming response format API Gateway expects.

## Project Structure

```
├── template.yaml              # SAM template (API Gateway REST API + Lambda)
├── events/
│   └── countdown-30s.json     # Sample API Gateway proxy event (30s countdown)
└── src/
    ├── Program.cs             # Streaming countdown handler (top-level statements)
    └── CountdownStreaming.csproj
```

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [AWS SAM CLI](https://docs.aws.amazon.com/serverless-application-model/latest/developerguide/install-sam-cli.html)
- AWS account with credentials configured (`aws configure`)

---

## Build & Deploy

### Build

```bash
sam build
```

### Deploy

```bash
sam deploy --guided
```

Follow the prompts to configure stack name, region, and confirm IAM role creation. On subsequent deploys:

```bash
sam deploy
```

---

## Testing

### 1. Get the API endpoint

```bash
API_URL=$(aws cloudformation describe-stacks \
    --stack-name sam-dotnet-responsestreaming-countdown \
    --query "Stacks[0].Outputs[?OutputKey=='ApiEndpoint'].OutputValue" \
    --output text)
```

### 2. Run the default countdown (60s, tick every 5s)

```bash
curl --no-buffer "$API_URL"
```

You'll see output streamed in real-time:

```
🚀 Countdown started! Duration: 60s, Interval: 5s
   Total ticks: 12
──────────────────────────────────────────────────
[14:30:05.123] ⏱️  Tick 1/12 — 55s remaining
[14:30:10.125] ⏱️  Tick 2/12 — 50s remaining
[14:30:15.127] ⏱️  Tick 3/12 — 45s remaining
...
[14:31:00.145] 🏁 Tick 12/12 — Countdown complete!
──────────────────────────────────────────────────
✅ Done. Streamed 12 ticks over 60 seconds.
```

### 3. Custom duration and interval

```bash
# 10 minute countdown, tick every 30 seconds
curl --no-buffer "$API_URL?duration=600&interval=30"

# 30 second countdown, tick every 2 seconds
curl --no-buffer "$API_URL?duration=30&interval=2"

# Quick 10 second demo, tick every 1 second
curl --no-buffer "$API_URL?duration=10&interval=1"
```

### Parameters

| Parameter  | Default | Range    | Description                      |
|------------|---------|----------|----------------------------------|
| `duration` | 60      | 1–600    | Total countdown duration (seconds) |
| `interval` | 5       | 1–60     | Seconds between each streamed tick |

---

## How It Works

### API Gateway Configuration

The key to enabling response streaming is the API Gateway integration configuration:

```yaml
Integration:
  Type: AWS_PROXY
  IntegrationHttpMethod: POST
  ResponseTransferMode: STREAM
  Uri: !Sub arn:aws:apigateway:${AWS::Region}:lambda:path/2021-11-15/functions/${CountdownFunction.Arn}/response-streaming-invocations
```

- `ResponseTransferMode: STREAM` tells API Gateway to stream the response.
- The URI uses `/response-streaming-invocations` instead of the standard `/invocations` endpoint, which triggers Lambda's `InvokeWithResponseStream` API.

### .NET Lambda Response Streaming

```csharp
// 1. Define HTTP response headers (sent immediately to the client)
var prelude = new HttpResponseStreamPrelude
{
    StatusCode = HttpStatusCode.OK,
    Headers = { { "Content-Type", "text/plain; charset=utf-8" } }
};

// 2. Create the streaming response — writes the prelude + 8-null-byte delimiter
using var responseStream = LambdaResponseStreamFactory.CreateHttpStream(prelude);
using var writer = new StreamWriter(responseStream);

// 3. Write and flush — each flush sends data through API Gateway to the client
for (var tick = 1; tick <= totalTicks; tick++)
{
    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
    await writer.WriteLineAsync($"Tick {tick}...");
    await writer.FlushAsync();  // <-- Pushes the chunk to the client
}
```

Key points:
- `CreateHttpStream()` writes the JSON metadata prelude and 8-null-byte delimiter that API Gateway expects.
- Each `FlushAsync()` call pushes buffered data through API Gateway to the client immediately.
- The function uses the **executable programming model** (top-level statements + `LambdaBootstrapBuilder`).
- API Gateway supports streaming responses for up to 15 minutes with timeouts up to 900,000 ms.

---

## Cleanup

```bash
sam delete
```

---

## Useful Commands

| Command | Description |
|---------|-------------|
| `sam build` | Build the Lambda function |
| `sam deploy --guided` | Deploy with interactive prompts |
| `sam deploy` | Deploy with saved config |
| `sam logs --tail` | Tail CloudWatch logs |
| `sam delete` | Tear down the stack |
| `dotnet build src/` | Build locally without SAM |

---

## References

- [Building responsive APIs with API Gateway response streaming](https://aws.amazon.com/blogs/compute/building-responsive-apis-with-amazon-api-gateway-response-streaming/)
- [API Gateway response streaming documentation](https://docs.aws.amazon.com/apigateway/latest/developerguide/response-transfer-mode.html)
- [Lambda Response Streaming (.NET SDK PR)](https://github.com/aws/aws-lambda-dotnet/pull/2288)
- [Lambda Response Streaming Documentation](https://docs.aws.amazon.com/lambda/latest/dg/configuration-response-streaming.html)
- [AWS SAM Developer Guide](https://docs.aws.amazon.com/serverless-application-model/latest/developerguide/)
