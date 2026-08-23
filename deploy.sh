#!/bin/bash

# Pull-based deploy: fetch the latest code and prebuilt image, recreate the container.
# No local build needed — GitHub Actions builds and publishes the image to GHCR.
set -e

cd "$(dirname "$0")"

echo "--- Pulling latest code ---"
git pull

echo "--- Pulling latest image from GHCR ---"
docker compose -f ~/docker/docker-compose.yml pull sonarrwatch

echo "--- Recreating containers ---"
docker compose -f ~/docker/docker-compose.yml up -d --remove-orphans sonarrwatch

echo "--- Deployment complete ---"
echo "Logs: docker compose -f ~/docker/docker-compose.yml logs -f sonarrwatch"
