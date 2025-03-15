import queue
import threading



class BatchQueue(queue.Queue):
    def __init__(self, frame_queue, maxsize):
        super().__init__(maxsize=maxsize)
        self.frame_queue = frame_queue

        self.batch_worker_thread = threading.Thread(target=self.batch_worker, daemon=True)
        self.batch_worker_thread.start()


    def batch_worker(self):
        while True:
            data = self.get()
            try:
                if len(data) < 4:
                    print("Received batch data is too short to contain header.")
                    continue

                num_frames = int.from_bytes(data[:4], byteorder='big')
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

                    while self.frame_queue.qsize() >= self.frame_queue.maxsize:
                        try:
                            _ = self.frame_queue.get_nowait()
                            print("Dropping an old frame due to queue overload.")
                        except queue.Empty:
                            break
                    self.frame_queue.put(frame_data)
            except Exception as e:
                print("Error unbatching data:", e)
            finally:
                self.task_done()
