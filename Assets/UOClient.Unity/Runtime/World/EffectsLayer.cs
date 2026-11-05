using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity
{
    /// <summary>The graphic effects running in the world (missiles, animations, lightning).</summary>
    public sealed class EffectsLayer : IDisposable
    {
        private readonly List<EffectView> _effects = new List<EffectView>();
        private readonly Func<GraphicEffect, EffectView> _makeView;

        /// <param name="makeView">Builds the view for an effect, or returns null when there is no world to show it in.</param>
        public EffectsLayer(Func<GraphicEffect, EffectView> makeView)
        {
            _makeView = makeView;
        }

        /// <summary>Effects alive right now.</summary>
        public int Count => _effects.Count;

        public void Start(GraphicEffect e)
        {
            if (_effects.Count > 64)
                return; // a storm of effects should not cost more than the fight it decorates
            EffectView v = _makeView(e);
            if (v != null)
                _effects.Add(v);
        }

        public void Update(WorldStreamer world)
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                EffectView v = _effects[i];
                if (v.Done)
                {
                    GraphicEffect e = v.Effect;
                    if (e.Type == GraphicEffect.Moving && e.Explode)
                        Start(new GraphicEffect
                        {
                            Type = GraphicEffect.AtLocation, Graphic = 0x36CB, Hue = e.Hue,
                            SourceX = (int)v.Position.x, SourceY = (int)v.Position.y, SourceZ = (int)v.Position.z
                        });
                    v.Dispose();
                    _effects.RemoveAt(i);
                    continue;
                }
                v.Render(world);
            }
        }

        /// <summary>Lightning bolts are gump frames (0x4E20..) dropped onto the target.</summary>
        public void DrawLightningGUI(UI.GumpArt gumpArt, Camera camera, WorldStreamer world, int artScale)
        {
            foreach (EffectView v in _effects)
            {
                if (!v.IsLightning)
                    continue;
                UI.UiImage img = gumpArt.Gump(0x4E20 + v.LightningFrame, v.Effect.Hue);
                if (img == null)
                    continue;
                // Bottom-center on the target's feet, sized as world art (project the top too, to get the scale).
                Vector3 p = v.Position;
                var feetPx = new Vector2((p.x - p.y) * 22f, (p.x + p.y) * 22f + 22f - p.z * 4f) * artScale;
                Vector2 feet = world.ToUnity(feetPx), top = world.ToUnity(feetPx - new Vector2(0, img.Height * artScale));
                Vector3 s = camera.WorldToScreenPoint(new Vector3(feet.x, feet.y, 0));
                Vector3 t = camera.WorldToScreenPoint(new Vector3(top.x, top.y, 0));
                float h = Mathf.Abs(t.y - s.y), w = h * img.Width / Mathf.Max(1, img.Height);
                GUI.DrawTexture(new Rect(s.x - w / 2, Screen.height - s.y - h, w, h), img.Texture);
            }
        }

        public void Dispose()
        {
            foreach (EffectView v in _effects)
                v.Dispose();
            _effects.Clear();
        }
    }
}
