#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/../.."
exec ./run.sh scenarios/narrow-lookup/query.sql
