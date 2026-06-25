# syntax=docker/dockerfile:1.7
# Build context MUST be the service root (MergeDuo.Transactions/).
# Example:
#   docker build -f src/MergeDuo.Transactions.Api/Dockerfile -t mergeduo-transactions:local .

# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /src

# Copy csproj/sln first to leverage Docker layer cache for restore
COPY MergeDuo.Transactions.sln ./
COPY src/MergeDuo.Transactions.Api/MergeDuo.Transactions.Api.csproj       src/MergeDuo.Transactions.Api/
COPY src/MergeDuo.Transactions.Domain/MergeDuo.Transactions.Domain.csproj src/MergeDuo.Transactions.Domain/
COPY src/MergeDuo.Transactions.Infra/MergeDuo.Transactions.Infra.csproj   src/MergeDuo.Transactions.Infra/

RUN dotnet restore src/MergeDuo.Transactions.Api/MergeDuo.Transactions.Api.csproj

# Copy the rest of the source and publish
COPY src/ src/

RUN dotnet publish src/MergeDuo.Transactions.Api/MergeDuo.Transactions.Api.csproj \
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

ENTRYPOINT ["dotnet", "MergeDuo.Transactions.Api.dll"]
