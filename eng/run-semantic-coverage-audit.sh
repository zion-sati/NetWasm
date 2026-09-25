#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

node compiler-qualification/audit-cil-coverage.mjs
node compiler-qualification/audit-cil-coverage.test.mjs
node --test --experimental-test-coverage \
  --test-coverage-include='compiler-qualification/semantic-coverage-validator.mjs' \
  --test-coverage-lines=100 --test-coverage-branches=100 --test-coverage-functions=100 \
  compiler-qualification/semantic-coverage-validator.test.mjs
