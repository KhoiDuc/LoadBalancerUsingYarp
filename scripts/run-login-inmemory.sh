#!/usr/bin/env bash
# DEMO LOGIN - IN-MEMORY: 2 backend SimpleLogin (key rieng tung node) + LB (5266).
# Ky vong: login o 1 node -> qua node khac MAT dang nhap (key khong chia se).
set -euo pipefail
cd "$(dirname "$0")/.."

echo ">> dotnet build..."
dotnet build -v quiet

echo ">> Start node1 (5001) & node2 (5002) - SimpleLogin IN-MEMORY"
dotnet run --project src/Backend.SimpleLogin.InMemory --no-launch-profile -- --NodeName node1 --urls http://localhost:5001 > node1.log 2>&1 &
N1=$!
dotnet run --project src/Backend.SimpleLogin.InMemory --no-launch-profile -- --NodeName node2 --urls http://localhost:5002 > node2.log 2>&1 &
N2=$!

cleanup() { echo; echo ">> Stopping backends..."; kill $N1 $N2 2>/dev/null || true; }
trap cleanup EXIT

sleep 4
echo ">> Load Balancer -> http://localhost:5266  (Ctrl+C de dung)"
echo "   Mo trinh duyet, bam Login, xem status doi theo node"
dotnet run --project src/LoadBalancer --no-launch-profile -- --urls http://localhost:5266
