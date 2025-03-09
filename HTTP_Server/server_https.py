import os
import io
import sys
import ssl
import queue
import threading
from concurrent.futures import ThreadPoolExecutor
import http.server
import socket
from PIL import Image
import datetime
import numpy as np

CERT_FILE = "cert.pem"
KEY_FILE = "key.pem"





def save_frame_to_file_worker():
    while True:
        frame_data = frame_queue.get()
        try:
            data = io.BytesIO(frame_data)
            image = Image.open(data)

            output_dir = "frames"
            if not os.path.exists(output_dir):
                os.makedirs(output_dir)

            filename = os.path.join(
                output_dir,
                "frame_{}.png".format(datetime.datetime.now().strftime("%Y%m%d_%H%M%S_%f"))
            )
            image.save(filename, "PNG")
            print("Saved frame to", filename)
        except Exception as e:
            print("Error processing frame:", e)
        finally:
            frame_queue.task_done()
 

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

        frame_queue.put(frame_data)




def run(server_class=http.server.HTTPServer, handler_class=FrameRequestHandler, port=8443):
    server_address = ('', port)
    httpd = server_class(server_address, handler_class)

    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.check_hostname = False
    context.verify_mode = ssl.CERT_NONE
    context.load_cert_chain(certfile=CERT_FILE, keyfile=KEY_FILE)
    httpd.socket = context.wrap_socket(httpd.socket, server_side=True)

    print(f"Starting HTTPS server on port {port}...")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\nServer is shutting down.")
        httpd.server_close()


if __name__ == "__main__":
    local_ip = socket.gethostbyname(socket.gethostname())
    print(f"Local IP address: {local_ip}", end='\n\n')

    frame_queue = queue.Queue()
    worker_thread = threading.Thread(target=save_frame_to_file_worker, daemon=True)
    worker_thread.start()

    run()
