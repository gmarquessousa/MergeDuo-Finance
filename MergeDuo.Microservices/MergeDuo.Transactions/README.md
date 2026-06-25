# MergeDuo.Transactions

## Objetivo

Gerencia lançamentos financeiros, parcelas, tags, grupos e endpoint interno de
criação usado pelo Scheduler.

## Projetos

- `src/MergeDuo.Transactions.Api`: API, auth, endpoint interno, métricas e
  Dockerfile.
- `src/MergeDuo.Transactions.Domain`: regras, contratos e documentos.
- `src/MergeDuo.Transactions.Infra`: Cosmos DB e leituras auxiliares.
- `tests/MergeDuo.Transactions.Tests`: testes de domínio e fluxo HTTP.

## Endpoints

- `GET /readyz`
- `GET /transactions`
- `POST /transactions`
- `GET /transactions/tags`
- `GET /transactions/tags/suggestions`
- `GET /transactions/groups/{groupId}`
- `DELETE /transactions/groups/{groupId}`
- `GET /transactions/{id}`
- `PATCH /transactions/{id}`
- `DELETE /transactions/{id}`
- `GET /internal/transactions/card-usage`
- `POST /internal/scheduler/transactions`

## Configuração

```text
Cosmos__ConnectionString
Jwt__Issuer
Jwt__Audience
Jwt__JwksUrl
Transactions__ContinuationTokenSecret
InternalApi__SchedulerKey
```

## Comandos

```powershell
dotnet test MergeDuo.Transactions.sln --configuration Release
dotnet run --project src/MergeDuo.Transactions.Api
```

## Docker

```powershell
docker build -f src/MergeDuo.Transactions.Api/Dockerfile -t mergeduo-transactions:test .
```

## Dependências

Cosmos DB, JWT/JWKS do Identity, Cards, FixedRules e Partnership para validações
e leituras auxiliares.
