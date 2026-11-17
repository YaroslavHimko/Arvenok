# Online services boundary

No online service is implemented here yet. Introduce a service only with an owning team, a measurable scaling/security boundary and an operational plan. Keep related capabilities in one deployable unit while that is sufficient.

Candidate bounded contexts include identity/session admission, matchmaking/session allocation, character/progression persistence and social systems. These are design candidates, not existing endpoints. Each introduced service should own:

```text
services/<context>/
  src/                 Executable host and internal implementation
  contracts/           Versioned public API/event schemas, not database models
  tests/               Integration, migration and compatibility checks
  migrations/          Expand/contract data changes
  deploy/              Artifact/container definition and deployment integration
  README.md            Owner, dependencies, local run and operational runbook link
```

Register C# projects in the solution/catalog. Keep service-to-service calls versioned; never reference another service's implementation project or database. Game simulation must not block on a service request in its tick. Define retry/idempotency, quotas, privacy, migrations, backup and recovery behavior before release. Avoid making every gameplay feature an independently deployed service.
