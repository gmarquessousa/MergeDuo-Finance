# syntax=docker/dockerfile:1.7
# Build context MUST be the service root (MergeDuo.Identity/).
# Example:
#   docker build -f src/MergeDuo.Identity.Api/Dockerfile -t mergeduo-identity:local .

# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /src

# Copy csproj/sln first to leverage Docker layer cache for restore
COPY MergeDuo.Identity.sln ./
COPY src/MergeDuo.Identity.Api/MergeDuo.Identity.Api.csproj       src/MergeDuo.Identity.Api/
COPY src/MergeDuo.Identity.Domain/MergeDuo.Identity.Domain.csproj src/MergeDuo.Identity.Domain/
COPY src/MergeDuo.Identity.Infra/MergeDuo.Identity.Infra.csproj   src/MergeDuo.Identity.Infra/

RUN dotnet restore src/MergeDuo.Identity.Api/MergeDuo.Identity.Api.csproj

# Copy the rest of the source and publish
COPY src/ src/

RUN dotnet publish src/MergeDuo.Identity.Api/MergeDuo.Identity.Api.csproj \
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

ENTRYPOINT ["dotnet", "MergeDuo.Identity.Api.dll"]
