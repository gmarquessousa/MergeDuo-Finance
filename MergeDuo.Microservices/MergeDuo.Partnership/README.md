# MergeDuo.Partnership

## Objetivo

Gerencia convites, aceite, revogação, pausa e encerramento de parcerias entre
usuários.

## Projetos

- `src/MergeDuo.Partnership.Api`: API, auth, métricas e Dockerfile.
- `src/MergeDuo.Partnership.Domain`: workflow, regras, contratos e documentos.
- `src/MergeDuo.Partnership.Infra`: Cosmos DB e serialização.
- `tests/MergeDuo.Partnership.Tests`: testes de domínio, serialização,
  exception handler e fluxo HTTP.

## Endpoints

- `GET /readyz`
- `POST /invites`
- `GET /invites/{token}`
- `POST /invites/{token}/accept`
- `POST /invites/{token}/revoke`
- `GET /partnerships/me`
- `POST /partnerships/{id}/pause`
- `POST /partnerships/{id}/end`

## Configuração

```text
Cosmos__ConnectionString
Jwt__Issuer
Jwt__Audience
Jwt__JwksUrl
PublicApp__InviteBaseUrl
Invite__ExpiresAfterHours
Invite__TokenEntropyBytes
```

## Comandos

```powershell
dotnet test MergeDuo.Partnership.sln --configuration Release
dotnet run --project src/MergeDuo.Partnership.Api
```

## Docker

```powershell
docker build -f src/MergeDuo.Partnership.Api/Dockerfile -t mergeduo-partnership:test .
```

## Dependências

Cosmos DB, JWT/JWKS do Identity e URL pública do frontend para links de convite.
