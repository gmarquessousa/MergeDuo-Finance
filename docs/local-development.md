# Onboarding Local

## Pré-requisitos

- Node.js 22.x
- .NET SDK 8.x
- Terraform 1.7+
- Docker Desktop, opcional
- Azure CLI, apenas para operações de infra/deploy

## Primeiro Setup

```powershell
cd T:\Projetos\MergeDuo

cd MergeDuo.React
npm install
Copy-Item .env.example .env.local
npm run dev
```

O arquivo `.env.local` é local e ignorado pelo Git. Mantenha nele apenas valores
de desenvolvimento.

## Configuração das APIs

Os `appsettings*.json` versionados não carregam segredos. Para rodar APIs contra
recursos reais, configure variáveis de ambiente no formato ASP.NET Core:

```powershell
$env:Cosmos__ConnectionString="<cosmos-connection-string>"
$env:Jwt__PrivateKeyPem="<pem>"
$env:RefreshTokens__Pepper="<random>"
$env:Transactions__ContinuationTokenSecret="<random>"
$env:BlobStorage__ConnectionString="DefaultEndpointsProtocol=...;"
```

Use nomes de variáveis específicos de cada serviço quando aplicável. Exemplo:
`Transactions__ContinuationTokenSecret` é usado pelo Transactions. Para a
chave compartilhada, configure o mesmo valor em
`TransactionsService__InternalKey` no Scheduler e
`InternalApi__SchedulerKey` no Transactions.

## Rodando Serviços

Cada solução pode ser executada isoladamente:

```powershell
dotnet run --project MergeDuo.Microservices/MergeDuo.Identity/src/MergeDuo.Identity.Api
dotnet run --project MergeDuo.Microservices/MergeDuo.Profile/src/MergeDuo.Profile.Api
dotnet run --project MergeDuo.Microservices/MergeDuo.Partnership/src/MergeDuo.Partnership.Api
dotnet run --project MergeDuo.Microservices/MergeDuo.Cards/src/MergeDuo.Cards.Api
dotnet run --project MergeDuo.Microservices/MergeDuo.FixedRules/src/MergeDuo.FixedRules.Api
dotnet run --project MergeDuo.Microservices/MergeDuo.Transactions/src/MergeDuo.Transactions.Api
dotnet run --project MergeDuo.Microservices/MergeDuo.Aggregates/src/MergeDuo.Aggregates.Api
dotnet run --project MergeDuo.Microservices/MergeDuo.Copilot/src/MergeDuo.Copilot.Api
dotnet run --project MergeDuo.Microservices/MergeDuo.Scheduler/src/MergeDuo.Scheduler.Job
```

Para uma sessão de desenvolvimento completa, suba primeiro `Identity`, depois os
serviços consumidos diretamente pelo frontend, e por último `Scheduler` e
`Copilot` quando precisar validar esses fluxos.

## Validação Local

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

## Troubleshooting

- Se o frontend não autenticar, confira `VITE_GOOGLE_CLIENT_ID` e a redirect URI
  registrada no Google Cloud.
- Se uma API falhar no startup, confira se as variáveis de ambiente sensíveis
  exigidas pelo serviço foram configuradas.
- Se cookies não persistirem no mobile, use o proxy same-origin do React em vez
  de chamar APIs cross-origin diretamente.
- Se o Scheduler não criar transações, confirme que
  `TransactionsService__InternalKey` no Scheduler e
  `InternalApi__SchedulerKey` no Transactions possuem o mesmo valor.
