namespace GoogleARCore.Examples.ARMonsterController
{
    using System;
    using System.Collections.Generic;
    using GoogleARCore;
    using GoogleARCore.Examples.Common;
    using GoogleARCore.Examples.ComputerVision;
    using UnityEngine;
    using UnityEngine.UI;
    using UnityEngine.EventSystems;

#if UNITY_EDITOR
    // Set up touch input propagation while using Instant Preview in the editor.
    using Input = InstantPreviewInput;
#endif

    /// <summary>
    /// Controls the HelloAR example.
    /// </summary>
    public class ARMonsterController : MonoBehaviour
    {
        public ARCoreSession ARSessionManager;

        public Camera FirstPersonCamera;

        // Game objects
        public GameObject DetectedPlanePrefab;
        public GameObject HousePrefabObject;
        public GameObject OctopusPrefabObject;
        public GameObject BallPrefabObject;

        // GPU image setup
        public Image EdgeDetectionBackgroundImage;
        public PointClickHandler ImageTextureToggle;
        public Text CameraIntrinsicsOutput;

        private byte[] edgeDetectionResultImage = null;
        private Texture2D edgeDetectionBackgroundTexture = null;
        private DisplayUvCoords cameraImageToDisplayUvTransformation;
        private ScreenOrientation? cachedOrientation = null;
        private Vector2 cachedScreenDimensions = Vector2.zero;
        private Text imageTextureToggleText;
        private ARCoreSession.OnChooseCameraConfigurationDelegate onChoseCameraConfiguration =
           null;
        private bool resolutioninitialized = false;

        private const float modelRotation = 180.0f;
        private bool isQuitting = false;
        private bool houseAdded = false;

        private List<GameObject> octopii = new List<GameObject>();
        private Anchor anchorHouse = null;
        private int startFrame;

        // Display "GAME OVER" message
        public RectTransform GameOverPanel;
        public Text GameOverText;
        private bool isGameOver = false;

        private global::ComputerVision cvController = null;

        public void Start()
        {
            Debug.Log("Starting ARMonster application");
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            ImageTextureToggle.OnPointClickDetected += onGPUCPUToggleButtonClicked;

            imageTextureToggleText = ImageTextureToggle.GetComponentInChildren<Text>();

            // Register the callback to set camera config before arcore session is enabled.
            onChoseCameraConfiguration = chooseCameraConfiguration;
            ARSessionManager.RegisterChooseCameraConfigurationCallback(
                onChoseCameraConfiguration);

            ARSessionManager.enabled = true;

            cvController = new global::ComputerVision();
            cvController.initialiseCirclePoints(10, 100, 50);
        }

        /// <summary>
        /// The Unity Update() method.
        /// </summary>
        public void Update()
        {
            updateApplicationLifecycle();

            if (!Session.Status.IsValid())
            {
                return;
            }

            if (houseAdded)
            {
                hidePlanesAfterFirstHit();
            }

            if (!isGameOver)
            {
                if (!houseAdded)
                {
                    waitToAddHouse();

                }
                else
                {
                    // Track all circles
                    using (var image = Frame.CameraImage.AcquireCameraImageBytes())
                    {
                        if (!image.IsAvailable)
                        {
                            Debug.Log("Image unavailable");
                            return;
                        }
                        else
                        {
                            Debug.Log("Aquired image ");
                            byte[] cameraImage = onImageAvailable(image.Width, image.Height, image.YRowStride, image.Y, 0);
                            //onImageAvailable2(image.Width, image.Height, image.Y, image.U, image.V, 0);


                        }

                        // Detect collision in the new image
                    }

                    // Move monsters
                    foreach (var octopus in octopii)
                    {
                        octopus.transform.Translate(new Vector3(0.0f, 0.0f, -0.005f), Space.World);

                        // Game over condition - TODO Uncomment - Compare to camera position
                        //////if ((FirstPersonCamera.transform.position - octopus.transform.position).magnitude < 1)
                        ////if (octopus.transform.position.z < 0.0f)  //crossed camera line
                        ////{
                        ////    Debug.Log("Game over");
                        ////    isGameOver = true;
                        ////    GameOverPanel.gameObject.SetActive(true);
                        ////}
                    }

                    // Generate more monsters
                    if (anchorHouse != null && octopii.Count > 0 && (Time.frameCount - startFrame) % 30 == 0)
                    {
                        // create new monster
                        float randomN = UnityEngine.Random.Range(1, 3);

                        Quaternion houseRotation = octopii[0].transform.parent.transform.rotation;

                        for (int i = 0; i < randomN; i++)
                        {
                            float randomX = UnityEngine.Random.Range(-0.3f, 0.3f);
                            float randomY = UnityEngine.Random.Range(0.0f, 0.2f);

                            Vector3 housePosition = new Vector3(octopii[0].transform.parent.transform.position.x + randomX,
                                 octopii[0].transform.parent.transform.position.y + randomY,
                                 octopii[0].transform.parent.transform.position.z);

                            var octopus = Instantiate(OctopusPrefabObject, housePosition, houseRotation);
                            octopus.transform.Rotate(0, modelRotation, 0, Space.Self);
                            octopus.transform.parent = anchorHouse.transform;
                            octopii.Add(octopus);
                        }
                    }
                }
            }

            var cameraIntrinsics = EdgeDetectionBackgroundImage.enabled
               ? Frame.CameraImage.ImageIntrinsics : Frame.CameraImage.TextureIntrinsics;
            string intrinsicsType =
                EdgeDetectionBackgroundImage.enabled ? "CPU Image" : "GPU Texture";
            CameraIntrinsicsOutput.text =
                cameraIntrinsicsToString(cameraIntrinsics, intrinsicsType);
        }

