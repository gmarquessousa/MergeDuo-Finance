# Configuração de Runtime no Azure

## Estratégia Atual

O MergeDuo não consome Key Vault nesta fase. Segredos ficam diretamente nos
Azure Container Apps e no Container Apps Job, enquanto valores não sensíveis
ficam como variáveis de ambiente.

O Terraform ignora alterações externas em `secret` e `env` para as APIs e para o
Scheduler. Assim, um `terraform apply` posterior não remove configurações feitas
com Azure CLI. O React continua com suas variáveis não sensíveis gerenciadas pelo
Terraform.

## Segredos

Crie os segredos nos recursos que realmente os utilizam:

| Recurso | Secret do Container App | Variável de ambiente |
| --- | --- | --- |
| Identity | `jwt-private-key` | `Jwt__PrivateKeyPem=secretref:jwt-private-key` |
| Identity | `refresh-pepper` | `RefreshTokens__Pepper=secretref:refresh-pepper` |
| Transactions | `continuation-secret` | `Transactions__ContinuationTokenSecret=secretref:continuation-secret` |
| Transactions | `scheduler-key` | `InternalApi__SchedulerKey=secretref:scheduler-key` |
| Scheduler | `scheduler-key` | `TransactionsService__InternalKey=secretref:scheduler-key` |

O mesmo valor aleatório deve ser usado para `scheduler-key` no
Transactions e no Scheduler.

Exemplo para uma API:

```powershell
az containerapp secret set `
  --resource-group "<resource-group>" `
  --name "<container-app>" `
  --secrets "jwt-private-key=<value>"

az containerapp update `
  --resource-group "<resource-group>" `
  --name "<container-app>" `
  --set-env-vars "Jwt__PrivateKeyPem=secretref:jwt-private-key"
```

Para o Scheduler, use `az containerapp job secret set` e
`az containerapp job update`.

## Valores Não Sensíveis

Configure pelo menos:

- Todas as APIs e o Scheduler: `Cosmos__Endpoint` e `Cosmos__Database`.
- APIs autenticadas: `Jwt__Issuer`, `Jwt__Audience` e `Jwt__JwksUrl`.
- Identity: `Google__ClientId`, `PublicApp__BaseUrl`,
  `BlobStorage__AccountUrl` e `Cors__AllowedOrigins__0`.
- Cards: `TransactionsService__BaseUrl`.
- Partnership: `PublicApp__InviteBaseUrl`.
- Scheduler: `TransactionsService__BaseUrl`.
- Demais APIs públicas: `Cors__AllowedOrigins__0`.

Os nomes de containers Cosmos possuem defaults versionados nos
`appsettings.json`. Sobrescreva-os apenas se a infraestrutura usar nomes
diferentes.

## Ordem Recomendada

1. Aplicar o Terraform.
2. Obter URLs e nomes com `terraform output`.
3. Configurar secrets e env vars nos Container Apps.
4. Executar manualmente os workflows de deploy.
5. Validar `/readyz` nas APIs e a execução do Scheduler.

## Rotação

Ao rotacionar um segredo, atualize o secret no recurso e crie uma nova revisão.
Para a chave compartilhada do Scheduler, atualize Transactions e Scheduler na
mesma janela para evitar falhas temporárias.
