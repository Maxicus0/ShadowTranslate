#!/usr/bin/env python3
"""Мок GitHub для тестов обновлятора. Режим берётся из файла MODE_FILE при каждом запросе."""
import hashlib, http.server, os, re, socketserver, sys, time, json

PORT = int(sys.argv[1]); ZIP = sys.argv[2]; MODE_FILE = sys.argv[3]; TAG = sys.argv[4] if len(sys.argv) > 4 else "0.2.0"
REPO = "Maxicus0/ShadowTranslate"; ASSET = "ShadowTranslate.zip"
data = open(ZIP, "rb").read(); sha = hashlib.sha256(data).hexdigest()
hits = {"drop": 0}

def mode():
    try: return open(MODE_FILE).read().strip()
    except Exception: return "ok"

class H(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    def log_message(self, *a): sys.stderr.write("MOCK " + (a[0] % a[1:]) + "\n")
    def send(self, code, body=b"", ctype="text/plain", extra=None):
        self.send_response(code); self.send_header("Content-Type", ctype); self.send_header("Content-Length", str(len(body)))
        for k, v in (extra or {}).items(): self.send_header(k, v)
        self.send_header("Connection", "close"); self.end_headers(); self.wfile.write(body)
    def do_GET(self):
        m = mode(); p = self.path; mirror = False
        if p.startswith("/m/"):
            p = re.sub(r"^/m/https?://[^/]+", "", p); mirror = True
        if p == f"/{REPO}/releases/latest":
            if m in ("no-web", "blocked"): return self.send(503 if m == "no-web" else 451)
            return self.send(302, extra={"Location": f"/{REPO}/releases/tag/{TAG}"})
        if p == f"/repos/{REPO}/releases/latest":
            if m in ("ratelimit", "blocked-all"):
                return self.send(403, b'{"message":"API rate limit exceeded"}', extra={"X-RateLimit-Remaining": "0"})
            if m == "blocked": return self.send(451)
            j = {"tag_name": TAG, "assets": [{"name": ASSET, "size": len(data), "digest": "sha256:" + sha}]}
            return self.send(200, json.dumps(j).encode(), "application/json")
        if p == f"/{REPO}/releases/expanded_assets/{TAG}":
            if m in ("blocked", "no-hash-page", "blocked-all"): return self.send(451)
            html = f'<a href="/{REPO}/releases/download/{TAG}/{ASSET}">{ASSET}</a><span>sha256:{sha}</span>'
            return self.send(200, html.encode(), "text/html")
        if p.endswith("/update.json"):
            return self.send(404)
        if p == f"/{REPO}/releases/download/{TAG}/{ASSET}":
            return self.download(m, mirror)
        self.send(404)
    def download(self, m, mirror=False):
        if m == "404": return self.send(404)
        if m in ("direct-dead", "mirror-bad") and not mirror: return self.send(404)
        body = data
        if mirror and m == "mirror-bad": body = bytearray(data); body[100] ^= 1; body = bytes(body)
        if m == "badhash": body = bytearray(data); body[len(body)//2] ^= 0xFF; body = bytes(body)
        start = 0; rng = self.headers.get("Range")
        if rng and m != "no-range":
            start = int(re.match(r"bytes=(\d+)-", rng).group(1))
        if start:
            self.send_response(206); self.send_header("Content-Range", f"bytes {start}-{len(body)-1}/{len(body)}")
        else: self.send_response(200)
        chunk = body[start:]
        self.send_header("Content-Type", "application/zip"); self.send_header("Content-Length", str(len(chunk))); self.send_header("Connection", "close"); self.end_headers()
        try:
            if m == "drop" and start == 0 and hits["drop"] == 0:
                hits["drop"] += 1; self.wfile.write(chunk[: len(chunk)*4//10]); self.wfile.flush(); self.connection.shutdown(2); return
            if m == "stall":
                self.wfile.write(chunk[: len(chunk)//10]); self.wfile.flush(); time.sleep(60); return
            if m == "slowdrip":
                for i in range(0, len(chunk), 8192):
                    self.wfile.write(chunk[i:i+8192]); self.wfile.flush(); time.sleep(0.01)
                return
            self.wfile.write(chunk)
        except Exception: pass

class S(socketserver.ThreadingMixIn, http.server.HTTPServer): daemon_threads = True; allow_reuse_address = True
S(("127.0.0.1", PORT), H).serve_forever()
