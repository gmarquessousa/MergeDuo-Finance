# MergeDuo.Profile

## Objetivo

Expõe leitura de perfis públicos, busca por handle e estatísticas do usuário
autenticado.

## Projetos

- `src/MergeDuo.Profile.Api`: API, auth, métricas e Dockerfile.
- `src/MergeDuo.Profile.Domain`: regras de handle, estatísticas e mapeamento.
- `src/MergeDuo.Profile.Infra`: repositórios Cosmos DB.
- `tests/MergeDuo.Profile.Tests`: testes de domínio e fluxo HTTP.

## Endpoints

- `GET /readyz`
- `GET /users/by-handle/{handle}`
- `GET /users/{userId}`
- `GET /me/stats`

## Configuração

```text
Cosmos__ConnectionString
Jwt__Issuer
Jwt__Audience
Jwt__JwksUrl
Stats__StaleAfterMinutes
Stats__DependencyTimeoutSeconds
```

## Comandos

```powershell
dotnet test MergeDuo.Profile.sln --configuration Release
dotnet run --project src/MergeDuo.Profile.Api
```

## Docker

```powershell
docker build -f src/MergeDuo.Profile.Api/Dockerfile -t mergeduo-profile:test .
```

## Dependências

Cosmos DB e JWT/JWKS do Identity.
