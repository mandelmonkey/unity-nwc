using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZXing;
using ZXing.Common;

namespace NostrWalletConnect
{
    public class ZBDQRScanner : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private RawImage cameraDisplay;
        [SerializeField] private RectTransform rectTransform;
        [SerializeField] private Button startScanButton;
        [SerializeField] private Button stopScanButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Text statusText;
        [SerializeField] private GameObject scannerPanel;

        [Header("Camera Settings")]
        [SerializeField] private bool preferBackCamera = true;
        [SerializeField] private int requestedFPS = 30;
        [SerializeField] private bool autoCloseOnScan = true;
        [SerializeField] private bool saveToPlayerPrefs = true;
        [SerializeField] private bool autoConnect = true;

        // Camera variables (exactly like MAKAKA)
        private WebCamTexture webCamTextureTarget;
        private WebCamTexture webCamTextureRear;
        private WebCamTexture webCamTextureFront;

        private WebCamDevice[] webCamDevices;
        private WebCamDevice webCamDeviceTarget;
        private WebCamDevice webCamDeviceRear;
        private WebCamDevice webCamDeviceFront;

        private DeviceOrientation deviceOrientationPrevious;
        private DeviceOrientation deviceOrientationLast;

        // MAKAKA constants
        private readonly float minimumWidthForOrientation = 100f;
        private readonly float eulerAnglesOfPI = 180f;

        private Rect uvRectForVideoVerticallyMirrored = new(1f, 0f, -1f, 1f);
        private Rect uvRectForVideoNotVerticallyMirrored = new(0f, 0f, 1f, 1f);

        private float currentCWNeeded;
        private float currentAspectRatio;
        private Vector3 currentLocalEulerAngles = Vector3.zero;

        private int frontFacingCameraIndex = 0;
        private bool isScanning = false;
        private Coroutine scanCoroutine;

        // Auto-created AspectRatioFitter
        private AspectRatioFitter aspectRatioFitter;

        // ZXing QR code reader
        private BarcodeReader barcodeReader;

        // Events
        public event System.Action<string> OnQRCodeDetected;
        public event System.Action<string> OnScanError;
        public event System.Action<string> OnNWCConnectionReady;

        private void Awake()
        {
            EnsureAspectRatioFitter();
            InitializeZXing();
            SetupUI();
            InitializeCameras();
        }

        private void InitializeZXing()
        {
            // Initialize ZXing barcode reader
            barcodeReader = new BarcodeReader();

            // Configure for better QR code detection
            barcodeReader.Options.TryHarder = true;
            barcodeReader.Options.PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE };

