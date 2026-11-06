using UnityEngine;
using UnityEngine.Rendering;

namespace UOClient.GroundPrototype
{
    /// <summary>
    /// One directional light + one flat ambient colour, shared by both sides (same scene, same light).
    /// Placed on the scene's "Shared Light Rig" root; creates the Light at runtime.
    /// </summary>
    public sealed class SharedLightRig : MonoBehaviour
    {
        public Vector3 sunEuler = new Vector3(50f, -30f, 0f);
        public Color sunColor = new Color(1f, 0.96f, 0.88f, 1f);
        public float sunIntensity = 1.1f;
        public Color ambient = new Color(0.36f, 0.38f, 0.42f, 1f);

        public Light Sun { get; private set; }

        public void Apply()
        {
            Sun = GetComponent<Light>();
            if (Sun == null) Sun = gameObject.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.color = sunColor;
            Sun.intensity = sunIntensity;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 0.6f;
            transform.rotation = Quaternion.Euler(sunEuler);

            RenderSettings.sun = Sun;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
        }
    }
}