        private void onImageAvailable2(int width, int height, IntPtr pixelBufferY, IntPtr pixelBufferU, IntPtr pixelBufferV, int bufferSize)
        {
            edgeDetectionBackgroundTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            //byte[] bufferYUV = new byte[width * height * 3 / 2];
            byte[] bufferY = new byte[width * height];
            byte[] bufferU = new byte[width * height / 4];
            byte[] bufferV = new byte[width * height / 4];
            System.Runtime.InteropServices.Marshal.Copy(pixelBufferY, bufferY, 0, width * height);
            System.Runtime.InteropServices.Marshal.Copy(pixelBufferU, bufferU, 0, width * height / 4);
            System.Runtime.InteropServices.Marshal.Copy(pixelBufferV, bufferV, 0, width * height / 4);

            Debug.Log("Color image aquired");

            //bufferSize = width * height * 3 / 2;
            //System.Runtime.InteropServices.Marshal.Copy(pixelBufferY, bufferYUV, 0, bufferSize);

            Color color = new Color();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {

                    float Yvalue = bufferY[y * width + x];
                    float Uvalue = bufferU[(y / 2) * (width / 2) + x / 2];
                    float Vvalue = bufferV[(y / 2) * (width / 2) + x / 2];
                    //float Uvalue = bufferU[(y / 2) * (width / 2) + x / 2];
                    //float Vvalue = bufferV[(y / 2) * (width / 2) + x / 2];
                    //float Uvalue = bufferU[(y / 2) * (width / 2) + x / 2 + (width * height)];
                    //float Vvalue = bufferV[(y / 2) * (width / 2) + x / 2 + (width * height) + (width * height) / 4];
                    color.r = Yvalue + (float)(1.37705 * (Vvalue - 128.0f));
                    color.g = Yvalue - (float)(0.698001 * (Vvalue - 128.0f)) - (float)(0.337633 * (Uvalue - 128.0f));
                    color.b = Yvalue + (float)(1.732446 * (Uvalue - 128.0f));

                    color.r /= 255.0f;
                    color.g /= 255.0f;
                    color.b /= 255.0f;

                    if (color.r < 0.0f)
                        color.r = 0.0f;
                    if (color.g < 0.0f)
                        color.g = 0.0f;
                    if (color.b < 0.0f)
                        color.b = 0.0f;

                    if (color.r > 1.0f)
                        color.r = 1.0f;
                    if (color.g > 1.0f)
                        color.g = 1.0f;
                    if (color.b > 1.0f)
                        color.b = 1.0f;

                    color.a = 1.0f;
                    edgeDetectionBackgroundTexture.SetPixel(width - 1 - x, y, color);
                }
            }

            edgeDetectionBackgroundTexture.Apply();
            EdgeDetectionBackgroundImage.material.SetTexture(
                    "_ImageTex", edgeDetectionBackgroundTexture);

