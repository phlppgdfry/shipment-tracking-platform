# Improvement traceability

| Requested improvement | Implementation | Verification |
| --- | --- | --- |
| Customer isolation | Server-owned JWT customer claim, EF filters, scoped caches/replays, guarded documents/events | CustomerIsolationTests, real JWT API smoke and browser account switch |
| Full lifecycle, documents, repeats | Valid transition chain, history key persistence, PDF handling, relational replay transaction | HTTP/real-SQL tests and package-smoke.py |
| Independent contract consumer | Documented 0.2.0 records, stable enum numbers, frozen 0.1 fixture, .NET 8 NuGet consumer | PortOps.Contracts release workflow and 132 tests |
| Useful KPI library | Explicit arrival status at as-of, future values, missing data, declarations, actual dashboard consumer | 23 tests, compiled packaged TypeScript consumer, six mapping cases |
| Connected demonstration | Shipment Desk uses published NuGet envelopes and npm calculations | Desktop/mobile real-API five-step scenario and recording |
| Beyond green tests | Expected baseline/after values, error recovery, source inspection, screenshot/video/JSON evidence | VERIFICATION.md and checked-in assets |
| Outcome-oriented Projects | Three milestones, acceptance criteria, linked checks and explicit next-three selection | Public Projects 6 and 7 |

Working scenarios are synthetic. Provider evaluation, real terminal adapters, production membership provisioning and native ARM datastore support remain future issues; the current milestone structure does not claim they are implemented.
