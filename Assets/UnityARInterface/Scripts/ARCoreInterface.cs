using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR;
using GoogleARCore;
using GoogleARCoreInternal;
//using ARCoreNative = GoogleARCoreInternal.NativeApi;

namespace UnityARInterface
{
    public class ARCoreInterface : ARInterface
    {
        private List<TrackedPlane> m_TrackedPlaneBuffer = new List<TrackedPlane>();
        private float? m_HorizontalFov;
        private float? m_VerticalFov;
        private ScreenOrientation m_CachedScreenOrientation;
        private Dictionary<TrackedPlane, BoundedPlane> m_TrackedPlanes = new Dictionary<TrackedPlane, BoundedPlane>();
        private ARCoreSession m_Session;
        private ARCoreSessionConfig m_SessionConfig;
        private Matrix4x4 m_DisplayTransform = Matrix4x4.identity;
        private List<Vector4> m_TempPointCloud = new List<Vector4>();

        private NativeApi m_NativeApi;

        public override bool StartService(Settings settings)
        {
            if (m_Session == null)
            {
                m_SessionConfig = ScriptableObject.CreateInstance<ARCoreSessionConfig>();
                m_SessionConfig.EnablePlaneFinding = settings.enablePlaneDetection;
                m_SessionConfig.EnableLightEstimation = settings.enableLightEstimation;

                var gameObject = new GameObject("Session Manager");

                // Deactivate the GameObject before adding the SessionComponent
                // or else the Awake method will be called before we have set
                // the session config.
                gameObject.SetActive(false);
                m_Session = gameObject.AddComponent<ARCoreSession>();
                m_Session.SessionConfig = m_SessionConfig;
                m_Session.ConnectOnAwake = false;

                gameObject.SetActive(true);
            }

            m_Session.Connect(m_SessionConfig);
            //m_NativeApi = NativeApi.CreateSession();
            
            Debug.Log("SESSION STATE: " + Session.ConnectionState.ToString());
            return true;
            //return Session.ConnectionState == SessionConnectionState.Connected;
        }

        public override void StopService()
        {
            // Not implemented on ARCore.
            return;
        }

        public override bool TryGetUnscaledPose(ref Pose pose)
        {
            if (Frame.TrackingState != TrackingState.Tracking)
                return false;

            pose.position = Frame.Pose.position;
            pose.rotation = Frame.Pose.rotation;
            return true;
        }

        public override bool TryGetCameraImage(ref CameraImage cameraImage)
        {
            /*
            ARCoreNative.NativeImage nativeImage = new ARCoreNative.NativeImage();
            if (ARCoreNative.Device.TryAcquireLatestImageBuffer(ref nativeImage))
            {
                cameraImage.width = (int)nativeImage.width;
                cameraImage.height = (int)nativeImage.height;

                var planeInfos = nativeImage.planeInfos;

                // The Y plane is always the first one.
                var yOffset = planeInfos[0].offset;
                var numYBytes = planeInfos[0].size;
                IntPtr yPlaneStart = new IntPtr(nativeImage.planeData.ToInt64() + yOffset);

                if (cameraImage.y == null || cameraImage.y.Length != numYBytes)
                    cameraImage.y = new byte[numYBytes];

                Marshal.Copy(yPlaneStart, cameraImage.y, 0, (int)numYBytes);

                // UV planes are not deterministic, but we want all the data in one go
                // so the offset will be the min of the two planes.
                int uvOffset = Mathf.Min(
                    (int)nativeImage.planeInfos[1].offset,
                    (int)nativeImage.planeInfos[2].offset);

                // Find the end of the uv plane data
                int uvDataEnd = 0;
                for (int i = 1; i < planeInfos.Count; ++i)
                {
                    uvDataEnd = Mathf.Max(uvDataEnd, (int)planeInfos[i].offset + planeInfos[i].size);
                }

                // Finally, compute the number of bytes by subtracting the end from the beginning
                var numUVBytes = uvDataEnd - uvOffset;
                IntPtr uvPlaneStart = new IntPtr(nativeImage.planeData.ToInt64() + uvOffset);

                if (cameraImage.uv == null || cameraImage.uv.Length != numUVBytes)
                    cameraImage.uv = new byte[numUVBytes];

                Marshal.Copy(uvPlaneStart, cameraImage.uv, 0, (int)numUVBytes);

                ARCoreNative.Device.ReleaseImageBuffer(nativeImage);

                // The data is usually provided as VU rather than UV,
                // so we need to swap the bytes.
                // There's no way to know this currently, but it's always
                // been this way on every device so far.
                for (int i = 1; i < numUVBytes; i += 2)
                {
                    var b = cameraImage.uv[i - 1];
                    cameraImage.uv[i - 1] = cameraImage.uv[i];
                    cameraImage.uv[i] = b;
                }

                return true;
            }
            */
            var camTexture = Frame.CameraImage.Texture;
            cameraImage.height = camTexture.height;
            cameraImage.width = camTexture.width;

            var uvs = Frame.CameraImage.DisplayUvCoords;

            return false;
        }

