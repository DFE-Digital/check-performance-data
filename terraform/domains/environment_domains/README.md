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
development only, validates it without printing it, and masks each address and
CIDR. A missing or invalid secret stops deployment. For local plan/apply, inject
the same environment variable through your secure secret tooling; do not paste
it into shell commands or save it in tracked tfvars. Development requires a
nonempty allowlist. Other environments receive an empty list.

## Rule behavior and verification

The shared module accepts one address per rule. Allow rules use priorities below
the geographic rule at 20, avoiding reserved priorities 5 and 10. This leaves
17 available priorities; more entries require a deliberate module change.
The built-in rate limit remains configured, but an earlier matching Allow rule
terminates WAF evaluation, so allowed sources bypass that rate limit. All other
IPv4 and IPv6 sources are blocked; the shared policy returns HTTP 429.

The deployment action's external health check is disabled for development
because GitHub-hosted runner addresses are not allowlisted. After deployment,
verify `/healthcheck` from an allowed network and verify denial from an unlisted
network. Health checks remain enabled for the other environments. Review all
four environment plans before applying; only development should gain source IP
restrictions. Secret changes take effect on the next development domains apply.

## Confidentiality

`sensitive = true` redacts normal Terraform output; it does not encrypt or omit
addresses from Terraform state or saved plans. Restrict access to the remote
state, saved plan artifacts, Azure WAF configuration and deployment credentials.
Do not publish raw plan JSON or enable shell tracing/debug logging with these
values. GitHub masking is a fallback, not permission to print the secret.

The workflow secret is available to trusted deployment code. Control who can
edit workflows and trigger deployments. Authorized readers of state or the
Azure WAF configuration can still see the addresses.
