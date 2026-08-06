provider "google" {
  project = "check-performance-data"
}

module "dfe_analytics" {
  count  = var.enable_dfe_analytics_federated_auth ? 1 : 0
  source = "./vendor/modules/aks//aks/dfe_analytics"

  azure_resource_prefix = var.azure_resource_prefix
  cluster               = var.cluster
  namespace             = var.namespace
  service_short         = var.service_short
  environment           = var.environment
  gcp_keyring           = "cypd-key-ring"
  gcp_key               = "cypd-key"
  gcp_taxonomy_id       = "7450003253557751837"
  gcp_policy_tag_id     = "7087412595261736586"

  gcp_table_deletion_protection = var.gcp_table_deletion_protection
}
