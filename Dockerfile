# Build stage
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src

# Copy the project files and restore any dependencies
COPY src/DD_Bot.Bot/DD_Bot.Bot.csproj DD_Bot.Bot/
COPY src/DD_Bot.Application/DD_Bot.Application.csproj DD_Bot.Application/
COPY src/DD_Bot.Domain/DD_Bot.Domain.csproj DD_Bot.Domain/
RUN dotnet restore "DD_Bot.Bot/DD_Bot.Bot.csproj"

# Copy the entire project directories
COPY src/DD_Bot.Bot/ DD_Bot.Bot/
COPY src/DD_Bot.Application/ DD_Bot.Application/
COPY src/DD_Bot.Domain/ DD_Bot.Domain/

WORKDIR "/src/DD_Bot.Bot"
RUN dotnet build "DD_Bot.Bot.csproj" -c Release -o /app/build
RUN dotnet publish "DD_Bot.Bot.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:6.0
WORKDIR /app
COPY --from=build /app/publish .

# Allow all users access to this so we can run the container as non-root.
RUN chmod -R 775 /app
USER root

ENTRYPOINT ["dotnet", "DD_Bot.Bot.dll"]