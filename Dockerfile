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

# Allow all users access to this so we can run the container as non-root.
RUN chmod -R 775 /app
USER root

ENTRYPOINT ["dotnet", "DD_Bot.Bot.dll"]