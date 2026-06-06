#!/usr/bin/env python3
"""Simple CTF challenge server - displays FLAG from env var."""
import os, http.server

FLAG = os.environ.get('GZCTF_FLAG') or os.environ.get('FLAG', 'flag{test_flag_not_set}')
HTML = os.environ.get('HTML', 'index.html')

class Handler(http.server.SimpleHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200)
        self.send_header('Content-Type', 'text/html; charset=utf-8')
        self.end_headers()
        try:
            with open(HTML, 'rb') as f:
                content = f.read().replace(b'{{FLAG}}', FLAG.encode())
        except:
            content = f'<h1>CTF Challenge</h1><p>Flag: {FLAG}</p>'.encode()
        self.wfile.write(content)

if __name__ == '__main__':
    port = int(os.environ.get('PORT', 8080))
    http.server.HTTPServer(('0.0.0.0', port), Handler).serve_forever()
