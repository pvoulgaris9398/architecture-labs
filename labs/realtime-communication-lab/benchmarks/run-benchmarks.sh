#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
lab_dir="$(cd "$script_dir/.." && pwd)"
run_id="${BENCHMARK_RUN_ID:-$(date -u +%Y%m%dT%H%M%SZ)}"
results_dir="$script_dir/results/local/$run_id"
configuration="Release"
warmup_seconds="${BENCHMARK_WARMUP_SECONDS:-5}"
duration_seconds="${BENCHMARK_DURATION_SECONDS:-20}"
drain_seconds="${BENCHMARK_DRAIN_SECONDS:-5}"
repetitions="${BENCHMARK_REPETITIONS:-2}"
payload_bytes="${BENCHMARK_PAYLOAD_BYTES:-256}"
read -r -a subscriber_tiers <<< "${BENCHMARK_SUBSCRIBERS:-10 100}"
read -r -a rates <<< "${BENCHMARK_RATES:-10 100}"
transports=(websocket sse long-polling)

mkdir -p "$results_dir"
printf '%s\n' \
  "run_id=$run_id" \
  "created_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  "warmup_seconds=$warmup_seconds" \
  "duration_seconds=$duration_seconds" \
  "drain_seconds=$drain_seconds" \
  "repetitions=$repetitions" \
  "payload_bytes=$payload_bytes" \
  "subscriber_tiers=${subscriber_tiers[*]}" \
  "rates=${rates[*]}" \
  > "$results_dir/session.txt"
dotnet build "$script_dir/RealtimeBenchmarks.slnx" --configuration "$configuration"

current_transport=""

stop_current_transport() {
  if [[ -n "$current_transport" ]]; then
    docker compose \
      --file "$lab_dir/transports/$current_transport/docker-compose.yaml" \
      down
    current_transport=""
  fi
}

trap stop_current_transport EXIT INT TERM

base_url_for() {
  case "$1" in
    websocket) echo "http://127.0.0.1:5000" ;;
    sse) echo "http://127.0.0.1:5001" ;;
    long-polling) echo "http://127.0.0.1:5002" ;;
    *) return 1 ;;
  esac
}

for subscribers in "${subscriber_tiers[@]}"; do
  for rate in "${rates[@]}"; do
    for ((repetition = 1; repetition <= repetitions; repetition++)); do
      rotation=$(((repetition - 1) % ${#transports[@]}))
      for ((offset = 0; offset < ${#transports[@]}; offset++)); do
        index=$(((rotation + offset) % ${#transports[@]}))
        transport="${transports[$index]}"
        base_url="$(base_url_for "$transport")"
        output="$results_dir/${transport}-s${subscribers}-r${rate}-run${repetition}.json"
        if [[ -s "$output" ]]; then
          echo "Skipping completed result: $output"
          continue
        fi

        current_transport="$transport"
        docker compose \
          --file "$lab_dir/transports/$transport/docker-compose.yaml" \
          up --build --detach server

        ready=0
        for _ in {1..60}; do
          if curl --fail --silent --show-error "$base_url/" >/dev/null; then
            ready=1
            break
          fi
          sleep 1
        done
        if [[ "$ready" -ne 1 ]]; then
          echo "Server did not become ready: $transport" >&2
          exit 1
        fi

        set +e
        dotnet run \
          --project "$script_dir/src/LoadGenerator/LoadGenerator.csproj" \
          --configuration "$configuration" \
          --no-build \
          -- \
          --transport "$transport" \
          --base-url "$base_url" \
          --subscribers "$subscribers" \
          --rate "$rate" \
          --payload-bytes "$payload_bytes" \
          --warmup-seconds "$warmup_seconds" \
          --duration-seconds "$duration_seconds" \
          --drain-seconds "$drain_seconds" \
          --container-name "architecture-labs-realtime-${transport}-server" \
          --output "$output"
        run_status=$?
        set -e

        if [[ "$run_status" -eq 3 && -s "$output" ]]; then
          invalid_output="${output%.json}-schedule-invalid-$(date -u +%H%M%S).json"
          mv "$output" "$invalid_output"
          echo "Publisher schedule was invalid; preserved $invalid_output" >&2
        fi
        if [[ "$run_status" -ne 0 ]]; then
          exit "$run_status"
        fi

        stop_current_transport
      done
    done
  done
done

dotnet run \
  --project "$script_dir/src/LoadGenerator/LoadGenerator.csproj" \
  --configuration "$configuration" \
  --no-build \
  -- \
  --summarize "$results_dir"
