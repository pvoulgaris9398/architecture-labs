#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/../.."
exec ./run.sh scenarios/ordered-build-quality/query.sql
