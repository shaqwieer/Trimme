# Observability

How to see what the API is doing in any environment: logs, traces, metrics and health. Decisions: D-118 (telemetry), D-038 (logging), D-108 (background work).

## 1. Logs

The API logs through Serilog.
- **Development:** a readable console line, `[time level] correlationId message`.
- **Every other environment:** one compact JSON object per line (CLEF), for the log collector to parse.

### Field conventions

| Field | Meaning | Source |
|---|---|---|
| `@t`, `@l`, `@m`, `@mt` | Time (UTC), level (omitted for Information), rendered message, message template | Serilog CLEF |
| `@x` | Exception, with its stack trace (server logs only; never returned to clients) | Serilog |
| `@tr`, `@sp` | OpenTelemetry trace and span id, the same ids as the exported trace | Serilog, from `Activity.Current` |
| `CorrelationId` | Request id: the caller's well-formed `X-Correlation-Id`, else a new one. Echoed on the response and in problem details as `traceId` | `CorrelationIdMiddleware` |
| `RequestId`, `RequestPath`, `RequestMethod`, `StatusCode`, `Elapsed` | One summary line per request (ms). Aborted requests are 499 | Serilog request logging |
| `SourceContext` | The logging class | Serilog |
| `Application` | Always `Trimme.Api` | Enricher |
| `EventId` | Stable id of `[LoggerMessage]` events | `Microsoft.Extensions.Logging` |
| Domain fields | PascalCase names of identifiers and types, for example `ShopId`, `BookingId`, `MessageType`, `Consumer`, `JobId`, `Attempts` | Message templates |

Rules:
- **Message templates never embed personal data.** Templates carry identifiers (GUIDs), types and counts. They never carry phone numbers, emails, names, message text, tokens or OTP codes.
- **The redaction enricher is the safety net, not the mechanism.**
  - Properties whose name contains a sensitive fragment (`phone`, `mobile`, `whatsapp`, `msisdn`, `password`, `secret`, `token`, `otp`, `cookie`, `authorization`, `apikey`, …) are replaced entirely.
  - Any string value is pattern-scrubbed: E.164 and local phone numbers, bearer tokens and emails.
- **Levels:**
  - `Error`: something an operator must look at (unhandled exception, dead-lettered message, failed job registration).
  - `Warning`: a degraded but handled path (consumer retry, provider error, rejected webhook).
  - `Information`: request summaries and lifecycle events.
- **Never log** request or response bodies, cookies, `Authorization` headers or WhatsApp payloads.

## 2. Traces and metrics (OpenTelemetry)

The API always records telemetry. It **exports only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set**, so each environment picks its collector with configuration alone. The standard variables apply:

| Variable | Example | Notes |
|---|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://otel-collector:4317` | Turns the exporter on |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` (default) or `http/protobuf` | |
| `OTEL_EXPORTER_OTLP_HEADERS` | `api-key=…` | Secrets come from the environment only |
| `OTEL_SERVICE_NAME` | `trimme-api` | Defaults to `trimme-api` |
| `OTEL_TRACES_SAMPLER_ARG` | `0.2` | Share of traces kept, by trace id (default 1, every trace). TRIMME sets its own sampler, so `OTEL_TRACES_SAMPLER` is not read |

The resource carries `service.name=trimme-api`, `service.version` and `deployment.environment.name` (the ASP.NET Core environment).

### Traces

**Sampling.** A trace follows its parent's decision. A new root is kept at the configured ratio, except a root **client** span, which is always dropped. Requests, Hangfire jobs and outbox messages each start their own span, so every database command or provider call made for real work has a parent. What is left without one is background polling: the outbox loop every two seconds, and Hangfire's queue and heartbeat queries. Kept, that polling outnumbered real traces by about six to one in a local run.

