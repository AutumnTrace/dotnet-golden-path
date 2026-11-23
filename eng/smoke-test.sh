#!/usr/bin/env bash
# End-to-end smoke test against a running service (docker compose, kind, or dotnet run).
#
#   eng/smoke-test.sh http://localhost:8080 jwt    # gets a token from POST /dev/tokens (Development only)
#   eng/smoke-test.sh http://localhost:8080 none
#
# Waits for readiness, then places an order and reads it back.
set -euo pipefail

base=${1:?usage: smoke-test.sh <base-url> <jwt|none>}
auth=${2:?usage: smoke-test.sh <base-url> <jwt|none>}

echo "Waiting for $base/health/ready ..."
for _ in $(seq 1 90); do
  if [[ "$(curl -s -o /dev/null -w '%{http_code}' "$base/health/ready" || true)" == "200" ]]; then
    break
  fi
  sleep 2
done
curl -fsS "$base/health/ready"
echo

headers=(-H "Content-Type: application/json")
if [[ "$auth" == "jwt" ]]; then
  status=$(curl -s -o /dev/null -w '%{http_code}' "$base/v1/orders")
  [[ "$status" == "401" ]] || { echo "Expected 401 without a token, got $status"; exit 1; }
  token=$(curl -fsS -X POST "$base/dev/tokens" "${headers[@]}" -d '{}' | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')
  headers+=(-H "Authorization: Bearer $token")
fi

order=$(curl -fsS -X POST "$base/v1/orders" "${headers[@]}" \
  -d '{"customerId":"cust-42","currency":"EUR","lines":[{"sku":"SKU-1","quantity":2,"unitPriceMinor":1250}]}')
echo "$order"
id=$(echo "$order" | sed -E 's/.*"id":"([^"]+)".*/\1/')

curl -fsS "$base/v1/orders/$id" "${headers[@]}" | grep -q '"totalMinor":2500'
echo "Smoke test passed: order $id placed and read back."
