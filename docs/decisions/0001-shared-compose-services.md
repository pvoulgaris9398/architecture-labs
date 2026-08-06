# ADR 0001: Share stable Compose service mechanics

- Status: Accepted
- Date: 2026-08-02

## Context

Architecture Labs will contain independently runnable experiments that may repeatedly use the
same infrastructure products. Copying complete service definitions into every lab creates drift,
but centralizing all configuration would hide experiment-specific variables and make labs harder
to understand or remove.

## Decision

Use Docker Compose `extends` for reusable individual service baselines. Shared definitions contain
only stable mechanics such as image configuration contracts, safe defaults, and health checks.
Labs retain ports, volumes, initialization, credentials, resource limits, monitoring, and any
configuration that affects the experiment.

Use Compose `include` only when a complete subsystem is intentionally consumed without service-
level customization. Do not create shared service definitions speculatively; introduce them when
a concrete lab establishes a real use and expand them only after repetition confirms the boundary.

The first application of this decision is `shared/compose/sqlserver/service.yaml`, extended by the
API load-test lab. Observability later moved to an independently runnable shared support stack as
recorded in ADR 0003; it is no longer modeled as individual Compose service baselines.

## Consequences

- Common SQL Server readiness behavior has one maintained definition.
- The lab still exposes its meaningful configuration in its own Compose file.
- Shared files add one level of navigation and require validation of the fully resolved model.
- Future labs should reuse this baseline only when its behavior matches their experimental needs.
- Labs must document the minimum supported Docker Compose version when adopting features that
  impose such a requirement.
