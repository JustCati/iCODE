using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

		private bool saveFrames = false; //* Save frames lcoally
		private bool SEND_TO_SERVER = true; //* Send frames to server

		private string port = "8443";
		private string serverIP = "192.168.1.45"; //* Equal to the local IP printed out from server
		private readonly Queue<byte[]> frameQueue = new Queue<byte[]>();

		public RawImage rawImage; 
    	private int imageIndex = 0;

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

			StartCoroutine(ProcessQueue());
		}

		//TODO: Maybe don't run on each update (?), but i don't know when instead 
        private void Update(){
			if (saveFrames){
				byte[] imgBytes = GetImageBytes();
				if (SEND_TO_SERVER)
					EnqueueFrame(imgBytes);
				else
					SaveImageToFile(imgBytes);
			}
        }

		//* Just ignore any certificate PLEASE
		public class BypassCertificate : CertificateHandler{
			protected override bool ValidateCertificate(byte[] certificateData){
				return true;
			}
		}

		public void EnqueueFrame(byte[] img){
			if (IsUniformImage(img) || HasOnlyTwoColors(img)){
				Debug.LogWarning("Skipping frame: image data is uniform.");
				return;
			}

			frameQueue.Enqueue(img);
		}

		private IEnumerator ProcessQueue(){
			while (true){
				if (frameQueue.Count > 0){
					byte[] frame = frameQueue.Dequeue();
					yield return StartCoroutine(SendFrame(frame));
				}
				else
					yield return null;
			}
		}

		private bool IsUniformImage(byte[] img){
			if (img == null || img.Length == 0)
				return true;

			byte first = img[0];
			foreach (byte b in img){
				if (b != first)
					return false;
			}
			return true;
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


		// //* This variable is only a workaround, in the ideal world it should not be necessary
		// private bool sending = false; //* Global variable to limit sending. This also prevents crashing from buffer overflow
		// IEnumerator SendImageToServer(byte[] img){
		// 	//* Prevent sending grayed images (no texture). This gray textures can also happen  
		// 	//* for a middle frame because it can catch when the texture is being updated
		// 	bool allElementsAreSame = img.AsEnumerable().All(b => b == img[0]);
		// 	if (allElementsAreSame){
		// 		Debug.LogWarning("All elements in the image are the same. Skipping this frame.");
		// 		yield break;
		// 	}

		// 	string serverAddress = "https://" + serverIP + ":" + port;
		// 	var www = new UnityWebRequest(serverAddress, "POST");	
		// 	www.uploadHandler = new UploadHandlerRaw(img);
		// 	www.downloadHandler = new DownloadHandlerBuffer();
		// 	www.certificateHandler = new BypassCertificate();
		// 	www.SetRequestHeader("Content-Type", "application/octet-stream");

		// 	if (sending){
		// 		Debug.LogWarning("Already sending an image to server. Skipping this frame.");
		// 		yield break;
		// 	}
		// 	sending = true;
		// 	Debug.Log("Sending " + img.Length + " bytes to server...");
		// 	yield return www.SendWebRequest();

		// 	//* This success log happens once every (i did not counted them) frames
		// 	//TODO: THIS IS THE PROBLEM. PROBABLY THE HTTPS SERVER IN PYTHON IS NOT
		// 	//TODO: RESPONDING FAST ENOUGH OR THE TRANSMISSION IS SLOW (DUNNO)
		// 	if (www.result == UnityWebRequest.Result.Success){
		// 		Debug.Log("Image sent to server successfully.");
		// 	}
		// 	else{
		// 		string error = www.error;
		// 		if (www.error == null)
		// 			error = "(Error code null: " + www.responseCode;
		// 		Debug.LogError("Error sending image to server: " + error + ")");
		// 	}
		// 	sending = false;
		// }

		//* I've already checked by saving the byte array to a file and GetRawTextureData() is creating the right byte array
        void SaveImageToFile(byte[] img){
			string path = Path.Combine("/storage/emulated/0/Pictures", $"frame_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.png");
			File.WriteAllBytes(path, img);
			Debug.Log("Saved image to: " + path);
			imageIndex++;
		}

		byte[] GetImageBytes(){
			if (rawImage == null || rawImage.texture == null) {
				Debug.LogError("RawImage or its texture is null. Cannot save image.");
				return null;
			}

			Texture2D texture = (Texture2D)rawImage.texture;

			//* Send raw bytes instead of PNG because it is easier to convert 
			//* to a numpy vector in Python (PNG has the header and bla bla bla)
			byte[] bytes = texture.GetRawTextureData(); 
			// byte[] bytes = texture.EncodeToPNG();
			return bytes;
		}


		public void StartScreenCapture(){
			androidInterface.RequestCapture();
			saveFrames = true;
		}

		public void StopScreenCapture(){
			androidInterface.StopCapture();
			saveFrames = false;
		}

		// Messages sent from Android

#pragma warning disable IDE0051 // Remove unused private members
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
#pragma warning restore IDE0051 // Remove unused private members
	}
}