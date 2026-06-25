# MergeDuo.Identity

## Objetivo

Centraliza autenticação, emissão de JWT, refresh token, descoberta JWKS, dados do
usuário autenticado e upload de avatar.

## Projetos

- `src/MergeDuo.Identity.Api`: API, auth Google, JWKS, usuário e avatar.
- `src/MergeDuo.Identity.Domain`: regras de identidade, handle e documentos.
- `src/MergeDuo.Identity.Infra`: Cosmos DB, JWT, Google token validation e Blob
  Storage.
- `tools/MergeDuo.Identity.ReservationsBackfill`: ferramenta de backfill de
  reservas de identidade.
- `tests/MergeDuo.Identity.Tests`: testes de domínio, Google token e fluxo HTTP.

## Endpoints

- `GET /readyz`
- `GET /auth/google/challenge`
- `POST /auth/google/redirect/start`
- `POST /auth/google/callback`
- `POST /auth/google/redirect-callback`
- `POST /auth/refresh`
- `POST /auth/logout`
- `GET /users/me`
- `PATCH /users/me`
- `POST /users/me/avatar`
- `DELETE /users/me`
- `GET /.well-known/openid-configuration`
- `GET /.well-known/jwks.json`

## Configuração

```text
Cosmos__ConnectionString
Google__ClientId
Jwt__Issuer
Jwt__Audience
Jwt__KeyId
Jwt__PrivateKeyPem
RefreshTokens__Pepper
PublicApp__BaseUrl
BlobStorage__ConnectionString
BlobStorage__AccountUrl
```

## Comandos

```powershell
dotnet test MergeDuo.Identity.sln --configuration Release
dotnet run --project src/MergeDuo.Identity.Api
```

## Docker

```powershell
docker build -f src/MergeDuo.Identity.Api/Dockerfile -t mergeduo-identity:test .
```

## Dependências

Cosmos DB, Google Identity Services, Azure Blob Storage e chave RSA privada para
assinatura JWT.