            Debug.Log("ZXing.NET QR code reader initialized");
        }

        private void EnsureAspectRatioFitter()
        {
            if (cameraDisplay == null)
            {
                Debug.LogError("ZBDQRScanner: cameraDisplay RawImage is required!");
                return;
            }

            // Get or add AspectRatioFitter to the RawImage
            aspectRatioFitter = cameraDisplay.GetComponent<AspectRatioFitter>();
            if (aspectRatioFitter == null)
            {
                aspectRatioFitter = cameraDisplay.gameObject.AddComponent<AspectRatioFitter>();
            }

            // Force it to EnvelopeParent mode
            aspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;

            Debug.Log("ZBDQRScanner: AspectRatioFitter configured with EnvelopeParent mode");
        }

        private void SetupUI()
        {
            if (startScanButton != null)
                startScanButton.onClick.AddListener(StartScanning);

            if (stopScanButton != null)
                stopScanButton.onClick.AddListener(StopScanning);

            if (closeButton != null)
                closeButton.onClick.AddListener(CloseScanner);

            UpdateButtonStates(false);
            if (scannerPanel != null)
                scannerPanel.SetActive(false);
            
            scannerPanel.SetActive(true);
        }

        private void InitializeCameras()
        {
            try
            {
                webCamDevices = WebCamTexture.devices;

                if (webCamDevices.Length == 0)
                {
                    UpdateStatus("No camera devices found");
                    OnScanError?.Invoke("Camera Not Found");
                    return;
                }

                // Find front camera index
                for (int i = 0; i < webCamDevices.Length; i++)
                {
                    if (webCamDevices[i].isFrontFacing)
                    {
                        frontFacingCameraIndex = i;
                        break;
                    }
                }

                webCamDeviceRear = webCamDevices[0];
                webCamDeviceFront = webCamDevices[frontFacingCameraIndex];

                UpdateStatus("Camera initialized - Ready to scan");
            }
            catch (System.Exception e)
            {
                UpdateStatus($"Camera initialization failed: {e.Message}");
                OnScanError?.Invoke(e.ToString());
            }
        }

        private void Update()
        {
            // MAKAKA's core orientation handling - this is the key!
            if (isScanning)
            {
                SetOrientationUpdate();
            }
        }

        private void SetOrientationUpdate()
        {
            if (IsWebCamTextureInitialized())
            {
                // MAKAKA's exact rotation calculation
                currentCWNeeded = webCamDeviceTarget.isFrontFacing
                    ? webCamTextureTarget.videoRotationAngle
                    : -webCamTextureTarget.videoRotationAngle;

                if (webCamTextureTarget.videoVerticallyMirrored)
                {
                    currentCWNeeded += eulerAnglesOfPI;
                }

                currentLocalEulerAngles.z = currentCWNeeded;
                cameraDisplay.rectTransform.localEulerAngles = currentLocalEulerAngles;

                // MAKAKA's aspect ratio handling - this is critical!
                currentAspectRatio = (float)webCamTextureTarget.width / (float)webCamTextureTarget.height;
                if (aspectRatioFitter != null)
                {
                    aspectRatioFitter.aspectRatio = currentAspectRatio;
                }

                // MAKAKA's UV rect handling
                if ((webCamTextureTarget.videoVerticallyMirrored && !webCamDeviceTarget.isFrontFacing) ||
                    (!webCamTextureTarget.videoVerticallyMirrored && webCamDeviceTarget.isFrontFacing))
                {
                    cameraDisplay.uvRect = uvRectForVideoVerticallyMirrored;
                }
                else
                {
                    cameraDisplay.uvRect = uvRectForVideoNotVerticallyMirrored;
                }
            }
        }

        private bool IsWebCamTextureInitialized()
        {
            return webCamTextureTarget && webCamTextureTarget.width >= minimumWidthForOrientation;
        }

        public void ShowScanner()
        {
            if (scannerPanel != null)
                scannerPanel.SetActive(true);
        }

        public void CloseScanner()
        {
            StopScanning();
            if (scannerPanel != null)
                scannerPanel.SetActive(false);
        }

        public void StartScanning()
        {
            if (isScanning) return;

            try
            {
                UpdateStatus("Starting camera...");

                // Create WebCamTexture exactly like MAKAKA
                if (preferBackCamera)
                {
                    webCamTextureRear = CreateWebCamTexture(webCamDeviceRear.name);
                    webCamDeviceTarget = webCamDeviceRear;
                    webCamTextureTarget = webCamTextureRear;
                }
                else
                {
                    webCamTextureFront = CreateWebCamTexture(webCamDeviceFront.name);
                    webCamDeviceTarget = webCamDeviceFront;
                    webCamTextureTarget = webCamTextureFront;
                }

                webCamTextureTarget.Play();
                cameraDisplay.texture = webCamTextureTarget;

                isScanning = true;
                UpdateButtonStates(true);

                StartCoroutine(WaitForCameraAndStartScanning());
            }
            catch (System.Exception e)
            {
                UpdateStatus($"Failed to start camera: {e.Message}");
                OnScanError?.Invoke(e.ToString());
            }
        }

        private IEnumerator WaitForCameraAndStartScanning()
        {
            // Wait for camera to initialize
            while (!IsWebCamTextureInitialized())
            {
                yield return null;
            }

            UpdateStatus("Camera ready - Point at QR code");

            // Start QR scanning
            scanCoroutine = StartCoroutine(ScanForQRCode());
        }

        public void StopScanning()
        {
            if (!isScanning) return;

            isScanning = false;

            if (scanCoroutine != null)
            {
                StopCoroutine(scanCoroutine);
                scanCoroutine = null;
            }

            if (webCamTextureTarget != null)
            {
                webCamTextureTarget.Stop();
                webCamTextureTarget = null;
            }

            if (webCamTextureRear != null)
            {
                webCamTextureRear.Stop();
                Destroy(webCamTextureRear);
                webCamTextureRear = null;
            }

            if (webCamTextureFront != null)
            {
                webCamTextureFront.Stop();
                Destroy(webCamTextureFront);
                webCamTextureFront = null;
            }

            if (cameraDisplay != null)
                cameraDisplay.texture = null;

            UpdateButtonStates(false);
            UpdateStatus("Camera stopped");
        }

        private WebCamTexture CreateWebCamTexture(string deviceName)
        {
            Debug.Log($"Creating WebCamTexture for: {deviceName}");
            return new WebCamTexture(deviceName, Screen.width, Screen.height, requestedFPS);
        }

        private IEnumerator ScanForQRCode()
        {
            while (isScanning && webCamTextureTarget != null && webCamTextureTarget.isPlaying)
            {
                try
                {
                    if (webCamTextureTarget.width > 100 && webCamTextureTarget.height > 100)
                    {
                        Color32[] pixels = webCamTextureTarget.GetPixels32();
                        string qrText = DecodeQR(pixels, webCamTextureTarget.width, webCamTextureTarget.height);

                        if (!string.IsNullOrEmpty(qrText))
                        {
                            UpdateStatus("QR Code detected!");
                            StartCoroutine(HandleQRCodeDetected(qrText));
                            yield break;
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"QR scanning error: {ex.Message}");
                }

                yield return new WaitForSeconds(0.5f);
            }
        }

        private string DecodeQR(Color32[] pixels, int width, int height)
        {
            try
            {
                // Use ZXing.NET for real QR code decoding
                var result = barcodeReader.Decode(pixels, width, height);

                if (result != null && !string.IsNullOrEmpty(result.Text))
                {
                    Debug.Log($"ZXing decoded QR: {result.Text}");

                    // Validate if it's a valid NWC connection string
                    if (QRCodeDecoder.IsValidNWCConnectionString(result.Text))
                    {
                        return result.Text;
                    }
                    else
                    {
                        Debug.Log("QR code found but not a valid NWC connection string");
                        return result.Text; // Return anyway - let the calling code decide
                    }
                }

                // Fallback: Keep your existing pattern detection for debugging
                QRCodeDecoder.DecodeQRFromPixels(pixels, width, height);

                // Manual test (keep for debugging)
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    Debug.Log("Manual QR test triggered");
                    string testNWC = "nostr+walletconnect://b889ff5b1513b641e2a139f661a661364979c5beee91842f8f0e8dda6766a3b3?relay=wss://relay.primal.net&secret=71a8c14c1407c113601079c4302dab36460f0ccd0ad506f884658492666e639d";
                    return testNWC;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"ZXing QR decoding error: {ex.Message}");
            }

            return null;
        }

        private IEnumerator HandleQRCodeDetected(string qrText)
        {
            bool success = false;
            string errorMessage = "";

            try
            {
                // Fire the detection event
                OnQRCodeDetected?.Invoke(qrText);

                // Save to PlayerPrefs if enabled
                if (saveToPlayerPrefs)
                {
                    Debug.Log("connection string "+qrText);
                    PlayerPrefs.SetString("NWC_ConnectionString", qrText);
                    PlayerPrefs.Save();
                    UpdateStatus("Connection string saved");
                    Debug.Log("NWC connection string saved to PlayerPrefs");
                }

                success = true;
            }
            catch (System.Exception ex)
            {
                errorMessage = ex.Message;
            }

            if (!success)
            {
                Debug.LogError($"Error handling QR detection: {errorMessage}");
                UpdateStatus($"Error: {errorMessage}");
                yield break;
            }

            // Wait a moment for user feedback
            yield return new WaitForSeconds(1f);

            // Auto-close if enabled
            if (autoCloseOnScan)
            {
                UpdateStatus("Auto-closing scanner...");
                CloseScanner();
            }

            // Fire ready event for auto-connection
            if (autoConnect)
            {
                OnNWCConnectionReady?.Invoke(qrText);
            }
        }

        public static string GetSavedConnectionString()
        {
            return PlayerPrefs.GetString("NWC_ConnectionString", "");
        }

        public static bool HasSavedConnectionString()
        {
            return !string.IsNullOrEmpty(GetSavedConnectionString());
        }

        public static void ClearSavedConnectionString()
        {
            PlayerPrefs.DeleteKey("NWC_ConnectionString");
            PlayerPrefs.Save();
            Debug.Log("Cleared saved NWC connection string");
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = $"{System.DateTime.Now:HH:mm:ss} - {message}";
            }
            Debug.Log($"QR Scanner: {message}");
        }

        private void UpdateButtonStates(bool scanning)
        {
            if (startScanButton != null)
                startScanButton.interactable = !scanning;

            if (stopScanButton != null)
                stopScanButton.interactable = scanning;
        }

        private void OnDestroy()
        {
            StopScanning();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && isScanning)
            {
                StopScanning();
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Test QR Scanner")]
        private void TestQRScanner()
        {
            var testQR = "nostr+walletconnect://b889ff5b1513b641e2a139f661a661364979c5beee91842f8f0e8dda6766a3b3?relay=wss://relay.primal.net&secret=71a8c14c1407c113601079c4302dab36460f0ccd0ad506f884658492666e639d";
            UpdateStatus("Testing QR detection...");
            OnQRCodeDetected?.Invoke(testQR);
        }
#endif
    }
}