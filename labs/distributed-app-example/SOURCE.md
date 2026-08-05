# Source provenance

This lab was migrated from `distributed-app-example` in
[pvoulgaris9398/python-samples](https://github.com/pvoulgaris9398/python-samples) at commit
`d063e95f3c4283afc12010953d7e9e651a7dbc50` on 2026-08-05.

The migration copied the files tracked by that commit and then made these monorepo-specific changes:

- added this provenance record, a lab README, and lab-specific contributor guidance;
- gave the Compose project and explicit container names lab-specific identifiers;
- connected the .NET gateway's RabbitMQ, gRPC, and OTLP clients to the environment variables that
  the Compose configuration already supplied; and
- omitted `nuclear.sh`, which indiscriminately removed all local Docker containers, images, and
  volumes rather than cleaning up only this lab.

No benchmark results or conclusions were migrated. The original worklog remains under `doc/` as
historical development context and may contain stale commands.