            const string TOP_LEFT_RIGHT = "_UvTopLeftRight";
            const string BOTTOM_LEFT_RIGHT = "_UvBottomLeftRight";
            EdgeDetectionBackgroundImage.material.SetVector(TOP_LEFT_RIGHT, new Vector4(
                cameraImageToDisplayUvTransformation.TopLeft.x,
                cameraImageToDisplayUvTransformation.TopLeft.y,
                cameraImageToDisplayUvTransformation.TopRight.x,
                cameraImageToDisplayUvTransformation.TopRight.y));
            EdgeDetectionBackgroundImage.material.SetVector(BOTTOM_LEFT_RIGHT, new Vector4(
                cameraImageToDisplayUvTransformation.BottomLeft.x,
                cameraImageToDisplayUvTransformation.BottomLeft.y,
                cameraImageToDisplayUvTransformation.BottomRight.x,
                cameraImageToDisplayUvTransformation.BottomRight.y));

            //var encodedpng = edgeDetectionBackgroundTexture.EncodeToPNG();
            //var path = Application.persistentDataPath;
            //File.WriteAllBytes(path + "/YUV2RGB.png", encodedpng);
        }


        /// <summary>
        /// Handles a new CPU image.
        /// </summary>
        /// <param name="width">Width of the image, in pixels.</param>
        /// <param name="height">Height of the image, in pixels.</param>
        /// <param name="rowStride">Row stride of the image, in pixels.</param>
        /// <param name="pixelBuffer">Pointer to raw image buffer.</param>
        /// <param name="bufferSize">The size of the image buffer, in bytes.</param>
        private byte[] onImageAvailable(
            int width, int height, int rowStride, IntPtr pixelBuffer, int bufferSize)
        {
            if (edgeDetectionBackgroundTexture == null ||
                edgeDetectionResultImage == null ||
                edgeDetectionBackgroundTexture.width != width ||
                edgeDetectionBackgroundTexture.height != height)
            {
                edgeDetectionBackgroundTexture =
                        new Texture2D(width, height, TextureFormat.R8, false, false);
                edgeDetectionResultImage = new byte[width * height];
                cameraImageToDisplayUvTransformation = Frame.CameraImage.ImageDisplayUvs;
            }

            // CACHING - TODO
            //if (m_CachedOrientation != Screen.orientation ||
            //    m_CachedScreenDimensions.x != Screen.width ||
            //    m_CachedScreenDimensions.y != Screen.height)
            //{
            //    m_CameraImageToDisplayUvTransformation = Frame.CameraImage.ImageDisplayUvs;
            //    m_CachedOrientation = Screen.orientation;
            //    m_CachedScreenDimensions = new Vector2(Screen.width, Screen.height);
            //}

            // Detect edges within the image.
            if (EdgeDetector.Detect(
                edgeDetectionResultImage, pixelBuffer, width, height, rowStride))
            {
                Debug.Log("Egdes detected");

                // Look for circles
                if (edgeDetectionResultImage != null)
                {
                    //Debug.Log("Image size: " + width + " " + height);
                    var result = cvController.detectCircles(edgeDetectionResultImage, width, height, 320, 240, 5, 25, 35);

                    if (result.Count > 0)
                    {
                        Debug.Log("Drawing circles");
                        foreach (var circle in result)
                        {
                            int[,] points;
                            bool pointsAvailable = cvController.circlePoints.TryGetValue(circle.Item1, out points);

                            if (pointsAvailable)
                            {
                                for (int j = 0; j < points.Length / 2; j++)
                                {
                                    int ycoord = circle.Item3 + points[j, 1];
                                    int xcoord = circle.Item2 + points[j, 0];
                                    if (xcoord >= 0 && xcoord < width && ycoord >= 0 && ycoord < height)
                                    {
                                        edgeDetectionResultImage[ycoord * width + xcoord] = 0x96;
                                    }
                                }
                            }
                        }
                    }
                }

                // Update the rendering texture with the edge image.
                edgeDetectionBackgroundTexture.LoadRawTextureData(edgeDetectionResultImage);
                edgeDetectionBackgroundTexture.Apply();
                EdgeDetectionBackgroundImage.material.SetTexture(
                    "_ImageTex", edgeDetectionBackgroundTexture);

                const string TOP_LEFT_RIGHT = "_UvTopLeftRight";
                const string BOTTOM_LEFT_RIGHT = "_UvBottomLeftRight";
                EdgeDetectionBackgroundImage.material.SetVector(TOP_LEFT_RIGHT, new Vector4(
                    cameraImageToDisplayUvTransformation.TopLeft.x,
                    cameraImageToDisplayUvTransformation.TopLeft.y,
                    cameraImageToDisplayUvTransformation.TopRight.x,
                    cameraImageToDisplayUvTransformation.TopRight.y));
                EdgeDetectionBackgroundImage.material.SetVector(BOTTOM_LEFT_RIGHT, new Vector4(
                    cameraImageToDisplayUvTransformation.BottomLeft.x,
                    cameraImageToDisplayUvTransformation.BottomLeft.y,
                    cameraImageToDisplayUvTransformation.BottomRight.x,
                    cameraImageToDisplayUvTransformation.BottomRight.y));

                return edgeDetectionResultImage;
            }

            return null;
        }

