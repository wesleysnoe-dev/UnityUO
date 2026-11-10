using UnityEngine;

namespace UOClient.Unity.Fx
{
    /// <summary>
    /// The FX layer's particle systems, made in code: a soft round sprite, one additive material
    /// (Resources/FxAdditive.shader), and builders for flames, embers, missile trails and explosion bursts. Sizes are in
    /// Unity units (= world pixels at the art scale).
    /// </summary>
    public static class FxParticles
    {
        private static Material _additive;
        private static Texture2D _soft;

        public static Material Additive
        {
            get
            {
                if (_additive == null)
                {
                    Shader s = Shader.Find("UOClient/FxAdditive");
                    _additive = new Material(s != null ? s : Shader.Find("Sprites/Default")) { mainTexture = Soft, renderQueue = 3100 };
                }
                return _additive;
            }
        }

        private static Texture2D Soft
        {
            get
            {
                if (_soft != null)
                    return _soft;
                const int n = 64;
                _soft = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        byte v = (byte)(255 * a * a);
                        px[y * n + x] = new Color32(255, 255, 255, v);
                    }
                _soft.SetPixels32(px);
                _soft.Apply(true, true);
                return _soft;
            }
        }

        private static ParticleSystem Make(string name, Transform parent, int max)
        {
            var go = new GameObject(name);
            if (parent != null)
                go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.playOnAwake = false;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Additive;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.OldestInFront;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private static Gradient Ramp(Color hot, Color mid, Color cool, float alpha = 1f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(hot, 0f), new GradientColorKey(mid, 0.35f), new GradientColorKey(cool, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alpha, 0.12f), new GradientAlphaKey(alpha * 0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        /// <summary>
        /// A living flame: hot tongues rising from a strip <paramref name="width"/> wide, a little sway, and (big fires)
        /// embers drifting up. <paramref name="height"/>: how high the tongues reach. Scale: the art scale.
        /// </summary>
        public static ParticleSystem Flame(Transform parent, float width, float height, float scale, bool embers)
        {
            float life = 0.55f;
            ParticleSystem ps = Make("Flame", parent, 120);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = 0f;
            // tongues, not a ball: small soft puffs, few at a time, fading as they rise
            main.startSize = new ParticleSystem.MinMaxCurve(Mathf.Max(2.5f * scale, width * 0.35f), Mathf.Max(3.5f * scale, width * 0.6f));
            main.startColor = Color.white;
            var em = ps.emission;
            em.rateOverTime = Mathf.Clamp(width * 1.4f / scale, 8f, 34f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(width * 0.75f, 1f, 0.1f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-2f * scale, 2f * scale);
            vel.y = new ParticleSystem.MinMaxCurve(height / life * 0.8f, height / life * 1.25f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Ramp(new Color(1f, 0.78f, 0.38f), new Color(1f, 0.42f, 0.08f), new Color(0.55f, 0.06f, 0.01f), 0.42f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.8f), new Keyframe(0.3f, 1f), new Keyframe(1, 0.15f)));
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 3f * scale;
            noise.frequency = 1.4f;
            noise.scrollSpeed = 1.2f;
            ps.Play();
            if (embers)
                Embers(ps.transform, width, height, scale);
            return ps;
        }

        private static void Embers(Transform parent, float width, float height, float scale)
        {
            ParticleSystem ps = Make("Embers", parent, 40);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f * scale, 2.2f * scale);
            main.startColor = new Color(1f, 0.6f, 0.2f);
            var em = ps.emission;
            em.rateOverTime = 6f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(width * 0.6f, 1f, 0.1f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-6f * scale, 6f * scale);
            vel.y = new ParticleSystem.MinMaxCurve(height * 0.9f, height * 1.8f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Ramp(new Color(1f, 0.9f, 0.5f), new Color(1f, 0.5f, 0.1f), new Color(0.5f, 0.05f, 0f));
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 8f * scale;
            noise.frequency = 0.8f;
            ps.Play();
        }

        /// <summary>A missile's trail: glowing puffs left behind in the effect's colour.</summary>
        public static ParticleSystem Trail(Vector3 at, Color color, float scale)
        {
            ParticleSystem ps = Make("Trail", null, 200);
            ps.transform.position = at; // placed before it plays: no streak from the world origin
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(7f * scale, 12f * scale);
            main.startColor = Color.white;
            var em = ps.emission;
            // by time, not distance: a jump (the world re-centring as the player walks) must not spray puffs along it
            em.rateOverTime = 80f;
            em.rateOverDistance = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 2f * scale;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Ramp(Color.Lerp(color, Color.white, 0.6f), color, color * 0.3f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.2f));
            ps.Play();
            return ps;
        }

        /// <summary>An explosion's burst: a flash, a ring of fire thrown out, and sparks; it removes itself.</summary>
        public static void Burst(Vector3 at, Color color, float scale)
        {
            ParticleSystem ps = Make("Burst", null, 80);
            ps.transform.position = at;
            var main = ps.main;
            // a billow of fire round the blast, not a ring thrown out across the screen
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f * scale, 22f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(6f * scale, 13f * scale);
            main.startColor = Color.white;
            main.gravityModifier = -2f * scale; // the heat lifts it
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 8f * scale;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Ramp(Color.Lerp(color, Color.white, 0.3f), color, color * 0.2f, 0.5f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.3f));
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.15f;
            limit.limit = 5f * scale;
            ps.Play();
        }

        /// <summary>
        /// A heal's, cure's or blessing's motes: soft sparks in the spell's colour rising round the target for about a
        /// second and fading; it removes itself.
        /// </summary>
        public static void Motes(Vector3 at, Color color, float scale)
        {
            ParticleSystem ps = Make("Motes", null, 60);
            ps.transform.position = at;
            var main = ps.main;
            main.duration = 0.7f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(2f * scale, 4.5f * scale);
            main.startColor = Color.white;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission;
            em.rateOverTime = 40f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(22f * scale, 34f * scale, 0.1f); // round the body
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-3f * scale, 3f * scale);
            vel.y = new ParticleSystem.MinMaxCurve(14f * scale, 30f * scale);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Ramp(Color.Lerp(color, Color.white, 0.7f), color, color * 0.4f, 0.8f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.4f), new Keyframe(0.25f, 1f), new Keyframe(1, 0f)));
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 4f * scale;
            noise.frequency = 1.1f;
            ps.Play();
        }

        /// <summary>A weapon hit's sparks: a quick spray of bright specks that arc down and die; it removes itself.</summary>
        public static void Sparks(Vector3 at, Color color, float scale)
        {
            ParticleSystem ps = Make("Sparks", null, 30);
            ps.transform.position = at;
            var main = ps.main;
            main.duration = 0.1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(25f * scale, 60f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f * scale, 2.6f * scale);
            main.startColor = Color.white;
            main.gravityModifier = 9f * scale; // they fall as they fly
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 2f * scale;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Ramp(Color.white, color, color * 0.3f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.2f));
            ps.Play();
        }

        /// <summary>Stops a system emitting and removes it once its particles are gone.</summary>
        public static void Release(ParticleSystem ps)
        {
            if (ps == null)
                return;
            ps.transform.SetParent(null, true);
            var main = ps.main;
            main.stopAction = ParticleSystemStopAction.Destroy;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
