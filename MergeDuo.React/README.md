# MergeDuo.React

Frontend React/Vite do MergeDuo.

## Rodar localmente

```powershell
npm install
npm run dev
```

URLs padrão usadas em desenvolvimento:

- Identity API: `https://localhost:7211`
- Profile API: `https://localhost:7212`
- Partnership API: `https://localhost:7085`
- Cards API: `https://localhost:7182`
- FixedRules API: `https://localhost:7129`
- Transactions API: `https://localhost:7282`
- Aggregates API: `https://localhost:7036`

## Variáveis de ambiente

Em produção (`vite build`), o app usa por padrão rotas same-origin:
`/auth`, `/users`, `/.well-known` e `/api/*`. O `server.js` do container Web
encaminha essas rotas para os microsserviços configurados por env vars de
runtime no Azure Container Apps. Esse modo é necessário para o login Google
funcionar no PWA instalado no iOS, porque o WebKit trata cookies cross-site de
forma restritiva.

As variáveis `VITE_*_API_BASE_URL` continuam válidas para desenvolvimento. Em
produção elas só substituem as URLs same-origin quando
`VITE_USE_CROSS_ORIGIN_APIS=true` estiver definido explicitamente.

| Variável | Obrigatória | Descrição |
| --- | --- | --- |
| `VITE_IDENTITY_API_BASE_URL` | não | Base URL do MergeDuo.Identity em desenvolvimento ou override cross-origin |
| `VITE_PROFILE_API_BASE_URL` | não | Base URL do MergeDuo.Profile em desenvolvimento ou override cross-origin |
| `VITE_PARTNERSHIP_API_BASE_URL` | não | Base URL do MergeDuo.Partnership em desenvolvimento ou override cross-origin |
| `VITE_CARDS_API_BASE_URL` | não | Base URL do MergeDuo.Cards em desenvolvimento ou override cross-origin |
| `VITE_FIXED_RULES_API_BASE_URL` | não | Base URL do MergeDuo.FixedRules em desenvolvimento ou override cross-origin |
| `VITE_TRANSACTIONS_API_BASE_URL` | não | Base URL do MergeDuo.Transactions em desenvolvimento ou override cross-origin |
| `VITE_AGGREGATES_API_BASE_URL` | não | Base URL do MergeDuo.Aggregates em desenvolvimento ou override cross-origin |
| `VITE_USE_CROSS_ORIGIN_APIS` | não | Define `true` para permitir overrides cross-origin em produção |
| `VITE_GOOGLE_CLIENT_ID` | sim | Client ID do Google Identity Services usado em `/auth/google/callback` |
| `VITE_APP_VERSION` | não | Exibido em telas internas/relatórios |

O container Web também usa env vars de runtime, configuradas pelo Terraform, para
o proxy do `server.js`:

- `IDENTITY_API_BASE_URL`
- `PROFILE_API_BASE_URL`
- `PARTNERSHIP_API_BASE_URL`
- `CARDS_API_BASE_URL`
- `FIXED_RULES_API_BASE_URL`
- `TRANSACTIONS_API_BASE_URL`
- `AGGREGATES_API_BASE_URL`
- `PORT`
- `NODE_ENV`

### Pré-requisitos no backend para produção

- O domínio do frontend deve apontar para o React Web Container App.
- O `server.js` deve receber as env vars de runtime dos microsserviços para
  encaminhar `/auth`, `/users`, `/.well-known` e `/api/*`.
- Cookies de sessão do Identity devem ser emitidos como first-party
  (`Secure`, `SameSite=Lax`, sem `Domain` compartilhado).
- O Partnership precisa de `PublicApp:InviteBaseUrl` apontando para o domínio
  público (gera os links em `/invites/{token}`).
- Aggregates e Transactions devem estar acessíveis pelo Scheduler para que as
  regras fixas sejam materializadas em transações reais (o frontend não
  materializa mais regras localmente).

## Scripts

| Script | Descrição |
| --- | --- |
| `npm run dev` | Servidor de desenvolvimento (Vite) |
| `npm run lint` | Lint (ESLint 9 + typescript-eslint) |
| `npm run build` | Build de produção (`tsc -b && vite build`) com gate de configuração |
| `npm run test` | Testes unitários (Vitest + jsdom + Testing Library) |
| `npm run preview` | Preview do bundle gerado por `npm run build` |

## Container

```powershell
docker build `
  --build-arg VITE_GOOGLE_CLIENT_ID="<google-client-id>" `
  --build-arg VITE_APP_VERSION="local" `
  -t mergeduo-web:test .

docker run --rm -p 8080:8080 `
  -e IDENTITY_API_BASE_URL="https://example-identity" `
  -e PROFILE_API_BASE_URL="https://example-profile" `
  -e PARTNERSHIP_API_BASE_URL="https://example-partnership" `
  -e CARDS_API_BASE_URL="https://example-cards" `
  -e FIXED_RULES_API_BASE_URL="https://example-fixedrules" `
  -e TRANSACTIONS_API_BASE_URL="https://example-transactions" `
  -e AGGREGATES_API_BASE_URL="https://example-aggregates" `
  mergeduo-web:test
```

## Integrações

- `src/api/http.ts`: cliente HTTP compartilhado com timeout, ProblemDetails, `Idempotency-Key`, `If-Match` e handler global de 401.
- `src/api/identity.ts`: login, refresh, logout, `/users/me` e avatar.
- `src/api/profile.ts`: `/me/stats`, `/users/{userId}` e `/users/by-handle/{handle}`.
- `src/api/partnership.ts`: convites, preview, aceite, revogação, parceria atual e encerramento (mapeia `startingBalance` real do parceiro).
- `src/api/cards.ts`: listagem, criação, remoção lógica e uso/fatura de cartões.
- `src/api/fixedRules.ts`: listagem, criação, pausa, retomada, remoção lógica e preview de lançamentos fixos.
- `src/api/transactions.ts`: listagem mensal, criação, edição preparada, remoção lógica e grupos de parcelas.
- `src/api/aggregates.ts`: agregados mensais/anuais consumidos pelo `SummaryHeader` e visão anual.

O `SummaryHeader` e a `AnnualView` consomem o MergeDuo.Aggregates como fonte
primária; quando o agregado não está disponível a UI usa o cálculo local
derivado das transações já carregadas (sem materializar regras fixas).
