# Segredos de Produção do MergeDuo

Atualmente o Terraform cria o Key Vault para uso futuro, mas não cria os valores
dos segredos da aplicação e não anexa referências do Key Vault aos Container Apps
ou aos Container Apps Jobs.

Os arquivos `appsettings*.json` versionados não devem conter valores reais de
segredos. Use variáveis de ambiente ou secrets do Container Apps no setup público
de portfólio atual. As referências do Key Vault são o caminho de endurecimento
pretendido para uma fase futura.

| Nome do segredo | Usado por | Variável de runtime | Valor necessário |
| --- | --- | --- | --- |
| `jwt-private-key-pem` | Identity | `Jwt__PrivateKeyPem` | Chave privada RSA em PEM usada para assinar os JWTs. |
| `refresh-token-pepper` | Identity | `RefreshTokens__Pepper` | Pepper aleatório forte (base64/string) para o hash dos refresh tokens. |
| `transactions-continuation-token-secret` | Transactions | `Transactions__ContinuationTokenSecret` | String aleatória forte para proteção do continuation token. |
| `scheduler-internal-key` | Scheduler | `TransactionsService__InternalKey` | Chave interna compartilhada para `POST /internal/scheduler/transactions`. |
| `scheduler-internal-key` | Transactions | `InternalApi__SchedulerKey` | Mesma chave interna compartilhada validada pelo Transactions. |
| `blob-storage-connection-string` | Identity | `BlobStorage__ConnectionString` | Connection string da storage account para compatibilidade do upload de avatar. |
| `cosmos-connection-string` | APIs e Scheduler | `Cosmos__ConnectionString` | Connection string do Cosmos DB. |

Comandos de geração recomendados:

```powershell
# JWT signing key
openssl genrsa -out jwt-private-key.pem 2048

# Random application secrets
openssl rand -base64 48
```

Exemplos de comandos Azure CLI para o setup futuro:

```powershell
$vault = "<key_vault_name_output>"

az keyvault secret set --vault-name $vault --name jwt-private-key-pem --file jwt-private-key.pem
az keyvault secret set --vault-name $vault --name refresh-token-pepper --value "<random>"
az keyvault secret set --vault-name $vault --name transactions-continuation-token-secret --value "<random>"
az keyvault secret set --vault-name $vault --name scheduler-internal-key --value "<random>"
az keyvault secret set --vault-name $vault --name blob-storage-connection-string --value "<storage-connection-string>"
az keyvault secret set --vault-name $vault --name cosmos-connection-string --value "<cosmos-connection-string>"
```
