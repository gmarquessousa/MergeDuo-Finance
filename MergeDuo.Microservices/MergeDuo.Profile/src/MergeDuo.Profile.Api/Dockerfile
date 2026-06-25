# syntax=docker/dockerfile:1.7
# Build context MUST be the service root (MergeDuo.Profile/).
# Example:
#   docker build -f src/MergeDuo.Profile.Api/Dockerfile -t mergeduo-profile:local .

# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /src

# Copy csproj/sln first to leverage Docker layer cache for restore
COPY MergeDuo.Profile.sln ./
COPY src/MergeDuo.Profile.Api/MergeDuo.Profile.Api.csproj       src/MergeDuo.Profile.Api/
COPY src/MergeDuo.Profile.Domain/MergeDuo.Profile.Domain.csproj src/MergeDuo.Profile.Domain/
COPY src/MergeDuo.Profile.Infra/MergeDuo.Profile.Infra.csproj   src/MergeDuo.Profile.Infra/

RUN dotnet restore src/MergeDuo.Profile.Api/MergeDuo.Profile.Api.csproj

# Copy the rest of the source and publish
COPY src/ src/

RUN dotnet publish src/MergeDuo.Profile.Api/MergeDuo.Profile.Api.csproj \
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

ENTRYPOINT ["dotnet", "MergeDuo.Profile.Api.dll"]
