resource "azurerm_container_registry" "main" {
  name                = local.acr_name
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "Basic"
  admin_enabled       = false
  tags                = var.tags
}

resource "azurerm_key_vault" "main" {
  name                          = local.key_vault_name
  location                      = azurerm_resource_group.main.location
  resource_group_name           = azurerm_resource_group.main.name
  tenant_id                     = data.azurerm_client_config.current.tenant_id
  sku_name                      = "standard"
  rbac_authorization_enabled    = true
  purge_protection_enabled      = false
  soft_delete_retention_days    = 7
  public_network_access_enabled = true
  tags                          = var.tags
}

resource "azurerm_storage_account" "avatars" {
  name                            = local.storage_name
  location                        = azurerm_resource_group.main.location
  resource_group_name             = azurerm_resource_group.main.name
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  account_kind                    = "StorageV2"
  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = true
  public_network_access_enabled   = true
  shared_access_key_enabled       = true
  tags                            = var.tags
}

resource "azurerm_storage_container" "avatars" {
  name                  = "avatars"
  storage_account_id    = azurerm_storage_account.avatars.id
  container_access_type = "blob"
}

resource "azurerm_cosmosdb_account" "main" {
  name                              = local.cosmos_name
  location                          = azurerm_resource_group.main.location
  resource_group_name               = azurerm_resource_group.main.name
  offer_type                        = "Standard"
  kind                              = "GlobalDocumentDB"
  automatic_failover_enabled        = false
  public_network_access_enabled     = true
  is_virtual_network_filter_enabled = false
  local_authentication_disabled     = false
  tags                              = var.tags

  capabilities {
    name = "EnableServerless"
  }

  consistency_policy {
    consistency_level = "Session"
  }

  geo_location {
    location          = azurerm_resource_group.main.location
    failover_priority = 0
  }
}

resource "azurerm_cosmosdb_sql_database" "main" {
  name                = local.cosmos_database_name
  resource_group_name = azurerm_resource_group.main.name
  account_name        = azurerm_cosmosdb_account.main.name
}

resource "azurerm_cosmosdb_sql_container" "containers" {
  for_each = local.cosmos_containers

  name                  = each.value.name
  resource_group_name   = azurerm_resource_group.main.name
  account_name          = azurerm_cosmosdb_account.main.name
  database_name         = azurerm_cosmosdb_sql_database.main.name
  partition_key_paths   = each.value.partition_key
  partition_key_kind    = each.value.kind
  partition_key_version = each.value.kind == "MultiHash" ? 2 : 1
}
