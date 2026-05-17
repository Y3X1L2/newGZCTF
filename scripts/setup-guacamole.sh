#!/bin/bash
# T003: Add Guacamole services to Docker Compose
# Run from src/GZCTF/ directory
set -e
echo "Starting Guacamole services..."
docker compose -f docker-compose.yml up -d guacd guacamole 2>/dev/null || {
  echo "Guacamole not in docker-compose.yml. Run: docker run -d --name guacd -p 4822:4822 guacamole/guacd"
}
echo "Guacamole services started."
