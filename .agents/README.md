# Isolated coding agents

Run these commands in your **host terminal**, from the repository root. Requirements:
Linux, rootless Podman, a Compose provider (`podman-compose` or Docker Compose used
through `podman compose`), Bash, GNU coreutils (`timeout`) and host Python 3 for
mount inspection. The runtime must support `keep-id:uid=1000,gid=1000` and container
DNS. Do not use sudo or run the host wrapper inside a container.

## Architecture

```text
host repository ── rw bind ──> codex:/workspace
                                │
                                └── agent-net ──> checkout-alias:8787 ──> provider
Codex home/config/auth/tools ── dedicated named volume
Headroom home/state          ── dedicated named volume
Proxy token                  ── named volume (read-only in Codex)
host browser:18787 ── loopback publication ──> read-only numeric stats:8788
Application/test containers ── agent-net (container DNS names)
```

The wrapper selects only `.agents/compose.yaml`, disables Compose dotenv loading,
and derives its project name from the repository path. State volumes and the
Headroom DNS alias are per checkout. This prevents the previous intermittent 401
error caused by different stacks sharing the generic `headroom` DNS name.
The named `agent-net` network is deliberately shared; attach only trusted services.

Agent containers publish no ports. Headroom has no repository bind. Both services
run as an unprivileged user with read-only root filesystems, dropped capabilities,
no-new-privileges and disposable `/tmp`. No engine sockets or host credential
directories are mounted. Codex's approvals cannot add host mounts.

The Containerfile shares a development-tools stage and has separate Codex and
Headroom targets. Build context is limited to `.agents` and an explicit allowlist;
repository files and secrets are never copied into image layers. The restored
bootstrap versions are Codex 0.159.3 and Headroom 0.39.1. State-volume Codex updates
can supersede the bootstrap version. Change build arguments and Compose image tags
together when updating an image baseline.

## First-time setup and authentication

```bash
./agent build
./agent test-isolation
./agent codex login --device-auth
./agent codex
```

Enable device-code login in ChatGPT account security settings or workspace
permissions if required. Open the displayed URL in your host browser and enter the
one-time code. No host browser callback port or host authentication mount is needed.
If this checkout's previous named volumes survive, existing authentication and
updates are reused; check with `./agent codex login status` before logging in again.

API billing can be configured by entering a key at this hidden prompt. The key
goes to Codex on stdin, without appearing in command arguments or tracked files:

```bash
./agent shell bash -c 'read -rsp "OpenAI API key: " key; printf "\n"; printf "%s" "$key" | codex login --with-api-key; unset key'
```

Authentication is stored in the dedicated Codex volume. Default config is created
only when absent; the original generated `http://headroom:8787/v1` endpoint migrates
to the checkout-specific alias without changing unrelated settings. Custom provider
endpoints remain untouched. Restart existing Codex sessions after environment changes.

Codex uses a custom Responses provider with OpenAI authentication enabled and an
`X-Headroom-Proxy-Token` header supplied from its read-only token volume. Headroom
is started directly as a network proxy; `headroom wrap codex` is never used.
Never print or copy auth.json, the proxy token or complete runtime configuration
into Git, logs or support messages. Headroom processes provider credentials in
transit and request content; treat its dedicated state as sensitive too.

