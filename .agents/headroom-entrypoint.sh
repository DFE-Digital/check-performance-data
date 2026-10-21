#!/usr/bin/env bash
set -euo pipefail
umask 077
if [[ ! -s /proxy-auth/token ]]; then
  python3 -c 'import secrets; from pathlib import Path; Path("/proxy-auth/token").write_text(secrets.token_hex(32))'
fi
export HEADROOM_PROXY_TOKEN
HEADROOM_PROXY_TOKEN=$(cat /proxy-auth/token)
# Home is a dedicated volume, never the host home. Do not log requests/auth.
python3 /usr/local/bin/headroom-dashboard.py &
exec headroom proxy --host 0.0.0.0 --port 8787
