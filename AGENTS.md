# Repository guidance

## Repository identity

- Public repository: `https://github.com/pvoulgaris9398/architecture-labs`
- Local working copy: `/c/Users/Peter/_work/_code/architecture-labs`
- This is a public, portfolio-oriented architecture laboratory organized as a monorepo.

## Working conventions

- Prefer Bash syntax for commands, scripts, and documentation.
- Prefer concise documentation. Suggest substantial documentation or scope expansions and wait for
  approval before adding them.
- Preserve the user's uncommitted changes. Do not commit, push, publish, or rewrite Git history;
  the user manages Git and GitHub.
- Keep changes focused and avoid unrelated formatting or generated-file churn.
- Do not create, copy, or migrate a lab without explicit approval.
- Read the nearest nested `AGENTS.md` before changing a lab; lab-specific guidance may extend this
  root guidance.
- Use the XML-based `.slnx` format for .NET solution files. Do not introduce legacy `.sln` files;
  keep each solution within the lab that owns its projects.

## Monorepo boundaries

- Keep every lab independently understandable, configurable, runnable, and removable.
- A lab should own its application source, environment, load tests, dashboards, documentation,
  and results unless a dependency is intentionally shared as part of the experiment.
- Put only genuinely reusable mechanics under `shared/` or `tools/`. Do not hide architectural
  coupling inside shared utilities.
- Use unique Compose project names, ports, networks, volumes, and service identifiers so labs do
  not collide. Document whether multiple labs can run concurrently.
- Use path-filtered validation when CI is introduced so unrelated labs are not rebuilt needlessly.

## Shared container and Compose policy

- Introduce shared container definitions only when a concrete lab needs them. Do not scaffold
  speculative databases, brokers, observability stacks, or other services in advance.
- Prefer Docker Compose `extends` for an individual service that needs shared defaults plus
  lab-specific customization. Prefer `include` only for a complete reusable subsystem that a lab
  consumes largely unchanged.
- Keep shared service definitions limited to stable mechanics such as health checks, safe runtime
  defaults, logging conventions, and standard environment-variable names.
- Keep experiment-specific behavior inside the lab, including initialization and schema files,
  host ports, named volumes, credentials, resource limits, scrape targets, dashboards, and any
  configuration that affects the hypothesis or measured outcome.
- Shared services should avoid fixed host ports and globally fixed container names so multiple
  labs can coexist without collisions.
- A lab must remain understandable by inspecting its own Compose file. Avoid chains of indirection
  that force readers to traverse several shared files to understand how a core dependency runs.
- Extract or expand a shared definition after repeated use demonstrates a stable common boundary.
  The first lab may introduce a deliberately small shared convention, which should be validated
  before it becomes the template for later labs.
- Validate the fully resolved Compose model from the lab directory with
  `docker compose config --quiet` and document the minimum supported Docker Compose version when
  using features such as `include`.
- Record material shared-structure decisions and their rationale in an architecture decision
  document when the first affected lab is implemented.

## Experimental integrity

- State the hypothesis, controlled variables, workload, success criteria, and limitations before
  presenting conclusions.
- Keep comparison workloads equivalent and change one intentional variable at a time when the lab
  claims a controlled comparison.
- Record relevant software versions, machine characteristics, test date, warm-up procedure, and
  measurement window with benchmark results.
- Preserve raw or summarized evidence needed to support conclusions, while excluding oversized or
  sensitive artifacts from Git.
- Prefer nuanced, context-dependent conclusions over universal winner/loser claims.
- Mark labs as planned, in progress, complete, or needing revalidation; do not present unfinished
  experiments as settled findings.

## Dependencies and container images

- For a newly introduced container image, verify and pin the latest LTS release available at the
  time of implementation. If no LTS channel exists, pin the latest stable production release.
- Do not use floating tags such as `latest` when a versioned tag is available.
- Treat existing pins as intentional. Evaluate later upgrades for compatibility in a separate,
  deliberate change.
- Keep dependency and image choices reproducible and document material version constraints.

## Documentation and public safety

- Write documentation for an external reader who has no prior conversation context.
- Keep the root catalog and status current whenever labs are added, renamed, or completed.
- Update commands, environment variables, diagrams, expected results, and cleanup instructions
  whenever their implementation changes.
- Never commit credentials, private data, proprietary source, employer or customer information, or
  results collected from systems the repository owner does not control.
- Put local secrets in ignored `.env` files and commit only safe `.env.example` placeholders.
- Clearly label configurations that are suitable only for local experiments and avoid implying
  that demo credentials, open ports, or unauthenticated endpoints are production-safe.

## Observability and Prometheus metric names

- Treat the metric name stored by Prometheus as authoritative for PromQL and Grafana dashboards.
  Do not infer it solely from the application's raw `/metrics` response: exporter content
  negotiation and Prometheus translation settings can cause the rendered and stored names to
  differ, including underscore-rendered OpenTelemetry names being stored with dots.
- Before finalizing or changing a dashboard query, verify the actual stored name through
  Prometheus, for example with `/api/v1/label/__name__/values`, and execute the intended expression
  through `/api/v1/query` or the Prometheus query UI.
- Query dotted metric names with an exact quoted name selector, for example
  `{__name__="websocket.outbound.queue.depth"}`, because a dotted name cannot be written as a bare
  PromQL identifier. Preserve the stored spelling rather than replacing dots with underscores.
- Validate dashboards against a running workload and Prometheus scrape, not only by parsing the
  dashboard JSON or confirming that the application exposes metrics. Confirm that each panel's
  PromQL returns the expected series and labels from Prometheus.

## Validation and destructive operations

- Give every lab documented build, format, smoke-test, and configuration-validation commands.
- Validate only the affected labs plus shared components they consume.
- Do not automatically run heavy load tests, incur external costs, or target non-local systems.
- Do not delete volumes, databases, captured results, or other material state without explicit
  approval. Call out destructive cleanup commands clearly in documentation.
- Run `git diff --check` after documentation or source changes.
