# Distributed app lab guidance

This file extends the repository-root `AGENTS.md`. Run lab commands from this directory.

- Keep the dashboard, gateway, Python services, protobuf contract, Compose topology, and README in
  sync when changing routes, ports, service names, or environment variables.
- Regenerate both Python and .NET protobuf clients when `Protos/inventory.proto` changes.
- Keep Compose names and host ports unique to this lab; do not add globally generic container names.
- Treat the committed credentials and exposed endpoints as local-only demonstration defaults.
- Do not run `docker compose down --volumes` without explicit approval.
- Validate Compose changes with `docker compose config --quiet` and source changes with the
  component-specific build before claiming the end-to-end flow works.
