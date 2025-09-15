using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace NostrWalletConnect
{
    public class QRScannerRuntimeUI : MonoBehaviour
    {
        [Header("Camera Settings")]
        [SerializeField] private int targetFPS = 30;
        [SerializeField] private bool fillScreen = true;
        [SerializeField] private bool preferBackCamera = true;
        [SerializeField] private bool invertRotationDirection = false;

        [Header("UI Theme")]
        [SerializeField] private Color overlayTint = new Color(0, 0, 0, 0.35f);
        [SerializeField] private Color panelBg = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        [SerializeField] private Color buttonBg = new Color(0.2f, 0.2f, 0.2f, 1f);
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private int fontSize = 24;

        // Runtime-created UI refs
        private Canvas canvas;
        private CanvasScaler canvasScaler;
        private GraphicRaycaster raycaster;
        private GameObject eventSystemGO;

        private RectTransform rootPanel;          // fills screen
        private RectTransform videoRootWrapper;   // stretches; contains centered RawImage
        private RawImage cameraDisplay;
        private Text statusText;
        private Button startBtn, stopBtn, closeBtn;
        private Image overlay;

        // Camera + scanning
        private WebCamTexture webCamTexture;
        private bool isScanning = false;
        private Coroutine scanCoroutine;

        // Rotation/fit cache
        private int lastVideoRotation = -999;
        private bool lastVerticallyMirrored = false;
        private Vector2 lastParentSize = Vector2.negativeInfinity;

        // Events (same API as your previous script)
        public event System.Action<string> OnQRCodeDetected;
        public event System.Action<string> OnScanError;

        // --- Public control (optional) ---
        public void ShowScanner()   { rootPanel.gameObject.SetActive(true); }
        public void HideScanner()   { rootPanel.gameObject.SetActive(false); }
        public void StartScanning() { if (!isScanning) StartCoroutine(InitializeCamera()); }
        public void StopScanning()
        {
            if (!isScanning) return;
            isScanning = false;

            if (scanCoroutine != null) { StopCoroutine(scanCoroutine); scanCoroutine = null; }

            if (webCamTexture != null)
            {
                webCamTexture.Stop();
                Destroy(webCamTexture);
                webCamTexture = null;
            }

            if (cameraDisplay != null) cameraDisplay.texture = null;

            // Professional approach handles orientation dynamically - no need to restore
            Debug.Log("Camera stopped - orientation handled dynamically");

            UpdateButtons(false);
            SetStatus("Camera stopped");
        }

        // ---------------- Unity ----------------
        private void Awake()
        {
            EnsureCanvasAndEventSystem();
            BuildUI();
            UpdateButtons(false);
            SetStatus("Ready");
            HideScanner(); // Start hidden; call ShowScanner() when you want it visible
        }

        private void Update()
        {
            // Apply proper professional camera orientation handling
            if (isScanning && webCamTexture != null && webCamTexture.isPlaying && webCamTexture.width > 16)
            {
                ApplyProfessionalCameraTransform();
            }
        }

        private void OnDestroy()
        {
            StopScanning();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && isScanning) StopScanning();
        }


        private void Start()
        {
            ShowScanner();
        }

        // --------------- UI Construction ---------------
        private void EnsureCanvasAndEventSystem()
        {
            // Try to find an existing Canvas in the scene; if none, create one.
            canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                var canvasGO = new GameObject("QRScannerCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = canvasGO.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                canvasScaler = canvasGO.GetComponent<CanvasScaler>();
                canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                canvasScaler.referenceResolution = new Vector2(1080, 1920);
                canvasScaler.matchWidthOrHeight = 1f; // bias to height (portrait)

                raycaster = canvasGO.GetComponent<GraphicRaycaster>();
            }
            else
            {
                canvasScaler = canvas.GetComponent<CanvasScaler>() ?? canvas.gameObject.AddComponent<CanvasScaler>();
                raycaster = canvas.GetComponent<GraphicRaycaster>() ?? canvas.gameObject.AddComponent<GraphicRaycaster>();
            }

            // EventSystem
            if (FindObjectOfType<EventSystem>() == null)
            {
                eventSystemGO = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }
        }

        private void BuildUI()
        {
            // Root panel that fills the canvas
            rootPanel = CreateRect("QRScannerRoot", canvas.transform as RectTransform, panelBg, true);
            rootPanel.gameObject.SetActive(true);

            // Semi-transparent overlay on top of everything (for dimming)
            overlay = CreateImage("Overlay", rootPanel, overlayTint, true);
            overlay.raycastTarget = false; // don't block clicks

            // Video wrapper (stretches to panel)
            videoRootWrapper = CreateRect("VideoRootWrapper", rootPanel, Color.clear, true);

            // The RawImage itself (centered, non-stretch)
            var raw = new GameObject("CameraDisplay", typeof(RawImage));
            raw.transform.SetParent(videoRootWrapper, false);
            cameraDisplay = raw.GetComponent<RawImage>();
            var imgRT = cameraDisplay.rectTransform;
            imgRT.anchorMin = imgRT.anchorMax = new Vector2(0.5f, 0.5f);
            imgRT.pivot = new Vector2(0.5f, 0.5f);
            imgRT.anchoredPosition = Vector2.zero;
            imgRT.sizeDelta = Vector2.zero;

            // Controls container (bottom bar)
            var controls = CreateRect("Controls", rootPanel, new Color(0,0,0,0.35f), false);
            controls.anchorMin = new Vector2(0, 0);
            controls.anchorMax = new Vector2(1, 0);
            controls.pivot = new Vector2(0.5f, 0);
            controls.sizeDelta = new Vector2(0, 160);
            controls.anchoredPosition = Vector2.zero;

            // Buttons
            startBtn = CreateButton("Start", controls, "Start", OnStartClicked);
            stopBtn  = CreateButton("Stop",  controls, "Stop",  OnStopClicked);
            closeBtn = CreateButton("Close", controls, "Close", OnCloseClicked);

            // Layout buttons horizontally
            var bw = 260f; var bh = 90f; var spacing = 40f;
            PositionButton(startBtn,  -bw - spacing/2, bw, bh);
            PositionButton(stopBtn,    0,              bw, bh);
            PositionButton(closeBtn,   bw + spacing/2, bw, bh);

            // Status text (top bar)
            var statusBar = CreateRect("StatusBar", rootPanel, new Color(0,0,0,0.35f), false);
            statusBar.anchorMin = new Vector2(0, 1);
            statusBar.anchorMax = new Vector2(1, 1);
            statusBar.pivot = new Vector2(0.5f, 1);
            statusBar.sizeDelta = new Vector2(0, 120);
            statusBar.anchoredPosition = Vector2.zero;

            statusText = CreateText("StatusText", statusBar, "Status", fontSize, TextAnchor.MiddleCenter);
        }

        private RectTransform CreateRect(string name, RectTransform parent, Color bg, bool stretch)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            var img = go.GetComponent<Image>();
            img.color = bg;
            rt.SetParent(parent, false);

            if (stretch)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
            }
            else
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
            }
            return rt;
        }

        private Image CreateImage(string name, RectTransform parent, Color color, bool stretch)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var img = go.GetComponent<Image>();
            img.color = color;
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            if (stretch)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            else
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = Vector2.zero;
            }
            return img;
        }

        private Text CreateText(string name, RectTransform parent, string initial, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var txt = go.GetComponent<Text>();
            txt.text = initial;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = size;
            txt.alignment = anchor;
            txt.color = textColor;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(20, 0);
            rt.offsetMax = new Vector2(-20, 0);
            return txt;
        }

        private Button CreateButton(string name, RectTransform parent, string label, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = buttonBg;

            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());

            var txt = CreateText(name + "_Label", go.transform as RectTransform, label, fontSize, TextAnchor.MiddleCenter);
            txt.color = textColor;

            return btn;
        }

        private void PositionButton(Button btn, float x, float width, float height)
        {
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(x, 0);
        }

        // --------------- Buttons ---------------
        private void OnStartClicked() => StartScanning();
        private void OnStopClicked()  => StopScanning();
        private void OnCloseClicked()
        {
            StopScanning();
            HideScanner();
        }

        private void UpdateButtons(bool scanning)
        {
            if (startBtn) startBtn.interactable = !scanning;
            if (stopBtn)  stopBtn.interactable  =  scanning;
        }

        private void SetStatus(string msg)
        {
            if (statusText) statusText.text = msg;
            Debug.Log($"QR Scanner: {msg}");
        }

        // --------------- Camera Init + Transform ---------------
        private IEnumerator InitializeCamera()
        {
            // Professional approach: handle orientation dynamically rather than locking
            Debug.Log($"Starting camera with dynamic orientation handling. Current orientation: {Screen.orientation}");

#if UNITY_ANDROID || UNITY_IOS
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                SetStatus("Requesting camera permission...");
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
                if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
                {
                    SetStatus("Camera permission denied");
                    OnScanError?.Invoke("Camera permission denied");
                    yield break;
                }
            }
