#!/usr/bin/env python3
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


RUNTIME_PATH = Path("/opt/gzctf/runtime/runtime.json")
FLAG_PATH = Path("/opt/gzctf/runtime/secrets/flag")


def load_runtime():
    payload = json.loads(RUNTIME_PATH.read_text(encoding="utf-8"))
    parameters = payload.get("parameters", payload.get("Parameters", {}))
    port = int(parameters["service_port"])
    if port < 1 or port > 65535:
        raise ValueError("service_port is outside the valid range")
    return str(parameters["service_name"]), port


SERVICE_NAME, SERVICE_PORT = load_runtime()


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        body = json.dumps({"service": SERVICE_NAME, "status": "ready"}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, _format, *_args):
        return


if not FLAG_PATH.is_file():
    raise RuntimeError("runtime flag secret is missing")

ThreadingHTTPServer(("0.0.0.0", SERVICE_PORT), Handler).serve_forever()
