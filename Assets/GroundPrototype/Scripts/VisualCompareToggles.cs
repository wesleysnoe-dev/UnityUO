using System.Collections.Generic;
using UnityEngine;

namespace UOClient.GroundPrototype
{
    /// <summary>
    /// P2 visual toggles (IMGUI buttons + keys), applied to BOTH sides at once:
    ///   T = sample textures on/off (off = flat palette colours, same soft mask)
    ///   F = fluids on/off (water + lava surfaces, probes and lava lights)
    ///   L = lava point light on/off (real light; lava emission stays on either way)
    ///   N = night on/off (SharedLightRig dims sun + ambient; same for both sides)
    /// Backend view modes stay on 1/2/3 in CompareOverlayLabels.
    /// </summary>
    public sealed class VisualCompareToggles : MonoBehaviour
    {
        [System.NonSerialized] public GroundVisualFactory visuals;
        public SharedLightRig lightRig;
        public readonly List<GameObject> fluidRoots = new List<GameObject>();
        public readonly List<Light> lavaLights = new List<Light>();

        public bool TexturesOn { get; private set; } = true;
        public bool FluidsOn { get; private set; } = true;
        public bool LavaLightOn { get; private set; } = true;
        public bool NightOn { get; private set; }

        public string StateLine =>
            $"Textures {(TexturesOn ? "ON" : "off")} · Fluids {(FluidsOn ? "ON" : "off")} · Lava light {(LavaLightOn ? "ON" : "off")} · {(NightOn ? "NIGHT" : "DAY")}";

        public void SetTextures(bool on)
        {
            TexturesOn = on;
            visuals?.SetTextures(on);
        }

        public void SetFluids(bool on)
        {
            FluidsOn = on;
            foreach (var root in fluidRoots)
                if (root != null) root.SetActive(on);
        }

        public void SetLavaLight(bool on)
        {
            LavaLightOn = on;
            foreach (var l in lavaLights)
                if (l != null) l.enabled = on;
        }

        public void SetNight(bool on)
        {
            NightOn = on;
            if (lightRig != null) lightRig.SetNight(on);
        }

        private void OnGUI()
        {
            HandleKeys();

            const float bw = 132f, gap = 6f;
            float x = Screen.width - 4 * (bw + gap) - 6f, y = 74f;
            if (GUI.Button(new Rect(x, y, bw, 24), "T Textures " + (TexturesOn ? "ON" : "off"))) SetTextures(!TexturesOn);
            if (GUI.Button(new Rect(x + (bw + gap), y, bw, 24), "F Fluids " + (FluidsOn ? "ON" : "off"))) SetFluids(!FluidsOn);
            if (GUI.Button(new Rect(x + 2 * (bw + gap), y, bw, 24), "L Lava light " + (LavaLightOn ? "ON" : "off"))) SetLavaLight(!LavaLightOn);
            if (GUI.Button(new Rect(x + 3 * (bw + gap), y, bw, 24), "N " + (NightOn ? "Night" : "Day"))) SetNight(!NightOn);
        }

        private void HandleKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            switch (e.keyCode)
            {
                case KeyCode.T: SetTextures(!TexturesOn); e.Use(); break;
                case KeyCode.F: SetFluids(!FluidsOn); e.Use(); break;
                case KeyCode.L: SetLavaLight(!LavaLightOn); e.Use(); break;
                case KeyCode.N: SetNight(!NightOn); e.Use(); break;
            }
        }
    }
}
