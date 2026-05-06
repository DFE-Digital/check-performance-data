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