# Architecture Labs

Architecture Labs is a public collection of reproducible experiments for evaluating reference
applications, architectural patterns, infrastructure choices, and their engineering trade-offs.
Each lab will combine runnable code with load tests, observability, operational analysis, and a
written conclusion grounded in collected evidence.

## Repository locations

- Public repository: [pvoulgaris9398/architecture-labs](https://github.com/pvoulgaris9398/architecture-labs)
- Local working copy: `/c/Users/Peter/_work/_code/architecture-labs`

## Goals

- Compare approaches under clearly documented and repeatable conditions.
- Evaluate performance, reliability, operability, security, cost, and developer experience.
- Capture both strengths and limitations instead of declaring context-free winners.
- Make results understandable without requiring readers to run every experiment themselves.
- Demonstrate practical architecture, observability, testing, and technical-writing skills.

## Planned organization

The repository will use a monorepo structure while keeping every lab independently runnable:

```text
architecture-labs/
├── AGENTS.md
├── README.md
├── docs/
├── labs/
│   └── <self-contained-lab>/
├── shared/
└── tools/
```

Directories will be added only when their first concrete use is introduced. Shared code should
contain reusable mechanics—not assumptions that couple otherwise independent experiments.

## Lab expectations

Each lab should document:

1. The question or hypothesis being evaluated.
2. Architecture, constraints, assumptions, and relevant alternatives.
3. Exact reproduction and cleanup instructions.
4. Workloads, success criteria, and experimental controls.
5. Performance and resource measurements with machine and environment context.
6. Failure behavior, recovery characteristics, and observability coverage.
7. Security, deployment, upgrade, and operational considerations.
8. Developer experience, maintenance cost, and known limitations.
9. Evidence-backed conclusions and the circumstances in which each approach is appropriate.

## Status

No labs have been added yet. The intended first lab is a migration of the existing API load-test
example that explores .NET, SQL Server query performance, connection pooling, k6, OpenTelemetry,
Prometheus, and Grafana. That migration will be planned and performed as a separate change.

## Public-repository policy

This repository is intended to be public. Do not include credentials, private data, proprietary
code, employer or customer materials, internal diagrams, or benchmark results from systems that
the repository owner does not control. Local secrets belong in ignored `.env` files, with safe
placeholders documented in tracked `.env.example` files.

## License

See [LICENSE](LICENSE).