See [official Codex authentication](https://learn.chatgpt.com/docs/auth),
[provider configuration](https://learn.chatgpt.com/docs/config-file/config-reference)
and [Headroom proxy documentation](https://github.com/headroomlabs-ai/headroom/blob/main/wiki/proxy.md).

## Daily use

```bash
./agent codex                          # interactive Codex, proxy started automatically
./agent codex "Explain this repository"
./agent shell                          # interactive Bash in /workspace
./agent shell git status --short
./agent build                         # build both images; layer cache is reused
./agent up                            # proxy and idle development container
./agent logs --follow headroom
./agent down                          # preserve named volumes and authentication
./agent test-isolation                 # non-zero exit on any failure
```

The wrapper prints progress before its Podman check and image builds. The initial
`podman info` check times out after 20 seconds with a five-second forced-stop grace
period. On timeout, diagnose the host runtime with
`timeout 20s podman --log-level=debug info`. Headroom startup waits for health and
fails explicitly if it never becomes ready.

Prefer `./agent up` to plain `podman compose up -d`: the latter can choose another
Compose file/project and different state volumes. `./agent down --volumes` deletes
this checkout's authentication and proxy state; use it only for an intentional reset.

### Browser stats

After startup, open **[Headroom stats](http://127.0.0.1:18787/dashboard)**.
[JSON counters](http://127.0.0.1:18787/stats) are also available. This read-only view
refreshes every five seconds and shows requests, token usage and savings. It is
distinct from any host-installed Headroom on port 8787. Session counters can reset
on proxy restart; no requests or savings are expected until Codex sends model traffic.

Host port 18787 binds only to loopback and maps to a separate listener on container
port 8788. Only allowlisted numeric aggregates are returned. It serves no filesystem
paths, prompts, request metadata, credentials, arbitrary proxy routes or admin/write
operations. The native proxy on container port 8787 remains token-protected for
network callers; browser access does not require copying a token into URLs/history.

### Codex self-updates

Keep the normal update prompt enabled and select Yes when you want to update.
npm runs as the unprivileged container user and installs into
`/home/node/.agent-tools/npm`; its cache is `/home/node/.agent-tools/npm-cache`.
Both live in Codex's dedicated state volume. The updated executable is first on
PATH, so updates persist across container recreation and image rebuilds. The old
root-owned `~/.npm` cache is unused; no sudo/chown or writable image root is needed.

Exit and relaunch Codex after an update. To update manually from the host:

```bash
./agent shell npm install -g @openai/codex
./agent shell codex --version
./agent codex
```

To remove the state-volume override and return to the image bootstrap while
retaining authentication: `./agent shell npm uninstall -g @openai/codex`.

## Isolation checks

`./agent test-isolation` inspects the actual host bind source and named volumes
for both containers, verifies loopback-only dashboard publication, then runs
`.agents/tests/isolation.sh` in Codex. It checks workspace read/write, Git, absent
host parent/home/SSH/config/socket paths, dropped host endpoint variables, read-only
root, unique proxy DNS, correct-token acceptance, missing/wrong-token rejection,
numeric-only dashboard routes and writable npm paths confined to dedicated state.
The host supplies exact home/repository/parent paths as test metadata, never mounts.
Every check prints PASS or FAIL and failure returns non-zero.

Inside an agent container, run `bash .agents/tests/isolation.sh` directly.
Authentication to a model provider is separate; filesystem/proxy tests need no
OpenAI credentials or billable model requests.

## Repository skills

Create `.agents/skills/<skill-name>/SKILL.md` with YAML `name` and `description`
front matter, followed by focused instructions. Codex discovers project-local
skills directly from `/workspace`; installing into host `~/.codex` is unnecessary.
Invoke the example with `$isolation-check`. Existing skills are preserved.
See [official skill discovery](https://learn.chatgpt.com/docs/build-skills).

## Adding another harness

Add a Containerfile target based on `development`, install the harness there and
define a Compose service modelled on `codex`. Give it its own named home/state
volume, the same repository bind and security options, and membership in agent-net.
Supply AGENT_HEADROOM_HOST using the same namespace interpolation as Codex. Configure
its provider URL to that alias and its proxy-token header using documented harness
settings. Add a wrapper case for `claude`, `aider` or `opencode` selecting the new
service. Do not share Codex authentication or use host wrapping/engine sockets.

The generic image includes Git, curl, Bash, jq, certificates, Node/npm, Python/pip,
compiler tools, CMake, pkg-config, zip/unzip and ripgrep. Add a separate image target
with the application's .NET SDK or run builds in application/test containers; the
generic bootstrap image does not include .NET.

## Application and test networking

Start the agent environment first. In the application's own Compose file or a
host-selected application override, attach services to the external network:

```yaml
services:
  web:
    networks:
      - agent-net
    # Listen on 0.0.0.0 inside the container.
networks:
  agent-net:
    external: true
    name: agent-net
```

Preserve other networks/dependencies the application needs. Agents can then use
`curl http://web:8080/` or configure integration tests against `http://web:8080`,
using the actual container port. `localhost` inside Codex means its own container;
application host-port publication is unnecessary. Stop other projects attached to
agent-net before `./agent down` if they keep the shared network in use.

## Security boundary

The writable repository bind includes ignored files already in the checkout. Keep
repository-local secrets out of the agent checkout if agents must not read them.
Symlinks pointing outside the checkout resolve in the container filesystem; they
cannot expose host targets. Worktrees with Git metadata outside the checkout may
not work: use a standalone clone instead of mounting the external Git directory.

The image's Linux files and dedicated volume contents are readable, not the host's
corresponding directories. Codex approval/sandbox settings cannot add mounts or
invoke a host engine. An agent can edit the writable wrapper/Compose sources, so
review those changes before executing them on the host. Host operators control
the boundary; an in-container approval does not broaden it.

Never mount:

- `$HOME`, `~/.ssh`, `~/.config`, or host credential stores.
- The repository parent, other repositories, `/`, `/home` or `/Users`.
- Podman/Docker sockets, SSH-agent sockets or remote engine endpoints.
- Host devices or filesystem roots, including through privileged containers.

Never add privileged mode, host PID/user namespaces, host networking, extra
capabilities or a host execution/filesystem service. Rootless containers share the
host kernel and retain network egress; this is filesystem isolation, not a VM or
network isolation policy. Only attach trusted application services, without a
host command/filesystem API, and never forward host credential-bearing environment
variables wholesale into agents. Preserve secret scanning and push protection.
