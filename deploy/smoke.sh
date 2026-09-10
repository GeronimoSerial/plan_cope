#!/bin/sh
set -eu

: "${POSTGRES_PASSWORD:?POSTGRES_PASSWORD is required}"
: "${ConnectionStrings__CentralDatabase:?ConnectionStrings__CentralDatabase is required}"
: "${Auth__SigningKey:?Auth__SigningKey is required}"
: "${GeApi__Username:?GeApi__Username is required}"
: "${GeApi__Password:?GeApi__Password is required}"

compose_file="$(dirname "$0")/compose.ci.yml"

cleanup() {
  docker compose -f "$compose_file" down --volumes --remove-orphans
}
trap cleanup EXIT INT TERM

docker compose -f "$compose_file" up --detach --build

attempt=0
until docker compose -f "$compose_file" exec -T api \
  curl --fail --silent http://localhost:8080/health/ready >/dev/null; do
  attempt=$((attempt + 1))
  if [ "$attempt" -ge 30 ]; then
    docker compose -f "$compose_file" ps
    docker compose -f "$compose_file" logs api migrate postgres
    exit 1
  fi
  sleep 2
done

docker compose -f "$compose_file" ps
