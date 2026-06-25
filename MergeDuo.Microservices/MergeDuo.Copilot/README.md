# MergeDuo.Copilot

## Objetivo

Expor dados financeiros consolidados e simulações para consumo por copilots ou
ferramentas externas controladas.

## Projetos

- `src/MergeDuo.Copilot.Api`: API pública do Copilot, Swagger e métricas.
- `src/MergeDuo.Copilot.Domain`: regras de leitura, simulação e contratos.
- `src/MergeDuo.Copilot.Infra`: repositório Cosmos DB.
- `tests/MergeDuo.Copilot.Tests`: testes de API e repositório fake.

## Endpoints

- `GET /`
- `GET /startupz`
- `GET /readyz`
- `GET /copilot/month-summary/{year}/{month}`
- `GET /copilot/next-three-months`
- `GET /copilot/cards`
- `POST /copilot/purchase-simulation`

## Configuração

```text
Cosmos__ConnectionString
Copilot__UserId
Copilot__BusinessTimeZone
Copilot__ProjectionMonths
Copilot__MaxSimulationInstallments
Copilot__SafetyMarginAmount
Cors__AllowedOrigins__0
```

## Comandos

```powershell
dotnet test MergeDuo.Copilot.sln --configuration Release
dotnet run --project src/MergeDuo.Copilot.Api
```

## Docker

```powershell
docker build -f src/MergeDuo.Copilot.Api/Dockerfile -t mergeduo-copilot:test .
```

## Dependências

Cosmos DB com dados de agregados, cartões e simulações financeiras.
