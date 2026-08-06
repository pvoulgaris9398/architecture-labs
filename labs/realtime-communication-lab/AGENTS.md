# Realtime communication lab guidance

This file extends the repository-root `AGENTS.md`. Run commands for an implementation from that
implementation's directory.

- Keep transport and broker implementations independent; do not couple comparisons through hidden
  shared application behavior.
- Keep workloads equivalent when comparing implementations and document any unavoidable semantic
  differences.
- Use lab-specific Compose project names, services, ports, networks, and volumes.
- Treat `transports/websocket` as the migrated baseline. Other directories are planned until they
  contain a runnable implementation and supporting evidence.
- Do not run load tests or destructive Docker cleanup without explicit approval.
- Update the root README and this lab's status when an implementation becomes runnable or a result
  is recorded.
