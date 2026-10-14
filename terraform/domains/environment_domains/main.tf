# Used to create domains to be managed by front door.
module "domains" {
  for_each            = var.hosted_zone
  source              = "./vendor/modules/domains//domains/environment_domains"
  zone                = each.key
  front_door_name     = each.value.front_door_name
  resource_group_name = each.value.resource_group_name
  domains             = each.value.domains
  environment         = each.value.environment_short
  host_name           = each.value.origin_hostname
  cached_paths        = try(each.value.cached_paths, [])
  rate_limit_max      = var.rate_limit_max
  allow_aks           = var.allow_aks
  block_ip            = var.block_ip
  # Pass through the allowlist variable. The vendor/module must be updated to consume this
  # and translate into a MatchRule allow entry (or alternatively the calling repo can craft
  # a rate_limit list using the variable and pass it as rate_limit). Provide values via
  # TF_VAR_allowed_source_ips in CI/pipeline, do NOT commit real IPs to git.
  allowed_source_ips  = var.allowed_source_ips
}
