using System.Collections.Generic;
using UnityEngine;

namespace BumperCars
{
    /// <summary>One shared mix for the two split-screen players.</summary>
    [DisallowMultipleComponent]
    public sealed class BumperCarAudio : MonoBehaviour
    {
        [Header("Mix")]
        [SerializeField, Range(0f, 1f)] private float masterVolume = 0.9f;
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.32f;
        [SerializeField, Range(0f, 1f)] private float effectsVolume = 0.8f;
        [SerializeField, Range(0f, 1f)] private float engineVolume = 0.26f;
        [SerializeField, Range(0f, 1f)] private float tireVolume = 0.15f;
        [SerializeField, Range(0f, 1f)] private float salivaVolume = 0.3f;

        private sealed class VehicleLoops
        {
            public BumperCarController car;
            public AudioSource engine;
            public AudioSource tires;
            public AudioSource saliva;
            public bool salivaRequested;
        }

        private struct PendingImpact
        {
            public float strength;
            public CarHitboxSection section;
            public float pan;
        }

        private static BumperCarAudio instance;
        private readonly List<VehicleLoops> vehicles = new List<VehicleLoops>(2);
        private readonly AudioSource[] effects = new AudioSource[16];
        private readonly Dictionary<ulong, PendingImpact> pendingImpacts = new Dictionary<ulong, PendingImpact>();
        private readonly Dictionary<ulong, float> lastImpacts = new Dictionary<ulong, float>();
        private GameObject audioRoot;
        private AudioSource music;
        private AudioClip impactClip, inkClip, inkHitClip, startClip, countdownClip, endClip;
        private AudioClip engineClip, tireClip, salivaClip;
        private AudioClip frontImpactClip, sideImpactClip, rearImpactClip, heavyImpactClip;
        private bool initialized, matchStarted, matchEnded;
        private int nextEffect;
        private float musicGain, duckGain = 1f, duckUntil;
        private float lastInkHit1 = float.NegativeInfinity, lastInkHit2 = float.NegativeInfinity;

        public static bool IsInitialized => instance != null && instance.initialized && instance.isActiveAndEnabled;

        public static BumperCarAudio Ensure(GameObject host)
        {
            if (instance != null) return instance;
            if (host == null) return null;
            BumperCarAudio audio = host.GetComponent<BumperCarAudio>();
            if (audio == null) audio = host.AddComponent<BumperCarAudio>();
            audio.Initialize();
            return audio;
        }

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (initialized) return;
            if (instance != null && instance != this)
            {
                // This component shares its host with the game manager.
                Destroy(this);
                return;
            }
            instance = this;
            audioRoot = new GameObject("Game Audio");
            audioRoot.transform.SetParent(transform, false);
            music = CreateSource("Music", 220);
            music.clip = Load("ArenaLoop");
            music.loop = true;
            impactClip = Load("Impact");
            frontImpactClip = Load("ImpactFront");
            sideImpactClip = Load("ImpactSide");
            rearImpactClip = Load("ImpactRear");
            heavyImpactClip = Load("ImpactHeavy");
            inkClip = Load("InkBurst");
            inkHitClip = Load("InkHit");
            startClip = Load("MatchStart");
            countdownClip = Load("Countdown");
            endClip = Load("MatchEnd");
            engineClip = Load("EngineLoop");
            tireClip = Load("TireRollLoop");
            salivaClip = Load("SalivaLoop");
            for (int i = 0; i < effects.Length; i++) effects[i] = CreateSource("Effect " + (i + 1), 100);
            initialized = true;
        }

        private static AudioClip Load(string name)
        {
            return Resources.Load<AudioClip>("Audio/BumperCars/" + name);
        }

        private AudioSource CreateSource(string label, int priority)
        {
            GameObject child = new GameObject(label, typeof(AudioSource));
            child.transform.SetParent(audioRoot.transform, false);
            AudioSource source = child.GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.priority = priority;
            source.volume = 0f;
            return source;
        }

        public static void BeginMatch()
        {
            if (!IsInitialized || instance.matchStarted) return;
            instance.matchStarted = true;
            instance.matchEnded = false;
            instance.musicGain = 0f;
            if (instance.music.clip != null) instance.music.Play();
            foreach (BumperCarController car in FindObjectsOfType<BumperCarController>())
                if (car.isActiveAndEnabled) instance.GetVehicle(car);
            instance.Emit(instance.startClip, 0.75f, 1f, 0f);
        }

        public static void EndMatch()
        {
            if (!IsInitialized || !instance.matchStarted || instance.matchEnded) return;
            instance.matchEnded = true;
            foreach (VehicleLoops vehicle in instance.vehicles) vehicle.salivaRequested = false;
            instance.Emit(instance.endClip, 0.9f, 1f, 0f);
        }

