module "application_configuration" {
  source = "./vendor/modules/aks//aks/application_configuration"

  namespace              = var.namespace
  environment            = var.environment
  azure_resource_prefix  = var.azure_resource_prefix
  service_short          = var.service_short
  config_short           = var.config_short
  secret_key_vault_short = "app"
  config_variables_path  = "${path.module}/config/${var.config}.yml"

  # Delete for non rails apps
  is_rails_application = false

  config_variables = {
    ENVIRONMENT_NAME = var.environment
    PGSSLMODE        = local.postgres_ssl_mode
  }
  secret_variables = {
    DATABASE_URL                       = module.postgres.url
    ConnectionStrings__Postgres        = module.postgres.dotnet_connection_string
    ConnectionStrings__AzureStorage    = module.storage.primary_connection_string
    ConnectionStrings__IngressStorage  = module.storage_private.primary_connection_string
    AZURE_STORAGE_ACCOUNT_NAME         = local.azure_storage_account_name
    AZURE_STORAGE_ACCESS_KEY           = local.azure_storage_access_key
    AZURE_STORAGE_CONTAINER            = local.azure_storage_container
  }
}

module "web_application" {
  source = "./vendor/modules/aks//aks/application"

  is_web = true

  namespace    = var.namespace
  environment  = var.environment
  service_name = var.service_name
  replicas     = var.replicas

  cluster_configuration_map  = module.cluster_data.configuration_map
  kubernetes_config_map_name = module.application_configuration.kubernetes_config_map_name
  kubernetes_secret_name     = module.application_configuration.kubernetes_secret_name

  docker_image = var.docker_image
  enable_logit = var.enable_logit
  web_port     = 8080

  send_traffic_to_maintenance_page = var.send_traffic_to_maintenance_page
}

locals {
  # The worker hosts a minimal HTTP health surface so the orchestrator can detect a
  # hung or crash-looping process. The shared module only exposes an HTTP probe for
  # web containers, so the worker is probed via an exec command that queries the same
  # endpoints over localhost.
  worker_health_port    = 8080
  worker_liveness_path  = "/healthz/live"
  worker_readiness_path = "/healthz/ready"
  worker_readiness_url  = "http://localhost:${local.worker_health_port}${local.worker_readiness_path}"
  worker_liveness_url   = "http://localhost:${local.worker_health_port}${local.worker_liveness_path}"
}

# Rules Engine Worker - background service that consumes the Postgres-backed queues.
module "rules_engine_worker" {
  source = "./vendor/modules/aks//aks/application"

  is_web = false

  namespace    = var.namespace
  environment  = var.environment
  service_name = "${var.service_name}-worker"
  replicas     = var.worker_replicas

  cluster_configuration_map  = module.cluster_data.configuration_map
  kubernetes_config_map_name = module.application_configuration.kubernetes_config_map_name
  kubernetes_secret_name     = module.application_configuration.kubernetes_secret_name

  docker_image = var.worker_docker_image
  enable_logit = var.enable_logit

  # Gate startup and liveness on readiness so a worker that cannot reach its database
  # or queue is detected and restarted rather than left silently stuck. The liveness
  # path is the lighter process-up signal exposed for orchestrators that probe it.
  probe_command = ["curl", "--fail", "--silent", "--show-error", local.worker_readiness_url]
}
