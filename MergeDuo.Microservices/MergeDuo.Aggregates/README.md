# MergeDuo.Aggregates

## Objetivo

Calcula e expõe agregados mensais e anuais para o frontend, reduzindo o custo de
recalcular totais a partir de todas as transações em cada tela.

## Projetos

- `src/MergeDuo.Aggregates.Api`: API, auth, change feed e métricas.
- `src/MergeDuo.Aggregates.Domain`: regras de agregação e contratos.
- `src/MergeDuo.Aggregates.Infra`: repositórios Cosmos DB.
- `tests/MergeDuo.Aggregates.Tests`: testes de domínio, recomputação e fluxo
  HTTP.

## Endpoints

- `GET /readyz`
- `GET /aggregates/me/{year}/{month}`
- `GET /aggregates/me/year/{year}`
- `GET /aggregates/{userId}/{year}/{month}`
- `GET /aggregates/{userId}/year/{year}`
- `POST /aggregates/me/backfill/{year}`
- `POST /aggregates/me/backfill/{year}/{month}`

## Configuração

Use variáveis de ambiente para runtime:

```text
Cosmos__ConnectionString
Jwt__Issuer
Jwt__Audience
Jwt__JwksUrl
Aggregates__SourceVersion
ChangeFeed__Enabled
```

## Comandos

```powershell
dotnet test MergeDuo.Aggregates.sln --configuration Release
dotnet run --project src/MergeDuo.Aggregates.Api
```

## Docker

```powershell
docker build -f src/MergeDuo.Aggregates.Api/Dockerfile -t mergeduo-aggregates:test .
```

## Dependências

Cosmos DB, JWT/JWKS do Identity, dados de Transactions, Partnership,
FixedRules, Cards e Users.
