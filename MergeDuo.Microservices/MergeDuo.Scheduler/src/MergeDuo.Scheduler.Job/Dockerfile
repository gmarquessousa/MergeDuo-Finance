# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /src

COPY MergeDuo.Scheduler.sln ./
COPY src/MergeDuo.Scheduler.Job/MergeDuo.Scheduler.Job.csproj src/MergeDuo.Scheduler.Job/

RUN dotnet restore src/MergeDuo.Scheduler.Job/MergeDuo.Scheduler.Job.csproj

COPY src/ src/

RUN dotnet publish src/MergeDuo.Scheduler.Job/MergeDuo.Scheduler.Job.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0-jammy AS final
WORKDIR /app

ENV DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish ./

USER $APP_UID

ENTRYPOINT ["dotnet", "MergeDuo.Scheduler.Job.dll"]
