# Shipment Tracking Platform

[![CI](https://github.com/phlppgdfry/shipment-tracking-platform/actions/workflows/ci.yml/badge.svg)](https://github.com/phlppgdfry/shipment-tracking-platform/actions/workflows/ci.yml)

Production-minded .NET 10 platform for creating, tracking and auditing logistics shipments. The API follows Clean Architecture and CQRS, while local infrastructure mirrors the Azure services used in deployment.

## Try the connected Shipment Desk

**0.2.0 preview · synthetic data · real API and SQL requests.** Create a shipment, record an arrival and customs hold, upload a manifest, release the hold and inspect the resulting KPIs.

[Scenario and first-run guide](docs/DEMO.md) · [Customer isolation](docs/CUSTOMER_ISOLATION.md) · [Verification evidence](docs/VERIFICATION.md) · [Release](https://github.com/phlppgdfry/shipment-tracking-platform/releases/tag/package-v0.2.0)

```bash
python3 scripts/start-demo.py
```

Run from a clone with Docker Desktop already started. The helper generates private local configuration; sign-in instructions are printed without revealing secrets. No registry login or cloud account is required. Developer builds first run `python3 scripts/prepare-demo.py` to obtain the verified library artifacts.

![Shipment Desk: actual synthetic baseline](docs/assets/shipment-desk-desktop.png)

[Watch the recorded workflow](docs/assets/shipment-desk-walkthrough.mp4)

## What is implemented

- ASP.NET Core 10 API, CQRS/MediatR, FluentValidation and EF Core SQL Server
- JWT authentication, server-owned customer boundaries, scoped caches/replays, global error responses and soft deletion
- Identity registration/login, password policy, roles and policy-protected write endpoints
- Azure Blob Storage document abstraction and transactional Service Bus outbox
- Background outbox publisher with retry metadata; Service Bus dead-lettering is enabled in IaC
- Redis-backed shipment-detail cache with invalidation on writes
- `Idempotency-Key` support for shipment POSTs; successful responses are replayed safely
- OpenTelemetry/Application Insights with idempotency replay/conflict metrics
- `/health` and `/ready` endpoints
- Upload signature validation; production deployments should additionally enable Defender for Storage malware scanning
- Unit/HTTP/real-SQL tests, legacy ownership upgrade, held delivery, replay boundaries and desktop/mobile browser evidence
- Docker Compose for SQL Server, Redis, Azurite and the API; Azure uses the in-memory cache fallback by default to avoid a dedicated Redis cost

## Architecture

```text
MAUI / HTTP clients
        |
ASP.NET Core API ── MediatR CQRS ── Domain
        |                 |
 SQL Server       Redis cache / Service Bus worker / Blob Storage
        |
Application Insights (traces, metrics and dependencies)
```

## Run locally

Prerequisites: Docker Desktop and .NET 10 SDK.

```bash
git clone https://github.com/phlppgdfry/shipment-tracking-platform.git
cd shipment-tracking-platform
python3 scripts/prepare-demo.py
cp .env.example .env
docker compose up --build
```

The API is available at `http://localhost:5001`; Swagger is at `/swagger` in the Development environment. Compose starts SQL Server, Redis and Azurite. The API creates the local schema on first start. Change all `.env` defaults before exposing any environment beyond your machine.

For a host-run API, start the infrastructure with `docker compose up sql redis azurite -d`, then run:

```bash
dotnet run --project src/WebAPI
```

## Configuration

Use environment variables, Key Vault or user secrets; do not commit production credentials. Set `KeyVault__Uri` in Azure to load secrets using the Container App managed identity.

| Setting | Purpose |
|---|---|
| `ConnectionStrings__DefaultConnection` | SQL Server connection string |
| `Redis__ConnectionString` | Optional Redis endpoint; without it the API uses in-memory cache (the Azure cost-conscious default) |
| `Azure__ServiceBus__ConnectionString` | Enables Service Bus publishing and worker consumption |
| `Azure__BlobStorage__ConnectionString` | Blob Storage/Azurite connection string |
| `JwtSettings__Secret` | 32+ character JWT signing secret |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Enables Azure Monitor OpenTelemetry export |

## API conventions

All shipment endpoints require a bearer token. Create a shipment with an idempotency key to make client retries safe:

```http
POST /api/shipments
Authorization: Bearer <token>
Idempotency-Key: 4e7280ab-0996-46ca-94e1-2b4d7b1aa0d0
Content-Type: application/json
```

Reusing the same key and payload returns the original successful response with `Idempotency-Replayed: true`; reusing it with a different payload returns `409 Conflict`.

## Tests

```bash
python3 scripts/prepare-demo.py
dotnet test ShipmentTracking.sln
npm --prefix demo test
```

The Testcontainers test starts an isolated SQL Server container, so Docker must be running. Use `dotnet test --filter "Category!=Container"` for the fast, Docker-free suite.

## CI/CD and deployment

The GitHub Actions workflow restores verified dependencies, runs the test suite (including Testcontainers), builds the candidate Docker image and exercises the real desktop/mobile demonstration on every push and pull request. A manual `workflow_dispatch` deployment job builds the same image, pushes it to Azure Container Registry and updates an Azure Container App. It uses GitHub-to-Azure OIDC: configure `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP`, `AZURE_CONTAINER_APP_NAME` and `AZURE_ACR_NAME` as repository variables, then federate the GitHub environment in Entra ID. The Bicep defaults to zero Container App replicas and disables Service Bus; enable paid components only when required.

The infrastructure is declared in [infra/main.bicep](infra/main.bicep). Deployment and recovery procedures are in [docs/runbook.md](docs/runbook.md); the reliability design is in [docs/architecture.md](docs/architecture.md).

## Key decisions

- **Clean Architecture:** Domain and Application do not depend on infrastructure implementations.
- **CQRS:** request handlers keep shipment commands and read models isolated.
- **Idempotency at the HTTP boundary:** retry safety is persisted with a unique request scope/key constraint.
- **Cache-aside:** shipment details are cached for five minutes and invalidated after status changes.
- **Transactional outbox:** publishing is separated from HTTP processing without losing events when Service Bus is unavailable.

## Published container package

[Run the packaged API](docs/PACKAGE.md) using `ghcr.io/phlppgdfry/shipment-tracking-api:0.2.0`. The local Compose example includes SQL Server, an opt-in synthetic dispatcher and sample shipment. Releases publish only after the real container passes integration and HTTP smoke tests.
