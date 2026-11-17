# Infrastructure boundary

**Design only. Nothing here provisions a cloud account or deploys a fleet.** No provider, cluster runtime, billing model or production region has been chosen.

Follow [regional hosting](../docs/operations/regional-hosting.md). When a provider is selected, introduce reusable IaC modules under `modules/<capability>` and isolated roots under `environments/<environment>/<region>`. Keep image/artifact digests, policy and service deployment definitions distinct from provider resource modules. Do not copy a whole infrastructure stack for each region.

Use remote state and locking per environment/region, workload identity, secret-manager references and CI plan artifacts. Never store state, credentials, private keys or real variable files in Git. Keep production application permissions separate from infrastructure administration. Production deployment, migrations and traffic promotion need environment controls configured in the hosting platform.

Before creating a fleet: verify the dedicated server artifact and health/drain behavior, choose UDP ingress/allocation semantics, measure session resource usage, and define recovery/data-residency requirements. Ordinary HTTP load balancing is not a session allocator for this ENet server.
