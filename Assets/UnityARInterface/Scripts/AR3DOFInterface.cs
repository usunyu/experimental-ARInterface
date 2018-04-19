using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UnityARInterface
{
    public class AR3DOFInterface : ARInterface
    {
        private Camera m_Camera;

        // Gyroscope

        private Gyroscope m_Gyroscope;
        private GameObject m_CameraContainer;
        private Quaternion m_Rotation;

        // Camera

        private WebCamTexture m_BackCamera;
        private RawImage m_Background;
        private AspectRatioFitter m_Fitter;

        public override bool IsSupported
        {
            get
            {
                // no gyroscope supported
#if !UNITY_EDITOR
                if (!SystemInfo.supportsGyroscope)
                {
                    return false;
                }
#endif
                // no camera detected
                if (WebCamTexture.devices.Length == 0)
                {
                    return false;
                }
                SetupBackCamera();
                // no back camera found
                if (m_BackCamera == null)
                {
                    return false;
                }
                return true;
            }
        }

        private void SetupBackCamera()
        {
#if UNITY_EDITOR
            if (m_BackCamera == null)
            {
                for (int i = 0; i < WebCamTexture.devices.Length; i++)
                {
                    m_BackCamera = new WebCamTexture(WebCamTexture.devices[i].name, Screen.width, Screen.height);
                    break;
                }
            }
#endif
            for (int i = 0; i < WebCamTexture.devices.Length; i++)
            {
                if (!WebCamTexture.devices[i].isFrontFacing)
                {
                    m_BackCamera = new WebCamTexture(WebCamTexture.devices[i].name, Screen.width, Screen.height);
                    break;
                }
            }
        }

        public void SetupBackgroundImage(RawImage background)
        {
            m_Background = background;
        }

        public void SetupAspectRatioFitter(AspectRatioFitter fitter)
        {
            m_Fitter = fitter;
        }

        public override IEnumerator StartService(Settings settings)
        {
            if (!IsSupported)
            {
                Debug.LogError("The 3 DOF orientation tracking is not supported");
                return null;
            }

            if (m_BackCamera == null)
            {
                SetupBackCamera();
            }

            // both services are supported, enable 3 DOF tracking
            m_CameraContainer = new GameObject("Camera Container");
            if (m_Camera == null)
            {
                // fallback to main camera
                m_Camera = Camera.main;
            }
            m_CameraContainer.transform.position = m_Camera.transform.position;
            m_Camera.transform.SetParent(m_CameraContainer.transform);

            m_Gyroscope = Input.gyro;
            m_Gyroscope.enabled = true;
            m_CameraContainer.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            m_Rotation = new Quaternion(0f, 0f, 1f, 0f);

            m_BackCamera.Play();
            m_Background.texture = m_BackCamera;

            IsRunning = true;

            return null;
        }

        public override void StopService()
        {
            IsRunning = false;
        }

        public override void SetupCamera(Camera camera)
        {
            m_Camera = camera;
        }

        public override void UpdateCamera(Camera camera)
        {
        }

        public override void Update()
        {
            if (!IsRunning || m_Background == null || m_Fitter == null)
                return;

            // update camera
            float ratio = (float) m_BackCamera.width / (float) m_BackCamera.height;
            m_Fitter.aspectRatio = ratio;

            float scaleY = m_BackCamera.videoVerticallyMirrored ? -1f : 1f;
            m_Background.rectTransform.localScale = new Vector3(1f, scaleY, 1f);

            int orient = -m_BackCamera.videoRotationAngle;
            m_Background.rectTransform.localEulerAngles = new Vector3(0, 0, orient);
            
            // update gyro
#if !UNITY_EDITOR
            m_Camera.transform.localRotation = m_Gyroscope.attitude * m_Rotation;
#endif
        }

        public override bool TryGetUnscaledPose(ref Pose pose)
        {
            return false;
        }

        public override bool TryGetCameraImage(ref CameraImage cameraImage)
        {
            return false;
        }

        public override bool TryGetPointCloud(ref PointCloud pointCloud)
        {
            return false;
        }

        public override LightEstimate GetLightEstimate()
        {
            return new LightEstimate();
        }

        public override Matrix4x4 GetDisplayTransform()
        {
            return Matrix4x4.identity;
        }

        public override List<HitTestResult> HitTest(Vector2 point, HitTestResultType type)
        {
            return new List<HitTestResult>();
        }
    }
}