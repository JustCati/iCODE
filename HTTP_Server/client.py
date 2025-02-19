import os
import io
import random
import tempfile
import socket
import ssl
from PIL import Image
import numpy as np
import requests


IMG_WIDTH = 1024
IMG_HEIGHT = 1024
SERVER_URL = "https://localhost:8443"


def generate_random_image():
    random_data = np.random.randint(0, 256, (IMG_HEIGHT, IMG_WIDTH, 3), dtype=np.uint8)
    image = Image.fromarray(random_data)
    return image


def image_to_png_bytes(image):
    img_bytes = image.tobytes()
    return img_bytes


def send_image_to_server(image_data):
    random_bytes = os.urandom(10240)
    try:
        response = requests.post(SERVER_URL, data=random_bytes, verify=False)
        print("Server response:", response.text)
    except Exception as e:
        print("Error sending image to server:", e)


if __name__ == "__main__":
    image = generate_random_image()
    image_data = image_to_png_bytes(image)
    send_image_to_server(image_data)
