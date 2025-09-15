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

public class ScaleControl : MonoBehaviour
{
    public void ScaleLocalScale(Vector3 scale)
    {
        transform.localScale = Vector3.Scale(transform.localScale, scale);
    }

    public void ScaleLocalScaleX(float scaleX)
    {
        transform.localScale =
            Vector3.Scale(transform.localScale, new Vector3(scaleX, 1f, 1f));
    }

    public void ScaleLocalScaleY(float scaleY)
    {
        transform.localScale =
            Vector3.Scale(transform.localScale, new Vector3(1f, scaleY, 1f));
    }

    public void ScaleLocalScaleZ(float scaleZ)
    {
        transform.localScale =
            Vector3.Scale(transform.localScale, new Vector3(1f, 1f, scaleZ));
    }
}
