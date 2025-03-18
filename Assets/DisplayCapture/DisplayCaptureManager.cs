using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.UI;


namespace Anaglyph.DisplayCapture{
	[DefaultExecutionOrder(-1000)]
	public class DisplayCaptureManager : MonoBehaviour{
		public static DisplayCaptureManager Instance { get; private set; }

		public bool startScreenCaptureOnStart = true;
		public bool flipTextureOnGPU = false;

		private bool saveFrames = false;

		public int listenPort = 4444;

		private string port = "34545";
		private string serverIP = "192.168.1.101"; //* Equal to the local IP printed out from server

		
		private HttpListener listener;
		private Thread listenerThread;
		public string localIP;


		private int FRAMERATE = 30;
		private int MEMORY_IN_SECONDS = 60;
		private readonly Queue<byte[]> frameQueue = new Queue<byte[]>();

		public RawImage rawImage; 

		[SerializeField] private Vector2Int textureSize = new(1024, 1024);
		public Vector2Int Size => textureSize;

		private Texture2D screenTexture;
		public Texture2D ScreenCaptureTexture => screenTexture;

		private RenderTexture flipTexture;

		public Matrix4x4 ProjectionMatrix { get; private set; }

		public UnityEvent<Texture2D> onTextureInitialized = new();
		public UnityEvent onStarted = new();
		public UnityEvent onPermissionDenied = new();
		public UnityEvent onStopped = new();
		public UnityEvent onNewFrame = new();

		private unsafe sbyte* imageData;
		private int bufferSize;

		private class AndroidInterface{
			private AndroidJavaClass androidClass;
			private AndroidJavaObject androidInstance;

			public AndroidInterface(GameObject messageReceiver, int textureWidth, int textureHeight){
				androidClass = new AndroidJavaClass("com.trev3d.DisplayCapture.DisplayCaptureManager");
				androidInstance = androidClass.CallStatic<AndroidJavaObject>("getInstance");
				androidInstance.Call("setup", messageReceiver.name, textureWidth, textureHeight);
			}

			public void RequestCapture() => androidInstance.Call("requestCapture");
			public void StopCapture() => androidInstance.Call("stopCapture");

			public unsafe sbyte* GetByteBuffer(){
				AndroidJavaObject byteBuffer = androidInstance.Call<AndroidJavaObject>("getByteBuffer");
				return AndroidJNI.GetDirectBufferAddress(byteBuffer.GetRawObject());
			}
		}

		private AndroidInterface androidInterface;

		private void Awake(){
			Instance = this;
			androidInterface = new AndroidInterface(gameObject, Size.x, Size.y);
			screenTexture = new Texture2D(Size.x, Size.y, TextureFormat.RGBA32, 1, false);
		}

		private void Start(){
			flipTexture = new RenderTexture(Size.x, Size.y, 1, RenderTextureFormat.ARGB32, 1);
			flipTexture.Create();

			onTextureInitialized.Invoke(screenTexture);

			if (startScreenCaptureOnStart)
			{
				StartScreenCapture();
			}
			bufferSize = Size.x * Size.y * 4; // RGBA_8888 format: 4 bytes per pixel

			listener = new HttpListener();
			// The URL prefix must match the callback URL used by the server.
			listener.Prefixes.Add($"http://*:{listenPort}/callback/");
			listener.Start();

			listenerThread = new Thread(ListenerLoop);
			listenerThread.IsBackground = true;
			listenerThread.Start();

			Debug.Log("CallbackReceiver started, listening on port " + listenPort);

			StartCoroutine(ProcessQueue());
		}


		public class CallbackData{
			public string timestamp;
			public int action;
		}

