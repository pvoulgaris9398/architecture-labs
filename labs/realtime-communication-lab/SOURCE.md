# Source provenance

The runnable WebSocket baseline was moved into the Architecture Labs monorepo from
[`pvoulgaris9398/signalr-example`](https://github.com/pvoulgaris9398/signalr-example) at commit
`f73ce2b2ec54fecf1bd7811fbdf3213a6f38d95a` on 2026-08-06.

The contents of the source repository's `stage1/` directory were moved to
`transports/websocket/`. The implementation uses raw ASP.NET Core WebSockets despite the original
repository name. Its local .NET tool manifest moved with the implementation. Standalone Git
metadata, build output, the placeholder root README, repository license, editor settings, and root
`.gitignore` were not nested into the lab.

Monorepo-specific changes made during migration:

- added the lab layout, documentation, contributor guidance, and this provenance record;
- gave the WebSocket Compose project and container lab-specific names; and
- corrected references that described the raw WebSocket endpoint as SignalR.

No benchmark evidence or conclusions were migrated. Preserve the original repository until a
deliberate decision is made about its history and retirement.