        public static bool PlayImpact(float strength, BumperCarController car,
            CarHitboxSection section = CarHitboxSection.Body, BumperCarController otherCar = null, int surfaceId = 0)
        {
            if (!IsInitialized) return false;
            if (instance.matchEnded) return true;
            uint first = car != null ? unchecked((uint)car.GetInstanceID()) : 0u;
            uint second = otherCar != null ? unchecked((uint)otherCar.GetInstanceID()) : unchecked((uint)surfaceId);
            ulong key = first < second ? ((ulong)first << 32) | second : ((ulong)second << 32) | first;
            if (instance.lastImpacts.TryGetValue(key, out float last) && Time.unscaledTime - last < 0.08f) return true;
            strength = Mathf.Clamp01(strength);
            // Both vehicle callbacks describe one crash. Keep the stronger hit,
            // including its victim's contact section, regardless of callback order.
            if (!instance.pendingImpacts.TryGetValue(key, out PendingImpact pending) || strength > pending.strength)
                instance.pendingImpacts[key] = new PendingImpact { strength = strength, section = section, pan = Pan(car) };
            return true;
        }

        private void LateUpdate()
        {
            foreach (KeyValuePair<ulong, PendingImpact> entry in pendingImpacts)
            {
                PendingImpact impact = entry.Value;
                AudioClip clip = impact.section == CarHitboxSection.Front ? frontImpactClip
                    : impact.section == CarHitboxSection.Rear ? rearImpactClip : sideImpactClip;
                float strength = impact.strength;
                float pitch = Mathf.Lerp(1.2f, 0.82f, strength) * Random.Range(0.98f, 1.02f);
                Emit(clip != null ? clip : impactClip, Mathf.Lerp(0.24f, 0.9f, Mathf.Sqrt(strength)), pitch, impact.pan);
                float weight = Mathf.InverseLerp(0.25f, 1f, strength);
                if (weight > 0f) Emit(heavyImpactClip, 0.72f * Mathf.Pow(weight, 1.3f),
                    Mathf.Lerp(1.08f, 0.78f, strength), impact.pan);
                if (strength >= 0.45f) DuckMusic();
                lastImpacts[entry.Key] = Time.unscaledTime;
            }
            // Already queued impacts still play if their damage ended the match.
            pendingImpacts.Clear();
        }

        public static void PlayInk(BumperCarController car)
        {
            if (!IsInitialized || instance.matchEnded) return;
            instance.Emit(instance.inkClip, 0.95f, Random.Range(0.96f, 1.04f), Pan(car));
            instance.DuckMusic();
        }

        public static void PlayInkHit(BumperCarPlayer player)
        {
            if (!IsInitialized || instance.matchEnded) return;
            float last = player == BumperCarPlayer.Player1 ? instance.lastInkHit1 : instance.lastInkHit2;
            if (Time.unscaledTime - last < 0.1f) return;
            if (player == BumperCarPlayer.Player1) instance.lastInkHit1 = Time.unscaledTime;
            else instance.lastInkHit2 = Time.unscaledTime;
            instance.Emit(instance.inkHitClip, 0.85f, Random.Range(0.94f, 1.06f), Pan(player));
        }

        public static void SetSaliva(BumperCarController car, bool active)
        {
            if (!IsInitialized || car == null || instance.matchEnded) return;
            VehicleLoops vehicle = instance.GetVehicle(car);
            vehicle.salivaRequested = active;
        }

        public static void PlayCountdown(int seconds)
        {
            if (!IsInitialized || instance.matchEnded || seconds < 1 || seconds > 10) return;
            instance.Emit(instance.countdownClip, seconds <= 3 ? 0.7f : 0.45f,
                seconds <= 3 ? 1.12f : 1f, 0f);
        }

        private VehicleLoops GetVehicle(BumperCarController car)
        {
            foreach (VehicleLoops existing in vehicles)
                if (existing.car == car) return existing;
            VehicleLoops vehicle = new VehicleLoops { car = car };
            vehicle.engine = CreateSource(car.name + " Engine", 180);
            vehicle.engine.clip = engineClip;
            vehicle.engine.loop = true;
            vehicle.engine.panStereo = Pan(car);
            vehicle.tires = CreateSource(car.name + " Tires", 185);
            vehicle.tires.clip = tireClip;
            vehicle.tires.loop = true;
            vehicle.tires.panStereo = Pan(car);
            vehicle.saliva = CreateSource(car.name + " Saliva", 120);
            vehicle.saliva.clip = salivaClip;
            vehicle.saliva.loop = true;
            vehicle.saliva.panStereo = Pan(car);
            vehicles.Add(vehicle);
            return vehicle;
        }