		void ProcessRequest(HttpListenerContext context){
			string body;
			using (var reader = new System.IO.StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
				body = reader.ReadToEnd();
			Debug.Log("Received callback: " + body);

			CallbackData data = JsonUtility.FromJson<CallbackData>(body);
			Debug.Log($"Timestamp: {data.timestamp}, Action: {data.action}");

			// // Example: Trigger an event if action == 1
			// if (data.action == 1 && onTargetAction != null)
			// {
			// 	onTargetAction.Invoke();
			// }

			byte[] responseBytes = System.Text.Encoding.UTF8.GetBytes("Received");
			context.Response.ContentLength64 = responseBytes.Length;
			context.Response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
			context.Response.OutputStream.Close();
		}


		void ListenerLoop(){
			while (listener.IsListening){
				try{
					HttpListenerContext context = listener.GetContext();
					ProcessRequest(context);
				}
				catch (Exception ex){
					Debug.LogError("Listener error: " + ex.Message);
				}
			}
		}



        private float captureInterval = 1f / 30f; // 30 frames per second
		private float timeSinceLastCapture = 0f;

		private void Update(){
			if (saveFrames){
				timeSinceLastCapture += Time.deltaTime;
				if (timeSinceLastCapture >= captureInterval){
					timeSinceLastCapture = 0;
					byte[] imgBytes = GetImageBytes();
					EnqueueFrame(imgBytes);
				}
			}
		}

		//* Just ignore any certificate PLEASE
		public class BypassCertificate : CertificateHandler{
			protected override bool ValidateCertificate(byte[] certificateData){
				return true;
			}
		}

		public void EnqueueFrame(byte[] img){
			if (img == null || img.Length == 0){
				Debug.LogWarning("Skipping frame: image data is empty.");
				return;
			}

			int maxQueueSize = FRAMERATE * MEMORY_IN_SECONDS;
			while (frameQueue.Count >= maxQueueSize){
				frameQueue.Dequeue();
				Debug.LogWarning("Dropping an old frame due to queue overload.");
			}

			frameQueue.Enqueue(img);
			Debug.Log("Frame enqueued with bytes: " + img.Length + ". Queue size: " + frameQueue.Count);
		}


		private IEnumerator ProcessQueue(){
			int batchSize = 60;
			while (true){
				if (frameQueue.Count >= batchSize){
					byte[] batchData;
					using (MemoryStream ms = new MemoryStream()){
						int numFrames = batchSize;
						byte[] numFramesBytes = BitConverter.GetBytes(numFrames);
						if (BitConverter.IsLittleEndian) Array.Reverse(numFramesBytes);
						ms.Write(numFramesBytes, 0, 4);

						for (int i = 0; i < batchSize; i++){
							byte[] frame = frameQueue.Dequeue();
							byte[] frameLengthBytes = BitConverter.GetBytes(frame.Length);
							if (BitConverter.IsLittleEndian) Array.Reverse(frameLengthBytes);
							ms.Write(frameLengthBytes, 0, 4);
							ms.Write(frame, 0, frame.Length);
						}
						batchData = ms.ToArray();
					}

					yield return StartCoroutine(SendFrame(batchData));
				}
				else
					yield return null;
			}
		}

		private bool HasOnlyTwoColors(byte[] img){
			if (img == null || img.Length == 0)
				return true;

			HashSet<int> distinctColors = new HashSet<int>();

			for (int i = 0; i < img.Length; i += 4){
				int color = BitConverter.ToInt32(img, i);
				distinctColors.Add(color);

				if (distinctColors.Count > 2)
					return false;
			}
			return true;
		}

		private IEnumerator SendFrame(byte[] img){
			string serverAddress = "https://" + serverIP + ":" + port;

			using (UnityWebRequest www = new UnityWebRequest(serverAddress, "POST")){
				www.uploadHandler = new UploadHandlerRaw(img);
				www.downloadHandler = new DownloadHandlerBuffer();
				www.certificateHandler = new BypassCertificate();
				www.SetRequestHeader("Content-Type", "application/octet-stream");

				Debug.Log("Sending " + img.Length + " bytes to server...");
				yield return www.SendWebRequest();

				if (www.result == UnityWebRequest.Result.Success){
					Debug.Log("Frame sent successfully.");
				}
				else{
					Debug.LogError("Error sending frame: " + www.error);
				}
			}
		}

		byte[] GetImageBytes(){
			if (rawImage == null || rawImage.texture == null) {
				Debug.LogError("RawImage or its texture is null. Cannot save image.");
				return null;
			}

			Texture2D texture = (Texture2D)rawImage.texture;
			if (HasOnlyTwoColors(texture.GetRawTextureData())){
				Debug.LogWarning("Skipping frame: image data is uniform.");
				return null;
			}
			return texture.EncodeToJPG(50);
		}


		public void StartScreenCapture(){
			androidInterface.RequestCapture();
			saveFrames = true;
		}

		public void StopScreenCapture(){
			androidInterface.StopCapture();
			saveFrames = false;
		}

#pragma warning disable IDE0051
		private unsafe void OnCaptureStarted(){
			onStarted.Invoke();
			imageData = androidInterface.GetByteBuffer();
		}

		private void OnPermissionDenied(){
			onPermissionDenied.Invoke();
		}

		private unsafe void OnNewFrameAvailable(){
			if (imageData == default) return;
			screenTexture.LoadRawTextureData((IntPtr)imageData, bufferSize);
			screenTexture.Apply();

			if (flipTextureOnGPU){
				Graphics.Blit(screenTexture, flipTexture, new Vector2(1, -1), Vector2.zero);
				Graphics.CopyTexture(flipTexture, screenTexture);
			}
			onNewFrame.Invoke();
		}

		private void OnCaptureStopped(){
			onStopped.Invoke();
		}
#pragma warning restore IDE0051
	}
}