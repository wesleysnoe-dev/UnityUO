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

        [Header("P2 night (dims this rig only; no global quality/pipeline settings)")]
        public Color nightSunColor = new Color(0.55f, 0.64f, 0.9f, 1f);
        public float nightSunIntensity = 0.16f;
        public Color nightAmbient = new Color(0.07f, 0.08f, 0.12f, 1f);

        public Light Sun { get; private set; }
        public bool Night { get; private set; }

        public void Apply()
        {
            Sun = GetComponent<Light>();
            if (Sun == null) Sun = gameObject.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.color = Night ? nightSunColor : sunColor;
            Sun.intensity = Night ? nightSunIntensity : sunIntensity;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 0.6f;
            transform.rotation = Quaternion.Euler(sunEuler);

            RenderSettings.sun = Sun;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Night ? nightAmbient : ambient;
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
        }

        /// <summary>Same sun direction, dimmer/cooler sun + darker flat ambient; both sides share it.</summary>
        public void SetNight(bool night)
        {
            Night = night;
            Apply();
        }
    }
}
