#!/usr/bin/env bash
# Luong REDIS: Redis + 2 backend share state (node1:5001, node2:5002) + load balancer (5266).
set -euo pipefail
cd "$(dirname "$0")/.."

echo ">> Start Redis (docker compose)..."
docker compose up -d

echo ">> dotnet build..."
dotnet build -v quiet

echo ">> Start node1 (5001) & node2 (5002)"
dotnet run --project src/Backend.Redis --no-launch-profile -- --NodeName node1 --urls http://localhost:5001 > node1.log 2>&1 &
N1=$!
dotnet run --project src/Backend.Redis --no-launch-profile -- --NodeName node2 --urls http://localhost:5002 > node2.log 2>&1 &
N2=$!

cleanup() { echo; echo ">> Stopping backends..."; kill $N1 $N2 2>/dev/null || true; }
trap cleanup EXIT

sleep 4
echo ">> Load Balancer -> http://localhost:5266  (Ctrl+C de dung; Redis van chay)"
dotnet run --project src/LoadBalancer --no-launch-profile -- --urls http://localhost:5266
