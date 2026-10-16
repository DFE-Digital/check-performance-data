run "unrestricted_environment" {
  command = plan
  assert {
    condition     = length(local.source_ip_rules) == 0
    error_message = "Other environments must not acquire source restriction rules."
  }
}

run "restricted_environment" {
  command = plan
  variables {
    restrict_source_ips = true
    allowed_source_ips  = ["8.8.8.8/32", "2606:4700::/32"]
  }
  assert {
    condition = alltrue([
      for rule in local.source_ip_rules : rule.match_variable == "SocketAddr"
    ])
    error_message = "Forwarding headers must not control access."
  }
  assert {
    condition = alltrue([
      for rule in local.source_ip_rules : rule.action != "Allow" || (rule.priority > 100 && rule.priority < 200)
    ])
    error_message = "Allowed sources must still pass the emergency block and rate limit."
  }
  assert {
    condition     = length(distinct([for rule in local.source_ip_rules : rule.priority])) == length(local.source_ip_rules)
    error_message = "Rule priorities must be unique."
  }
}

run "missing_allowlist" {
  command = plan
  variables {
    restrict_source_ips = true
  }
  expect_failures = [var.restrict_source_ips]
}

run "aks_bypass" {
  command = plan
  variables {
    restrict_source_ips = true
    allowed_source_ips  = ["8.8.8.8/32"]
    allow_aks           = true
  }
  expect_failures = [var.restrict_source_ips]
}

run "host_bits" {
  command = plan
  variables {
    allowed_source_ips = ["8.8.8.8/24"]
  }
  expect_failures = [var.allowed_source_ips]
}

run "private_ipv4" {
  command = plan
  variables {
    allowed_source_ips = ["10.0.0.0/8"]
  }
  expect_failures = [var.allowed_source_ips]
}

run "overlaps_private_ipv4" {
  command = plan
  variables {
    allowed_source_ips = ["8.0.0.0/6"]
  }
  expect_failures = [var.allowed_source_ips]
}

run "private_ipv6" {
  command = plan
  variables {
    allowed_source_ips = ["fc00::/7"]
  }
  expect_failures = [var.allowed_source_ips]
}

run "documentation_ipv6" {
  command = plan
  variables {
    allowed_source_ips = ["2001:db8::/32"]
  }
  expect_failures = [var.allowed_source_ips]
}

run "all_ipv4" {
  command = plan
  variables {
    allowed_source_ips = ["0.0.0.0/0"]
  }
  expect_failures = [var.allowed_source_ips]
}

run "all_ipv6" {
  command = plan
  variables {
    allowed_source_ips = ["::/0"]
  }
  expect_failures = [var.allowed_source_ips]
}

run "malformed_cidr" {
  command = plan
  variables {
    allowed_source_ips = ["invalid"]
  }
  expect_failures = [var.allowed_source_ips]
}

run "too_many_sources" {
  command = plan
  variables {
    allowed_source_ips = [for index in range(18) : "8.8.8.8/32"]
  }
  expect_failures = [var.allowed_source_ips]
}
