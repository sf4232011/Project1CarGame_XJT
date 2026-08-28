using UnityEngine;

namespace BumperCars
{
    public sealed class BumperCarImpactFeedback : MonoBehaviour
    {
        [SerializeField] private ParticleSystem impactParticles;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private float minPitch = 0.85f;
        [SerializeField] private float maxPitch = 1.25f;
        [SerializeField] private float minVolume = 0.35f;
        [SerializeField] private float maxVolume = 0.9f;

        private AudioClip generatedImpactClip;

        private void Awake()
        {
            if (impactParticles == null)
            {
                impactParticles = GetComponentInChildren<ParticleSystem>();
            }

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (audioSource != null && audioSource.clip == null)
            {
                generatedImpactClip = CreateImpactClip();
                audioSource.clip = generatedImpactClip;
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f;
            }
        }

        public void Play(Vector3 worldPosition, float normalizedStrength)
        {
            float strength = Mathf.Clamp01(normalizedStrength);

            if (impactParticles != null)
            {
                impactParticles.transform.position = worldPosition;
                ParticleSystem.MainModule main = impactParticles.main;
                main.startSpeed = Mathf.Lerp(2.5f, 7f, strength);
                main.startSize = Mathf.Lerp(0.16f, 0.36f, strength);
                impactParticles.Emit(Mathf.RoundToInt(Mathf.Lerp(10f, 28f, strength)));
            }

            if (audioSource != null)
            {
                audioSource.pitch = Mathf.Lerp(minPitch, maxPitch, Random.value);
                audioSource.volume = Mathf.Lerp(minVolume, maxVolume, strength);
                audioSource.PlayOneShot(audioSource.clip);
            }
        }

        private static AudioClip CreateImpactClip()
        {
            const int sampleRate = 22050;
            const float duration = 0.12f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Exp(-32f * t);
                float lowTone = Mathf.Sin(2f * Mathf.PI * 95f * t);
                float click = Random.Range(-1f, 1f) * Mathf.Exp(-90f * t);
                samples[i] = (lowTone * 0.65f + click * 0.35f) * envelope;
            }

            AudioClip clip = AudioClip.Create("Generated_BumperCar_Impact", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
