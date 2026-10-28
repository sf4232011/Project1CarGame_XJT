using UnityEngine;
using UnityEngine.Rendering;

namespace BumperCars
{
    /// <summary>A wet, irregular surface using the existing slow-zone footprint and lifetime.</summary>
    [DisallowMultipleComponent]
    public sealed class SalivaPuddleVfx : MonoBehaviour
    {
        private static readonly int Opacity = Shader.PropertyToID("_Opacity");
        private static readonly int Progress = Shader.PropertyToID("_Progress");
        private static readonly int Phase = Shader.PropertyToID("_Phase");
        private MeshRenderer surface;
        private Transform surfaceTransform;
        private MaterialPropertyBlock properties;
        private float duration;
        private float age;
        private float bubbleTimer;
        private float phase;

        public void Initialize(float lifetime)
        {
            duration = Mathf.Max(0.1f, lifetime);
            age = 0f;
            phase = Random.Range(0f, 20f);
            bubbleTimer = Random.Range(0.6f, 1.6f);

            if (surface == null)
            {
                GameObject child = new GameObject("Saliva / glossy irregular puddle");
                child.layer = gameObject.layer;
                child.transform.SetParent(transform, false);
                // The original zone is only .05m tall. Lift the surface slightly above the trigger center.
                child.transform.localPosition = new Vector3(0f, 0.65f, 0f);
                child.AddComponent<MeshFilter>().sharedMesh = SkillLiquidVfx.GetPuddleMesh();
                surface = child.AddComponent<MeshRenderer>();
                surface.sharedMaterial = SkillLiquidVfx.GetPuddleMaterial();
                surface.shadowCastingMode = ShadowCastingMode.Off;
                surface.receiveShadows = false;
                surface.lightProbeUsage = LightProbeUsage.Off;
                surface.reflectionProbeUsage = ReflectionProbeUsage.Off;
                surfaceTransform = child.transform;
                properties = new MaterialPropertyBlock();
            }

            UpdateSurface();
        }

        private void Update()
        {
            if (surface == null) return;
            age += Time.deltaTime;
            UpdateSurface();

            bubbleTimer -= Time.deltaTime;
            if (bubbleTimer <= 0f && age < duration * 0.85f)
            {
                Vector3 point = transform.TransformPoint(new Vector3(Random.Range(-0.32f, 0.32f), 0.7f, Random.Range(-0.3f, 0.3f)));
                SkillLiquidVfx.Bubble(point, SkillLiquidVfx.GetSalivaScale(transform));
                bubbleTimer = Random.Range(1.2f, 2f);
            }
        }

        private void UpdateSurface()
        {
            float spread = Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0f, 1f, age / 0.22f));
            surfaceTransform.localScale = new Vector3(spread, 1f, spread);
            properties.SetFloat(Phase, phase);
            properties.SetFloat(Opacity, 0.94f);
            properties.SetFloat(Progress, Mathf.InverseLerp(duration * 0.82f, duration, age));
            surface.SetPropertyBlock(properties);
        }
    }
}