        // Returns locations and size of circle
        private List<Tuple<int, int, float>> DetectBall(IntPtr pixelBuffer, int width, int height, int rowStride)
        {
            //EdgeDetector.Sobel(outputImage, pixelBuffer, width, height, rowStride);
            return new List<Tuple<int, int, float>>();
        }

        private void addHouse(TrackableHit hit)
        {
            // Instantiate house model at the hit pose.
            var houseObject = Instantiate(HousePrefabObject, hit.Pose.position, hit.Pose.rotation);

            // Instantiate 3 octopii model at the hit pose.
            float randomX = UnityEngine.Random.Range(-0.3f, 0.3f);
            float randomY = UnityEngine.Random.Range(0.0f, 0.2f);

            var octopusPos1 = new Vector3(hit.Pose.position.x, hit.Pose.position.y, hit.Pose.position.z);
            var octopusPos2 = new Vector3(hit.Pose.position.x + randomX, hit.Pose.position.y + randomY, hit.Pose.position.z);
            var octopusPos3 = new Vector3(hit.Pose.position.x + randomX, hit.Pose.position.y + randomY, hit.Pose.position.z);

            octopii.Add(Instantiate(OctopusPrefabObject, octopusPos1, hit.Pose.rotation));
            octopii.Add(Instantiate(OctopusPrefabObject, octopusPos2, hit.Pose.rotation));
            octopii.Add(Instantiate(OctopusPrefabObject, octopusPos3, hit.Pose.rotation));

            // Compensate for the hitPose rotation facing away from the raycast (i.e.
            // camera).
            houseObject.transform.Rotate(0, modelRotation, 0, Space.Self);
            foreach (var octopus in octopii)
            {
                octopus.transform.Rotate(0, modelRotation, 0, Space.Self);
            }

            // Create an anchor to allow ARCore to track the hitpoint as understanding of
            // the physical world evolves.
            anchorHouse = hit.Trackable.CreateAnchor(hit.Pose);

            // Make Andy model a child of the anchor.
            houseObject.transform.parent = anchorHouse.transform;

            foreach (var octopus in octopii)
            {
                octopus.transform.parent = anchorHouse.transform;
            }

            houseAdded = true;
        }

        private void waitToAddHouse()
        {
            // If we are still waiting to add the house - wait for a touch
            Touch touch;
            if (Input.touchCount < 1 || (touch = Input.GetTouch(0)).phase != TouchPhase.Began)
            {
                return;
            }

            // Should not handle input if the player is pointing on UI.
            if (EventSystem.current.IsPointerOverGameObject(touch.fingerId))
            {
                return;
            }

            // Raycast against the location the player touched to search for planes.
            TrackableHit hit;
            TrackableHitFlags raycastFilter = TrackableHitFlags.PlaneWithinPolygon |
                TrackableHitFlags.FeaturePointWithSurfaceNormal;

            if (Frame.Raycast(touch.position.x, touch.position.y, raycastFilter, out hit))
            {
                // Use hit pose and camera pose to check if hittest is from the
                // back of the plane, if it is, no need to create the anchor.
                if ((hit.Trackable is DetectedPlane) &&
                    Vector3.Dot(FirstPersonCamera.transform.position - hit.Pose.position,
                        hit.Pose.rotation * Vector3.up) < 0)
                {
                    Debug.Log("Hit at back of the current DetectedPlane");
                }
                else
                {
                    addHouse(hit);

                    startFrame = Time.frameCount;

                    EdgeDetectionBackgroundImage.gameObject.SetActive(true);
                }
            }
        }

        private void hidePlanesAfterFirstHit()
        {
            //List<DetectedPlane> temp = new List<DetectedPlane>();
            //Session.GetTrackables<DetectedPlane>(temp);
            //foreach (var plane in temp)
            //{
            //    //planeX = plane.ExtentX;
            //    //planeZ = plane.ExtentZ;
            //}

            // Hide display of detected planes (planes are still tracked in the background)
            foreach (GameObject plane in GameObject.FindGameObjectsWithTag("DetectedPlaneTag"))
            {
                Renderer r = plane.GetComponent<Renderer>();
                DetectedPlaneVisualizer t = plane.GetComponent<DetectedPlaneVisualizer>();
                r.enabled = false;
                t.enabled = false;
            }
        }

