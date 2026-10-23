using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MazeRush
{
    public enum MazeThemeId { Jungle, SciFi, Medieval }

    public struct MazeTheme
    {
        public Color fogColor; public float fogDensity;
        public Color ambient; public Color lightTint; public float lightIntensity;
        public Color background;
        public float bloom; public float vignette; public Color colorFilter; public float saturation;

        public static MazeTheme Get(MazeThemeId id)
        {
            switch (id)
            {
                case MazeThemeId.Jungle:
                    return new MazeTheme { fogColor = new Color(0.12f, 0.22f, 0.12f), fogDensity = 0.02f, ambient = new Color(0.6f, 0.7f, 0.55f),
                        lightTint = new Color(1f, 0.95f, 0.7f), lightIntensity = 1.4f, background = new Color(0.1f, 0.2f, 0.12f),
                        bloom = 0.4f, vignette = 0.3f, colorFilter = new Color(0.95f, 1f, 0.9f), saturation = 15f };
                case MazeThemeId.Medieval:
                    return new MazeTheme { fogColor = new Color(0.16f, 0.12f, 0.09f), fogDensity = 0.02f, ambient = new Color(0.7f, 0.55f, 0.4f),
                        lightTint = new Color(1f, 0.7f, 0.4f), lightIntensity = 1.4f, background = new Color(0.1f, 0.08f, 0.06f),
                        bloom = 0.3f, vignette = 0.4f, colorFilter = new Color(1f, 0.93f, 0.82f), saturation = -5f };
                default:
                    return new MazeTheme { fogColor = new Color(0.03f, 0.05f, 0.12f), fogDensity = 0.015f, ambient = new Color(0.45f, 0.55f, 0.8f),
                        lightTint = new Color(0.6f, 0.8f, 1f), lightIntensity = 1.4f, background = new Color(0.02f, 0.03f, 0.08f),
                        bloom = 0.8f, vignette = 0.3f, colorFilter = new Color(0.9f, 0.95f, 1f), saturation = 10f };
            }
        }
    }

    // CONTRACT. Owner: polish agent. Safe to call from any client; purely visual/audio.
    public static class Fx
    {
        public static MazeThemeId Theme = MazeThemeId.SciFi;

        static Material s_mat;
        static AudioClip s_coin, s_unlock, s_hit;
        static GameObject s_volume;

        static bool Headless => Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        public static void CoinCollected(Vector3 pos)
        {
            if (Headless) return;
            Burst(pos, new Color(1f, 0.85f, 0.2f), 14, 0.35f, 2.5f, 0.07f, 0.5f, false);
            Play(ref s_coin, pos, 0.25f, () => Tone(880f, 1320f, 0.12f, 0.5f));
        }

        public static void ExitUnlocked(Vector3 pos)
        {
            if (Headless) return;
            Burst(pos, new Color(0.3f, 1f, 0.4f), 60, 0.9f, 5f, 0.12f, 1.1f, true);
            Play(ref s_unlock, pos, 0.35f, () => Tone(440f, 1100f, 0.6f, 0.5f));
        }

        public static void MonsterHit(Vector3 pos)
        {
            if (Headless) return;
            Burst(pos, new Color(1f, 0.15f, 0.1f), 20, 0.3f, 3f, 0.15f, 0.4f, false);
            Play(ref s_hit, pos, 0.35f, () => Buzz(110f, 0.3f));
        }

        public static void ApplyAtmosphere()
        {
            if (Headless) return;
            try
            {
                MazeTheme t = MazeTheme.Get(Theme);
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = t.fogColor;
                RenderSettings.fogDensity = t.fogDensity;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = t.ambient;

                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                {
                    if (l.type != LightType.Directional) continue;
                    l.color = t.lightTint; l.intensity = t.lightIntensity; break;
                }

                var cam = Camera.main;
                if (cam != null)
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = t.background;
                    var data = cam.GetUniversalAdditionalCameraData();
                    if (data != null) data.renderPostProcessing = true;
                }

                if (s_volume == null)
                {
                    s_volume = new GameObject("FxVolume") { hideFlags = HideFlags.DontSave };
                    var vol = s_volume.AddComponent<Volume>();
                    vol.isGlobal = true; vol.priority = 50; vol.weight = 1f;
                    vol.profile = ScriptableObject.CreateInstance<VolumeProfile>();
                }
                var profile = s_volume.GetComponent<Volume>().profile;
                if (!profile.TryGet(out Bloom b)) b = profile.Add<Bloom>(true);
                b.intensity.Override(t.bloom); b.threshold.Override(0.9f); b.scatter.Override(0.6f);
                if (!profile.TryGet(out Vignette v)) v = profile.Add<Vignette>(true);
                v.intensity.Override(Mathf.Min(t.vignette, 0.2f)); v.smoothness.Override(0.4f);
                if (!profile.TryGet(out ColorAdjustments c)) c = profile.Add<ColorAdjustments>(true);
                c.colorFilter.Override(t.colorFilter); c.saturation.Override(t.saturation); c.contrast.Override(8f);
            }
            catch (System.Exception e) { Debug.LogWarning("Fx.ApplyAtmosphere skipped: " + e.Message); }
        }

        // ---------- particles ----------
        static Material GetMat()
        {
            if (s_mat != null) return s_mat;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) return null;
            s_mat = new Material(sh) { hideFlags = HideFlags.DontSave };
            if (s_mat.HasProperty("_Surface"))
            {
                s_mat.SetFloat("_Surface", 1); s_mat.SetFloat("_Blend", 2);
                s_mat.SetFloat("_SrcBlend", 1); s_mat.SetFloat("_DstBlend", 1); s_mat.SetFloat("_ZWrite", 0);
                s_mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                s_mat.renderQueue = 3000;
            }
            return s_mat;
        }

        static void Burst(Vector3 pos, Color col, int count, float life, float speed, float size, float duration, bool ring)
        {
            var mat = GetMat();
            if (mat == null) return;
            var go = new GameObject("FxBurst") { hideFlags = HideFlags.DontSave };
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.5f; main.loop = false; main.playOnAwake = false;
            main.startLifetime = life; main.startSpeed = speed; main.startSize = size;
            main.startColor = col; main.maxParticles = count; main.gravityModifier = ring ? 0f : 0.5f;
            var em = ps.emission; em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape;
            if (ring) { sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.1f; sh.radiusThickness = 0f; }
            else { sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.1f; }
            var col2 = ps.colorOverLifetime; col2.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col2.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
            Object.Destroy(go, duration + life + 0.5f);
        }

        // ---------- audio ----------
        static void Play(ref AudioClip clip, Vector3 pos, float vol, System.Func<AudioClip> make)
        {
            if (clip == null) clip = make();
            AudioSource.PlayClipAtPoint(clip, pos, vol);
        }

        const int Rate = 22050;

        static AudioClip Tone(float f0, float f1, float len, float amp)
        {
            int n = (int)(Rate * len);
            var d = new float[n];
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                ph += 2 * System.Math.PI * Mathf.Lerp(f0, f1, t) / Rate;
                float env = Mathf.Min(1f, i / 200f) * (1f - t);
                d[i] = (float)System.Math.Sin(ph) * amp * env;
            }
            var c = AudioClip.Create("fx_tone", n, 1, Rate, false); c.SetData(d, 0);
            return c;
        }

        static AudioClip Buzz(float f, float len)
        {
            int n = (int)(Rate * len);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float saw = ((i * f / Rate) % 1f) * 2f - 1f;
                d[i] = saw * 0.4f * Mathf.Min(1f, i / 100f) * (1f - t);
            }
            var c = AudioClip.Create("fx_buzz", n, 1, Rate, false); c.SetData(d, 0);
            return c;
        }
    }
}
