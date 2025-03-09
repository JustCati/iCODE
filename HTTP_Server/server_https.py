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


MAX_QUEUE_SIZE = 10000
CERT_FILE = "cert.pem"
KEY_FILE = "key.pem"



def save_frame_to_file(frame_data):
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


def frame_worker():
    while True:
        frame_data = frame_queue.get()
        try:
            save_frame_to_file(frame_data)
        finally:
            frame_queue.task_done()


def batch_worker():
    while True:
        data = batch_queue.get()
        try:
            if len(data) < 4:
                print("Received batch data is too short to contain header.")
                continue

            num_frames = int.from_bytes(data[:4], byteorder='big')
            print(f"Batch contains {num_frames} frames.")
            offset = 4

            for i in range(num_frames):
                if offset + 4 > len(data):
                    print("Incomplete frame header in batch.")
                    break
                frame_length = int.from_bytes(data[offset:offset+4], byteorder='big')
                offset += 4

                if offset + frame_length > len(data):
                    print("Incomplete frame data in batch.")
                    break
                frame_data = data[offset:offset+frame_length]
                offset += frame_length

                while frame_queue.qsize() >= MAX_QUEUE_SIZE:
                    try:
                        _ = frame_queue.get_nowait()
                        print("Dropping an old frame due to queue overload.")
                    except queue.Empty:
                        break
                frame_queue.put(frame_data)
        except Exception as e:
            print("Error unbatching data:", e)
        finally:
            batch_queue.task_done()



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

        batch_queue.put(frame_data)




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
    worker_thread = threading.Thread(target=frame_worker, daemon=True).start()
    
    batch_queue = queue.Queue()
    batch_thread = threading.Thread(target=batch_worker, daemon=True).start()

    run()

    batch_queue.join()
    frame_queue.join() 
    print("All frames have been processed.")
