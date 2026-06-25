output "resource_group_name" {
  description = "Production resource group name."
  value       = azurerm_resource_group.main.name
}

output "acr_name" {
  description = "Azure Container Registry name."
  value       = azurerm_container_registry.main.name
}

output "acr_login_server" {
  description = "Azure Container Registry login server."
  value       = azurerm_container_registry.main.login_server
}

output "key_vault_name" {
  description = "Key Vault name where manual production secrets must be created."
  value       = azurerm_key_vault.main.name
}

output "cosmos_account_name" {
  description = "Cosmos DB account name. Use with Azure CLI to retrieve connection strings when runtime configuration is managed outside Terraform."
  value       = azurerm_cosmosdb_account.main.name
}

output "cosmos_endpoint" {
  description = "Cosmos DB account endpoint."
  value       = azurerm_cosmosdb_account.main.endpoint
}

output "storage_account_name" {
  description = "Storage account name for avatar blobs. Use with Azure CLI to retrieve the storage connection string when needed."
  value       = azurerm_storage_account.avatars.name
}

output "storage_blob_endpoint" {
  description = "Storage blob endpoint for avatar blobs."
  value       = azurerm_storage_account.avatars.primary_blob_endpoint
}

output "container_app_names" {
  description = "Container App names by service."
  value = {
    for key, app in local.container_apps : key => app.name
  }
}

output "container_app_urls" {
  description = "Public Container App API URLs by service."
  value = {
    for key, app in local.container_apps : key => "https://${app.name}.${azurerm_container_app_environment.main.default_domain}"
  }
}

output "react_container_app_name" {
  description = "Azure Container App name used by the React deployment workflow."
  value       = azurerm_container_app.web.name
}

output "react_container_app_url" {
  description = "Public React Container App URL."
  value       = "https://${azurerm_container_app.web.ingress[0].fqdn}"
}

output "scheduler_job_name" {
  description = "Container Apps Job name for fixed rules scheduling."
  value       = local.scheduler_job.name
}

output "github_oidc_values" {
  description = "Values to configure as GitHub Actions secrets or variables for Azure OIDC login."
  value = {
    AZURE_CLIENT_ID       = azurerm_user_assigned_identity.github_deploy.client_id
    AZURE_TENANT_ID       = data.azurerm_client_config.current.tenant_id
    AZURE_SUBSCRIPTION_ID = data.azurerm_client_config.current.subscription_id
  }
}

output "github_actions_microservice_vars" {
  description = "Repository/environment variables expected by each microservice workflow."
  value       = local.github_actions_microservice_vars
}

output "github_actions_scheduler_vars" {
  description = "Repository/environment variables expected by the Scheduler workflow."
  value       = local.github_actions_scheduler_vars
}

output "github_actions_web_vars" {
  description = "Repository/environment variables expected by the React web Container App workflow."
  value       = local.github_actions_web_vars
}

output "github_actions_global_vars" {
  description = "Shared GitHub environment variables for monorepo deploy workflows."
  value = {
    ACR_LOGIN_SERVER     = azurerm_container_registry.main.login_server
    ACR_NAME             = azurerm_container_registry.main.name
    AZURE_RESOURCE_GROUP = azurerm_resource_group.main.name
  }
}

output "google_oauth_redirect_uri" {
  description = "Google OAuth redirect URI to register for production."
  value       = "https://${azurerm_container_app.web.ingress[0].fqdn}/auth/google/redirect-callback"
}
