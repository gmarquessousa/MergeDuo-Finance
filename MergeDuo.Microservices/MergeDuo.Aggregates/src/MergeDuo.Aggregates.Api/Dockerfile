# syntax=docker/dockerfile:1.7
# Build context MUST be the service root (MergeDuo.Aggregates/).
# Example:
#   docker build -f src/MergeDuo.Aggregates.Api/Dockerfile -t mergeduo-aggregates:local .

# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /src

# Copy csproj/sln first to leverage Docker layer cache for restore
COPY MergeDuo.Aggregates.sln ./
COPY src/MergeDuo.Aggregates.Api/MergeDuo.Aggregates.Api.csproj      src/MergeDuo.Aggregates.Api/
COPY src/MergeDuo.Aggregates.Domain/MergeDuo.Aggregates.Domain.csproj src/MergeDuo.Aggregates.Domain/
COPY src/MergeDuo.Aggregates.Infra/MergeDuo.Aggregates.Infra.csproj   src/MergeDuo.Aggregates.Infra/

RUN dotnet restore src/MergeDuo.Aggregates.Api/MergeDuo.Aggregates.Api.csproj

# Copy the rest of the source and publish
COPY src/ src/

RUN dotnet publish src/MergeDuo.Aggregates.Api/MergeDuo.Aggregates.Api.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy AS final
WORKDIR /app

# ACA expects the container to listen on its configured target port (commonly 8080).
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

COPY --from=build /app/publish ./

USER $APP_UID

ENTRYPOINT ["dotnet", "MergeDuo.Aggregates.Api.dll"]