#endif
            SetStatus("Starting camera...");

            if (WebCamTexture.devices.Length == 0)
            {
                SetStatus("No camera devices found");
                OnScanError?.Invoke("No camera devices found");
                yield break;
            }

            string deviceName = WebCamTexture.devices[0].name;
            if (preferBackCamera)
            {
                foreach (var d in WebCamTexture.devices)
                {
                    if (!d.isFrontFacing) { deviceName = d.name; break; }
                }
            }

            string err = null;
            try
            {
                webCamTexture = new WebCamTexture(deviceName, Screen.width, Screen.height, targetFPS);
            }
            catch (Exception ex)
            {
                err = ex.Message;
            }

            if (webCamTexture == null)
            {
                SetStatus($"Camera initialization failed: {err}");
                OnScanError?.Invoke($"Camera initialization failed: {err}");
                yield break;
            }

            cameraDisplay.texture = webCamTexture;
            webCamTexture.Play();

            // Wait for a valid frame
            int safety = 0;
            while ((webCamTexture.width <= 16 || webCamTexture.height <= 16) && safety < 120)
            {
                yield return null;
                safety++;
            }

            if (webCamTexture.width <= 16 || webCamTexture.height <= 16)
            {
                SetStatus("Failed to get camera frame");
                OnScanError?.Invoke("Failed to get camera frame");
                yield break;
            }

            // Initial setup complete - transform will be handled in Update

            isScanning = true;
            UpdateButtons(true);
            SetStatus("Camera ready - Point at QR code");

            // Start QR scanning
            scanCoroutine = StartCoroutine(ScanForQRCode());
        }

        private void ApplyProfessionalCameraTransform()
        {
            if (cameraDisplay == null || webCamTexture == null) return;
            if (webCamTexture.width <= 16 || webCamTexture.height <= 16) return;

            var parent = videoRootWrapper;
            if (parent == null) return;

            var img = cameraDisplay.rectTransform;

            // Professional rotation handling (from MAKAKA GAMES)
            float rotationNeeded;

            // Get device info
            WebCamDevice? targetDevice = null;
            foreach (var device in WebCamTexture.devices)
            {
                if (device.name == webCamTexture.deviceName)
                {
                    targetDevice = device;
                    break;
                }
            }

            bool isFrontFacing = targetDevice?.isFrontFacing ?? false;

            // Calculate proper rotation with configurable direction
            float baseRotation = webCamTexture.videoRotationAngle;

            if (isFrontFacing)
            {
                rotationNeeded = invertRotationDirection ? -baseRotation : baseRotation;
            }
            else
            {
                rotationNeeded = invertRotationDirection ? baseRotation : -baseRotation;
            }

            // Handle vertical mirroring
            if (webCamTexture.videoVerticallyMirrored)
            {
                rotationNeeded += 180f;
            }

            // Apply rotation
            img.localEulerAngles = new Vector3(0, 0, rotationNeeded);

            // Professional UV rect handling
            bool shouldMirrorUV = (webCamTexture.videoVerticallyMirrored && !isFrontFacing) ||
                                  (!webCamTexture.videoVerticallyMirrored && isFrontFacing);

            if (shouldMirrorUV)
            {
                cameraDisplay.uvRect = new Rect(1f, 0f, -1f, 1f); // Mirrored
            }
            else
            {
                cameraDisplay.uvRect = new Rect(0f, 0f, 1f, 1f); // Normal
            }

            // Professional scaling for full screen
            float cameraAspect = (float)webCamTexture.width / (float)webCamTexture.height;
            Vector2 parentSize = parent.rect.size;
            float parentAspect = parentSize.x / parentSize.y;

            Vector2 scale = Vector2.one;

            if (fillScreen)
            {
                // Cover mode - fill screen, may crop
                if (cameraAspect > parentAspect)
                {
                    scale.y = cameraAspect / parentAspect;
                }
                else
                {
                    scale.x = parentAspect / cameraAspect;
                }
            }

            img.localScale = new Vector3(scale.x, scale.y, 1f);

            // Ensure proper anchoring
            img.anchorMin = Vector2.zero;
            img.anchorMax = Vector2.one;
            img.offsetMin = Vector2.zero;
            img.offsetMax = Vector2.zero;
            img.anchoredPosition = Vector2.zero;
            img.sizeDelta = Vector2.zero;

            Debug.Log($"Camera Debug: device={webCamTexture.deviceName}, " +
                     $"videoRotation={baseRotation}°, isFront={isFrontFacing}, " +
                     $"vertMirrored={webCamTexture.videoVerticallyMirrored}, " +
                     $"invertFlag={invertRotationDirection}, finalRotation={rotationNeeded:F1}°, " +
                     $"shouldMirrorUV={shouldMirrorUV}, scale={scale}");
        }

        // --------------- QR Scanning (placeholder) ---------------
        private IEnumerator ScanForQRCode()
        {
            while (isScanning && webCamTexture != null && webCamTexture.isPlaying)
            {
                try
                {
                    if (webCamTexture.width > 100 && webCamTexture.height > 100)
                    {
                        Color32[] pixels = webCamTexture.GetPixels32();
                        string qrText = DecodeQR(pixels, webCamTexture.width, webCamTexture.height);
                        if (!string.IsNullOrEmpty(qrText))
                        {
                            SetStatus("QR Code detected!");
                            OnQRCodeDetected?.Invoke(qrText);
                            yield break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"QR scanning error: {ex.Message}");
                }
                yield return new WaitForSeconds(0.4f);
            }
        }

        private string DecodeQR(Color32[] pixels, int width, int height)
        {
            // Hook up to your existing decoder
            string decodedText = QRCodeDecoder.DecodeQRFromPixels(pixels, width, height);

            if (!string.IsNullOrEmpty(decodedText) && QRCodeDecoder.IsValidNWCConnectionString(decodedText))
            {
                return decodedText;
            }

            // Simple fallback detection for testing
            return TrySimpleQRDetection(pixels, width, height);
        }

        private string TrySimpleQRDetection(Color32[] pixels, int width, int height)
        {
            try
            {
                int centerX = width / 2;
                int centerY = height / 2;
                int scanSize = Mathf.Min(width, height) / 3;

                int darkPixelCount = 0;
                int totalPixels = 0;

                for (int y = centerY - scanSize/2; y < centerY + scanSize/2; y += 8)
                {
                    for (int x = centerX - scanSize/2; x < centerX + scanSize/2; x += 8)
                    {
                        if (x >= 0 && x < width && y >= 0 && y < height)
                        {
                            int index = y * width + x;
                            if (index >= 0 && index < pixels.Length)
                            {
                                Color32 pixel = pixels[index];
                                float brightness = (pixel.r + pixel.g + pixel.b) / 3f;

                                totalPixels++;
                                if (brightness < 120)
                                {
                                    darkPixelCount++;
                                }
                            }
                        }
                    }
                }

                float darkRatio = (float)darkPixelCount / totalPixels;

                if (darkRatio > 0.2f && darkRatio < 0.8f)
                {
                    SetStatus($"QR pattern detected (dark ratio: {darkRatio:F2})");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Simple QR detection error: {ex.Message}");
            }

            return null;
        }

        // --------------- Editor test ---------------
#if UNITY_EDITOR
        [ContextMenu("Test QR Scanner UI")]
        private void TestQRScanner()
        {
            ShowScanner();
            SetStatus("Testing UI only...");
        }
#endif
    }
}
