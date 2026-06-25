resource "azurerm_user_assigned_identity" "github_deploy" {
  name                = "uami-${var.prefix}-github-deploy"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tags                = var.tags
}

resource "azurerm_federated_identity_credential" "github_branch" {
  name                      = "fic-github-${var.github_branch}"
  user_assigned_identity_id = azurerm_user_assigned_identity.github_deploy.id
  issuer                    = "https://token.actions.githubusercontent.com"
  audience                  = ["api://AzureADTokenExchange"]
  subject                   = "repo:${var.github_owner}/${var.github_repo}:ref:refs/heads/${var.github_branch}"
}

resource "azurerm_federated_identity_credential" "github_environment" {
  count = var.github_environment == null ? 0 : 1

  name                      = "fic-github-env-${var.github_environment}"
  user_assigned_identity_id = azurerm_user_assigned_identity.github_deploy.id
  issuer                    = "https://token.actions.githubusercontent.com"
  audience                  = ["api://AzureADTokenExchange"]
  subject                   = "repo:${var.github_owner}/${var.github_repo}:environment:${var.github_environment}"
}

resource "azurerm_user_assigned_identity" "runtime" {
  for_each = merge(
    { for key, value in local.container_apps : key => value.name },
    { scheduler = local.scheduler_job.name }
  )

  name                = "uami-${each.value}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tags                = var.tags
}

resource "azurerm_user_assigned_identity" "web_runtime" {
  name                = "uami-${local.web_container_app.name}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tags                = var.tags
}

resource "azurerm_role_assignment" "github_acr_push" {
  scope                = azurerm_container_registry.main.id
  role_definition_name = "AcrPush"
  principal_id         = azurerm_user_assigned_identity.github_deploy.principal_id
}

resource "azurerm_role_assignment" "github_container_apps_contributor" {
  scope                = azurerm_resource_group.main.id
  role_definition_name = "Container Apps Contributor"
  principal_id         = azurerm_user_assigned_identity.github_deploy.principal_id
}

resource "azurerm_role_assignment" "github_container_apps_jobs_contributor" {
  scope                = azurerm_resource_group.main.id
  role_definition_name = "Container Apps Jobs Contributor"
  principal_id         = azurerm_user_assigned_identity.github_deploy.principal_id
}

resource "azurerm_role_assignment" "github_managed_identity_operator" {
  scope                = azurerm_resource_group.main.id
  role_definition_name = "Managed Identity Operator"
  principal_id         = azurerm_user_assigned_identity.github_deploy.principal_id
}

resource "azurerm_role_assignment" "github_reader" {
  scope                = azurerm_resource_group.main.id
  role_definition_name = "Reader"
  principal_id         = azurerm_user_assigned_identity.github_deploy.principal_id
}

resource "azurerm_role_assignment" "runtime_acr_pull" {
  for_each = azurerm_user_assigned_identity.runtime

  scope                = azurerm_container_registry.main.id
  role_definition_name = "AcrPull"
  principal_id         = each.value.principal_id
}

resource "azurerm_role_assignment" "web_acr_pull" {
  scope                = azurerm_container_registry.main.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.web_runtime.principal_id
}

resource "azurerm_role_assignment" "runtime_key_vault_secrets_user" {
  for_each = azurerm_user_assigned_identity.runtime

  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = each.value.principal_id
}

resource "azurerm_role_assignment" "current_key_vault_admin" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Administrator"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_role_assignment" "identity_storage_blob_contributor" {
  scope                = azurerm_storage_account.avatars.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.runtime["identity"].principal_id
}

resource "azurerm_cosmosdb_sql_role_assignment" "runtime_cosmos_contributor" {
  for_each = azurerm_user_assigned_identity.runtime

  resource_group_name = azurerm_resource_group.main.name
  account_name        = azurerm_cosmosdb_account.main.name
  role_definition_id  = "${azurerm_cosmosdb_account.main.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002"
  principal_id        = each.value.principal_id
  scope               = azurerm_cosmosdb_account.main.id
}
