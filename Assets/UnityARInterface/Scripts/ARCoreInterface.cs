using System;
using System.Collections.Generic;
using UnityEngine;
using GoogleARCore;

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
        private ARCoreBackgroundRenderer m_BackgroundRenderer;
        private Matrix4x4 m_DisplayTransform = Matrix4x4.identity;
        private List<Vector4> m_TempPointCloud = new List<Vector4>();

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

            // this task is async, and is not connected when we return true
            // but it works anyway. we could make this method an iterator.
            m_Session.Connect(m_SessionConfig);
            return true;
        }

        public IEnumerator<CustomYieldInstruction> ConnectServiceSync()
        {
            var asyncTask = m_Session.Connect(m_SessionConfig);
            yield return asyncTask.WaitForCompletion();
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
            if (Frame.TrackingState != TrackingState.Tracking)
                return false;

            var camTexture = Frame.CameraImage.Texture;
            cameraImage.height = camTexture.height;
            cameraImage.width = camTexture.width;
            return true;
        }

        public override bool TryGetPointCloud(ref PointCloud pointCloud)
        {
            // Fill in the data to draw the point cloud.
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

        public override void SetupCamera(Camera camera)
        {
            camera.gameObject.SetActive(false);
            m_BackgroundRenderer = camera.gameObject.AddComponent<ARCoreBackgroundRenderer>();
            m_BackgroundRenderer.BackgroundMaterial = Resources.Load("Materials/ARBackground", typeof(Material)) as Material;
            camera.gameObject.SetActive(true);
        }

        public override void UpdateCamera(Camera camera)
        {
            if (Screen.orientation == m_CachedScreenOrientation)
                return;

            CalculateDisplayTransform();
            m_CachedScreenOrientation = Screen.orientation;
        }

        private bool PlaneUpdated(TrackedPlane tp, BoundedPlane bp)
        {
            var extents = (tp.ExtentX != bp.extents.x || tp.ExtentZ != bp.extents.y);
            var rotation = tp.Rotation != bp.rotation;
            var position = tp.Position != bp.center;
            return (extents || rotation || position);
        }

        public override void Update()
        {
            if (Frame.TrackingState != TrackingState.Tracking)
                return;

            Frame.GetPlanes(m_TrackedPlaneBuffer);
            foreach (var trackedPlane in m_TrackedPlaneBuffer)
            {
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
                    else if (PlaneUpdated(trackedPlane, boundedPlane))
                    {
                        boundedPlane.center = trackedPlane.Position;
                        boundedPlane.rotation = trackedPlane.Rotation;
                        boundedPlane.extents.x = trackedPlane.ExtentX;
                        boundedPlane.extents.y = trackedPlane.ExtentZ;
                        OnPlaneUpdated(boundedPlane);
                    }
                }
                // add any new planes
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
                    planesToRemove.Add(trackedPlane);
                }
            }

            foreach (var plane in planesToRemove)
                m_TrackedPlanes.Remove(plane);
        }
    }
}