        public override bool TryGetPointCloud(ref PointCloud pointCloud)
        {
            // Fill in the data to draw the point cloud.
            // for performance reasons & access to the confidence data,
            // we should probably convert PointCloud to use Vector4s
            m_TempPointCloud.Clear();
            Frame.PointCloud.CopyPoints(m_TempPointCloud);

            if (m_TempPointCloud.Count == 0)
                return false;

            if (pointCloud.points == null)
                pointCloud.points = new List<Vector3>();

            pointCloud.points.Clear();
            foreach (Vector3 point in m_TempPointCloud)
                pointCloud.points.Add(point);

            return true;
        }

        public override LightEstimate GetLightEstimate()
        {
            if (Session.ConnectionState == SessionConnectionState.Connected)
            {
                return new LightEstimate()
                {
                    capabilities = LightEstimateCapabilities.AmbientIntensity,
                    ambientIntensity = Frame.LightEstimate.PixelIntensity
                };
            }
            else
            {
                // Zero initialized means capabilities will be None
                return new LightEstimate();
            }
        }

		public override Matrix4x4 GetDisplayTransform()
		{
			return m_DisplayTransform;
		}

        private void CalculateDisplayTransform()
        {
            var cosTheta = 1f;
            var sinTheta = 0f;

            switch (Screen.orientation)
            {
                case ScreenOrientation.Portrait:
                    cosTheta = 0f;
                    sinTheta = -1f;
                    break;
                case ScreenOrientation.PortraitUpsideDown:
                    cosTheta = 0f;
                    sinTheta = 1f;
                    break;
                case ScreenOrientation.LandscapeLeft:
                    cosTheta = 1f;
                    sinTheta = 0f;
                    break;
                case ScreenOrientation.LandscapeRight:
                    cosTheta = -1f;
                    sinTheta = 0f;
                    break;
            }

            m_DisplayTransform.m00 = cosTheta;
            m_DisplayTransform.m01 = sinTheta;
            m_DisplayTransform.m10 = sinTheta;
            m_DisplayTransform.m11 = -cosTheta;
        }

        ARBackgroundRenderer m_BackgroundRenderer;

        public override void SetupCamera(Camera camera)
        {
            m_BackgroundRenderer = new ARBackgroundRenderer();
            m_BackgroundRenderer.backgroundMaterial =
                Resources.Load("Materials/ARBackground", typeof(Material)) as Material;
            m_BackgroundRenderer.mode = ARRenderMode.MaterialAsBackground;
            m_BackgroundRenderer.camera = camera;
        }

        public override void UpdateCamera(Camera camera)
        {
            if (Screen.orientation == m_CachedScreenOrientation)
                return;

            CalculateDisplayTransform();

            m_CachedScreenOrientation = Screen.orientation;

            if (m_CachedScreenOrientation == ScreenOrientation.Portrait ||
                m_CachedScreenOrientation == ScreenOrientation.PortraitUpsideDown)
            {
                if (m_HorizontalFov.HasValue)
                {
                    camera.fieldOfView = m_HorizontalFov.Value;
                }
                else
                {
                    /*
                    float fieldOfView;
                    if (ARCoreNative.Device.TryGetHorizontalFov(out fieldOfView))
                    {
                        m_HorizontalFov = fieldOfView;
                        camera.fieldOfView = fieldOfView;
                    }
                    */
                }
            }
            else
            {
                if (m_VerticalFov.HasValue)
                {
                    camera.fieldOfView = m_VerticalFov.Value;
                }
                else
                {
                    /*
                    float fieldOfView;
                    if (ARCoreNative.Device.TryGetVerticalFov(out fieldOfView))
                    {
                        m_VerticalFov = fieldOfView;
                        camera.fieldOfView = fieldOfView;
                    }
                    */
                }
            }
        }

