/*
===================================================================
Unity Assets by MAKAKA GAMES: https://makaka.org/o/all-unity-assets
===================================================================

Online Docs (Latest): https://makaka.org/unity-assets
Offline Docs: You have a PDF file in the package folder.

=======
SUPPORT
=======

First of all, read the docs. If it didn’t help, get the support.

Web: https://makaka.org/support
Email: info@makaka.org

If you find a bug or you can’t use the asset as you need, 
please first send email to info@makaka.org
before leaving a review to the asset store.

I am here to help you and to improve my products for the best.
*/

using System.Collections;

using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[HelpURL("https://makaka.org/unity-assets")]
public class CameraFeedOnBackgroundControl : MonoBehaviour
{
    [SerializeField]
    private RectTransform rectTransform;

    [SerializeField]
    private RawImage rawImage;

    [SerializeField]
    private AspectRatioFitter aspectRatioFitter;

    [Space]
    [SerializeField]
    private bool isFrontFacingUsed = false;
    private int frontFacingCameraIndex = 0;

    [Space]
    [Header("This is not the FPS of App." +
        "\nCamera will use the closest supported FPS." +
        "\nZero (0) will lead to Value by Default.")]
    [Range(0, 60)]
    [SerializeField]
    private int requestedFPS = 60;

    private WebCamTexture webCamTextureTarget;
    private WebCamTexture webCamTextureRear;
    private WebCamTexture webCamTextureFront;

    private WebCamDevice[] webCamDevices;
    private WebCamDevice webCamDeviceTarget;
    private WebCamDevice webCamDeviceRear;
    private WebCamDevice webCamDeviceFront;

    private DeviceOrientation deviceOrientationPrevious;
    private DeviceOrientation deviceOrientationLast;

    private readonly float minimumWidthForOrientation = 100f;
    private readonly float eulerAnglesOfPI = 180f;

    private Rect uvRectForVideoVerticallyMirrored = new(1f, 0f, -1f, 1f);
    private Rect uvRectForVideoNotVerticallyMirrored = new(0f, 0f, 1f, 1f);

    private float currentCWNeeded;
    private float currentAspectRatio;

    private readonly float scaleFixForPortraitMode = 0.005f;

    private Vector3 currentLocalEulerAngles = Vector3.zero;

    private Coroutine fixScaleBaseCoroutineReference;
    private Coroutine switchCameraCoroutineReference;

#if UNITY_ANDROID

    private bool isFirstFixingScaleAndroid = true;

    private readonly float delayBeforeFixingScaleOnAndroid = 0.5f;

#endif

    private readonly float delayBeforeSwitchingCamera = 0.01f;

    [Header("Events")]
    [Space]
    [SerializeField]
    private UnityEvent OnInitialized;

    [Space]
    [SerializeField]
    private UnityEvent<string> OnCameraIsNotAvailable;

    [Space]
    [SerializeField]
    private UnityEvent OnCameraSwitched;

    private void Awake()
    {
        try
        {
            webCamDevices = WebCamTexture.devices;

            if (webCamDevices.Length == 0)
            {
                DebugPrinter.Print("█ [Camera Feed] No 🎥 Device Cameras Found");

                OnCameraIsNotAvailable.Invoke("Camera Not Found");
            }
            else
            {
                for (int i = 0; i < webCamDevices.Length; i++)
                {
                    if (webCamDevices[i].isFrontFacing)
                    {
                        frontFacingCameraIndex = i;
                    }
                }

                webCamDeviceRear = webCamDevices[0];

                webCamDeviceFront = webCamDevices[frontFacingCameraIndex];

                // 0 is Main Camera (Rear Camera on Mobiles with Both Cameras)

                if (isFrontFacingUsed)
                {
                    webCamTextureFront =
                        CreateWebCamTexture(webCamDeviceFront.name);

                    webCamDeviceTarget = webCamDeviceFront;

                    webCamTextureTarget = webCamTextureFront;
                }
                else
                {
                    webCamTextureRear =
                        CreateWebCamTexture(webCamDeviceRear.name);

                    webCamDeviceTarget = webCamDeviceRear;

                    webCamTextureTarget = webCamTextureRear;
                }

                webCamTextureTarget.Play();

                rawImage.texture = webCamTextureTarget;

                PrintAvailableResolutions();

                PrintSettings();

                OnInitialized.Invoke();
            }
        }
        catch (System.Exception e)
        {
            DebugPrinter.Print(
                "█ [Camera Feed] Device Camera 🎥 is NOT Available: " + e);

            OnCameraIsNotAvailable.Invoke(e.ToString());
        }
    }

    private void Update()
    {
        SetOrientationUpdate();
    }

