#!/usr/bin/env bash
set -euo pipefail

observability_dir="${OBSERVABILITY_DIR:-../../shared/observability}"
observability_env="${OBSERVABILITY_ENV_FILE:-${observability_dir}/.env}"
if [[ ! -f "${observability_env}" ]]; then
  printf 'Missing observability environment file: %s\n' "${observability_env}" >&2
  exit 2
fi
observability_compose=(
  docker compose
  --project-directory "${observability_dir}"
  --env-file "${observability_env}"
  -f "${observability_dir}/docker-compose.yaml"
)

if [[ "${RUN_LOAD_TESTS:-}" != "1" ]]; then
  printf 'This script runs a full k6 profile. Re-run with RUN_LOAD_TESTS=1 after reading doc/observability-validation.md.\n' >&2
  exit 2
fi

script="${1:-load-test.js}"
case "${script}" in
  load-test.js|load-test-scan.js) ;;
  *)
    printf 'Expected load-test.js or load-test-scan.js, got %s.\n' "${script}" >&2
    exit 2
    ;;
esac

index_state='not-applicable'
if [[ "${script}" == 'load-test-scan.js' ]]; then
  index_state="${INDEX_STATE:-}"
  case "${index_state}" in
    with-index|without-index) ;;
    *)
      printf 'Set INDEX_STATE=with-index or INDEX_STATE=without-index for load-test-scan.js.\n' >&2
      exit 2
      ;;
  esac
fi

verify_index_state() {
  local expected_state="$1"
  local actual_count

  actual_count="$(
    MSYS_NO_PATHCONV=1 docker compose exec -T db-server /bin/bash -c \
      '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d LoadTestDb -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.indexes WHERE name = '\''IX_Orders_CustomerId'\'' AND object_id = OBJECT_ID('\''dbo.Orders'\'');"'
  )"
  actual_count="${actual_count//[[:space:]]/}"

  if [[ "${actual_count}" != '0' && "${actual_count}" != '1' ]]; then
    printf 'Could not determine IX_Orders_CustomerId state; sqlcmd returned %s.\n' \
      "${actual_count:-no output}" >&2
    return 1
  fi

  if [[ "${expected_state}" == 'with-index' && "${actual_count}" != '1' ]] ||
    [[ "${expected_state}" == 'without-index' && "${actual_count}" != '0' ]]; then
    printf 'INDEX_STATE=%s does not match the database: IX_Orders_CustomerId is %s.\n' \
      "${expected_state}" "$([[ "${actual_count}" == '1' ]] && printf 'present' || printf 'absent')" >&2
    return 1
  fi

  printf 'Verified database index state: %s.\n' "${expected_state}"
}

collector_metrics_url="${COLLECTOR_METRICS_URL:-http://127.0.0.1:18888/metrics}"
settle_seconds="${TELEMETRY_SETTLE_SECONDS:-15}"
if [[ ! "${settle_seconds}" =~ ^[0-9]+$ ]]; then
  printf 'TELEMETRY_SETTLE_SECONDS must be a non-negative integer.\n' >&2
  exit 2
fi

results_dir="${RESULTS_DIR:-results/local/telemetry-volume-${script%.js}-${EPOCHSECONDS:-0}}"
mkdir -p "${results_dir}"

wait_for_endpoint() {
  local url="$1"
  local name="$2"
  local attempts="${READINESS_ATTEMPTS:-60}"
  local interval_seconds="${READINESS_INTERVAL_SECONDS:-2}"

  for ((attempt = 1; attempt <= attempts; attempt += 1)); do
    if curl --silent --show-error --fail --max-time 5 "${url}" >/dev/null; then
      return 0
    fi
    if ((attempt < attempts)); then
      sleep "${interval_seconds}"
    fi
  done

  printf '%s did not become ready at %s after %s attempts.\n' "${name}" "${url}" "${attempts}" >&2
  return 1
}

capture_collector_metrics() {
  local output="$1"
  curl --silent --show-error --fail --max-time 10 "${collector_metrics_url}" >"${output}"
}

seq_size_kib() {
  MSYS_NO_PATHCONV=1 "${observability_compose[@]}" exec -T seq du -sk /data | while read -r size _; do
    printf '%s\n' "${size}"
    break
  done
}

metric_total() {
  local file="$1"
  local metric="$2"
  local required_label="${3:-}"
  awk -v wanted="${metric}" -v required_label="${required_label}" '
    $1 !~ /^#/ {
      name = $1
      sub(/\{.*/, "", name)
      if ((name == wanted || name == wanted "_total") &&
          (required_label == "" || index($1, required_label) > 0)) total += $NF
    }
    END { printf "%.0f", total + 0 }
  ' "${file}"
}

