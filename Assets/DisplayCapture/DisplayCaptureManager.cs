using System;
using System.Collections;
using System.IO;
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

		private bool saveFrames = false;
		private bool SEND_TO_SERVER = true;

		private string port = "8443";
		private string serverIP = "192.168.137.1";

		public RawImage rawImage;  // Reference to the RawImage component
    	private int imageIndex = 0; // Counter to give unique names to saved images

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
		}

        private void Update(){
			if (saveFrames){
				byte[] imgBytes = GetImageBytes();
				if (SEND_TO_SERVER)
					StartCoroutine(SendImageToServer(imgBytes));
				else
					SaveImageToFile(imgBytes);
			}
        }

		public class BypassCertificate : CertificateHandler{
			protected override bool ValidateCertificate(byte[] certificateData){
				return true;
			}
		}

		IEnumerator SendImageToServer(byte[] img){
			string serverAddress = "https://" + serverIP + ":" + port;
			var www = new UnityWebRequest(serverAddress, "POST");	
			www.uploadHandler = new UploadHandlerRaw(img);
			www.downloadHandler = new DownloadHandlerBuffer();
			www.certificateHandler = new BypassCertificate();
			www.SetRequestHeader("Content-Type", "application/octet-stream");

			Debug.Log("Sending " + img.Length + " bytes to server...");
			yield return www.SendWebRequest();

			if (www.result == UnityWebRequest.Result.Success){
				Debug.Log("Error While Sending: " + www.error);
			}
			else{
				string error = www.error;
				if (www.error == null)
					error = "(Error code null: " + www.responseCode;
				Debug.LogError("Error sending image to server: " + error + ")");
			}
		}

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