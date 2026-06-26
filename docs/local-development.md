# Rodar Localmente (passo a passo)

Guia completo para clonar, configurar e executar o MergeDuo na sua máquina: o
frontend React, as APIs .NET e os serviços de apoio.

## 1. Pré-requisitos

- Node.js 22.x
- .NET SDK 8.x
- Git
- Cosmos DB: uma conta no Azure (Serverless) **ou** o Azure Cosmos DB Emulator
- Docker Desktop (opcional, para imagens locais)
- Azure CLI e Terraform 1.7+ (apenas para infra/deploy)

As APIs locais usam HTTPS, então confie no certificado de desenvolvimento do
.NET uma vez:

```powershell
dotnet dev-certs https --trust
```

## 2. Clonar o repositório

```powershell
git clone https://github.com/gmarquessousa/MergeDuo.git
cd MergeDuo
```

## 3. Criar um cliente OAuth do Google (login)

O login usa o Google Identity Services, então você precisa de um **OAuth 2.0
Client ID** (tipo *Web application*) no Google Cloud Console:

1. Acesse o [Google Cloud Console → Credenciais](https://console.cloud.google.com/apis/credentials).
2. Crie/escolha um projeto e clique em **Create credentials → OAuth client ID**.
3. Tipo de aplicativo: **Web application**.
4. **Authorized JavaScript origins**: `http://localhost:5173`.
5. **Authorized redirect URIs**: `https://localhost:7211/auth/google/redirect-callback`
   (origem da API Identity em dev — veja a tabela de portas abaixo).
6. Copie o **Client ID** gerado (`...apps.googleusercontent.com`).

> O redirect é sempre `<base>/auth/google/redirect-callback`, onde `<base>` é
> `PublicApp:BaseUrl` (se definido) ou a origem da Identity. Em produção esse
> valor sai de `terraform output -raw google_oauth_redirect_uri`.

## 4. Frontend (React)

```powershell
cd MergeDuo.React
npm install
Copy-Item .env.example .env.local
```

Edite `.env.local` e defina o Client ID do Google:

```dotenv
VITE_GOOGLE_CLIENT_ID=SEU_CLIENT_ID.apps.googleusercontent.com
```

Em desenvolvimento (`npm run dev`), o app chama cada API **diretamente** nas
portas HTTPS locais (não usa o proxy same-origin de produção). Os valores padrão
de `VITE_*_API_BASE_URL` já apontam para essas portas, então normalmente você só
precisa configurar o `VITE_GOOGLE_CLIENT_ID`.

```powershell
npm run dev
```

O frontend sobe em http://localhost:5173.

## 5. Backend (APIs .NET)

### 5.1 Banco de dados

As APIs usam Cosmos DB. Defina a connection string por variável de ambiente
(ASP.NET Core lê `Cosmos__ConnectionString`). Use uma conta real ou o emulador
local:

```powershell
$env:Cosmos__ConnectionString="AccountEndpoint=https://localhost:8081/;AccountKey=<chave-do-emulador>"
```

### 5.2 Segredos de cada serviço

Os `appsettings*.json` versionados só têm placeholders. Configure por variáveis
de ambiente (formato ASP.NET Core, com `__` separando as seções):

```powershell
$env:Cosmos__ConnectionString="<cosmos>"
$env:Jwt__PrivateKeyPem="<chave RSA privada em PEM>"
$env:RefreshTokens__Pepper="<valor aleatório>"
$env:Transactions__ContinuationTokenSecret="<valor aleatório>"
$env:BlobStorage__ConnectionString="DefaultEndpointsProtocol=...;"
$env:Google__ClientId="SEU_CLIENT_ID.apps.googleusercontent.com"
```

A chave compartilhada do Scheduler precisa do **mesmo valor** em dois nomes:
`TransactionsService__InternalKey` (no Scheduler) e `InternalApi__SchedulerKey`
(no Transactions).

> Inventário completo de segredos:
> [../MergeDuo.Terraform/docs/secrets.md](../MergeDuo.Terraform/docs/secrets.md)
> e [azure-runtime-configuration.md](azure-runtime-configuration.md).

### 5.3 Subir as APIs

Rode cada serviço com o profile **https**, para casar com as portas que o
frontend espera:

```powershell
dotnet run --launch-profile https --project MergeDuo.Microservices/MergeDuo.Identity/src/MergeDuo.Identity.Api
dotnet run --launch-profile https --project MergeDuo.Microservices/MergeDuo.Profile/src/MergeDuo.Profile.Api
dotnet run --launch-profile https --project MergeDuo.Microservices/MergeDuo.Partnership/src/MergeDuo.Partnership.Api
dotnet run --launch-profile https --project MergeDuo.Microservices/MergeDuo.Cards/src/MergeDuo.Cards.Api
dotnet run --launch-profile https --project MergeDuo.Microservices/MergeDuo.FixedRules/src/MergeDuo.FixedRules.Api
dotnet run --launch-profile https --project MergeDuo.Microservices/MergeDuo.Transactions/src/MergeDuo.Transactions.Api
dotnet run --launch-profile https --project MergeDuo.Microservices/MergeDuo.Aggregates/src/MergeDuo.Aggregates.Api
```

Portas locais esperadas pelo frontend:

| Serviço | URL local |
| --- | --- |
| Identity | https://localhost:7211 |
| Profile | https://localhost:7212 |
| Partnership | https://localhost:7085 |
| Cards | https://localhost:7182 |
| FixedRules | https://localhost:7129 |
| Transactions | https://localhost:7282 |
| Aggregates | https://localhost:7036 |

Serviços de apoio (rode quando for validar esses fluxos):

```powershell
dotnet run --launch-profile https --project MergeDuo.Microservices/MergeDuo.Copilot/src/MergeDuo.Copilot.Api
dotnet run --project MergeDuo.Microservices/MergeDuo.Scheduler/src/MergeDuo.Scheduler.Job
```

Ordem recomendada: **Identity** primeiro; depois os serviços consumidos pelo
frontend (Profile, Partnership, Cards, FixedRules, Transactions, Aggregates); e
por fim Scheduler/Copilot.

## 6. Abrir o app

Com o frontend e as APIs rodando, acesse http://localhost:5173 e entre com o
Google.

## 7. Validação

Frontend:

```powershell
cd MergeDuo.React
npm run lint
npm run test
npm run build
```

.NET:

```powershell
Get-ChildItem MergeDuo.Microservices -Filter *.sln -Recurse |
  ForEach-Object { dotnet test $_.FullName --configuration Release }
```

Terraform:

```powershell
cd MergeDuo.Terraform
terraform init
terraform fmt -check -recursive
terraform validate
```

## 8. Troubleshooting

- **Login não funciona / popup do Google fecha**: confira `VITE_GOOGLE_CLIENT_ID`
  e se `http://localhost:5173` está em *Authorized JavaScript origins* e
  `https://localhost:7211/auth/google/redirect-callback` em *Authorized redirect
  URIs*.
- **Erro de certificado HTTPS**: rode `dotnet dev-certs https --trust`.
- **CORS bloqueado**: garanta que a API permita a origem `http://localhost:5173`
  (em dev, via `Cors__AllowedOrigins__0`).
- **API falha no startup**: confira as variáveis de ambiente sensíveis exigidas
  pelo serviço (Cosmos, JWT, etc.).
- **Cookies não persistem no mobile**: prefira o proxy same-origin do React em
  vez de chamar APIs cross-origin diretamente.
- **Scheduler não cria transações**: confirme que
  `TransactionsService__InternalKey` (Scheduler) e `InternalApi__SchedulerKey`
  (Transactions) têm o mesmo valor.

## Deploy

Para publicar no Azure (Terraform, OIDC, GitHub Environment, Google OAuth de
produção e segredos de runtime), siga o [guia de deploy](deploy.md).
