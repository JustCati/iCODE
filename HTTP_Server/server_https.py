import os
import ssl
from concurrent.futures import ThreadPoolExecutor
import http.server
import socket
from PIL import Image
import datetime
import numpy as np


IMG_WIDTH = 1024
IMG_HEIGHT = 1024
BYTE_SIZE = IMG_WIDTH * IMG_HEIGHT * 4 # 4 bytes per pixel (RGBA)

CERT_FILE = "cert.pem"
KEY_FILE = "key.pem"



def save_frame_to_file(frame_data, output_dir="frames"):
    frame_data = np.frombuffer(frame_data, dtype=np.uint8)
    frame_data = frame_data.reshape((IMG_HEIGHT, IMG_WIDTH, 4))
    image = Image.fromarray(frame_data)
    print("Created image from frame data.")

    if not os.path.exists(output_dir):
        os.makedirs(output_dir)
    filename = os.path.join(output_dir, "frame_{}.png".format(datetime.datetime.now().strftime("%Y%m%d_%H%M%S_%f")))
    image.save(filename, "PNG")
    print("Saved frame to", filename)
 

class FrameRequestHandler(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        print()
        print("Received POST request.")
        frame_data = self.rfile.read(BYTE_SIZE)
        print(f"Received frame ({len(frame_data)} bytes).")

        #TODO: maybe this needs to be faster (?)
        self.send_response(200)
        self.end_headers()
        self.flush_headers()
        self.wfile.write(b"OK")
        print("Sent response.")

        # Thread so it doesn't block the server
        with ThreadPoolExecutor() as executor:
            executor.submit(save_frame_to_file, frame_data)




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

    run()