# MergeDuo.Microservices

APIs, jobs e integrações backend do MergeDuo. Cada serviço tem solução própria,
testes próprios e Dockerfile próprio para deploy independente em Azure Container
Apps.

## Serviços

| Serviço | Responsabilidade | Solução |
| --- | --- | --- |
| Aggregates | Agregados mensais/anuais e recomputação | `MergeDuo.Aggregates/MergeDuo.Aggregates.sln` |
| Cards | Cartões e faturas | `MergeDuo.Cards/MergeDuo.Cards.sln` |
| Copilot | Endpoints de leitura/simulação para copilots | `MergeDuo.Copilot/MergeDuo.Copilot.sln` |
| FixedRules | Regras financeiras recorrentes | `MergeDuo.FixedRules/MergeDuo.FixedRules.sln` |
| Identity | Login, refresh, JWKS, usuário autenticado e avatar | `MergeDuo.Identity/MergeDuo.Identity.sln` |
| Partnership | Convites e ciclo de vida de parceria | `MergeDuo.Partnership/MergeDuo.Partnership.sln` |
| Profile | Perfil público, handle e estatísticas | `MergeDuo.Profile/MergeDuo.Profile.sln` |
| Scheduler | Job de materialização de regras fixas | `MergeDuo.Scheduler/MergeDuo.Scheduler.sln` |
| Transactions | Lançamentos, parcelas, tags e endpoint interno | `MergeDuo.Transactions/MergeDuo.Transactions.sln` |

## Padrão de Projeto

A maioria das APIs segue a divisão:

```text
src/<Servico>.Api/      Minimal API, auth, métricas, Dockerfile
src/<Servico>.Domain/   regras, contratos, documentos e serviços de domínio
src/<Servico>.Infra/    Cosmos DB, storage ou clientes externos
tests/<Servico>.Tests/  testes de domínio e fluxo HTTP
```

O Scheduler é um job .NET com testes de core logic.

## Configuração

Os `appsettings*.json` versionados não contêm segredos reais. Configure runtime
por variáveis de ambiente ASP.NET Core, como:

```text
Cosmos__ConnectionString
Jwt__PrivateKeyPem
RefreshTokens__Pepper
Transactions__ContinuationTokenSecret
BlobStorage__ConnectionString
```

Para a chave compartilhada do Scheduler, use
`TransactionsService__InternalKey` no job e `InternalApi__SchedulerKey` no
Transactions.

## Validação

```powershell
Get-ChildItem MergeDuo.Microservices -Filter *.sln -Recurse |
  ForEach-Object { dotnet test $_.FullName --configuration Release }
```

## Deploy

Os workflows manuais em `.github/workflows/deploy-*.yml` chamam o workflow
reutilizável `_deploy-container-app.yml`, que restaura, testa, cria a imagem
Docker, publica no ACR e atualiza o Container App ou Job.
