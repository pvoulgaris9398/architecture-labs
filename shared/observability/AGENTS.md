# Shared observability guidance

This file extends the repository-root `AGENTS.md`. Run support-stack commands from this directory.

- Keep the API load-test and distributed-app Collector pipelines independent.
- Keep application-specific dashboards in their named Grafana folders.
- Preserve the API load-test lab's direct Prometheus scrape of `api-service:8080`.
- Do not put application databases, brokers, caches, or experiment workloads in this project.
- Keep every image and environment value explicit in `.env.example`; do not add fallbacks.
- Validate this Compose model and both consuming lab models after changing service names, network
  names, ports, scrape targets, collector endpoints, or provisioning.
- Do not run `docker compose down --volumes` without explicit approval; those volumes retain
  telemetry for every connected lab.
