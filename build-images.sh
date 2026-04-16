#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVICES_DIR="$SCRIPT_DIR/services"

echo "Building archcraft Docker images..."

cd "$SERVICES_DIR"

docker build -f synthetic/Dockerfile        -t archcraft/synthetic:latest      synthetic/
docker build -f adapters/PgAdapter/Dockerfile    -t archcraft/pg-adapter:latest    adapters/
docker build -f adapters/RedisAdapter/Dockerfile -t archcraft/redis-adapter:latest adapters/
docker build -f adapters/HttpAdapter/Dockerfile  -t archcraft/http-adapter:latest  adapters/
docker build -f adapters/KafkaAdapter/Dockerfile -t archcraft/kafka-adapter:latest adapters/

echo ""
echo "Done. Images built:"
docker images | grep "^archcraft"
