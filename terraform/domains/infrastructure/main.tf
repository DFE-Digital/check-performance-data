module "domains_infrastructure" {
  source                 = "./vendor/modules/domains//domains/infrastructure"
  hosted_zone            = var.hosted_zone
  deploy_default_records = var.deploy_default_records

  # Keep the Front Door access, health-probe and WAF logs (#686). The module creates a Log
  # Analytics workspace "<front door name>-log" in the domains resource group and a diagnostic
  # setting for every log category; without it the logs are dropped. The platform team turns
  # this on only while troubleshooting, for cost; we accepted the cost to keep the logs (#619).
  azure_enable_monitoring = true
}
