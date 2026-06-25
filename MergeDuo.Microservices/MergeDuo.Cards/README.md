# MergeDuo.Cards

## Objetivo

Gerencia cartões de crédito, remoção lógica e consulta de uso/fatura a partir
das transações.

## Projetos

- `src/MergeDuo.Cards.Api`: API, auth, métricas e Dockerfile.
- `src/MergeDuo.Cards.Domain`: regras e contratos de cartões.
- `src/MergeDuo.Cards.Infra`: Cosmos DB e cliente de uso em Transactions.
- `tests/MergeDuo.Cards.Tests`: testes de domínio e fluxo HTTP.

## Endpoints

- `GET /readyz`
- `GET /cards`
- `POST /cards`
- `GET /cards/{id}`
- `PATCH /cards/{id}`
- `DELETE /cards/{id}`
- `GET /cards/{id}/usage`

## Configuração

```text
Cosmos__ConnectionString
Jwt__Issuer
Jwt__Audience
Jwt__JwksUrl
TransactionsService__BaseUrl
TransactionsService__TimeoutSeconds
```

## Comandos

```powershell
dotnet test MergeDuo.Cards.sln --configuration Release
dotnet run --project src/MergeDuo.Cards.Api
```

## Docker

```powershell
docker build -f src/MergeDuo.Cards.Api/Dockerfile -t mergeduo-cards:test .
```

## Dependências

Cosmos DB, JWT/JWKS do Identity e Transactions para calcular uso de cartão.
