#!/usr/bin/env bash
set -euo pipefail

if [[ "${RUN_LOAD_TESTS:-}" != "1" ]]; then
  printf 'This script runs the full k6 profile repeatedly. Re-run with RUN_LOAD_TESTS=1 after reading doc/observability-validation.md.\n' >&2
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

rounds="${VALIDATION_ROUNDS:-3}"
if [[ ! "${rounds}" =~ ^[1-9][0-9]*$ ]]; then
  printf 'VALIDATION_ROUNDS must be a positive integer.\n' >&2
  exit 2
fi

results_dir="${RESULTS_DIR:-results/local/telemetry-overhead-${EPOCHSECONDS:-0}}"
mkdir -p "${results_dir}"

printf 'script=%s\nrounds=%s\nstarted_epoch=%s\n' \
  "${script}" "${rounds}" "${EPOCHSECONDS:-unknown}" >"${results_dir}/manifest.txt"

restore_otlp_export() {
  TELEMETRY_OTLP_ENABLED=true docker compose up -d --build --force-recreate api-service
}

trap restore_otlp_export EXIT

wait_for_api() {
  local base_url="${K6_BASE_URL:-http://127.0.0.1:18080}"
  local health_url="${base_url%/}/health"
  local attempts="${READINESS_ATTEMPTS:-60}"
  local interval_seconds="${READINESS_INTERVAL_SECONDS:-2}"

  for ((attempt = 1; attempt <= attempts; attempt += 1)); do
    if curl --silent --show-error --fail --max-time 5 "${health_url}" >/dev/null; then
      printf 'API readiness passed at %s after %s attempt(s).\n' "${health_url}" "${attempt}"
      return 0
    fi

    if ((attempt < attempts)); then
      sleep "${interval_seconds}"
    fi
  done

  printf 'API did not become ready at %s after %s attempts.\n' "${health_url}" "${attempts}" >&2
  docker compose logs --tail 100 api-service >&2
  return 1
}

run_case() {
  local mode="$1"
  local round="$2"
  local enabled='true'
  [[ "${mode}" == 'disabled' ]] && enabled='false'

  printf '\nRound %s: OTLP export %s\n' "${round}" "${mode}"
  TELEMETRY_OTLP_ENABLED="${enabled}" docker compose up -d --build --force-recreate api-service
  wait_for_api

  local test_id="otel-${mode}-r${round}-${EPOCHSECONDS:-0}"
  local output="${results_dir}/${script%.js}-${mode}-round-${round}.json"
  TELEMETRY_OTLP_ENABLED="${enabled}" K6_TEST_ID="${test_id}" \
    bash run-k6.sh "${script}" --no-thresholds --summary-export "${output}"
}

for ((round = 1; round <= rounds; round += 1)); do
  if ((round % 2 == 1)); then
    run_case disabled "${round}"
    run_case enabled "${round}"
  else
    run_case enabled "${round}"
    run_case disabled "${round}"
  fi
done

restore_otlp_export
trap - EXIT
printf '\nSaved local summaries to %s and restored OTLP export.\n' "${results_dir}"
