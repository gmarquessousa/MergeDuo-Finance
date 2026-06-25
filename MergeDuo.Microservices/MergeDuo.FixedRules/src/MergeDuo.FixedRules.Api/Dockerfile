# syntax=docker/dockerfile:1.7
# Build context MUST be the service root (MergeDuo.FixedRules/).
# Example:
#   docker build -f src/MergeDuo.FixedRules.Api/Dockerfile -t mergeduo-fixedrules:local .

# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /src

# Copy csproj/sln first to leverage Docker layer cache for restore
COPY MergeDuo.FixedRules.sln ./
COPY src/MergeDuo.FixedRules.Api/MergeDuo.FixedRules.Api.csproj       src/MergeDuo.FixedRules.Api/
COPY src/MergeDuo.FixedRules.Domain/MergeDuo.FixedRules.Domain.csproj src/MergeDuo.FixedRules.Domain/
COPY src/MergeDuo.FixedRules.Infra/MergeDuo.FixedRules.Infra.csproj   src/MergeDuo.FixedRules.Infra/

RUN dotnet restore src/MergeDuo.FixedRules.Api/MergeDuo.FixedRules.Api.csproj

# Copy the rest of the source and publish
COPY src/ src/

RUN dotnet publish src/MergeDuo.FixedRules.Api/MergeDuo.FixedRules.Api.csproj \
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

ENTRYPOINT ["dotnet", "MergeDuo.FixedRules.Api.dll"]
