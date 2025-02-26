# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/ .

WORKDIR /src/DD_Bot.Bot
RUN dotnet publish DD_Bot.Bot.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .

# Install necessary packages
RUN apt-get update && apt-get install -y procps gawk

# Create a docker group with a default GID (this will be modified at runtime)
RUN groupadd -g 999 docker

# Create non-root user and add them to the docker group
RUN adduser --disabled-password --gecos "" nonroot && \
    usermod -aG docker nonroot

# Copy the entrypoint script into the image
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# Optional: Ensure the /app folder has the right permissions
RUN chown -R nonroot:docker /app && chmod -R 775 /app

# Use the entrypoint script
ENTRYPOINT ["/entrypoint.sh"]