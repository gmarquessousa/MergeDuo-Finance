# MergeDuo.FixedRules

## Objetivo

Gerencia regras recorrentes, como assinaturas e despesas fixas, e permite
preview de ocorrências futuras.

## Projetos

- `src/MergeDuo.FixedRules.Api`: API, auth, métricas e Dockerfile.
- `src/MergeDuo.FixedRules.Domain`: regras de recorrência e contratos.
- `src/MergeDuo.FixedRules.Infra`: Cosmos DB e leitura de cartões.
- `tests/MergeDuo.FixedRules.Tests`: testes de domínio e fluxo HTTP.

## Endpoints

- `GET /readyz`
- `GET /fixed-rules`
- `POST /fixed-rules`
- `GET /fixed-rules/{id}`
- `PATCH /fixed-rules/{id}`
- `POST /fixed-rules/{id}/pause`
- `POST /fixed-rules/{id}/resume`
- `DELETE /fixed-rules/{id}`
- `GET /fixed-rules/{id}/preview`

## Configuração

```text
Cosmos__ConnectionString
Jwt__Issuer
Jwt__Audience
Jwt__JwksUrl
Preview__MaxMonths
Preview__BusinessCalendar
```

## Comandos

```powershell
dotnet test MergeDuo.FixedRules.sln --configuration Release
dotnet run --project src/MergeDuo.FixedRules.Api
```

## Docker

```powershell
docker build -f src/MergeDuo.FixedRules.Api/Dockerfile -t mergeduo-fixedrules:test .
```

## Dependências

Cosmos DB, JWT/JWKS do Identity e leitura de Cards para validar regras de cartão.
