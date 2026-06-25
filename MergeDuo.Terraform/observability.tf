resource "azurerm_log_analytics_workspace" "main" {
  name                = local.log_analytics_name
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  daily_quota_gb      = var.log_analytics_daily_quota_gb
  tags                = var.tags
}

resource "azurerm_application_insights" "main" {
  name                 = local.app_insights_name
  location             = azurerm_resource_group.main.location
  resource_group_name  = azurerm_resource_group.main.name
  workspace_id         = azurerm_log_analytics_workspace.main.id
  application_type     = "web"
  daily_data_cap_in_gb = var.log_analytics_daily_quota_gb
  tags                 = var.tags
}
