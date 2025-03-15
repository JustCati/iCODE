import argparse
import threading

import os
import sys
import warnings
warnings.filterwarnings("ignore")
sys.path.append(os.path.join(os.path.dirname(os.path.abspath(__file__)), "src", "testra", "src"))

from src.server.server.Server import Server
from src.testra.Testra.Testra import Testra
from src.testra.src.rekognition_online_action_detection.utils.parser import load_cfg




def main(cfg):
    key_file = os.path.join(os.path.dirname(os.path.abspath(__file__)), "src", "server", "server", "cert", "key.pem")
    cert_file = os.path.join(os.path.dirname(os.path.abspath(__file__)), "src", "server", "server", "cert", "cert.pem")

    server = Server(key_file, cert_file, port=8443, max_queue_size=10000)

    frame_queue = server.get_frame_queue()
    model = Testra(cfg, frame_queue)

    try:
        server_thread = threading.Thread(target=server.run, daemon=True)
        server_thread.start()

        model_thread = threading.Thread(target=model.model_worker, daemon=True)
        model_thread.start()

        model_thread.join()
    except KeyboardInterrupt:
        model_thread.join()
        server_thread.join()
        server.batch_queue.batch_worker_thread.join()

        print("Server has been shut down.")
        print("Model has been shut down.")
        exit(0)




if __name__ == "__main__":
    parser = argparse.ArgumentParser(description='Execute TeSTra-Mamba on a video')
    parser.add_argument('--config_file', type=str, help='path to config file')
    parser.add_argument('--gpu', default='0', type=str, help='specify visible devices')
    parser.add_argument('opts', default=None, nargs='*', help='modify config options using the command-line',)
    args = parser.parse_args()
    main(load_cfg(args))
