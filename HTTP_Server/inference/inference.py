import os
import torch
import argparse
import threading
import torch.nn as nn

from ..server_https import Server

import sys
import warnings
warnings.filterwarnings("ignore")
sys.path.append(os.path.join(os.path.dirname(os.path.abspath(__file__)), "src"))

from src.rekognition_online_action_detection.models import build_model
from src.rekognition_online_action_detection.utils.parser import load_cfg
from src.rekognition_online_action_detection.utils.logger import setup_logger
from src.rekognition_online_action_detection.utils.env import setup_environment
from src.rekognition_online_action_detection.utils.checkpointer import setup_checkpointer




class CompleteTeSTra(torch.nn.Module):
    def __init__(self, model, backbone, *args, **kwargs):
        super(CompleteTeSTra, self).__init__()
        self.model = model
        self.backbone = backbone
        self.device = kwargs.get("device", torch.device("cpu"))

        self.model.eval()
        self.backbone.eval()

        self.model.to(self.device)
        self.backbone.to(self.device)


    def forward(self, x):
        with torch.no_grad(), torch.autocast(device_type="cuda"):
            x = self.backbone(x)
            x = x.unsqueeze(0)
            x = x.to(self.device)
            x = self.model(x, x, x)
        return x



class FeatureExtractor(nn.Module):
    def __init__(self, *args, **kwargs):
        super(FeatureExtractor, self).__init__()
        self.device = kwargs.get("device", torch.device("cpu"))

        model = model = torch.hub.load('hankyul2/EfficientNetV2-pytorch', 'efficientnet_v2_s', pretrained=True)
        self.features = nn.Sequential(*list(model.children())[:-1], 
                                *list(list(model.children())[-1].children())[:-2],
                                nn.AvgPool1d(kernel_size=257, stride=1))
        self.features.to(self.device)
        self.features.eval()

    def forward(self, x):
        with torch.no_grad(), torch.autocast(device_type="cuda"):
            x = self.features(x.to(self.device))
        return x




class Testra(nn.Module):
    def __init__(self, cfg, server):
        super().__init__()
        device = setup_environment(cfg)
        logger = setup_logger(cfg, phase='test')
        checkpointer = setup_checkpointer(cfg, phase='test')

        model = build_model(cfg, device)
        backbone = FeatureExtractor(device=device)

        logger.info("")
        logger.info("MODEL STRUCTURE:")
        logger.info(model)
        logger.info("")

        checkpointer.load(model)

        self.server = server
        self.model = CompleteTeSTra(model, backbone, device=device)
        self.model.eval()




    # for start, end in tqdm(zip(
    #                     range(0, 
    #                         len(all_frames), 
    #                         cfg.MODEL.LSTR.WORK_MEMORY_LENGTH),
    #                     range(cfg.MODEL.LSTR.WORK_MEMORY_LENGTH,
    #                         len(all_frames),
    #                         cfg.MODEL.LSTR.WORK_MEMORY_LENGTH)), 
    #                        total=math.ceil(len(all_frames) / cfg.MODEL.LSTR.WORK_MEMORY_LENGTH)):
    #     frames_paths = all_frames[start:end]

    #     frames = []
    #     with ThreadPoolExecutor(max_workers=cpu_count()) as executor:
    #         frames = list(executor.map(lambda x: load_image(os.path.join(video_path, x), 2.0, device), frames_paths))

    #     with torch.no_grad(), torch.autocast(device_type="cuda"):
    #         output = model(torch.cat(frames, dim=0))
    #     output = torch.softmax(output, dim=1)
    #     results.extend(output.cpu().numpy()[0])

    # results = np.array(results)
    # np.save(os.path.join(os.getcwd(), "output.npy"), results)



def main(cfg, args):
    server = Server()
    server_thread = threading.Thread(target=server.run, daemon=True).start()

    model = Testra(cfg, server)

    while True:
        pass





if __name__ == "__main__":
    parser = argparse.ArgumentParser(description='Execute TeSTra-Mamba on a video')
    parser.add_argument('--config_file', type=str, help='path to config file')
    parser.add_argument('--gpu', default='0', type=str, help='specify visible devices')
    parser.add_argument('opts', default=None, nargs='*', help='modify config options using the command-line',)
    args = parser.parse_args()
    main(load_cfg(args), args)
