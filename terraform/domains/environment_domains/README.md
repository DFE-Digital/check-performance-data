# Development source IP restrictions

Only `config/development.tfvars.json` enables `restrict_source_ips`. QA,
preproduction and production retain their existing WAF behavior. Development
disables the built-in AKS allowance so it cannot bypass the source allowlist.

## GitHub secret

Create an **Actions repository secret** named **`DEV_ALLOWED_SOURCE_IPS`** under
Settings → Secrets and variables → Actions → Repository secrets. Use a secret,
not a GitHub configuration variable. The domains deployment job uses the
production GitHub environment for the shared DNS infrastructure credentials;
a secret scoped only to the development environment will not reach that job.

Its value must be a JSON array containing 1–17 public CIDRs. For example, the
masked structure below illustrates individual IPv4 addresses and a subnet:

```json
["218.x.x.x/32", "219.x.x.x/32", "218.x.x.0/24"]
```

**These masked examples are not valid input.** Replace every `x` with the actual
address only when entering the secret. Use `/32` for individual public IPv4
addresses, `/128` for individual public IPv6 addresses, or the intended network
prefix for a subnet. Subnets must use their network address. Use the public
egress address visible to Front Door, including monitoring or VPN egress where
needed. Do not put actual addresses in this README, Terraform files, PRs or logs.

The workflow injects the JSON through **`TF_VAR_allowed_source_ips`** for
development only and masks each address and CIDR. Terraform validates the count,
network address and public-unicast ranges for both CI and local use. The policy
conservatively excludes special-purpose allocations, including allocations with
globally reachable exceptions. IPv6 sources must be within `2000::/3`. Use
Terraform's canonical address spelling (compressed lowercase IPv6).
A missing or invalid secret fails the development domains deployment; the other
domain environments continue because the matrix disables fail-fast.
For local plan/apply, inject
the same environment variable through your secure secret tooling; do not paste
it into shell commands or save it in tracked tfvars. Development requires a
nonempty allowlist. Other environments receive an empty list.

## Rule behavior and verification

The shared module accepts one address per rule. Development explicitly disables
the module's nonproduction geographic restriction, replacing it with this source
allowlist. The emergency block at priority 5 and rate limit at 100 run before
source allow rules at priorities 101–117. Catch-all blocks at 200 and 201 deny
other IPv4 and IPv6 sources; the policy returns HTTP 429. Rules match
`SocketAddr`, the address connecting to Front Door, rather than the client-supplied
`X-Forwarded-For` header. For VPNs and proxies, allow the public egress address.
Matching Allow rules still terminate subsequent WAF evaluation; adding later
protections requires reviewing these priorities and the shared module interface.

The repository-owned deployment action explicitly skips its external health check for development
because GitHub-hosted runner addresses are not allowlisted. After deployment,
verify `/healthcheck` from an allowed network and verify denial from an unlisted
network. Health checks remain enabled for the other environments. Review all
four environment plans before applying; only development should gain source IP
restrictions. Secret changes take effect on the next development domains apply.

Run `python3 scripts/test-domain-source-ips.py` from the repository root to check
the policy expressions without Azure credentials or remote state. A full
development plan still requires the real list: do not disable restrictions or use
a placeholder list in a plan intended for apply. Plans for unrelated DNS work
must include the current security configuration to avoid accidentally changing it.

## Origin protection

These WAF rules protect the Front Door domain only. They do not restrict direct
access to the AKS origin hostname. Before treating development as private, verify
that ingress accepts only Front Door backend traffic and checks the expected
`X-Azure-FDID`, or use an equivalent private origin arrangement. The ID header
alone is not sufficient because direct clients can supply it. Confirm ingress
controller support and the deployment identity's access before applying origin
controls; preserve Front Door and Kubernetes health probes.

## Confidentiality

`sensitive = true` redacts normal Terraform output; it does not encrypt or omit
addresses from Terraform state or saved plans. Restrict access to the remote
state, saved plan artifacts, Azure WAF configuration and deployment credentials.
Do not publish raw plan JSON or enable shell tracing/debug logging with these
values. GitHub masking is a fallback, not permission to print the secret.

The workflow secret is available to trusted deployment code. Control who can
edit workflows and trigger deployments. Authorized readers of state or the
Azure WAF configuration can still see the addresses.
