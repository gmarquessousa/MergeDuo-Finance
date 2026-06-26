# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /src

COPY MergeDuo.Profile.sln ./
COPY src/MergeDuo.Profile.Api/MergeDuo.Profile.Api.csproj       src/MergeDuo.Profile.Api/
COPY src/MergeDuo.Profile.Domain/MergeDuo.Profile.Domain.csproj src/MergeDuo.Profile.Domain/
COPY src/MergeDuo.Profile.Infra/MergeDuo.Profile.Infra.csproj   src/MergeDuo.Profile.Infra/

RUN dotnet restore src/MergeDuo.Profile.Api/MergeDuo.Profile.Api.csproj

COPY src/ src/

RUN dotnet publish src/MergeDuo.Profile.Api/MergeDuo.Profile.Api.csproj \
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

ENTRYPOINT ["dotnet", "MergeDuo.Profile.Api.dll"]
