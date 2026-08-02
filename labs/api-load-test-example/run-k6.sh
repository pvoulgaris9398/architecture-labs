#!/usr/bin/env bash
set -euo pipefail

script="${1:-load-test.js}"
if (($# > 0)); then
  shift
fi

export K6_PROMETHEUS_RW_SERVER_URL="${K6_PROMETHEUS_RW_SERVER_URL:-http://localhost:9090/api/v1/write}"
export K6_PROMETHEUS_RW_TREND_STATS="${K6_PROMETHEUS_RW_TREND_STATS:-p(95),p(99),max}"
export K6_BASE_URL="${K6_BASE_URL:-http://127.0.0.1:18080}"

test_id="${K6_TEST_ID:-local-${EPOCHSECONDS:-0}-${RANDOM}}"

printf 'API base URL: %s\n' "${K6_BASE_URL}"
printf 'k6 metrics destination: %s\n' "${K6_PROMETHEUS_RW_SERVER_URL}"

health_url="${K6_BASE_URL%/}/health"
status_marker='__K6_HEALTH_STATUS__'
response="$(curl --silent --show-error --max-time 5 \
  --include \
  --write-out "${status_marker}%{http_code}" \
  "${health_url}")" || curl_exit=$?
http_status="${response##*${status_marker}}"
response="${response%${status_marker}*}"

response_server='unknown'
while IFS= read -r header_line; do
  header_line="${header_line%$'\r'}"
  if [[ "${header_line,,}" == server:* ]]; then
    response_server="${header_line#*:}"
    response_server="${response_server#"${response_server%%[![:space:]]*}"}"
    break
  fi
done <<< "${response}"

if [[ "${curl_exit:-0}" -ne 0 || "${http_status}" != "200" ]]; then
  response_preview="${response//$'\r'/}"
  response_preview="${response_preview//$'\n'/ }"
  response_preview="${response_preview:0:240}"
  printf 'API readiness preflight failed at %s (HTTP %s, server: %s).\n' \
    "${health_url}" "${http_status:-unavailable}" "${response_server}" >&2
  [[ -n "${response_preview}" ]] && printf 'Response preview: %s\n' "${response_preview}" >&2
  if [[ "${http_status}" != "000" ]]; then
    printf 'A different process may own this host/port, especially if the server is not Kestrel.\n' >&2
    printf 'Compare IPv4 explicitly with: curl -i %s\n' "${health_url}" >&2
  fi
  printf 'Start the lab with docker compose up --build -d and inspect docker compose logs api-service.\n' >&2
  exit 1
fi

printf 'API readiness preflight passed at %s (HTTP 200, server: %s).\n' \
  "${health_url}" "${response_server}"

exec k6 run \
  -o experimental-prometheus-rw \
  --tag "testid=${test_id}" \
  "$@" \
  "$script"
