#!/bin/bash

# Redis Connection Test Script for Railway
echo "🔍 Testing Redis connection in Railway environment..."

# Check if Redis service is accessible
echo "📡 Checking Redis service availability..."

# Try to connect to Redis using redis-cli if available
if command -v redis-cli &> /dev/null; then
    echo "🧪 Testing with redis-cli..."
    redis-cli -h redis.railway.internal -p 6379 -a jiJceFvaWIjdTAepEepOTppPdLOEEsCo ping
else
    echo "⚠️ redis-cli not available"
fi

# Check network connectivity
echo "📡 Testing network connectivity to Redis host..."
nc -zv redis.railway.internal 6379 2>&1 || echo "❌ Cannot reach Redis host"

# Try with different connection methods
echo "🔄 Testing different Redis URL formats..."
echo "URL 1: redis://default:jiJceFvaWIjdTAepEepOTppPdLOEEsCo@redis.railway.internal:6379"
echo "URL 2: redis.railway.internal:6379"

echo "✅ Test script completed"