| Source | Spans |
|---|---|
| ASP.NET Core | One server span per request, named `METHOD route`. `/health/*` is not traced. Query string values are redacted by the instrumentation (`?search=Redacted`) |
| HttpClient | Outgoing calls: the WhatsApp provider, the geocoder, SMTP is not HTTP |
| Npgsql | Every database command, EF Core's included, as a child of the request or job. The SQL text uses parameter placeholders; values are never recorded |
| `Trimme` | `job <Type>.<Method>` per Hangfire job run, tagged `job.name` and `job.outcome`; `outbox <type>` per outbox message delivered, tagged `message.type`. Job arguments and payloads are never recorded |
| SignalR | Hub connections and invocations (`OnConnectedAsync`, …) |

EF Core and Hangfire have only prerelease OpenTelemetry instrumentation, so TRIMME uses Npgsql's stable tracing and its own job filter instead (D-118).

### Metrics

| Instrument | Unit | Tags | Meaning |
|---|---|---|---|
| `http.server.request.duration` | s | route, method, status | ASP.NET Core |
| `http.client.request.duration` | s | host, status | Outgoing HTTP |
| `db.client.operation.duration`, connection pool metrics | s | | Npgsql |
| `process.runtime.dotnet.*` | | | GC, thread pool, exceptions |
| `trimme.jobs.executed` | {job} | `job.name`, `job.outcome` | Hangfire job runs |
| `trimme.jobs.duration` | s | `job.name`, `job.outcome` | Job run time |
| `trimme.outbox.processed` | {message} | `message.type` | Messages delivered to every consumer |
| `trimme.outbox.failures` | {delivery} | `message.type`, `consumer` | Failed consumer deliveries (retried, then dead-lettered) |
| `trimme.outbox.delivery_lag` | s | `message.type` | Commit to full delivery |

### Suggested alerts and dashboards

| Signal | Alert when |
|---|---|
| `http.server.request.duration` p95 on `/api/v1/public/*` and availability | above 500 ms for 10 minutes |
| 5xx rate | above 1 % for 5 minutes |
| `trimme.outbox.failures` | any increase for 15 minutes |
| `trimme.outbox.delivery_lag` p95 | above 60 s |
| `trimme.jobs.executed{job.outcome=failed}` | any reminder job failure |
| `/health/ready` | `Degraded` for 10 minutes (see below) |
| Npgsql pool | waiting connections above 0 for 5 minutes |

A dashboard per area: requests (rate, errors, duration by route), database (operations, pool), background work (jobs, outbox) and the runtime.

## 3. Health

| Endpoint | Checks | Status codes |
|---|---|---|
| `/health/live` | None: the process answers | 200 |
| `/health/ready` | `database` (EF Core can connect), `jobs`, `outbox` | 200 Healthy or Degraded, 503 Unhealthy |

- **`jobs`**: a Hangfire server of this deployment sent a heartbeat in the last 120 s (`Jobs:Health:ServerHeartbeatSeconds`). Healthy when jobs are switched off in this process.
- **`outbox`**: the oldest message still due is at most 300 s old (`Jobs:Health:OutboxLagSeconds`), and no message is dead-lettered. The payload reports `lagSeconds` and `deadLettered` per check.
- **Both report `Degraded` at worst, never `Unhealthy`.** Readiness decides whether the instance takes traffic, and stalled background work must not take the API out of rotation. It raises an alert instead.
- The container's `HEALTHCHECK` uses `/health/live`.
- The health endpoints are not under `/api`, so the single public domain never exposes them (Nginx forwards only `/api` and `/hubs`).

The payload is only a status per check:

```json
{"status":"Degraded","checks":[{"name":"database","status":"Healthy"},{"name":"jobs","status":"Healthy"},{"name":"outbox","status":"Degraded"}]}
```

## 4. Operations dashboard

`/api/ops/jobs` is the Hangfire dashboard: read-only, for platform admins with `Admin.Jobs.View` (D-108). It shows queues, scheduled reminders, retries and failures. Job arguments are identifiers only.

## 5. Trying it locally

```bash
docker run --rm -p 4317:4317 -p 16686:16686 jaegertracing/all-in-one:latest
# then, for the API (compose or dotnet run):
OTEL_EXPORTER_OTLP_ENDPOINT=http://host.docker.internal:4317
```

Open Jaeger at http://localhost:16686 and pick the `trimme-api` service.
