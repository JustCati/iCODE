import queue
import threading


class FrameQueue(queue.Queue):
    def __init__(self, maxsize):
        super().__init__(maxsize=maxsize)


    def dequeue_batch(self, num_frames):
        batch = []

        for _ in range(num_frames):
            item = self.get()
            batch.append(item)
            self.task_done()
        return batch