        /// <summary>
        /// Code from GoogleARCore Computer Vision example - modified
        /// </summary>
        private void updateApplicationLifecycle()
        {
            // Exit the app when the 'back' button is pressed.
            if (Input.GetKey(KeyCode.Escape))
            {
                Application.Quit();
            }

            quitOnConnectionErrors();

            imageTextureToggleText.text = EdgeDetectionBackgroundImage.enabled ?
                    "Switch to GPU Texture" : "Switch to CPU Image";
        }

        /// <summary>
        /// Code from GoogleARCore Computer Vision example - modified
        /// </summary>
        private void quitOnConnectionErrors()
        {
            if (isQuitting)
            {
                return;
            }

            // Quit if ARCore was unable to connect and give Unity some time for the toast to
            // appear.
            if (Session.Status == SessionStatus.ErrorPermissionNotGranted)
            {
                showAndroidToastMessage("Camera permission is needed to run this application.");
                isQuitting = true;
                Invoke("_DoQuit", 0.5f);
            }
            else if (Session.Status == SessionStatus.FatalError)
            {
                showAndroidToastMessage(
                    "ARCore encountered a problem connecting.  Please start the app again.");
                isQuitting = true;
                Invoke("_DoQuit", 0.5f);
            }
        }

        /// <summary>
        /// Code from GoogleARCore Computer Vision example - modified
        /// </summary>
        private void doQuit()
        {
            Application.Quit();
        }

        /// <summary>
        /// Code from GoogleARCore Computer Vision example
        /// </summary>
        private void showAndroidToastMessage(string message)
        {
            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            AndroidJavaObject unityActivity =
                unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

            if (unityActivity != null)
            {
                AndroidJavaClass toastClass = new AndroidJavaClass("android.widget.Toast");
                unityActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    AndroidJavaObject toastObject =
                        toastClass.CallStatic<AndroidJavaObject>(
                            "makeText", unityActivity, message, 0);
                    toastObject.Call("show");
                }));
            }
        }

        /// <summary>
        /// Code from GoogleARCore Computer Vision example - modified
        /// </summary>
        private void onGPUCPUToggleButtonClicked()
        {
            EdgeDetectionBackgroundImage.enabled = !EdgeDetectionBackgroundImage.enabled;
        }

        /// <summary>
        /// Code from GoogleARCore Computer Vision example 
        /// </summary>
        private string cameraIntrinsicsToString(CameraIntrinsics intrinsics, string intrinsicsType)
        {
            float fovX = 2.0f * Mathf.Rad2Deg * Mathf.Atan2(
                intrinsics.ImageDimensions.x, 2 * intrinsics.FocalLength.x);
            float fovY = 2.0f * Mathf.Rad2Deg * Mathf.Atan2(
                intrinsics.ImageDimensions.y, 2 * intrinsics.FocalLength.y);

            string message = string.Format(
                "Unrotated Camera {4} Intrinsics:{0}  Focal Length: {1}{0}  " +
                "Principal Point: {2}{0}  Image Dimensions: {3}{0}  " +
                "Unrotated Field of View: ({5}°, {6}°)",
                Environment.NewLine, intrinsics.FocalLength.ToString(),
                intrinsics.PrincipalPoint.ToString(), intrinsics.ImageDimensions.ToString(),
                intrinsicsType, fovX, fovY);
            return message;
        }

        /// <summary>
        /// Code from GoogleARCore Computer Vision example - modified
        /// </summary>
        private int chooseCameraConfiguration(List<CameraConfig> supportedConfigurations)
        {
            if (!resolutioninitialized)
            {
                Vector2 ImageSize = supportedConfigurations[0].ImageSize;
                //LowResConfigToggle.GetComponentInChildren<Text>().text = string.Format(
                //    "Low Resolution CPU Image ({0} x {1})", ImageSize.x, ImageSize.y);
                ImageSize = supportedConfigurations[supportedConfigurations.Count - 1].ImageSize;
                //HighResConfigToggle.GetComponentInChildren<Text>().text = string.Format(
                //    "High Resolution CPU Image ({0} x {1})", ImageSize.x, ImageSize.y);

                resolutioninitialized = true;
            }

            //if (m_UseHighResCPUTexture)
            //{
            //    return supportedConfigurations.Count - 1;
            //}

            return 0;
        }
    }
}
