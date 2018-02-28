using UnityEngine;
using UnityEngine.XR;

namespace UnityARInterface
{
    public class ARInterfaceBackgroundRenderer : ARBackgroundRenderer
    {
        public bool Enabled { get; protected set; }

        public ARInterfaceBackgroundRenderer()
        {
            Enabled = true;
        }

        public bool EnableRendering()
        {
            return Enabled = EnableARBackgroundRendering();
        }

        public void DisableRendering()
        {
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = Color.black;

            DisableARBackgroundRendering();
        
            Enabled = false;
        }
    }
}