        // this replaces the old SDK's "isUpdated" property
        private static bool ExtentsUpdated(TrackedPlane tp, BoundedPlane bp)
        {
            return (tp.ExtentX != bp.extents.x || tp.ExtentZ != bp.extents.y);
        }

        // this is the code inside of ARCoreBackgroundRenderer's update loop basically
        private void UpdateBackground()
        {
            const string mainTex = "_MainTex";
            const string topLeftRight = "_UvTopLeftRight";
            const string bottomLeftRight = "_UvBottomLeftRight";

            var bgMaterial = m_BackgroundRenderer.backgroundMaterial;
            bgMaterial.SetTexture(mainTex, Frame.CameraImage.Texture);

            ApiDisplayUvCoords uvQuad = Frame.CameraImage.DisplayUvCoords;

            bgMaterial.SetVector(topLeftRight,
                new Vector4(uvQuad.TopLeft.x, uvQuad.TopLeft.y, uvQuad.TopRight.x, uvQuad.TopRight.y));
            bgMaterial.SetVector(bottomLeftRight,
                new Vector4(uvQuad.BottomLeft.x, uvQuad.BottomLeft.y, uvQuad.BottomRight.x, uvQuad.BottomRight.y));

            var camera = m_BackgroundRenderer.camera;
            camera.projectionMatrix = Frame.CameraImage.GetCameraProjectionMatrix(camera.nearClipPlane, camera.farClipPlane);
        }

        public override void Update()
        {
            UpdateBackground();

            //SessionManager.Instance.EarlyUpdate();

            if (Frame.TrackingState != TrackingState.Tracking)
                return;

            Frame.GetPlanes(m_TrackedPlaneBuffer);
            foreach (var trackedPlane in m_TrackedPlaneBuffer)
            {
                Debug.Log("tracked plane found !");
                BoundedPlane boundedPlane;
                if (m_TrackedPlanes.TryGetValue(trackedPlane, out boundedPlane))
                {
                    // remove any subsumed planes
                    if (trackedPlane.SubsumedBy != null)
                    {
                        OnPlaneRemoved(boundedPlane);
                        m_TrackedPlanes.Remove(trackedPlane);
                    }
                    // update any planes with changed extents
                    else if (ExtentsUpdated(trackedPlane, boundedPlane))
                    {
                        boundedPlane.extents.x = trackedPlane.ExtentX;
                        boundedPlane.extents.y = trackedPlane.ExtentZ;
                        OnPlaneUpdated(boundedPlane);
                    }
                }
                else
                {
                    boundedPlane = new BoundedPlane()
                    {
                        id = Guid.NewGuid().ToString(),
                        center = trackedPlane.Position,
                        rotation = trackedPlane.Rotation,
                        extents = new Vector2(trackedPlane.ExtentX, trackedPlane.ExtentZ)
                    };

                    m_TrackedPlanes.Add(trackedPlane, boundedPlane);
                    OnPlaneAdded(boundedPlane);
                }
            }

            
            // Check for planes that were removed from the tracked plane list
            List<TrackedPlane> planesToRemove = new List<TrackedPlane>();
            foreach (var kvp in m_TrackedPlanes)
            {
                var trackedPlane = kvp.Key;

                if (!m_TrackedPlaneBuffer.Exists(x => x == trackedPlane))
                {
                    OnPlaneRemoved(kvp.Value);

                    // Add to list here to avoid mutating the dictionary
                    // while iterating over it.
                    planesToRemove.Add(trackedPlane);
                }
            }

            foreach (var plane in planesToRemove)
            {
                m_TrackedPlanes.Remove(plane);
            }
        }
    }
}
