#!/usr/bin/env bash
set -euo pipefail

compose=(docker compose)
sqlcmd=(env MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*' "${compose[@]}" exec -T db-server bash -lc '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b "$@"' bash)
run_id=$(powershell.exe -NoProfile -Command '[guid]::NewGuid().ToString()' | tr -d '\r')

finish_run() {
    exit_code=$?
    trap - EXIT
    run_status=passed
    if ((exit_code != 0)); then
        run_status=failed
    fi
    "${sqlcmd[@]}" -d LogReturnsLab -v RunId="$run_id" RunStatus="$run_status" \
        -i /dev/stdin < sql/finish-run.sql || true
    exit "$exit_code"
}

"${sqlcmd[@]}" -i /dev/stdin < sql/init.sql
"${sqlcmd[@]}" -d LogReturnsLab -v RunId="$run_id" -i /dev/stdin < sql/start-run.sql
trap finish_run EXIT
"${sqlcmd[@]}" -d LogReturnsLab -v RunId="$run_id" -i /dev/stdin < sql/validate.sql
"${sqlcmd[@]}" -d LogReturnsLab -v RunId="$run_id" -i /dev/stdin < sql/benchmark.sql