    private void SetOrientationUpdate()
    {
        if (IsWebCamTextureInitialized())
        {
            currentCWNeeded = webCamDeviceTarget.isFrontFacing
                ? webCamTextureTarget.videoRotationAngle
                : -webCamTextureTarget.videoRotationAngle;

            if (webCamTextureTarget.videoVerticallyMirrored)
            {
                currentCWNeeded += eulerAnglesOfPI;
            }

            currentLocalEulerAngles.z = currentCWNeeded;
            rawImage.rectTransform.localEulerAngles = currentLocalEulerAngles;

            currentAspectRatio = (float)webCamTextureTarget.width
                / (float)webCamTextureTarget.height;

            aspectRatioFitter.aspectRatio = currentAspectRatio;

            // -------------------------------------
            // webCamTexture.videoVerticallyMirrored
            // -------------------------------------
            // MacBook Air M2	- Front	-	No
            // iPhone XS Max	- Front	-	Yes
            // iPhone XS Max	- Rear	-	Yes
            // Galaxy A71		- Front	-	No
            // Galaxy A71		- Rear	-	No

            //DebugPrinter.Print("videoVerticallyMirrored: " +
            //    webCamTextureTarget.videoVerticallyMirrored);

            //DebugPrinter.Print($"isFrontFacing: {targetDevice.isFrontFacing}");

            if ((webCamTextureTarget.videoVerticallyMirrored
                && !webCamDeviceTarget.isFrontFacing)
                ||
                (!webCamTextureTarget.videoVerticallyMirrored
                && webCamDeviceTarget.isFrontFacing))
            {
                rawImage.uvRect = uvRectForVideoVerticallyMirrored;
            }
            else
            {
                rawImage.uvRect = uvRectForVideoNotVerticallyMirrored;
            }

            FixScale(false);
        }
    }

    private bool IsWebCamTextureInitialized()
    {
        return webCamTextureTarget
            && webCamTextureTarget.width >= minimumWidthForOrientation;
    }

    private void FixScale(bool isCameraSwitching)
    {
        deviceOrientationPrevious = Input.deviceOrientation;

        if (isCameraSwitching || deviceOrientationLast != deviceOrientationPrevious)
        {
            deviceOrientationLast = Input.deviceOrientation;

            if (deviceOrientationLast == DeviceOrientation.Portrait
                || deviceOrientationLast == DeviceOrientation.PortraitUpsideDown)
            {
                // Android has a delay of Setting Screen.orientation
                DebugPrinter.Print(
                    $"█ [Camera Feed] Device Orientation => Portrait." +
                    $" Scr Ornt = {Screen.orientation}");

                StartFixingScaleBase(true, isCameraSwitching);
            }
            else if (deviceOrientationLast == DeviceOrientation.LandscapeLeft
                || deviceOrientationLast == DeviceOrientation.LandscapeRight)
            {
                // Android has a delay of Setting Screen.orientation
                DebugPrinter.Print(
                    $"█ [Camera Feed] Device Orientation => Landscape." +
                    $" Scr Ornt = {Screen.orientation}");

                StartFixingScaleBase(false, isCameraSwitching);
            }
            else
            {
                // Android has a delay of Setting Screen.orientation
                DebugPrinter.Print($"█ [Camera Feed] Device Orientation =>" +
                    $" FaceUp/FaceDown/Unknown." +
                    $" Scr Ornt = {Screen.orientation}");
            }
        }
    }

    private void StartFixingScaleBase(
        bool isPortraitDeviceOrientation, bool isCameraSwitching)
    {
        if (fixScaleBaseCoroutineReference != null)
        {
            StopCoroutine(fixScaleBaseCoroutineReference);

            fixScaleBaseCoroutineReference = null;
        }

        fixScaleBaseCoroutineReference =
            StartCoroutine(FixScaleBaseCoroutine(
                isPortraitDeviceOrientation, isCameraSwitching));
    }

