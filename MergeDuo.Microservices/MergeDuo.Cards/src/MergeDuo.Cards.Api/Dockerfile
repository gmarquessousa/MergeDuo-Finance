# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /src

COPY MergeDuo.Cards.sln ./
COPY src/MergeDuo.Cards.Api/MergeDuo.Cards.Api.csproj       src/MergeDuo.Cards.Api/
COPY src/MergeDuo.Cards.Domain/MergeDuo.Cards.Domain.csproj src/MergeDuo.Cards.Domain/
COPY src/MergeDuo.Cards.Infra/MergeDuo.Cards.Infra.csproj   src/MergeDuo.Cards.Infra/

RUN dotnet restore src/MergeDuo.Cards.Api/MergeDuo.Cards.Api.csproj

COPY src/ src/

RUN dotnet publish src/MergeDuo.Cards.Api/MergeDuo.Cards.Api.csproj \
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

ENTRYPOINT ["dotnet", "MergeDuo.Cards.Api.dll"]
