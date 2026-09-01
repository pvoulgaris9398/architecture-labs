#!/usr/bin/env bash
set -euo pipefail

scenario_sql=("$@")
if ((${#scenario_sql[@]} == 0)); then
    scenario_sql=(
        sql/benchmark.sql
        scenarios/long-asset-history/query.sql
    )
fi

for sql_file in "${scenario_sql[@]}"; do
    if [[ ! -f "$sql_file" ]]; then
        printf 'Scenario SQL file not found: %s\n' "$sql_file" >&2
        exit 1
    fi
done

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
for sql_file in "${scenario_sql[@]}"; do
    "${sqlcmd[@]}" -d LogReturnsLab -v RunId="$run_id" -i /dev/stdin < "$sql_file"
done