    private IEnumerator FixScaleBaseCoroutine(
        bool isPortraitDeviceOrientation, bool isCameraSwitching)
    {
        DebugPrinter.Print("█ [Camera Feed] Fixing Scale...");

        // Due to Switching and Creating a separate/new WCT
        // for the 2nd camera for the 1st time
        while (!IsWebCamTextureInitialized())
        {
            yield return null;
        }

        PrintSettings();

        if (isPortraitDeviceOrientation)
        {

#if UNITY_ANDROID

			if (isFirstFixingScaleAndroid)
			{
                // to avoid visible zooming at scene loading

                yield return null;
            }
			else
			{
                // to avoid seeing incorrect scale due to nuances of Android

                yield return new WaitForSeconds(
                    delayBeforeFixingScaleOnAndroid);
            }
#else

            yield return null;

#endif
            // Checking Game Orientation is located here due to
            // delay in setting process Screen.orientation on Android
            if (Screen.orientation == ScreenOrientation.PortraitUpsideDown
                || Screen.orientation == ScreenOrientation.Portrait
                || Screen.orientation == ScreenOrientation.AutoRotation)
            {
                SetScaleToPortrait();
            }
            else if (isCameraSwitching
                && (Screen.orientation == ScreenOrientation.LandscapeLeft
                    || Screen.orientation == ScreenOrientation.LandscapeRight))
            {
                SetScaleToLandscape();

                DebugPrinter.Print("█ [Camera Feed] Scale INVERTED:" +
                    " Game Orientation was locked before " +
                    " in Code or Player Settings");
            }
            else
            {
                DebugPrinter.Print("█ [Camera Feed] Scale NOT Fixed:" +
                    " Game Orientation was locked before" +
                    " in Code or Player Settings");
            }
        }
        else // Landscape Device Orientation
        {

#if UNITY_ANDROID

            // to avoid seeing incorrect scale due to nuances of Android

            yield return new WaitForSeconds(
                    delayBeforeFixingScaleOnAndroid);

#else

            yield return null;

#endif
            // Checking of Game Orientation is located here due to
            // delay in setting process Screen.orientation on Android
            if (Screen.orientation == ScreenOrientation.LandscapeLeft
                || Screen.orientation == ScreenOrientation.LandscapeRight
                || Screen.orientation == ScreenOrientation.AutoRotation)
            {
                SetScaleToLandscape();
            }
            else if (isCameraSwitching
                && (Screen.orientation == ScreenOrientation.Portrait
                    || Screen.orientation == ScreenOrientation.PortraitUpsideDown))
            {
                SetScaleToPortrait();

                DebugPrinter.Print("█ [Camera Feed] Scale INVERTED:" +
                    " Game Orientation was locked before " +
                    " in Code or Player Settings");
            }
            else
            {
                DebugPrinter.Print("█ [Camera Feed] Scale NOT Fixed:" +
                    " Game Orientation was locked before" +
                    " in Code or Player Settings");
            }
        }

#if UNITY_ANDROID

        if (isFirstFixingScaleAndroid)
		{
            isFirstFixingScaleAndroid = false;
        }

#endif

        fixScaleBaseCoroutineReference = null;

        DebugPrinter.Print("█ [Camera Feed] Fixing Scale... Completed" +
            $" Scr Ornt = {Screen.orientation}");
    }

    private void SetScaleToLandscape()
    {
        rectTransform.localScale = Vector3.one;
    }

    private void SetScaleToPortrait()
    {
        rectTransform.localScale = new Vector3(
            1f / currentAspectRatio + scaleFixForPortraitMode,
            1f / currentAspectRatio + scaleFixForPortraitMode,
            1f / currentAspectRatio + scaleFixForPortraitMode);
    }

    private WebCamTexture CreateWebCamTexture(string deviceName)
    {
        DebugPrinter.Print($"█ [Camera Feed] New WCT for: {deviceName}");

        return new WebCamTexture(
            deviceName, Screen.width, Screen.height, requestedFPS);
    }

    public void SwitchCamera()
    {
        if (switchCameraCoroutineReference != null)
        {
            StopCoroutine(switchCameraCoroutineReference);

            switchCameraCoroutineReference = null;
        }

        switchCameraCoroutineReference = StartCoroutine(
            SwitchCameraCoroutine());
    }

    private IEnumerator SwitchCameraCoroutine()
    {
        if (webCamDevices.Length <= 1)
        {
            DebugPrinter.Print($"█ [Camera Feed] No Camera to Switch");
        }
        else
        {
            OnCameraSwitched.Invoke();

            // to avoid seeing the old texture for a moment
            yield return new WaitForSeconds(delayBeforeSwitchingCamera);

            webCamTextureTarget.Stop();

            if (webCamTextureTarget.deviceName == webCamDeviceRear.name)
            {
                if (!webCamTextureFront)
                {
                    webCamTextureFront =
                        CreateWebCamTexture(webCamDeviceFront.name);
                }

                webCamDeviceTarget = webCamDeviceFront;

                webCamTextureTarget = webCamTextureFront;

                DebugPrinter.Print($"█ [Camera Feed] Switch => Front");
            }
            else
            {
                if (!webCamTextureRear)
                {
                    webCamTextureRear =
                        CreateWebCamTexture(webCamDeviceRear.name);
                }

                webCamDeviceTarget = webCamDeviceRear;

                webCamTextureTarget = webCamTextureRear;

                DebugPrinter.Print($"█ [Camera Feed] Switch => Rear");
            }

            webCamTextureTarget.Play();

            rawImage.texture = webCamTextureTarget;

            FixScale(true);
        }
    }

    public WebCamTexture GetWebCamTexture()
    {
        return webCamTextureTarget;
    }

    public void PrintSettings()
    {
        DebugPrinter.Print($"█ [Camera Feed] Size:" +
            $" {webCamTextureTarget.width} x {webCamTextureTarget.height}" +
            $"; FPS: {webCamTextureTarget.requestedFPS}" +
            $"; App Target FPS: {Application.targetFrameRate}");
    }

    private void PrintAvailableResolutions()
    {
        if (webCamDeviceTarget.availableResolutions != null)
        {
            foreach (var item in webCamDeviceTarget.availableResolutions)
            {
                DebugPrinter.Print(
                    "█ [Camera Feed] Resolution: " + item.ToString());
            }
        }
    }

    private void OnDestroy()
    {
        if (webCamTextureTarget)
        {
            webCamTextureTarget.Stop();
        }
    }
}