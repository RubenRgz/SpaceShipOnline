using UnityEngine;

public class CS_CameraManager : MonoBehaviour
{
    #region [Variables]
    // Set value to the in-world distance between the left & right edges of your scene.
    float sceneWidth = 20.0f;
    private int CurrentWidth = 0;
    private int CurrentHeight = 0;

    Camera Camera;
    #endregion

    #region [Unity]
    // Start is called before the first frame update
    void Start()
    {
        Camera = GetComponent<Camera>();
        SetFixedViewSize();
    }

    // Update is called once per frame
    void Update()
    {
        if (Screen.width != CurrentWidth || Screen.height != CurrentHeight)
        {
            SetFixedViewSize();
        }
    }
    #endregion

    #region [View Settings]
    /// <summary>
    /// Sets the desired view size in the current ortographic camera
    /// </summary>
    private void SetFixedViewSize()
    {
        // Update current screen size
        CurrentWidth = Screen.width;
        CurrentHeight = Screen.height;

        // Resize view to mantain the same desired unities in screen (width and height)
        float unitsPerPixel = sceneWidth / Screen.width;
        float desiredHalfHeight = 0.5f * unitsPerPixel * Screen.height;
        Camera.orthographicSize = desiredHalfHeight;
    }
    #endregion
}
