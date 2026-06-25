# syntax=docker/dockerfile:1.7
# Build context MUST be the service root (MergeDuo.Cards/).
# Example:
#   docker build -f src/MergeDuo.Cards.Api/Dockerfile -t mergeduo-cards:local .

# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /src

# Copy csproj/sln first to leverage Docker layer cache for restore
COPY MergeDuo.Cards.sln ./
COPY src/MergeDuo.Cards.Api/MergeDuo.Cards.Api.csproj       src/MergeDuo.Cards.Api/
COPY src/MergeDuo.Cards.Domain/MergeDuo.Cards.Domain.csproj src/MergeDuo.Cards.Domain/
COPY src/MergeDuo.Cards.Infra/MergeDuo.Cards.Infra.csproj   src/MergeDuo.Cards.Infra/

RUN dotnet restore src/MergeDuo.Cards.Api/MergeDuo.Cards.Api.csproj

# Copy the rest of the source and publish
COPY src/ src/

RUN dotnet publish src/MergeDuo.Cards.Api/MergeDuo.Cards.Api.csproj \
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

ENTRYPOINT ["dotnet", "MergeDuo.Cards.Api.dll"]
