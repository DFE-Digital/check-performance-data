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

# GitHub Actions repository secret DEV_ALLOWED_SOURCE_IPS supplies this JSON list
# through TF_VAR_allowed_source_ips. See README.md for masked format examples.
variable "allowed_source_ips" {
  type        = list(string)
  default     = []
  description = "Public source CIDRs for development. Inject through TF_VAR_allowed_source_ips; never commit actual addresses."
  sensitive   = true
  nullable    = false

  validation {
    condition = length(var.allowed_source_ips) <= 17 && alltrue([
      for cidr in var.allowed_source_ips : try(
        cidrhost(cidr, 0) == split("/", cidr)[0] &&
        length(split("/", cidr)) == 2 &&
        (strcontains(cidr, ":") ? (
          cidrhost("${split("/", cidr)[0]}/3", 0) == "2000::" &&
          tonumber(split("/", cidr)[1]) >= 3 &&
          alltrue([
            for excluded in local.excluded_source_ipv6_cidrs :
            cidrhost("${split("/", cidr)[0]}/${min(tonumber(split("/", cidr)[1]), tonumber(split("/", excluded)[1]))}", 0) !=
            cidrhost("${split("/", excluded)[0]}/${min(tonumber(split("/", cidr)[1]), tonumber(split("/", excluded)[1]))}", 0)
          ])
          ) : alltrue([
            for excluded in local.excluded_source_ipv4_cidrs :
            cidrhost("${split("/", cidr)[0]}/${min(tonumber(split("/", cidr)[1]), tonumber(split("/", excluded)[1]))}", 0) !=
            cidrhost("${split("/", excluded)[0]}/${min(tonumber(split("/", cidr)[1]), tonumber(split("/", excluded)[1]))}", 0)
        ])), false
      )
    ])
    error_message = "Supply at most 17 canonical public-unicast IPv4/IPv6 network CIDRs. Special-purpose ranges are excluded. Use /32 or /128 for individual addresses."
  }
}

variable "restrict_source_ips" {
  type        = bool
  default     = false
  nullable    = false
  description = "Enable the source CIDR allowlist. Enabled only in development configuration."

  validation {
    condition     = !var.restrict_source_ips || (length(var.allowed_source_ips) > 0 && !var.allow_aks)
    error_message = "Source IP restrictions require a nonempty allowlist and allow_aks=false to prevent an AKS bypass."
  }
}
