locals {
  # Preserve the emergency block (5) and rate limit (100). Development's
  # source restriction replaces the module's nonproduction geographic rule.

  # The upstream module expects one scalar match_values value per rule and
  # unconditionally reads duration/limit, even for MatchRule entries.
  source_ip_rules = var.restrict_source_ips ? concat([
    # Only the count is public; keep individual address values sensitive. The
    # shared module uses the rule list length in its resource count expression.
    for index in range(nonsensitive(length(var.allowed_source_ips))) : {
      agent          = "AllowSource${index + 1}"
      priority       = 101 + index
      enabled        = true
      type           = "MatchRule"
      action         = "Allow"
      match_variable = "SocketAddr"
      operator       = "IPMatch"
      match_values   = var.allowed_source_ips[index]
      selector       = ""
      duration       = null
      limit          = null
    }
    ], [
    for index, cidr in ["0.0.0.0/0", "::/0"] : {
      agent          = "BlockOther${index == 0 ? "IPv4" : "IPv6"}"
      priority       = 200 + index
      enabled        = true
      type           = "MatchRule"
      action         = "Block"
      match_variable = "SocketAddr"
      operator       = "IPMatch"
      match_values   = cidr
      selector       = ""
      duration       = null
      limit          = null
    }
  ]) : []
}
