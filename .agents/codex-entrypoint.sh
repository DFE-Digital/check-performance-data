#!/usr/bin/env bash
set -euo pipefail
umask 077
mkdir -p "$CODEX_HOME"
mkdir -p "$NPM_CONFIG_PREFIX" "$NPM_CONFIG_CACHE"
if [[ ! -w "$NPM_CONFIG_PREFIX" || ! -w "$NPM_CONFIG_CACHE" ]]; then
  echo 'FAIL: the dedicated npm install directory or cache is not writable.' >&2
  exit 1
fi
if [[ ! -e "$CODEX_HOME/config.toml" ]]; then
  cat > "$CODEX_HOME/config.toml" <<'CONFIG'
cli_auth_credentials_store = "file"
model_provider = "headroom"

[model_providers.headroom]
name = "Headroom container proxy"
base_url = "http://headroom:8787/v1"
wire_api = "responses"
requires_openai_auth = true
supports_websockets = true
env_http_headers = { "X-Headroom-Proxy-Token" = "HEADROOM_PROXY_TOKEN" }
CONFIG
fi
if [[ -n ${AGENT_HEADROOM_HOST:-} ]]; then
  # Migrate only the original generated endpoint; preserve other user settings.
  python3 - "$CODEX_HOME/config.toml" "$AGENT_HEADROOM_HOST" <<'PY'
import re
import sys
import tomllib
from pathlib import Path
path = Path(sys.argv[1])
host = sys.argv[2]
if not re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9.-]*", host):
    raise SystemExit("FAIL: invalid Headroom DNS alias")
text = path.read_text()
provider = tomllib.loads(text).get("model_providers", {}).get("headroom", {})
if provider.get("base_url") == "http://headroom:8787/v1":
    pattern = r"(?ms)(^\[model_providers\.headroom\][^\n]*\n)(.*?)(?=^\[|\Z)"
    def migrate(match):
        body, count = re.subn(
            r'(?m)^(\s*base_url\s*=\s*)[\'\"]http://headroom:8787/v1[\'\"]',
            lambda line: line.group(1) + '"http://' + host + ':8787/v1"',
            match.group(2),
        )
        if count != 1:
            raise SystemExit("FAIL: cannot migrate default Headroom endpoint")
        return match.group(1) + body
    updated, count = re.subn(pattern, migrate, text)
    if count != 1:
        raise SystemExit("FAIL: cannot find default Headroom provider table")
    temporary = path.with_name("config.toml.agent-new")
    temporary.write_text(updated)
    temporary.replace(path)
PY
fi
if [[ -s /proxy-auth/token ]]; then
  export HEADROOM_PROXY_TOKEN
  HEADROOM_PROXY_TOKEN=$(cat /proxy-auth/token)
fi
if [[ ${1:-} == codex && -z ${HEADROOM_PROXY_TOKEN:-} ]]; then
  echo 'FAIL: Headroom proxy token is missing; start Codex using ./agent codex.' >&2
  exit 1
fi
cd /workspace
exec "$@"
