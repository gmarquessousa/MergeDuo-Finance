variable "subscription_id" {
  description = "Azure subscription id. Leave null to use ARM_SUBSCRIPTION_ID or Azure CLI defaults when supported by the provider."
  type        = string
  default     = null
}

variable "tenant_id" {
  description = "Azure tenant id. Leave null to use Azure CLI defaults."
  type        = string
  default     = null
}

variable "location" {
  description = "Azure region for all production resources."
  type        = string
  default     = "Brazil South"
}

variable "prefix" {
  description = "Short resource name prefix."
  type        = string
  default     = "mergeduo"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,20}[a-z0-9]$", var.prefix))
    error_message = "prefix must be lowercase alphanumeric with optional hyphens, start with a letter, and end with a letter or number."
  }
}

variable "environment" {
  description = "Environment suffix."
  type        = string
  default     = "prod"
}

variable "unique_suffix" {
  description = "Optional deterministic suffix for globally unique Azure names. When null, Terraform creates a random suffix."
  type        = string
  default     = null

  validation {
    condition     = var.unique_suffix == null || can(regex("^[a-z0-9]{3,12}$", var.unique_suffix))
    error_message = "unique_suffix must be null or 3-12 lowercase letters/numbers."
  }
}

variable "github_owner" {
  description = "GitHub organization or user that owns the monorepo."
  type        = string
}

variable "github_repo" {
  description = "GitHub monorepo name."
  type        = string
}

variable "github_branch" {
  description = "GitHub branch allowed to federate into Azure."
  type        = string
  default     = "main"
}

variable "github_environment" {
  description = "Optional GitHub environment allowed to federate into Azure. When set, Terraform creates an environment-scoped federated credential."
  type        = string
  default     = null
}

variable "initial_container_image" {
  description = "Bootstrap image used before GitHub Actions deploys real service images."
  type        = string
  default     = "mcr.microsoft.com/dotnet/samples:aspnetapp"
}

variable "container_cpu" {
  description = "Default Container Apps CPU."
  type        = number
  default     = 0.25
}

variable "container_memory" {
  description = "Default Container Apps memory."
  type        = string
  default     = "0.5Gi"
}

variable "container_min_replicas" {
  description = "Default Container Apps min replicas."
  type        = number
  default     = 0
}

variable "container_max_replicas" {
  description = "Default Container Apps max replicas."
  type        = number
  default     = 1
}

variable "log_analytics_daily_quota_gb" {
  description = "Daily ingestion cap for Log Analytics in GB. Lower values reduce cost risk; logs can be dropped after the cap is reached."
  type        = number
  default     = 0.25
}

variable "tags" {
  description = "Tags applied to all supported resources."
  type        = map(string)
  default = {
    project     = "MergeDuo"
    environment = "prod"
    managed_by  = "terraform"
  }
}
