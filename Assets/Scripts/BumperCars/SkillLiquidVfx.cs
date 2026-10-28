using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BumperCars
{
    /// <summary>Shared, world-space particle emitters. Vehicle scale never stretches the liquid.</summary>
    [DisallowMultipleComponent]
    public sealed class SkillLiquidVfx : MonoBehaviour
    {
        private struct SalivaImpact
        {
            public Vector3 position;
            public float time;
            public float scale;
        }

        private static SkillLiquidVfx instance;
        private static Mesh puddleMesh;
        private readonly List<SalivaImpact> impacts = new List<SalivaImpact>(32);
        private ParticleSystem salivaDrops;
        private ParticleSystem salivaSplash;
        private ParticleSystem bubbles;
        private ParticleSystem inkCore;
        private ParticleSystem inkDrops;
        private ParticleSystem inkMist;

        private static SkillLiquidVfx Instance
        {
            get
            {
                if (instance == null)
                {
                    GameObject root = new GameObject("Skill Liquid VFX (World Space)");
                    instance = root.AddComponent<SkillLiquidVfx>();
                    instance.Initialize();
                }

                return instance;
            }
        }

        public static Material GetPuddleMaterial()
        {
            return Resources.Load<Material>("VFX/SalivaPuddle");
        }

        public static Mesh GetPuddleMesh()
        {
            if (puddleMesh != null)
            {
                return puddleMesh;
            }

            puddleMesh = new Mesh { name = "Liquid Splat Quad", hideFlags = HideFlags.HideAndDontSave };
            puddleMesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f)
            };
            puddleMesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            puddleMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            puddleMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            puddleMesh.RecalculateNormals();
            puddleMesh.RecalculateBounds();
            return puddleMesh;
        }

        public static float GetSalivaScale(Transform zone)
        {
            Vector3 footprint = zone.lossyScale;
            return Mathf.Clamp(Mathf.Sqrt(Mathf.Abs(footprint.x * footprint.z) / (2.8f * 1.2f)), 0.6f, 6f);
        }

        public static void PlaySaliva(Vector3 source, Vector3 landing, Vector3 direction, float visualScale = 1f)
        {
            SkillLiquidVfx vfx = Instance;
            Vector3 side = Vector3.Cross(Vector3.up, direction.normalized);
            visualScale = Mathf.Clamp(visualScale, 0.6f, 6f);
            float flightTime = 0.24f + 0.04f * (visualScale - 1f);
            for (int i = 0; i < 8; i++)
            {
                Vector3 target = landing + side * Random.Range(-0.7f, 0.7f) * visualScale
                    + direction * Random.Range(-0.22f, 0.22f) * visualScale;
                Vector3 velocity = (target - source) / flightTime + Vector3.up * (9.81f * 0.8f * flightTime * 0.5f);
                Emit(vfx.salivaDrops, source, velocity, Random.Range(0.13f, 0.26f) * visualScale, flightTime + 0.04f);
            }

            vfx.impacts.Add(new SalivaImpact { position = landing, time = flightTime, scale = visualScale });
        }

        public static void Bubble(Vector3 position, float visualScale = 1f)
        {
            Emit(Instance.bubbles, position + Vector3.up * 0.04f,
                Vector3.up * Random.Range(0.12f, 0.28f) * visualScale,
                Random.Range(0.12f, 0.23f) * visualScale, Random.Range(0.45f, 0.8f));
        }

        public static void PlayInk(InkFlightPath flight, float width)
        {
            PlayInk(flight.Origin, flight.Direction, flight.Distance, width, flight.Duration);
        }

        public static void PlayInk(Vector3 origin, Vector3 direction, float range, float width, float flightTime = 1.15f)
        {
            SkillLiquidVfx vfx = Instance;
            direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            if (direction.sqrMagnitude < 0.01f)
            {
                direction = Vector3.forward;
            }

            Vector3 side = Vector3.Cross(Vector3.up, direction);
            range = Mathf.Max(0.5f, range);
            width = Mathf.Max(0.2f, width);
            flightTime = Mathf.Max(0.2f, flightTime);
            float visualScale = Mathf.Clamp(Mathf.Sqrt(width / 2.5f), 0.7f, 2.5f);

            float speed = range / flightTime;
            Vector3 velocity = direction * speed;
            // Existing shots keep their particles when several long flights overlap.
            EnsureInkCapacity(vfx.inkCore, 28);
            EnsureInkCapacity(vfx.inkDrops, 36);
            EnsureInkCapacity(vfx.inkMist, 12);

            // Initial offsets form a liquid packet; all parts then fly together without gravity.
            for (int i = 0; i < 28; i++)
            {
                Vector3 offset = side * Random.Range(-0.3f, 0.3f) * visualScale
                    + Vector3.up * Random.Range(-0.1f, 0.1f) * visualScale
                    + direction * Random.Range(0f, 0.25f);
                Emit(vfx.inkCore, origin + offset, velocity,
                    Random.Range(0.42f, 0.85f) * visualScale, flightTime);
            }
            for (int i = 0; i < 36; i++)
            {
                Vector3 offset = side * Random.Range(-0.7f, 0.7f) * visualScale
                    + Vector3.up * Random.Range(-0.2f, 0.3f) * visualScale
                    + direction * Random.Range(-0.4f, 0.3f);
                Emit(vfx.inkDrops, origin + offset, velocity,
                    Random.Range(0.09f, 0.25f) * visualScale, flightTime);
            }
            for (int i = 0; i < 12; i++)
            {
                Vector3 offset = side * Random.Range(-0.3f, 0.3f) * visualScale
                    + Vector3.up * Random.Range(0f, 0.2f) - direction * Random.Range(0f, 0.4f);
                Emit(vfx.inkMist, origin + offset, velocity,
                    Random.Range(0.65f, 1.2f) * visualScale, flightTime);
            }
        }

        private static void EnsureInkCapacity(ParticleSystem system, int incomingCount)
        {
            ParticleSystem.MainModule main = system.main;
            main.maxParticles = Mathf.Max(main.maxParticles, system.particleCount + incomingCount);
        }

        private void Initialize()
        {
            Material saliva = Resources.Load<Material>("VFX/SalivaDrop");
            Material ink = Resources.Load<Material>("VFX/InkDrop");
            Material mist = Resources.Load<Material>("VFX/InkMist");
            salivaDrops = CreateEmitter("Saliva / arcing droplets", saliva, 160, 0.8f, true, false);
            salivaSplash = CreateEmitter("Saliva / landing splashes", saliva, 128, 0.7f, true, false);
            bubbles = CreateEmitter("Saliva / rising bubbles", saliva, 96, -0.01f, false, false);
            inkCore = CreateEmitter("Ink / thick liquid jet", ink, 128, 0f, false, false);
            inkDrops = CreateEmitter("Ink / flying droplets", ink, 160, 0f, true, false);
            inkMist = CreateEmitter("Ink / dissipating cloud", mist, 64, 0f, false, true);
            ConfigureInkFlight(inkCore, false);
            ConfigureInkFlight(inkDrops, false);
            ConfigureInkFlight(inkMist, true);
        }

        private static void ConfigureInkFlight(ParticleSystem system, bool cloud)
        {
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            Gradient gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.95f, 1f) });
            color.color = gradient;
            if (!cloud)
            {
                ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(1f, 1f)));
            }
        }

        private ParticleSystem CreateEmitter(string label, Material material, int capacity, float gravity, bool stretched, bool expanding)
        {
            GameObject child = new GameObject(label);
            child.transform.SetParent(transform, false);
            ParticleSystem system = child.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = capacity;
            main.gravityModifier = gravity;
            main.startSpeed = 0f;
            main.startColor = Color.white;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.9f, 0.55f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, expanding
                ? new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.35f, 1.15f), new Keyframe(1f, 1.8f))
                : new AnimationCurve(new Keyframe(0f, 0.65f), new Keyframe(0.12f, 1f), new Keyframe(0.7f, 0.9f), new Keyframe(1f, 0.15f)));

            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = stretched ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = stretched ? 1.1f : 1f;
            renderer.velocityScale = stretched ? 0.025f : 0f;
            renderer.maxParticleSize = 0.18f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            system.Play();
            return system;
        }

        private static void Emit(ParticleSystem system, Vector3 position, Vector3 velocity, float size, float lifetime)
        {
            ParticleSystem.EmitParams parameters = new ParticleSystem.EmitParams
            {
                position = position, velocity = velocity, startSize = size,
                startLifetime = lifetime, startColor = Color.white, rotation = Random.Range(0f, 360f)
            };
            // Non-looping emitters need to resume after their first second, without clearing live particles.
            if (!system.isPlaying)
            {
                system.Play();
            }
            system.Emit(parameters, 1);
        }

        private void Update()
        {
            for (int i = impacts.Count - 1; i >= 0; i--)
            {
                SalivaImpact impact = impacts[i];
                impact.time -= Time.deltaTime;
                if (impact.time > 0f)
                {
                    impacts[i] = impact;
                    continue;
                }

                for (int j = 0; j < 5; j++)
                {
                    Vector2 spread = Random.insideUnitCircle;
                    Emit(salivaSplash, impact.position + Vector3.up * 0.045f,
                        new Vector3(spread.x * 1.8f, Random.Range(0.5f, 1.3f), spread.y * 1.1f) * Mathf.Sqrt(impact.scale),
                        Random.Range(0.09f, 0.18f) * impact.scale, Random.Range(0.16f, 0.28f));
                }

                Bubble(impact.position, impact.scale);
                impacts.RemoveAt(i);
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
            if (puddleMesh != null)
            {
                if (Application.isPlaying) Destroy(puddleMesh);
                else DestroyImmediate(puddleMesh);
                puddleMesh = null;
            }
        }
    }
}
