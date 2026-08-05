---
name: worklog.md
description: working log for this project
date: 7/19/2026
---

## `Sunday, 7/19/2026`

- Made a lot of progress on this `polyglot` sample
- TODO: Make sure configurable elements are passed-in as environment variables, logging is correctly setup and it fully works end-to-end
- TODO: Add `grafana` and `prometheus`
- TODO: Add rate-limiting with `nginx` proxy
- TODO: Add ssl-termination

```bash

curl -X POST http://localhost:5281/products/seed

curl http://localhost:5281/products/1

curl -X POST http://localhost:5281/checkout \
     -H "Content-Type: application/json" \
     -d '{"ProductId": 1, "Quantity": 2}'


python -m grpc_tools.protoc -I/path/to/shared/protos --python_out=. --grpc_python_out=. /path/to/shared/protos/inventory.proto


```