write_report_row() {
  local label="$1"
  local metric="$2"
  local required_label="${3:-}"
  local before after
  before="$(metric_total "${results_dir}/collector-before.prom" "${metric}" "${required_label}")"
  after="$(metric_total "${results_dir}/collector-after.prom" "${metric}" "${required_label}")"
  printf '%s\t%s\t%s\t%s\n' "${label}" "${before}" "${after}" "$((after - before))" \
    >>"${results_dir}/volume-summary.tsv"
}

api_health_url="${K6_BASE_URL:-http://127.0.0.1:18080}"
api_health_url="${api_health_url%/}/health"
wait_for_endpoint "${api_health_url}" 'API'
wait_for_endpoint "${collector_metrics_url}" 'Collector metrics'
wait_for_endpoint "${SEQ_HEALTH_URL:-http://127.0.0.1:5341/health}" 'Seq'

if [[ "${script}" == 'load-test-scan.js' ]]; then
  verify_index_state "${index_state}"
fi

collector_id_before="$("${observability_compose[@]}" ps -q api-load-test-collector)"
if [[ -z "${collector_id_before}" ]]; then
  printf 'The Collector container is not running.\n' >&2
  exit 1
fi

test_id="${K6_TEST_ID:-volume-${script%.js}-${EPOCHSECONDS:-0}}"
printf 'script=%s\ntest_id=%s\nstarted_epoch=%s\nsettle_seconds=%s\nindex_state=%s\nlog_successful_requests=%s\nlog_slow_requests=%s\nslow_request_threshold_ms=%s\n' \
  "${script}" "${test_id}" "${EPOCHSECONDS:-unknown}" "${settle_seconds}" \
  "${index_state}" "${LOG_SUCCESSFUL_REQUESTS:-false}" "${LOG_SLOW_REQUESTS:-false}" \
  "${SLOW_REQUEST_THRESHOLD_MS:-500}" \
  >"${results_dir}/manifest.txt"

capture_collector_metrics "${results_dir}/collector-before.prom"
seq_kib_before="$(seq_size_kib)"

K6_TEST_ID="${test_id}" bash run-k6.sh "${script}" --no-thresholds \
  --summary-export "${results_dir}/k6-summary.json"

if ((settle_seconds > 0)); then
  printf 'Waiting %s seconds for tail-sampling and exporter queues to settle.\n' "${settle_seconds}"
  sleep "${settle_seconds}"
fi

capture_collector_metrics "${results_dir}/collector-after.prom"
seq_kib_after="$(seq_size_kib)"
collector_id_after="$("${observability_compose[@]}" ps -q api-load-test-collector)"

if [[ "${collector_id_before}" != "${collector_id_after}" ]]; then
  printf 'The Collector restarted during the run; counter deltas would be invalid. Raw evidence remains in %s.\n' \
    "${results_dir}" >&2
  exit 1
fi

printf 'measurement\tbefore\tafter\tdelta\n' >"${results_dir}/volume-summary.tsv"
write_report_row 'receiver accepted spans' 'otelcol_receiver_accepted_spans'
write_report_row 'receiver refused spans' 'otelcol_receiver_refused_spans'
write_report_row 'receiver accepted log records' 'otelcol_receiver_accepted_log_records'
write_report_row 'receiver refused log records' 'otelcol_receiver_refused_log_records'
write_report_row 'Seq exporter sent spans' 'otelcol_exporter_sent_spans' 'exporter="otlphttp/seq"'
write_report_row 'Seq exporter failed spans' 'otelcol_exporter_send_failed_spans' 'exporter="otlphttp/seq"'
write_report_row 'Seq exporter sent log records' 'otelcol_exporter_sent_log_records' 'exporter="otlphttp/seq"'
write_report_row 'Seq exporter failed log records' 'otelcol_exporter_send_failed_log_records' 'exporter="otlphttp/seq"'
printf 'Seq data size KiB\t%s\t%s\t%s\n' \
  "${seq_kib_before}" "${seq_kib_after}" "$((seq_kib_after - seq_kib_before))" \
  >>"${results_dir}/volume-summary.tsv"

printf '\nTelemetry-volume summary for %s (%s):\n' "${script}" "${test_id}"
while IFS= read -r line; do
  printf '%s\n' "${line}"
done <"${results_dir}/volume-summary.tsv"
printf '\nSaved raw Collector snapshots, the k6 summary, and the volume summary to %s.\n' \
  "${results_dir}"
