# Distributed app lab guidance

This file extends the repository-root `AGENTS.md`. Run lab commands from this directory.

- Keep the dashboard, gateway, Python services, protobuf contract, Compose topology, and README in
  sync when changing routes, ports, service names, or environment variables.
- Regenerate both Python and .NET protobuf clients when `Protos/inventory.proto` changes.
- Keep Compose names and host ports unique to this lab; do not add globally generic container names.
- Treat the values in `.env.example` and exposed endpoints as local-only demonstrations.
- Keep `.env.example`, Compose variable requirements, application startup validation, and README
  configuration instructions synchronized. Required application settings must not have fallbacks.
- The lab depends on the external `architecture-labs-observability` network created by
  `shared/observability`; do not reintroduce observability services into this Compose file.
- Do not run `docker compose down --volumes` in either project without explicit approval.
- Validate .NET source changes with `dotnet build DistributedAppExample.slnx` and Compose changes
  with `docker compose --env-file .env.example config --quiet` before claiming the end-to-end flow
  works.
