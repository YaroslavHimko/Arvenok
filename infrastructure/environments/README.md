# Environments and regions

`local` currently means a developer-run Godot server with loopback binding. There are no provisioned staging or production environments.

Future environment roots should declare environment, region, artifact digest, protocol compatibility, capacity limits, identity/secret references, observability destinations and data ownership. Keep resource identities stable and secrets external. Region names in `ARVENOK_REGION` are metadata today; setting a region does not create routing or infrastructure.

Promote the same tested artifact through staging and production. Scope plans, state, quotas and rollback per region. Add new regions through reviewed configuration after residency, latency and outage behavior are tested; never treat a region list as evidence of deployed capacity.
