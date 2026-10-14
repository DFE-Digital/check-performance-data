variable "hosted_zone" {
  type    = map(any)
  default = {}
}

variable "rate_limit_max" {
  type        = number
  default     = null
  description = "create a block rule that will limit any IP that goes above var.rate_limit_max over a 5 minute period"
}

variable "allow_aks" {
  type        = bool
  nullable    = false
  default     = false
  description = "allow all ingress from the aks cluster when a rate limit rule is active"
}

variable "block_ip" {
  type        = bool
  nullable    = false
  default     = false
  description = "create a specific IP block rule that is disabled. Makes it easier to add an IP and then enable the rule if required."
}

# New variable: list of allowed source IPs/CIDRs to be injected at deploy time (sensitive)
# This should be provided via pipeline secrets or a secure variable store; do NOT check real IPs into git.
variable "allowed_source_ips" {
  type        = list(string)
  default     = []
  description = "List of IPs/CIDRs to allow through Front Door/WAF. Provide via TF_VAR_allowed_source_ips or secret-managed tfvars."
  sensitive   = true
}

# Custom rate_limit rules - allows the calling repo to declare MatchRule/RateLimitRule entries
# Example structure matches the terraform-modules environment_domains rate_limit expectation.
variable "rate_limit" {
  type = list(object({
    agent          = optional(string)
    priority       = optional(number)
    duration       = optional(number)
    limit          = optional(number)
    selector       = optional(string)
    operator       = optional(string)
    match_values   = optional(string)
    match_variable = optional(string)
    type           = optional(string)
    action         = optional(string)
    enabled        = optional(string)
  }))
  default = null
  description = "Custom WAF rules to pass through to the environment_domains module. Use this to declare Allow/Block MatchRule entries."
}
