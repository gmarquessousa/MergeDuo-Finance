# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /src

COPY MergeDuo.Aggregates.sln ./
COPY src/MergeDuo.Aggregates.Api/MergeDuo.Aggregates.Api.csproj      src/MergeDuo.Aggregates.Api/
COPY src/MergeDuo.Aggregates.Domain/MergeDuo.Aggregates.Domain.csproj src/MergeDuo.Aggregates.Domain/
COPY src/MergeDuo.Aggregates.Infra/MergeDuo.Aggregates.Infra.csproj   src/MergeDuo.Aggregates.Infra/

RUN dotnet restore src/MergeDuo.Aggregates.Api/MergeDuo.Aggregates.Api.csproj

COPY src/ src/

RUN dotnet publish src/MergeDuo.Aggregates.Api/MergeDuo.Aggregates.Api.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

COPY --from=build /app/publish ./

USER $APP_UID

ENTRYPOINT ["dotnet", "MergeDuo.Aggregates.Api.dll"]
