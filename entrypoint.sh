#!/bin/sh
# entrypoint.sh

# Get the group ID of /var/run/docker.sock (if it exists)
DOCKER_GID=$(stat -c '%g' /var/run/docker.sock 2>/dev/null || echo 0)

# If we retrieved a non-zero value, modify the docker group's GID.
if [ "$DOCKER_GID" -ne "0" ]; then
    echo "Setting docker group GID to $DOCKER_GID"
    # Adjust the docker group's GID. If the group already exists, this updates it.
    groupmod -g "$DOCKER_GID" docker 2>/dev/null || true
fi

# Adjust ownership of /app if necessary
chown -R nonroot:docker /app

# Execute the actual application as the non-root user
exec su nonroot -c "dotnet DD_Bot.Bot.dll"
