#!/bin/bash

# Exit on any error
set -e

echo "--- 📥 Pulling latest changes from Git ---"
git pull

echo "--- 🛠️ Building the Sonarr-Watch Docker image ---"
# We use --no-cache for now to ensure all code changes and sqlite3 are definitely included
docker build -t sonarr-watch .

echo "--- 🚀 Starting the containers ---"
docker compose -f ~/docker/docker-compose.yml up -d --remove-orphans

echo "--- ✅ Deployment Complete! ---"
echo "You can check logs with: docker compose logs -f sonarrwatch"
