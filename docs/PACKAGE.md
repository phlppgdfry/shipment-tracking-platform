# Shipment Tracking API — container package

**Preview 0.2.0 · .NET 10 · linux/amd64 · synthetic local demonstration.** The image contains the existing application, not a replacement demo API. It uses SQL Server, JWT authentication, role-restricted shipment writes, an outbox and local document storage. Redis and Azure integrations are optional.

## Run the published image

Requires Docker Compose, an amd64-compatible Docker environment and enough memory for SQL Server (at least 2 GB available to SQL Server; allocate 4 GB or more to Docker). Apple Silicon uses amd64 emulation; compatibility and speed depend on Docker's emulation settings. The image is not a native ARM build.

```bash
git clone https://github.com/phlppgdfry/shipment-tracking-platform.git
cd shipment-tracking-platform
cp .env.package.example .env
# Edit .env with your own strong SQL and dispatcher passwords, and a JWT secret of 32+ characters.
docker compose -f docker-compose.package.yml up -d --wait --wait-timeout 300
```

Passwords must have at least 12 characters, uppercase, lowercase, a digit and punctuation. The sample API binds only to `127.0.0.1:5001`; SQL Server is not exposed on the host. The API image runs as the unprivileged `app` user. `http://localhost:5001/swagger` offers interactive API documentation; `/health` reports database health.

Sign in at `POST /api/auth/login` with userName `demo-dispatcher` and the password you set in `DEMO_DISPATCHER_PASSWORD`. Copy the returned access token into Swagger's **Authorize** field. Three synthetic baseline shipments and their source events are seeded. You can list, create and inspect shipments. New public registrations receive the Customer role and cannot create shipments.

The opt-in `Demo__Seed=true` flag works only in Development. Restarting does not duplicate the seeded shipment or reset an existing dispatcher's password. Changing `.env` passwords does not change accounts already stored in the SQL volume; use a fresh demo volume when necessary. Never enable this demonstration setup for a production deployment.

## Automated smoke check

Provide the dispatcher password through your local shell environment, then run:

```bash
python3 scripts/package-smoke.py
```

The script verifies database health, rejection of unauthenticated reads, seeded data, dispatcher creation/read and rejection of customer writes. It creates synthetic records and a synthetic customer, and never prints tokens. Publication runs this check against the candidate image and a real SQL Server before pushing the image.

## Storage and cleanup

`package-sql` stores database data. `package-documents` stores uploaded local documents. Stopping preserves them:

```bash
docker compose -f docker-compose.package.yml down
```

To deliberately discard **all local demo data**, add `-v` to that command. Use only for disposable synthetic data.

## Image and release policy

`ghcr.io/phlppgdfry/shipment-tracking-api:0.2.0` is the versioned image; `latest` follows the newest published package release. Prefer a version or immutable digest for repeatable runs. `package-v*` GitHub releases validate the application version, run full tests and metric-mapping checks, build the candidate, exercise desktop/mobile behavior and real JWT recovery against SQL, then publish. Existing version tags are not replaced. Screenshots, a walkthrough and proof JSON are attached to the release. OCI metadata links the image to its source repository and MIT license.

This is a portfolio preview. Production identity, TLS, tenant isolation review, secret management, backups, migrations and real integration credentials require a separate deployment design. No production service is deployed by the publication workflow.

## Connected desk and first-run helper

Open `http://localhost:5001` for the working Shipment Desk; `/swagger` remains available. [DEMO.md](DEMO.md) explains the complete five-step scenario, expected baseline metrics and customer boundary. `python3 scripts/start-demo.py` generates private first-run configuration and runs the released 0.2.0 image. For a source build, run `python3 scripts/prepare-demo.py` first so the verified NuGet feed and npm-derived browser assets exist.

The published image is verified on linux/amd64 in CI. Local visual development may use a native ARM API build with emulated amd64 SQL; release verification uses the actual bundled image without a source bind mount.
