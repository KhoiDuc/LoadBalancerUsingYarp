#!/usr/bin/env bash
# Luong IN-MEMORY: 2 backend (node1:5001, node2:5002) + load balancer (5266). Khong can Redis.
set -euo pipefail
cd "$(dirname "$0")/.."

echo ">> dotnet build..."
dotnet build -v quiet

echo ">> Start node1 (5001) & node2 (5002)"
dotnet run --project src/Backend.InMemory --no-launch-profile -- --NodeName node1 --urls http://localhost:5001 > node1.log 2>&1 &
N1=$!
dotnet run --project src/Backend.InMemory --no-launch-profile -- --NodeName node2 --urls http://localhost:5002 > node2.log 2>&1 &
N2=$!

cleanup() { echo; echo ">> Stopping backends..."; kill $N1 $N2 2>/dev/null || true; }
trap cleanup EXIT

sleep 4
echo ">> Load Balancer -> http://localhost:5266  (Ctrl+C de dung)"
dotnet run --project src/LoadBalancer --no-launch-profile -- --urls http://localhost:5266
