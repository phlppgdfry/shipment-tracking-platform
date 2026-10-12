# Customer isolation and upgrade behavior

## Rules

The JWT issuer obtains CustomerId from the server's identity record. Requests never choose customer membership. A registered public user receives a generated ID and only Customer; the reserved dispatcher is provisioned only by the opt-in development seed. Existing untrusted accounts under its reserved name are rejected instead of promoted.

EF filters combine soft deletion with non-empty matching customer scope. Documents and operational events must also reference a customer-visible parent shipment. Repository ID reads execute filtered queries rather than relying on `FindAsync` tracking behavior. Request writes validate their record's ownership, and customer ownership cannot be changed by an ordinary operational write. Explicit system contexts are reserved for trusted provisioning/migration code and still need deliberate filter bypass for operational reads.

The detail cache is keyed by customer + shipment ID. Document uploads invalidate the same scoped detail key. Idempotency records are keyed by a SHA-256 scope of customer + caller + method + creation route. Middleware runs after authentication and authorization; a replay cannot bypass a role rejection. Creation and response storage share a relational transaction so a concurrent duplicate conflict rolls back the losing creation. Decoded request text is hashed: reformatting JSON under the same key may conflict even when the parsed meaning is similar.

Administrators remain customer-scoped. Trusted multi-customer administration is not implemented. Public registration cannot provision shared company membership; that is future work.

## Upgrade from 0.1

The migration adds CustomerId to identity users, shipments and documents. Existing identity IDs of at most 64 characters become their customer IDs. Shipments are attributed only when CreatedBy matches an existing user name; documents inherit their parent shipment's attribution. Unattributable records retain empty ownership and are invisible to operational queries. Review and explicitly provision them before use; they are not deleted.

A previous token without the new customer claim cannot access operational endpoints. Sign in again after migration. The isolation migration deliberately refuses a downgrade that would silently remove the boundary.

Development seeding is restricted to Development and an explicit flag. Legacy trusted demo dispatcher accounts can be recognized by their existing Dispatcher role and synthetic email; arbitrary public accounts are not promoted. Seeding generates the current synthetic baseline without assigning unrelated records to the demo company. Use a fresh demo database for reproducible screenshots.

## What the checks establish

CustomerIsolationTests cover list/search/detail and warmed cache, status denial across customers including an Administrator, document list/upload/download, replay boundaries per customer and caller, unauthenticated/role-denied replay, body conflicts and valid status recovery. OperationalEventTests cover event scope, source-ID replay, held departure, release and explicit timezone requirements.

Real JWT smoke checks register another customer while attempting client-selected membership and Administrator role; the server issues an independent Customer identity. That identity sees no dispatcher shipments, documents or events and cannot reuse a privileged creation response. Browser tests also switch accounts and verify prior rows, documents and inspector state disappear.

SQL tests apply migrations to a real database, verify status-history inserts and upgrade an initial schema with owned and unowned records. These are scoped portfolio checks, not a certification of every possible production integration or threat model.
