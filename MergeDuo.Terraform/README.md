# MergeDuo Terraform

Infraestrutura Terraform de produção para o deploy de portfólio do MergeDuo.

## Arquitetura

```text
User
 -> Azure Container Apps React Web
 -> server.js same-origin proxy
 -> public Azure Container Apps APIs
 -> Cosmos DB / Storage / Key Vault
```

Esta é a topologia de portfólio de menor custo. Azure API Management, Azure Front
Door, WAF, integração com VNet e private endpoint do Cosmos não são provisionados
intencionalmente nesta versão. O frontend React e as APIs rodam em endpoints
HTTPS do Azure Container Apps.

Controles de custo aplicados atualmente:

- Container Apps rodam em Consumption com `min_replicas = 0`.
- Container Apps usam por padrão `0.25` de CPU, `0.5Gi` de memória e
  `max_replicas = 1`.
- Log Analytics tem um limite diário baixo de ingestão.
- Cosmos DB usa Serverless com autenticação local habilitada.

## Apply

```powershell
cd T:\MergeDuo\MergeDuo.Terraform
Copy-Item terraform.tfvars.example terraform.tfvars
# Edit terraform.tfvars

terraform init
terraform fmt
terraform validate
terraform plan -out mergeduo.tfplan
terraform apply mergeduo.tfplan
```

`terraform.tfvars`, `terraform.tfstate*` e os arquivos de plano são apenas locais
e não devem ser versionados. Se um arquivo real de state ou tfvars já tiver sido
publicado, rotacione os valores afetados e limpe o histórico Git antes de tornar
o repositório público.

Este stack Terraform provisiona a infraestrutura e configura apenas as variáveis
de runtime não sensíveis do proxy do React Web Container App. Ele não configura
segredos da aplicação nem secret references das APIs em Container Apps. Configure
os valores de runtime das APIs e do Scheduler diretamente no Azure Container Apps,
como documentado em `../docs/azure-runtime-configuration.md`.

O Key Vault ainda é criado para uso futuro, mas o Terraform não cria nem anexa
segredos da aplicação nesta fase. Os arquivos `appsettings*.json` versionados
contêm apenas placeholders; configure os valores reais por variáveis de ambiente,
secrets do Container Apps ou uma futura integração com Key Vault. Consulte
`docs/secrets.md` como inventário de segredos.

O Terraform ignora os blocos `secret` e `env` gerenciados externamente para as
APIs em Container Apps e o Scheduler Job, de modo que applies posteriores
preservam a configuração feita via Azure CLI.

## Configuração de Runtime

Os serviços usam atualmente a configuração empacotada com cada aplicação,
incluindo o `appsettings.json`. A autenticação local do Cosmos DB está habilitada
para que os serviços possam usar `Cosmos:ConnectionString` em vez de managed
identity nesta fase.

O Terraform não expõe connection strings como output. Após o `terraform apply`,
obtenha os valores necessários manualmente:

```powershell
$resourceGroup = terraform output -raw resource_group_name
$cosmosAccount = terraform output -raw cosmos_account_name
$storageAccount = terraform output -raw storage_account_name

az cosmosdb keys list `
  --resource-group $resourceGroup `
  --name $cosmosAccount `
  --type connection-strings `
  --query "connectionStrings[0].connectionString" `
  -o tsv

az storage account show-connection-string `
  --resource-group $resourceGroup `
  --name $storageAccount `
  --query connectionString `
  -o tsv
```

O acesso público de rede ao Cosmos está habilitado nesta topologia de menor custo.
Proteja a connection string com cuidado e mova a configuração de runtime para o
Key Vault ou variáveis de ambiente antes de tratar isto como um setup de produção
endurecido.

Após o `terraform apply`, use estes outputs para configurar as URLs da aplicação:

```powershell
terraform output react_container_app_url
terraform output container_app_urls
terraform output google_oauth_redirect_uri
```

O bundle do navegador do React usa caminhos de API same-origin por padrão. O
Terraform define as URLs upstream não sensíveis no React Web Container App para o
proxy do `server.js`.

## GitHub Actions

Use o output `github_oidc_values` para o login no Azure:

```text
AZURE_CLIENT_ID
AZURE_TENANT_ID
AZURE_SUBSCRIPTION_ID
```

Use `github_actions_global_vars` como variáveis compartilhadas do GitHub
Environment para os workflows de deploy do monorepo.

Use `github_actions_microservice_vars` e `github_actions_scheduler_vars` como
referência para os alvos de Container Apps e os caminhos Docker gerados para o
layout do monorepo.

Use `github_actions_web_vars` para o workflow do React Web Container App. O
workflow do React ainda espera que `VITE_GOOGLE_CLIENT_ID` esteja configurado no
GitHub, porque o Vite o embute no bundle do navegador em tempo de build.

Os workflows de deploy são manuais (`workflow_dispatch`). Mantenha-os manuais até
que o ambiente `production` e todos os valores de runtime da aplicação estejam
configurados.

## Google OAuth

Registre esta redirect URI no Google Cloud:

```text
terraform output -raw google_oauth_redirect_uri
```

## Validação

Verificações esperadas após o deploy:

- A URL padrão do React Container App retorna o app React.
- Os endpoints públicos dos Container Apps respondem via HTTPS.
- A redirect URI do Google OAuth aponta para o React Container App.
- O GitHub Actions consegue publicar imagens no ACR e atualizar os Container Apps
  via OIDC.
