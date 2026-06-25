resource "random_string" "suffix" {
  length  = 6
  lower   = true
  numeric = true
  special = false
  upper   = false
}

locals {
  suffix          = coalesce(var.unique_suffix, random_string.suffix.result)
  normalized_name = replace("${var.prefix}-${var.environment}", "_", "-")
  compact_name    = substr(replace("${var.prefix}${var.environment}${local.suffix}", "-", ""), 0, 24)

  resource_group_name = "rg-${local.normalized_name}"
  log_analytics_name  = "log-${local.normalized_name}"
  app_insights_name   = "appi-${local.normalized_name}"
  acr_name            = substr("acr${local.compact_name}", 0, 50)
  key_vault_name      = substr("kv-${local.normalized_name}-${local.suffix}", 0, 24)
  storage_name        = substr("st${local.compact_name}", 0, 24)
  cosmos_name         = substr("cdb-${local.normalized_name}-${local.suffix}", 0, 44)

  cosmos_database_name = "mergeduo"

  cosmos_containers = {
    users = {
      name          = "users"
      partition_key = ["/id"]
      kind          = "Hash"
    }
    devices = {
      name          = "devices"
      partition_key = ["/userId"]
      kind          = "Hash"
    }
    identityReservations = {
      name          = "identityReservations"
      partition_key = ["/id"]
      kind          = "Hash"
    }
    cards = {
      name          = "cards"
      partition_key = ["/userId"]
      kind          = "Hash"
    }
    fixedRules = {
      name          = "fixedRules"
      partition_key = ["/userId"]
      kind          = "Hash"
    }
    transactions = {
      name          = "transactions"
      partition_key = ["/userId", "/yearMonth"]
      kind          = "MultiHash"
    }
    monthlyAggregates = {
      name          = "monthlyAggregates"
      partition_key = ["/userId"]
      kind          = "Hash"
    }
    partnerships = {
      name          = "partnerships"
      partition_key = ["/userId"]
      kind          = "Hash"
    }
    mergeInvites = {
      name          = "mergeInvites"
      partition_key = ["/inviterUserId"]
      kind          = "Hash"
    }
    transactionsLeases = {
      name          = "transactions-leases"
      partition_key = ["/id"]
      kind          = "Hash"
    }
  }

  service_names = {
    identity     = "identity"
    profile      = "profile"
    partnership  = "partnership"
    cards        = "cards"
    fixedrules   = "fixedrules"
    transactions = "transactions"
    aggregates   = "aggregates"
  }

  service_directories = {
    identity     = "MergeDuo.Microservices/MergeDuo.Identity"
    profile      = "MergeDuo.Microservices/MergeDuo.Profile"
    partnership  = "MergeDuo.Microservices/MergeDuo.Partnership"
    cards        = "MergeDuo.Microservices/MergeDuo.Cards"
    fixedrules   = "MergeDuo.Microservices/MergeDuo.FixedRules"
    transactions = "MergeDuo.Microservices/MergeDuo.Transactions"
    aggregates   = "MergeDuo.Microservices/MergeDuo.Aggregates"
    scheduler    = "MergeDuo.Microservices/MergeDuo.Scheduler"
  }

  dockerfiles = {
    identity     = "src/MergeDuo.Identity.Api/Dockerfile"
    profile      = "src/MergeDuo.Profile.Api/Dockerfile"
    partnership  = "src/MergeDuo.Partnership.Api/Dockerfile"
    cards        = "src/MergeDuo.Cards.Api/Dockerfile"
    fixedrules   = "src/MergeDuo.FixedRules.Api/Dockerfile"
    transactions = "src/MergeDuo.Transactions.Api/Dockerfile"
    aggregates   = "src/MergeDuo.Aggregates.Api/Dockerfile"
    scheduler    = "src/MergeDuo.Scheduler.Job/Dockerfile"
  }

  container_apps = {
    for key, name in local.service_names : key => {
      name             = "aca-${var.prefix}-${name}"
      image_repository = "mergeduo-${name}"
      docker_context   = local.service_directories[key]
      dockerfile       = "${local.service_directories[key]}/${local.dockerfiles[key]}"
    }
  }

  scheduler_job = {
    name             = "acaj-${var.prefix}-scheduler"
    image_repository = "mergeduo-scheduler"
    docker_context   = local.service_directories.scheduler
    dockerfile       = "${local.service_directories.scheduler}/${local.dockerfiles.scheduler}"
  }

  web_container_app = {
    name             = "aca-${var.prefix}-web"
    image_repository = "mergeduo-web"
    docker_context   = "MergeDuo.React"
    dockerfile       = "MergeDuo.React/Dockerfile"
  }

  github_actions_microservice_vars = {
    for key, app in local.container_apps : key => {
      ACA_KIND             = "app"
      ACA_NAME             = app.name
      ACR_LOGIN_SERVER     = azurerm_container_registry.main.login_server
      ACR_NAME             = azurerm_container_registry.main.name
      AZURE_RESOURCE_GROUP = azurerm_resource_group.main.name
      DOCKER_CONTEXT       = app.docker_context
      DOCKERFILE           = app.dockerfile
      IMAGE_REPOSITORY     = app.image_repository
    }
  }

  github_actions_scheduler_vars = {
    ACA_KIND             = "job"
    ACA_NAME             = local.scheduler_job.name
    ACR_LOGIN_SERVER     = azurerm_container_registry.main.login_server
    ACR_NAME             = azurerm_container_registry.main.name
    AZURE_RESOURCE_GROUP = azurerm_resource_group.main.name
    DOCKER_CONTEXT       = local.scheduler_job.docker_context
    DOCKERFILE           = local.scheduler_job.dockerfile
    IMAGE_REPOSITORY     = local.scheduler_job.image_repository
  }

  github_actions_web_vars = {
    ACR_LOGIN_SERVER     = azurerm_container_registry.main.login_server
    ACR_NAME             = azurerm_container_registry.main.name
    AZURE_RESOURCE_GROUP = azurerm_resource_group.main.name
    DOCKER_CONTEXT       = local.web_container_app.docker_context
    DOCKERFILE           = local.web_container_app.dockerfile
    WEB_ACA_NAME         = local.web_container_app.name
    WEB_IMAGE_REPOSITORY = local.web_container_app.image_repository
  }
}
