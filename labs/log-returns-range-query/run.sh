#!/usr/bin/env bash
set -euo pipefail

compose=(docker compose)
sqlcmd=(env MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*' "${compose[@]}" exec -T db-server bash -lc '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b "$@"' bash)

"${sqlcmd[@]}" -i /dev/stdin < sql/init.sql
"${sqlcmd[@]}" -d LogReturnsLab -i /dev/stdin < sql/benchmark.sql
