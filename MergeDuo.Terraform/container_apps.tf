resource "azurerm_container_app_environment" "main" {
  name                       = "cae-${local.normalized_name}"
  location                   = azurerm_resource_group.main.location
  resource_group_name        = azurerm_resource_group.main.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id
  tags                       = var.tags

  workload_profile {
    name                  = "Consumption"
    workload_profile_type = "Consumption"
  }
}

resource "azurerm_container_app" "app" {
  for_each = local.container_apps

  name                         = each.value.name
  resource_group_name          = azurerm_resource_group.main.name
  container_app_environment_id = azurerm_container_app_environment.main.id
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.runtime[each.key].id]
  }

  lifecycle {
    ignore_changes = [
      registry,
      secret,
      template[0].container[0].env,
      template[0].container[0].image
    ]
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  template {
    min_replicas = var.container_min_replicas
    max_replicas = var.container_max_replicas

    container {
      name   = each.key
      image  = var.initial_container_image
      cpu    = var.container_cpu
      memory = var.container_memory
    }
  }

  depends_on = [
    azurerm_role_assignment.runtime_acr_pull,
    azurerm_cosmosdb_sql_role_assignment.runtime_cosmos_contributor
  ]
}

resource "azurerm_container_app" "web" {
  name                         = local.web_container_app.name
  resource_group_name          = azurerm_resource_group.main.name
  container_app_environment_id = azurerm_container_app_environment.main.id
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.web_runtime.id]
  }

  lifecycle {
    ignore_changes = [
      registry,
      template[0].container[0].image
    ]
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  template {
    min_replicas = var.container_min_replicas
    max_replicas = var.container_max_replicas

    container {
      name   = "web"
      image  = var.initial_container_image
      cpu    = var.container_cpu
      memory = var.container_memory

      env {
        name  = "PORT"
        value = "8080"
      }

      env {
        name  = "NODE_ENV"
        value = "production"
      }

      env {
        name  = "IDENTITY_API_BASE_URL"
        value = "https://${local.container_apps.identity.name}.${azurerm_container_app_environment.main.default_domain}"
      }

      env {
        name  = "PROFILE_API_BASE_URL"
        value = "https://${local.container_apps.profile.name}.${azurerm_container_app_environment.main.default_domain}"
      }

      env {
        name  = "PARTNERSHIP_API_BASE_URL"
        value = "https://${local.container_apps.partnership.name}.${azurerm_container_app_environment.main.default_domain}"
      }

      env {
        name  = "CARDS_API_BASE_URL"
        value = "https://${local.container_apps.cards.name}.${azurerm_container_app_environment.main.default_domain}"
      }

      env {
        name  = "FIXED_RULES_API_BASE_URL"
        value = "https://${local.container_apps.fixedrules.name}.${azurerm_container_app_environment.main.default_domain}"
      }

      env {
        name  = "TRANSACTIONS_API_BASE_URL"
        value = "https://${local.container_apps.transactions.name}.${azurerm_container_app_environment.main.default_domain}"
      }

      env {
        name  = "AGGREGATES_API_BASE_URL"
        value = "https://${local.container_apps.aggregates.name}.${azurerm_container_app_environment.main.default_domain}"
      }
    }
  }

  depends_on = [
    azurerm_role_assignment.web_acr_pull,
    azurerm_container_app.app
  ]
}

resource "azurerm_container_app_job" "scheduler" {
  name                         = local.scheduler_job.name
  location                     = azurerm_resource_group.main.location
  resource_group_name          = azurerm_resource_group.main.name
  container_app_environment_id = azurerm_container_app_environment.main.id
  replica_timeout_in_seconds   = 600
  replica_retry_limit          = 1
  workload_profile_name        = "Consumption"
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.runtime["scheduler"].id]
  }

  lifecycle {
    ignore_changes = [
      registry,
      secret,
      template[0].container[0].env,
      template[0].container[0].image
    ]
  }

  schedule_trigger_config {
    cron_expression          = "0 6 * * *"
    parallelism              = 1
    replica_completion_count = 1
  }

  template {
    container {
      name   = "scheduler"
      image  = var.initial_container_image
      cpu    = var.container_cpu
      memory = var.container_memory
    }
  }

  depends_on = [
    azurerm_role_assignment.runtime_acr_pull,
    azurerm_cosmosdb_sql_role_assignment.runtime_cosmos_contributor,
    azurerm_container_app.app
  ]
}
