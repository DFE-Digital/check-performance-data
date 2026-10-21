"""Host-side inspection: verify bind sources, not just container-visible paths."""
import json
import os
import sys

repository, project = sys.argv[1:]
containers = json.load(sys.stdin)
assert len(containers) == 2, "expected Codex and Headroom containers"
for container in containers:
    name = container["Name"].lstrip("/")
    is_codex = name.endswith("-codex-1")
    expected = {
        "/home/node": f"{project}_{'codex' if is_codex else 'headroom'}-state",
        "/proxy-auth": f"{project}_proxy-auth",
    }
    seen = set()
    for mount in container["Mounts"]:
        destination = mount["Destination"]
        if mount["Type"] == "bind":
            assert is_codex and destination == "/workspace", "unexpected host bind"
            assert os.path.realpath(mount["Source"]) == repository, "wrong repository bind source"
            assert mount["RW"], "repository must be writable"
        else:
            assert mount["Type"] == "volume", "unexpected mount type"
            assert destination in expected, "unexpected volume target"
            assert mount["Name"] == expected[destination], "unexpected volume source"
            assert mount["RW"] == (destination != "/proxy-auth" or not is_codex), "wrong volume permissions"
        seen.add(destination)
    assert seen == set(expected) | ({"/workspace"} if is_codex else set()), "missing mounts"
    assert "agent-net" in container["NetworkSettings"]["Networks"], "missing agent-net"
    assert container["HostConfig"]["ReadonlyRootfs"], "writable root filesystem"
    assert not container["HostConfig"]["Privileged"], "privileged container"
    ports = container["HostConfig"].get("PortBindings") or {}
    if is_codex:
        assert not ports, "agent has published ports"
    else:
        assert set(ports) == {"8788/tcp"}, "unexpected Headroom port publication"
        assert ports["8788/tcp"] == [{"HostIp": "127.0.0.1", "HostPort": "18787"}], "dashboard must bind loopback only"
    print(f"PASS: host inspection of {name}: approved mounts, ports and agent-net")
