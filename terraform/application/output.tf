output "url" {
  value = module.web_application.url
}

output "external_urls" {
  value = [
    module.web_application.url
  ]
}

output "storage_account_name" {
  value = module.storage.name
}
