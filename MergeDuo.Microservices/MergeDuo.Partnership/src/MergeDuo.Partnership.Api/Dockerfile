# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /src

COPY MergeDuo.Partnership.sln ./
COPY src/MergeDuo.Partnership.Api/MergeDuo.Partnership.Api.csproj       src/MergeDuo.Partnership.Api/
COPY src/MergeDuo.Partnership.Domain/MergeDuo.Partnership.Domain.csproj src/MergeDuo.Partnership.Domain/
COPY src/MergeDuo.Partnership.Infra/MergeDuo.Partnership.Infra.csproj   src/MergeDuo.Partnership.Infra/

RUN dotnet restore src/MergeDuo.Partnership.Api/MergeDuo.Partnership.Api.csproj

COPY src/ src/

RUN dotnet publish src/MergeDuo.Partnership.Api/MergeDuo.Partnership.Api.csproj \
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

ENTRYPOINT ["dotnet", "MergeDuo.Partnership.Api.dll"]
