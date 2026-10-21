---
name: isolation-check
description: Verify this repository's isolated coding-agent container before development or when filesystem boundaries are uncertain. Run inside the container; host operators use ./agent test-isolation.
---

1. Confirm the current directory is /workspace and AGENT_ISOLATED is 1.
2. Run `bash /workspace/.agents/tests/isolation.sh`. Report each FAIL and the exit code. The host wrapper ./agent must not be run inside the container; the engine is deliberately unavailable.
3. Confirm /workspace is the sole host-directory bind, /home/node is dedicated state and /proxy-auth is read-only. Inspect /proc/self/mountinfo without reading or printing credentials.
4. Verify `getent hosts "$AGENT_HEADROOM_HOST"` and `curl --fail "http://$AGENT_HEADROOM_HOST:8787/livez"`. The checkout-specific alias must resolve to one proxy.
5. If checks fail, stop development and report the failure. Ask the operator to run ./agent test-isolation on the host for exact host-path and mount-source verification.
6. Never request home, SSH, host config, parent-directory, socket, privileged or host-network mounts. Agent approvals must not broaden the container boundary.