        private void Emit(AudioClip clip, float gain, float pitch, float pan)
        {
            if (clip == null) return;
            int selected = nextEffect;
            for (int i = 0; i < effects.Length; i++)
            {
                int candidate = (nextEffect + i) % effects.Length;
                if (!effects[candidate].isPlaying) { selected = candidate; break; }
            }
            AudioSource source = effects[selected];
            nextEffect = (selected + 1) % effects.Length;
            if (source.isPlaying) source.Stop();
            source.pitch = pitch;
            source.panStereo = pan;
            source.volume = masterVolume * effectsVolume * Mathf.Clamp01(gain);
            source.clip = clip;
            source.Play();
        }

        private void DuckMusic()
        {
            duckUntil = Mathf.Max(duckUntil, Time.unscaledTime + 0.22f);
        }

        private void Update()
        {
            if (!initialized || !matchStarted) return;
            float dt = Time.unscaledDeltaTime;
            musicGain = Mathf.MoveTowards(musicGain, matchEnded ? 0f : 1f, dt / (matchEnded ? 0.8f : 1f));
            duckGain = Mathf.MoveTowards(duckGain, Time.unscaledTime < duckUntil ? 0.55f : 1f, dt * 4f);
            music.volume = masterVolume * musicVolume * musicGain * duckGain;
            if (matchEnded && musicGain <= 0f) music.Stop();
            for (int i = vehicles.Count - 1; i >= 0; i--)
            {
                VehicleLoops vehicle = vehicles[i];
                if (vehicle.car == null)
                {
                    Destroy(vehicle.engine.gameObject);
                    Destroy(vehicle.tires.gameObject);
                    Destroy(vehicle.saliva.gameObject);
                    vehicles.RemoveAt(i);
                    continue;
                }
                bool running = !matchEnded && vehicle.car.ControlsEnabled && Time.timeScale > 0f;
                float planarSpeed = vehicle.car.Body != null ? vehicle.car.CurrentPlanarSpeed : 0f;
                float speed = Mathf.Clamp01(planarSpeed / vehicle.car.MaxDrivingSpeed);
                float throttle = Mathf.Abs(vehicle.car.ThrottleInput);
                float mix = masterVolume * effectsVolume;
                float engineTarget = running ? mix * engineVolume * Mathf.Clamp01(0.12f + 0.7f * Mathf.Sqrt(speed) + 0.18f * throttle) : 0f;
                float revs = Mathf.Clamp01(speed * 0.85f + throttle * 0.15f);
                vehicle.engine.pitch = Mathf.Lerp(vehicle.engine.pitch, Mathf.Lerp(0.72f, 1.7f, revs), 1f - Mathf.Exp(-5f * dt));
                FadeLoop(vehicle.engine, engineTarget, dt, 0.18f);
                float rolling = vehicle.car.IsOnGround ? Mathf.InverseLerp(0.6f, vehicle.car.MaxDrivingSpeed, planarSpeed) : 0f;
                float tireTarget = running ? mix * tireVolume * Mathf.Sqrt(rolling) : 0f;
                vehicle.tires.pitch = Mathf.Lerp(0.7f, 1.45f, speed);
                FadeLoop(vehicle.tires, tireTarget, dt, 0.16f);
                float salivaTarget = running && vehicle.salivaRequested ? mix * salivaVolume : 0f;
                FadeLoop(vehicle.saliva, salivaTarget, dt, salivaTarget > 0f ? 0.08f : 0.15f);
            }
        }

        private static void FadeLoop(AudioSource source, float target, float dt, float fadeSeconds)
        {
            source.volume = Mathf.Lerp(source.volume, target, 1f - Mathf.Exp(-3f * dt / Mathf.Max(0.01f, fadeSeconds)));
            if (source.volume > 0.001f && !source.isPlaying && source.clip != null) source.Play();
            else if (source.volume <= 0.001f && target <= 0f && source.isPlaying) source.Stop();
        }

        private static float Pan(BumperCarController car)
        {
            return car != null ? Pan(car.Player) : 0f;
        }

        private static float Pan(BumperCarPlayer player)
        {
            return player == BumperCarPlayer.Player1 ? -0.12f : 0.12f;
        }

        private void OnEnable()
        {
            if (initialized && matchStarted && !matchEnded && music.clip != null) music.Play();
        }

        private void OnDisable()
        {
            if (!initialized) return;
            pendingImpacts.Clear();
            lastImpacts.Clear();
            music.Stop();
            musicGain = 0f;
            foreach (AudioSource source in effects) source.Stop();
            foreach (VehicleLoops vehicle in vehicles)
            {
                vehicle.salivaRequested = false;
                vehicle.engine.Stop(); vehicle.engine.volume = 0f;
                vehicle.tires.Stop(); vehicle.tires.volume = 0f;
                vehicle.saliva.Stop(); vehicle.saliva.volume = 0f;
            }
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            if (audioRoot != null)
            {
                if (Application.isPlaying) Destroy(audioRoot);
                else DestroyImmediate(audioRoot);
            }
        }
    }
}
