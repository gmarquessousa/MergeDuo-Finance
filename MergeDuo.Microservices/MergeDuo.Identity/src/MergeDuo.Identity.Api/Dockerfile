# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /src

COPY MergeDuo.Identity.sln ./
COPY src/MergeDuo.Identity.Api/MergeDuo.Identity.Api.csproj       src/MergeDuo.Identity.Api/
COPY src/MergeDuo.Identity.Domain/MergeDuo.Identity.Domain.csproj src/MergeDuo.Identity.Domain/
COPY src/MergeDuo.Identity.Infra/MergeDuo.Identity.Infra.csproj   src/MergeDuo.Identity.Infra/

RUN dotnet restore src/MergeDuo.Identity.Api/MergeDuo.Identity.Api.csproj

COPY src/ src/

RUN dotnet publish src/MergeDuo.Identity.Api/MergeDuo.Identity.Api.csproj \
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

ENTRYPOINT ["dotnet", "MergeDuo.Identity.Api.dll"]
