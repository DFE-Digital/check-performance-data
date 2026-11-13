locals {
  # Conservative public-unicast policy: special-purpose allocations are excluded,
  # including allocations with individually globally reachable exceptions.
  excluded_source_ipv4_cidrs = [
    "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8",
    "169.254.0.0/16", "172.16.0.0/12", "192.0.0.0/24", "192.0.2.0/24",
    "192.88.99.0/24", "192.168.0.0/16", "198.18.0.0/15",
    "198.51.100.0/24", "203.0.113.0/24", "224.0.0.0/4", "240.0.0.0/4",
  ]
  excluded_source_ipv6_cidrs = ["2001::/23", "2001:db8::/32", "2002::/16", "3fff::/20"]

}
