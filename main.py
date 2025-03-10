import io
import torch
import argparse
import threading
from PIL import Image
import torch.nn as nn
import torchvision.transforms as transforms

from src.server.server.Server import Server

import os
import sys
import warnings
warnings.filterwarnings("ignore")
sys.path.append(os.path.join(os.path.dirname(os.path.abspath(__file__)), "src", "testra", "src"))

from src.testra.src.rekognition_online_action_detection.models import build_model
from src.testra.src.rekognition_online_action_detection.utils.parser import load_cfg
from src.testra.src.rekognition_online_action_detection.utils.logger import setup_logger
from src.testra.src.rekognition_online_action_detection.utils.env import setup_environment
from src.testra.src.rekognition_online_action_detection.utils.checkpointer import setup_checkpointer



class Testra(nn.Module):
    def __init__(self, cfg, frame_queue):
        super().__init__()

        self.cfg = cfg
        self.queue = frame_queue

        self.device = setup_environment(cfg)
        checkpointer = setup_checkpointer(cfg, phase='test')

        # Build backbone
        effnet = torch.hub.load('hankyul2/EfficientNetV2-pytorch', 'efficientnet_v2_s', pretrained=True)
        self.backbone = nn.Sequential(*list(effnet.children())[:-1], 
                                *list(list(effnet.children())[-1].children())[:-2],
                                nn.AvgPool1d(kernel_size=257, stride=1))
        self.backbone.to(self.device)
        self.backbone.eval()

        # Build testra
        self.testra = build_model(cfg, self.device)
        self.testra.to(self.device)
        self.testra.eval()

        checkpointer.load(self.testra)


    def forward(self, x):
        with torch.no_grad(), torch.autocast(device_type="cuda"):
            x = self.backbone(x.to(self.device))
            x = x.unsqueeze(0)
            x = x.to(self.device)
            x = self.testra(x, x, x)
        return x


    def model_worker(self):
        while True:
            batch_frames = self.queue.dequeue_batch(self.cfg.MODEL.LSTR.WORK_MEMORY_LENGTH)

            frames = []
            for frame_bytes in batch_frames:
                frame = Image.open(io.BytesIO(frame_bytes)).convert("RGB")
                frame = transforms.ToTensor()(frame)
                frames.append(frame)

            frames = torch.stack(frames, dim=0)
            with torch.no_grad(), torch.autocast(device_type="cuda"):
                output = self.model(frames)
            output = torch.softmax(output, dim=1)
            results = output.cpu().numpy()[0]
            print(results)




def main(cfg):
    key_file = os.path.join(os.path.dirname(os.path.abspath(__file__)), "src", "server", "server", "cert", "key.pem")
    cert_file = os.path.join(os.path.dirname(os.path.abspath(__file__)), "src", "server", "server", "cert", "cert.pem")

    server = Server(key_file, cert_file, port=8443, max_queue_size=10000)
    server_thread = threading.Thread(target=server.run, daemon=True)
    server_thread.start()

    frame_queue = server.get_frame_queue()
    model = Testra(cfg, frame_queue)

    try:
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
