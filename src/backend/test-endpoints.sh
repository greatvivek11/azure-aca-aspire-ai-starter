#!/bin/bash

# Test script for backend endpoints

echo "Starting backend service..."
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"
ASPNETCORE_URLS=http://localhost:8080 dotnet run --no-launch-profile > /dev/null 2>&1 &
BACKEND_PID=$!

# Wait for service to start
sleep 5

echo "Testing health endpoint..."
curl -s http://localhost:8080/v1/health | grep -q '"status":"Healthy"' && echo "✅ Health endpoint working" || echo "❌ Health endpoint failed"

echo "Testing AI ping endpoint..."
HTTP_STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:8080/v1/ping-ai)
if [ "$HTTP_STATUS" -eq 503 ]; then
    echo "✅ AI ping endpoint working (returns 503 without valid API key)"
elif [ "$HTTP_STATUS" -eq 200 ]; then
    echo "✅ AI ping endpoint working (connected to AI service)"
else
    echo "❌ AI ping endpoint failed (HTTP $HTTP_STATUS)"
fi

# Kill the backend service
kill $BACKEND_PID > /dev/null 2>&1

echo "Test complete."