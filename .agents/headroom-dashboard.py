"""Read-only browser counters. Never forward arbitrary paths, auth, or file reads."""
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.error import URLError
from urllib.parse import urlsplit
from urllib.request import urlopen

# Only these aggregate numeric values may leave the proxy through this listener.
FIELDS = {
    "requests": ("total", "cached", "failed", "rate_limited"),
    "tokens": ("input", "output", "saved", "total_before_compression", "savings_percent"),
    "savings": ("total_tokens",),
}

PAGE = b'''<!doctype html>
<html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Headroom container stats</title>
<style>
:root{color-scheme:dark}body{font:16px system-ui;background:#10151e;color:#e8edf5;margin:0;padding:40px;max-width:1000px;margin:auto}
h1{font-size:30px;margin-bottom:8px}p{color:#a8b5c9;line-height:1.6}.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:16px;margin:28px 0}
article{background:#1b2534;border:1px solid #334258;border-radius:12px;padding:24px}article span{display:block;color:#a8b5c9}article strong{display:block;font-size:32px;margin-top:10px;color:#7ee0c3}
table{width:100%;border-collapse:collapse}td,th{text-align:left;padding:12px;border-bottom:1px solid #334258}td:last-child{text-align:right;font-variant-numeric:tabular-nums}
#status{font-size:14px}a{color:#7ee0c3}</style>
<h1>Headroom container stats</h1><p>Live aggregate counters from the Headroom proxy used by this repository. Refreshes every 5 seconds.</p>
<div class="cards"><article><span>Requests</span><strong id="requests">\xe2\x80\x94</strong></article><article><span>Tokens saved</span><strong id="saved">\xe2\x80\x94</strong></article><article><span>Input reduction</span><strong id="percent">\xe2\x80\x94</strong></article></div>
<table><thead><tr><th>Counter</th><th>Value</th></tr></thead><tbody id="counters"></tbody></table>
<p id="status" role="status">Connecting...</p><p>Read-only view. Prompts, credentials and administrative controls are excluded. <a href="/stats">JSON counters</a></p>
<script>
const format=n=>Number(n||0).toLocaleString();
async function refresh(){try{
 const response=await fetch('/stats',{cache:'no-store'});if(!response.ok)throw Error('Proxy not ready (HTTP '+response.status+')');const data=await response.json();
 document.getElementById('requests').textContent=format(data.requests.total);
 document.getElementById('saved').textContent=format(data.tokens.saved);
 document.getElementById('percent').textContent=Number(data.tokens.savings_percent||0).toFixed(2)+'%';
 const table=document.getElementById('counters');table.replaceChildren();
 for(const [group,values] of Object.entries(data))for(const [name,value] of Object.entries(values)){
  const row=document.createElement('tr'),label=document.createElement('td'),count=document.createElement('td');
  label.textContent=(group+' / '+name).replaceAll('_',' ');count.textContent=format(value);row.append(label,count);table.append(row);
 }
 document.getElementById('status').textContent='Updated '+new Date().toLocaleTimeString()+'. Session counters may reset when Headroom restarts.';
 }catch(error){document.getElementById('status').textContent=error.message;}}
refresh();setInterval(refresh,5000);
</script></html>'''


class Dashboard(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass  # No request/query logging.

    def reply(self, status, content_type, content):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(content)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.end_headers()
        self.wfile.write(content)

    def do_GET(self):
        path = urlsplit(self.path).path
        if path in {"/", "/dashboard", "/dashboard/"}:
            self.reply(200, "text/html; charset=utf-8", PAGE)
        elif path == "/stats":
            try:
                with urlopen("http://127.0.0.1:8787/stats", timeout=5) as response:
                    raw = json.load(response)
                data = {
                    group: {key: value for key in keys
                            if isinstance(value := raw.get(group, {}).get(key), (int, float))}
                    for group, keys in FIELDS.items()
                }
                self.reply(200, "application/json", json.dumps(data).encode())
            except (URLError, TimeoutError, ValueError, TypeError, AttributeError):
                self.reply(503, "application/json", b'{"error":"Headroom is not ready"}')
        else:
            self.reply(404, "text/plain", b"Not found")


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", 8788), Dashboard).serve_forever()
