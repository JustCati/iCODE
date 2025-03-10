import ssl
import socket
import http.server

from src.server.queue.FrameQueue import FrameQueue
from src.server.queue.BatchQueue import BatchQueue


class Server():
    def __init__(self, key_file, cert_file, port=8443, max_queue_size=10000):
        self.port = port
        self.KEY_FILE = key_file
        self.CERT_FILE = cert_file
        self.max_queue_size = max_queue_size

        self.frame_queue = FrameQueue(max_queue_size)
        self.batch_queue = BatchQueue(self.frame_queue, max_queue_size)


    class FrameRequestHandler(http.server.BaseHTTPRequestHandler):
        def do_POST(self):
            print()
            print("Received POST request.")
            content_length = int(self.headers.get('Content-Length', 0))
            frame_data = self.rfile.read(content_length)
            print(f"Received frame ({len(frame_data)} bytes).")

            self.send_response(200)
            self.end_headers()
            self.flush_headers()
            self.wfile.write(b"OK")
            print("Sent response.")

            self.__class__.batch_queue.put(frame_data)


    def run(self, server_class=http.server.HTTPServer, handler_class=FrameRequestHandler, port=8443):
        handler_class.batch_queue = self.batch_queue
        local_ip = socket.gethostbyname(socket.gethostname())
        server_address = (local_ip, port)
        httpd = server_class(server_address, handler_class)

        context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        context.check_hostname = False
        context.verify_mode = ssl.CERT_NONE
        context.load_cert_chain(certfile=self.CERT_FILE, keyfile=self.KEY_FILE)
        httpd.socket = context.wrap_socket(httpd.socket, server_side=True)

        print(f"Starting HTTPS server on ip {local_ip} and port {port}...", end="\n\n")
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            httpd.server_close()
            self.batch_queue.batch_worker_thread.join()
            print("Server has been shut down.")


    def get_frame_queue(self):
        return self.frame_queue
