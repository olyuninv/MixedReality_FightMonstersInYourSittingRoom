//-----------------------------------------------------------------------
// <copyright file="HelloARController.cs" company="Google">
//
// Copyright 2017 Google Inc. All Rights Reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// </copyright>
//-----------------------------------------------------------------------

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

        /// <summary>
        /// The first-person camera being used to render the passthrough camera image (i.e. AR
        /// background).
        /// </summary>
        public Camera FirstPersonCamera;

        // Display "GAME OVER" message
        public RectTransform gameOverPanel;
        public Text gameOverText;

        public bool isGameOver = false;

        /// <summary>
        /// A housePrefab for tracking and visualizing detected planes.
        /// </summary>
        public GameObject DetectedPlanePrefab;

        public Image EdgeDetectionBackgroundImage;
              

        /// <summary>
        /// A model to place when a raycast from a user touch hits a plane.
        /// </summary>
        public GameObject housePrefabObject;

        /// <summary>
        /// A model to place when a raycast from a user touch hits a feature point.
        /// </summary>
        public GameObject octopusPrefabObject;

        /// <summary>
        /// A model to place when a raycast from a user touch hits a feature point.
        /// </summary>
        public GameObject ballPrefabObject;

        /// <summary>
        /// The rotation in degrees need to apply to model when the Andy model is placed.
        /// </summary>
        private const float k_ModelRotation = 180.0f;
        private byte[] edgeDetectionResultImage = null;
        private Texture2D edgeDetectionBackgroundTexture = null;
        private DisplayUvCoords m_CameraImageToDisplayUvTransformation;
        public PointClickHandler ImageTextureToggle;
 		private ScreenOrientation? m_CachedOrientation = null;
        private Vector2 m_CachedScreenDimensions = Vector2.zero;
        private Text m_ImageTextureToggleText;
        private ARCoreSession.OnChooseCameraConfigurationDelegate m_OnChoseCameraConfiguration =
           null;
        private bool m_Resolutioninitialized = false;
        public Text CameraIntrinsicsOutput;

        /// <summary>
        /// True if the app is in the process of quitting due to an ARCore connection error,
        /// otherwise false.
        /// </summary>
        private bool m_IsQuitting = false;
        private bool houseAdded = false;

        private List<GameObject> octopii = new List<GameObject>();
        Anchor anchorHouse = null;

        int startFrame;

        public void Start()
        {
            Debug.Log("Starting ARMonster application");
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            ImageTextureToggle.OnPointClickDetected += _OnBackgroundClicked;

            m_ImageTextureToggleText = ImageTextureToggle.GetComponentInChildren<Text>();

            // Register the callback to set camera config before arcore session is enabled.
            m_OnChoseCameraConfiguration = _ChooseCameraConfiguration;
            ARSessionManager.RegisterChooseCameraConfigurationCallback(
                m_OnChoseCameraConfiguration);

            ARSessionManager.enabled = true;
        }

        /// <summary>
        /// The Unity Update() method.
        /// </summary>
        public void Update()
        {
            _UpdateApplicationLifecycle();

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
                    //Camera.current.
                    //android.PixelCopy.request(view, bitmap, (copyResult)->{ })

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
                            _OnImageAvailable(image.Width, image.Height, image.YRowStride, image.Y, 0);
                        }

                        // Detect collision in the new image
                    }

                    // Move monsters
                    foreach (var octopus in octopii)
                    {
                        octopus.transform.Translate(new Vector3(0.0f, 0.0f, -0.005f), Space.World);

                        // Compare to camera position
                        //if ((FirstPersonCamera.transform.position - octopus.transform.position).magnitude < 1)
                        if (octopus.transform.position.z < 0.0f)  //crossed camera line
                        {
                            Debug.Log("Game over");
                            isGameOver = true;
                            gameOverPanel.gameObject.SetActive(true);
                        }
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

                            var octopus = Instantiate(octopusPrefabObject, housePosition, houseRotation);
                            octopus.transform.Rotate(0, k_ModelRotation, 0, Space.Self);
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
                _CameraIntrinsicsToString(cameraIntrinsics, intrinsicsType);
        }

        /// <summary>
        /// Handles a new CPU image.
        /// </summary>
        /// <param name="width">Width of the image, in pixels.</param>
        /// <param name="height">Height of the image, in pixels.</param>
        /// <param name="rowStride">Row stride of the image, in pixels.</param>
        /// <param name="pixelBuffer">Pointer to raw image buffer.</param>
        /// <param name="bufferSize">The size of the image buffer, in bytes.</param>
        private void _OnImageAvailable(
            int width, int height, int rowStride, IntPtr pixelBuffer, int bufferSize)
        {
            //List<Tuple<int, int, float>> locations = DetectBall(pixelBuffer, width, height, rowStride);

            //if (!EdgeDetectionBackgroundImage.enabled)
            //{
            //    return;
            //}

            if (edgeDetectionBackgroundTexture == null ||
                edgeDetectionResultImage == null ||
                edgeDetectionBackgroundTexture.width != width ||
                edgeDetectionBackgroundTexture.height != height)
            {
                edgeDetectionBackgroundTexture =
                        new Texture2D(width, height, TextureFormat.R8, false, false);
                edgeDetectionResultImage = new byte[width * height];
                m_CameraImageToDisplayUvTransformation = Frame.CameraImage.ImageDisplayUvs;
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

                //EdgeDetectionARMonster.CannyEdgeDetector(m_EdgeDetectionResultImage, width, height);

                // Update the rendering texture with the edge image.
                edgeDetectionBackgroundTexture.LoadRawTextureData(edgeDetectionResultImage);
                edgeDetectionBackgroundTexture.Apply();
                EdgeDetectionBackgroundImage.material.SetTexture(
                    "_ImageTex", edgeDetectionBackgroundTexture);

                const string TOP_LEFT_RIGHT = "_UvTopLeftRight";
                const string BOTTOM_LEFT_RIGHT = "_UvBottomLeftRight";
                EdgeDetectionBackgroundImage.material.SetVector(TOP_LEFT_RIGHT, new Vector4(
                    m_CameraImageToDisplayUvTransformation.TopLeft.x,
                    m_CameraImageToDisplayUvTransformation.TopLeft.y,
                    m_CameraImageToDisplayUvTransformation.TopRight.x,
                    m_CameraImageToDisplayUvTransformation.TopRight.y));
                EdgeDetectionBackgroundImage.material.SetVector(BOTTOM_LEFT_RIGHT, new Vector4(
                    m_CameraImageToDisplayUvTransformation.BottomLeft.x,
                    m_CameraImageToDisplayUvTransformation.BottomLeft.y,
                    m_CameraImageToDisplayUvTransformation.BottomRight.x,
                    m_CameraImageToDisplayUvTransformation.BottomRight.y));
            }
        }

        // Returns locations and size of circle
        private List<Tuple<int, int, float>> DetectBall(IntPtr pixelBuffer, int width, int height, int rowStride)
        {
            //EdgeDetector.Sobel(outputImage, pixelBuffer, width, height, rowStride);
            return new List<Tuple<int, int, float>>();
        }

        //private static void Sobel(
        //    byte[] outputImage, IntPtr inputImage, int width, int height, int rowStride)
        //{
        //    // Adjust buffer size if necessary.
        //    int bufferSize = rowStride * height;
        //    if (bufferSize != s_ImageBufferSize || s_ImageBuffer.Length == 0)
        //    {
        //        s_ImageBufferSize = bufferSize;
        //        s_ImageBuffer = new byte[bufferSize];
        //    }

        //    // Move raw data into managed buffer.
        //    System.Runtime.InteropServices.Marshal.Copy(inputImage, s_ImageBuffer, 0, bufferSize);

        private void addHouse()
        {
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
                    // Instantiate house model at the hit pose.
                    var houseObject = Instantiate(housePrefabObject, hit.Pose.position, hit.Pose.rotation);

                    // Instantiate 3 octopii model at the hit pose.
                    float randomX = UnityEngine.Random.Range(-0.3f, 0.3f);
                    float randomY = UnityEngine.Random.Range(0.0f, 0.2f);

                    var octopusPos1 = new Vector3(hit.Pose.position.x, hit.Pose.position.y, hit.Pose.position.z);
                    var octopusPos2 = new Vector3(hit.Pose.position.x + randomX, hit.Pose.position.y + randomY, hit.Pose.position.z);
                    var octopusPos3 = new Vector3(hit.Pose.position.x + randomX, hit.Pose.position.y + randomY, hit.Pose.position.z);

                    octopii.Add(Instantiate(octopusPrefabObject, octopusPos1, hit.Pose.rotation));
                    octopii.Add(Instantiate(octopusPrefabObject, octopusPos2, hit.Pose.rotation));
                    octopii.Add(Instantiate(octopusPrefabObject, octopusPos3, hit.Pose.rotation));

                    // Compensate for the hitPose rotation facing away from the raycast (i.e.
                    // camera).
                    houseObject.transform.Rotate(0, k_ModelRotation, 0, Space.Self);
                    foreach (var octopus in octopii)
                    {
                        octopus.transform.Rotate(0, k_ModelRotation, 0, Space.Self);
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
                    startFrame = Time.frameCount;

                    gameOverPanel.gameObject.SetActive(true);
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
        /// Check and update the application lifecycle.
        /// </summary>
        private void _UpdateApplicationLifecycle()
        {
            // Exit the app when the 'back' button is pressed.
            if (Input.GetKey(KeyCode.Escape))
            {
                Application.Quit();
            }

            //// Only allow the screen to sleep when not tracking.
            //if (Session.Status != SessionStatus.Tracking)
            //{
            //    const int lostTrackingSleepTimeout = 15;
            //    Screen.sleepTimeout = lostTrackingSleepTimeout;
            //}
            //else
            //{
            //    Screen.sleepTimeout = SleepTimeout.NeverSleep;
            //}

            _QuitOnConnectionErrors();

            m_ImageTextureToggleText.text = EdgeDetectionBackgroundImage.enabled ?
                    "Switch to GPU Texture" : "Switch to CPU Image";
        }

        private void _QuitOnConnectionErrors()
        {
            if (m_IsQuitting)
            {
                return;
            }

            // Quit if ARCore was unable to connect and give Unity some time for the toast to
            // appear.
            if (Session.Status == SessionStatus.ErrorPermissionNotGranted)
            {
                _ShowAndroidToastMessage("Camera permission is needed to run this application.");
                m_IsQuitting = true;
                Invoke("_DoQuit", 0.5f);
            }
            else if (Session.Status == SessionStatus.FatalError)
            {
                _ShowAndroidToastMessage(
                    "ARCore encountered a problem connecting.  Please start the app again.");
                m_IsQuitting = true;
                Invoke("_DoQuit", 0.5f);
            }
        }

        /// <summary>
        /// Actually quit the application.
        /// </summary>
        private void _DoQuit()
        {
            Application.Quit();
        }

        /// <summary>
        /// Show an Android toast message.
        /// </summary>
        /// <param name="message">Message string to show in the toast.</param>
        private void _ShowAndroidToastMessage(string message)
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

        private void _OnBackgroundClicked()
        {
            EdgeDetectionBackgroundImage.enabled = !EdgeDetectionBackgroundImage.enabled;
        }

        private string _CameraIntrinsicsToString(CameraIntrinsics intrinsics, string intrinsicsType)
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

        private int _ChooseCameraConfiguration(List<CameraConfig> supportedConfigurations)
        {
            if (!m_Resolutioninitialized)
            {
                Vector2 ImageSize = supportedConfigurations[0].ImageSize;
                //LowResConfigToggle.GetComponentInChildren<Text>().text = string.Format(
                //    "Low Resolution CPU Image ({0} x {1})", ImageSize.x, ImageSize.y);
                ImageSize = supportedConfigurations[supportedConfigurations.Count - 1].ImageSize;
                //HighResConfigToggle.GetComponentInChildren<Text>().text = string.Format(
                //    "High Resolution CPU Image ({0} x {1})", ImageSize.x, ImageSize.y);

                m_Resolutioninitialized = true;
            }

            //if (m_UseHighResCPUTexture)
            //{
            //    return supportedConfigurations.Count - 1;
            //}

            return 0;
        }
    }
}
