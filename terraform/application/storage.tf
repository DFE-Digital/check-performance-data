module "storage" {
  source = "./vendor/modules/aks//aks/storage_account"
#  count  = var.deploy_azure_backing_services ? 1 : 0

  name = "app"

  environment                       = var.environment
  azure_resource_prefix             = var.azure_resource_prefix
  service_short                     = var.service_short
  config_short                      = var.config_short
  cluster_configuration_map         = module.cluster_data.configuration_map
  public_network_access_enabled     = false
  infrastructure_encryption_enabled = false
  create_encryption_scope           = false
  use_private_storage               = var.use_private_storage
  # Create containers for the application (all containers are private)
  containers = [
    { name = "files" },
    { name = "question-flows" },
    # Rules-engine configuration. Holds rules.json + country-languages.json,
    # editable by business users without redeploying the worker.
    # NOTE: the rules-engine blobs must be excluded from the lifecycle delete
    # policy (a deleted rules.json would force the worker into cold-fallback);
    # delegate that to the storage_account module's lifecycle rules.
    { name = "rules-config" }
  ]
  # Configure blob lifecycle management (default: delete after 7 days)
  container_delete_retention_days = var.container_delete_retention_days

  blob_delete_after_days = var.blob_delete_after_days
}

module "storage_private" {
  source = "./vendor/modules/aks//aks/storage_account"

  name                          = "lds"
  environment                   = var.environment
  azure_resource_prefix         = var.azure_resource_prefix
  service_short                 = var.service_short
  config_short                  = var.config_short
  public_network_access_enabled = false
  cluster_configuration_map     = module.cluster_data.configuration_map
  use_private_storage           = var.use_private_storage

  infrastructure_encryption_enabled = true
  create_encryption_scope           = false

  containers = [
    { name = "files" }
  ]

  queues = [
    { name = "performance-requests" }
  ]

  container_delete_retention_days = var.container_delete_retention_days
  blob_delete_after_days          = var.blob_delete_after_days
}
