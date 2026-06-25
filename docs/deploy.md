# Deploy no Azure (passo a passo)

Guia de ponta a ponta para publicar o MergeDuo no Azure: provisionar a
infraestrutura com Terraform, configurar o OIDC e o ambiente do GitHub, registrar
o Google OAuth de produção, definir os segredos de runtime e executar os
workflows de deploy.

## Visão geral do fluxo

```text
Terraform (infra + identidades OIDC + RBAC)
  -> GitHub Environment "production" (secrets + vars)
  -> Google OAuth (redirect URI de produção)
  -> Segredos de runtime nos Container Apps
  -> Workflows de deploy (manuais) publicam imagens no ACR e atualizam os Apps
```

A infraestrutura é a topologia de portfólio de menor custo: Azure Container Apps
em Consumption (`min_replicas = 0`), Cosmos DB Serverless e Log Analytics com cota
diária baixa. **Não** há API Management, Front Door, WAF nem private endpoints.

## Pré-requisitos

- Azure CLI autenticado (`az login`) com permissão de administrador no
  subscription — o Terraform cria identidades gerenciadas e role assignments.
- Terraform 1.7+.
- Acesso ao repositório no GitHub (`gmarquessousa/MergeDuo`).
- Um projeto no Google Cloud Console para o OAuth.

## 1. Provisionar a infraestrutura (Terraform)

```powershell
cd MergeDuo.Terraform
Copy-Item terraform.tfvars.example terraform.tfvars
```

Edite `terraform.tfvars` com os valores reais:

- `subscription_id` e `tenant_id`
- `github_owner`, `github_repo`, `github_branch`, `github_environment`
- `location`, `prefix`, `environment`

```powershell
terraform init
terraform fmt
terraform validate
terraform plan -out mergeduo.tfplan
terraform apply mergeduo.tfplan
```

O Terraform provisiona o Resource Group, ACR, Container Apps Environment + Apps,
o Scheduler Job, Cosmos DB, Storage, Log Analytics, o Key Vault (reservado para
uso futuro) e — importante — as **identidades gerenciadas, as federated
credentials (OIDC) e os RBAC** usados pelo GitHub Actions.

> `terraform.tfvars` e `terraform.tfstate*` são locais e não devem ser
> versionados (já estão no `.gitignore`).

## 2. Coletar os outputs

```powershell
terraform output github_oidc_values
terraform output github_actions_global_vars
terraform output github_actions_web_vars
terraform output github_actions_microservice_vars
terraform output github_actions_scheduler_vars
terraform output -raw react_container_app_url
terraform output -raw google_oauth_redirect_uri
```

## 3. Configurar o GitHub Environment `production`

Em **Settings → Environments**, crie o ambiente com o mesmo nome usado em
`github_environment` (por padrão `production`). Os workflows de deploy são
manuais e exigem esse ambiente. Proteções e *required reviewers* são opcionais.

**Secrets** (de `github_oidc_values`):

```text
AZURE_CLIENT_ID
AZURE_TENANT_ID
AZURE_SUBSCRIPTION_ID
```

**Variables compartilhadas** (de `github_actions_global_vars`):

```text
ACR_LOGIN_SERVER
ACR_NAME
AZURE_RESOURCE_GROUP
```

**Variables do React** (de `github_actions_web_vars`):

```text
WEB_ACA_NAME
WEB_IMAGE_REPOSITORY
VITE_GOOGLE_CLIENT_ID
```

**Variables por microserviço e do Scheduler**: use
`github_actions_microservice_vars` e `github_actions_scheduler_vars` (nomes dos
Container Apps e os caminhos de imagem gerados para o monorepo).

> O OIDC já está pronto: o Terraform criou a federated credential para
> `repo:gmarquessousa/MergeDuo:environment:production`, então o GitHub Actions
> autentica no Azure **sem** client secret.

## 4. Configurar o Google OAuth (produção)

No OAuth 2.0 Client ID (tipo *Web application*) no Google Cloud Console:

- **Authorized JavaScript origins**: a URL do React
  (`terraform output -raw react_container_app_url`).
- **Authorized redirect URIs**: `terraform output -raw google_oauth_redirect_uri`
  (algo como `https://<web-fqdn>/auth/google/redirect-callback`).
- Use o mesmo **Client ID** na variável `VITE_GOOGLE_CLIENT_ID` (build do React)
  e em `Google__ClientId` da Identity.

## 5. Definir os segredos de runtime nos Container Apps

O Terraform **não** configura segredos da aplicação. Crie-os diretamente nos
Container Apps e no Scheduler Job. Resumo dos segredos:

| Recurso | Secret | Variável de ambiente |
| --- | --- | --- |
| Identity | `jwt-private-key` | `Jwt__PrivateKeyPem=secretref:jwt-private-key` |
| Identity | `refresh-pepper` | `RefreshTokens__Pepper=secretref:refresh-pepper` |
| Transactions | `continuation-secret` | `Transactions__ContinuationTokenSecret=secretref:continuation-secret` |
| Transactions | `scheduler-key` | `InternalApi__SchedulerKey=secretref:scheduler-key` |
| Scheduler | `scheduler-key` | `TransactionsService__InternalKey=secretref:scheduler-key` |

Mais os valores **não sensíveis** (`Cosmos__Endpoint`, `Cosmos__Database`,
`Jwt__Issuer`, `Jwt__Audience`, `Jwt__JwksUrl`, `Google__ClientId`,
`PublicApp__BaseUrl`, `BlobStorage__AccountUrl`, `Cors__AllowedOrigins__0`,
`*Service__BaseUrl`, etc.).

> Passo a passo detalhado, com `az containerapp secret set` /
> `az containerapp update`, o equivalente para o Scheduler Job e como obter as
> connection strings do Cosmos/Storage:
> [azure-runtime-configuration.md](azure-runtime-configuration.md).

## 6. Executar os workflows de deploy

Os workflows são `workflow_dispatch` (manuais). Em **Actions**, execute:

- `Deploy React Web`
- `Deploy <Serviço>` para cada API (Identity, Profile, Partnership, Cards,
  FixedRules, Transactions, Aggregates)
- `Deploy Scheduler`

Cada workflow valida as variáveis do ambiente, builda a imagem, publica no ACR
(via OIDC) e atualiza o Container App correspondente.

Ordem recomendada: **Identity → demais APIs → Scheduler → React Web**.

## 7. Validar

- A URL do React (`react_container_app_url`) carrega o app.
- `GET /readyz` e `GET /healthz` das APIs respondem via HTTPS.
- O login com Google completa o redirect de volta ao app.
- O Scheduler Job executa e materializa as regras fixas.

## Rotação de segredos

Ao trocar um segredo, atualize-o no recurso e crie uma nova revisão. Para a chave
compartilhada do Scheduler, atualize Transactions e Scheduler na mesma janela
para evitar falhas temporárias.

## Referências

- Infra e outputs: [../MergeDuo.Terraform/README.md](../MergeDuo.Terraform/README.md)
- Configuração de runtime sem Key Vault: [azure-runtime-configuration.md](azure-runtime-configuration.md)
- Inventário de segredos: [../MergeDuo.Terraform/docs/secrets.md](../MergeDuo.Terraform/docs/secrets.md)
