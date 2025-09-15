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

using UnityEngine;

[HelpURL("https://makaka.org/unity-assets")]
public class RotationByMouseControl : MonoBehaviour
{
    [Header("Button")]
    public bool isRotationWithButton = true;
    public int button = 1;

    [Header("Horizontal")]
    [Tooltip("Object for Horizontal Rotation")]
    public bool isHorizontalParent = false;
    public Transform horizontal;
    public string horizontalAxis = "Mouse X";
    public float horizontalDeltaWebGLFactor = 0.1f;
    public float speedHorizontal = 8f;

    private float horizontalDelta;

    [Header("Vertical")]
    [Tooltip("Object for Vertical Rotation")]
    public bool isVerticalParent = false;
    public Transform vertical;
    public string verticalAxis = "Mouse Y";
    public float verticalDeltaWebGLFactor = 0.1f;
    public float speedVertical = -8f;

    private float verticalDelta;

    private void LateUpdate()
    {
        if (isRotationWithButton)
        {
            if (Input.GetMouseButton(button))
            {
                Rotate();
            }
        }
        else
        {
            Rotate();
        }
    }

    private void Rotate()
    {
        horizontalDelta = Input.GetAxis(horizontalAxis);
        verticalDelta = Input.GetAxis(verticalAxis);

#if UNITY_WEBGL && !UNITY_EDITOR

        horizontalDelta *= horizontalDeltaWebGLFactor;
        verticalDelta *= verticalDeltaWebGLFactor;

#endif

        if (horizontal)
        {
            (isHorizontalParent ? horizontal.parent : horizontal).Rotate(
                0f,
                horizontalDelta * speedHorizontal,
                0f);
        }

        if (vertical)
        {
            (isVerticalParent ? vertical.parent : vertical).Rotate(
                verticalDelta * speedVertical,
                0f,
                0f);
        }
    }

}
