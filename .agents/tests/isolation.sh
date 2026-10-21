#!/usr/bin/env bash
set -uo pipefail
failures=0
check() {
  local label=$1
  shift
  if "$@"; then printf 'PASS: %s\n' "$label";
  else printf 'FAIL: %s\n' "$label"; failures=$((failures+1)); fi
}
absent() { [[ ! -e "$1" && ! -L "$1" ]]; }
writable_workspace() {
  local probe
  probe=$(mktemp /workspace/.agent-isolation.XXXXXX) || return 1
  printf 'isolation probe\n' > "$probe" && rm -- "$probe"
}
check 'working directory is /workspace' test "$PWD" = /workspace
check '/workspace is readable' test -r /workspace/agent
check '/workspace is writable' writable_workspace
check 'git works in repository' git -C /workspace rev-parse --is-inside-work-tree
check 'container is non-root' test "$(id -u)" -ne 0
check 'npm installs and caches only in writable dedicated harness state' python3 -c '
import os, subprocess, tempfile
from pathlib import Path
expected = {"NPM_CONFIG_PREFIX": "/home/node/.agent-tools/npm", "NPM_CONFIG_CACHE": "/home/node/.agent-tools/npm-cache"}
for key, value in expected.items():
    assert os.environ.get(key) == value, "unexpected npm state directory"
    directory = Path(value).resolve()
    assert directory.is_relative_to(Path("/home/node")), "npm state escapes harness volume"
    with tempfile.TemporaryFile(dir=directory) as probe:
        probe.write(b"writable")
assert subprocess.check_output(["npm", "prefix", "-g"], text=True).strip() == expected["NPM_CONFIG_PREFIX"]
assert subprocess.check_output(["npm", "config", "get", "cache"], text=True).strip() == expected["NPM_CONFIG_CACHE"]
'
check 'parent traversal reaches container root' test "$(cd /workspace/.. && pwd -P)" = /
for path in "${ISOLATION_HOST_PARENT:-/home/darkangel/Repos}" \
  "${ISOLATION_HOST_REPO:-/home/darkangel/Repos/check-performance-data}" \
  "${ISOLATION_HOST_HOME:-/home/darkangel}" \
  "${ISOLATION_HOST_HOME:-/home/darkangel}/.ssh" \
  "${ISOLATION_HOST_HOME:-/home/darkangel}/.config" \
  /home/darkangel /Users /host /mnt/host /run/host /root/.ssh /root/.config \
  /var/run/docker.sock /run/docker.sock /run/podman/podman.sock \
  "/run/user/$(id -u)/podman/podman.sock"; do
  check "host path unavailable: $path" absent "$path"
done
check 'no engine socket anywhere under /run or /tmp' python3 -c '
import os, stat
for root in ("/run", "/tmp"):
    for directory, _, names in os.walk(root):
        for name in names:
            if name.endswith(".sock") and ("podman" in name or "docker" in name):
                assert not stat.S_ISSOCK(os.stat(os.path.join(directory, name)).st_mode)
'
check 'no container-engine or SSH-agent environment endpoints' bash -c \
  '[[ -z ${DOCKER_HOST:-} && -z ${CONTAINER_HOST:-} && -z ${SSH_AUTH_SOCK:-} ]]'
headroom_host=${AGENT_HEADROOM_HOST:-headroom}
check 'Headroom resolves through container DNS' getent hosts "$headroom_host"
check 'Headroom responds over agent-net' curl --fail --silent --show-error "http://$headroom_host:8787/livez"
printf '\n'
check 'checkout DNS is unique; its token is accepted; missing and wrong tokens are rejected' python3 -c '
import os, socket, json
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.error import HTTPError
host = os.environ["AGENT_HEADROOM_HOST"]
assert len({r[4][0] for r in socket.getaddrinfo(host, 8787)}) == 1, "ambiguous proxy DNS"
token = Path("/proxy-auth/token").read_text().strip()
assert token, "missing proxy token"
url = "http://" + host + ":8787/stats"
request = Request(url, headers={"X-Headroom-Proxy-Token": token})
with urlopen(request, timeout=10) as response:
    assert response.status == 200
    assert "requests" in json.load(response)
for headers in ({}, {"X-Headroom-Proxy-Token": "invalid-isolation-probe"}):
    try:
        urlopen(Request(url, headers=headers), timeout=10)
    except HTTPError as error:
        assert error.code == 401
    else:
        raise AssertionError("proxy accepted missing or incorrect token")
'
check 'browser view serves only numeric stats and rejects sensitive routes and writes' python3 -c '
import json, os
from urllib.request import Request, urlopen
from urllib.error import HTTPError
base = "http://" + os.environ["AGENT_HEADROOM_HOST"] + ":8788"
with urlopen(base + "/dashboard", timeout=10) as response:
    assert response.status == 200
with urlopen(base + "/stats", timeout=10) as response:
    data = json.load(response)
assert set(data) == {"requests", "tokens", "savings"}
assert all(isinstance(value, (int, float)) for counters in data.values() for value in counters.values())
for path in ("/v1/responses", "/settings", "/etc/passwd", "/proxy-auth/token"):
    try:
        urlopen(base + path, timeout=10)
    except HTTPError as error:
        assert error.code == 404
    else:
        raise AssertionError("unexpected dashboard route")
try:
    urlopen(Request(base + "/stats", data=b"{}", method="POST"), timeout=10)
except HTTPError as error:
    assert error.code == 501
else:
    raise AssertionError("dashboard accepted a write")
'
check 'root filesystem is read-only and only expected data/device mounts exist' python3 -c '
from pathlib import Path
import stat
mounts = [line.split() for line in Path("/proc/self/mountinfo").read_text().splitlines()]
root = next(m for m in mounts if m[4] == "/")
assert "ro" in root[5].split(","), "writable root"
expected = {"/workspace", "/home/node", "/proxy-auth", "/etc/hosts", "/etc/hostname", "/etc/resolv.conf", "/run/.containerenv"}
devices = {"/dev/null", "/dev/zero", "/dev/full", "/dev/tty", "/dev/random", "/dev/urandom", "/proc/kcore", "/proc/keys", "/proc/latency_stats", "/proc/timer_list", "/proc/interrupts"}
for m in mounts:
    target, fs = m[4], m[m.index("-") + 1]
    if target in devices and fs == "devtmpfs":
        assert stat.S_ISCHR(Path(target).stat().st_mode), "unexpected device type"
        continue
    if target != "/" and fs not in {"proc", "sysfs", "tmpfs", "devpts", "cgroup2", "mqueue"}:
        assert target in expected, "unexpected mount: " + target
assert any(m[4] == "/workspace" for m in mounts), "missing workspace mount"
assert any(m[4] == "/proxy-auth" and "ro" in m[5].split(",") for m in mounts), "writable proxy token mount"
'
printf '\nIsolation checks: %s failure(s)\n' "$failures"
((failures == 0))
