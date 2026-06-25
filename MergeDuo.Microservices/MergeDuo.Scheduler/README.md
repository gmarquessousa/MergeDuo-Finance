# MergeDuo.Scheduler

## Objetivo

Executa como Azure Container Apps Job para materializar regras fixas vencidas em
transações reais.

## Projetos

- `src/MergeDuo.Scheduler.Job`: job, clientes HTTP e lógica de agendamento.
- `tests/MergeDuo.Scheduler.Tests`: testes da lógica de ocorrência,
  idempotência e chamadas HTTP.

## Fluxo

1. Consulta regras fixas ativas.
2. Resolve a ocorrência devida para a data atual.
3. Chama `Transactions` por endpoint interno com
   `TransactionsService__InternalKey`.
4. Atualiza checkpoint apenas depois de sucesso.

## Endpoints

Não expõe endpoints HTTP. O Scheduler é executado como job agendado e consome o
endpoint interno `POST /internal/scheduler/transactions`.

## Configuração

```text
Cosmos__ConnectionString
TransactionsService__BaseUrl
TransactionsService__TimeoutSeconds
TransactionsService__InternalKey
Scheduler__BusinessTimeZone
Scheduler__MaxRulesPerRun
Scheduler__DryRun
```

## Comandos

```powershell
dotnet test MergeDuo.Scheduler.sln --configuration Release
dotnet run --project src/MergeDuo.Scheduler.Job
```

## Docker

```powershell
docker build -f src/MergeDuo.Scheduler.Job/Dockerfile -t mergeduo-scheduler:test .
```

## Dependências

Cosmos DB para leitura/checkpoint e Transactions para criar lançamentos.
