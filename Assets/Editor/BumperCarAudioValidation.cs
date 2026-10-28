using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BumperCars.Editor
{
    public static class BumperCarAudioValidation
    {
        private const string Pending = "BumperCars.AudioValidation.Pending";
        internal static readonly string[] ClipNames =
        {
            "ArenaLoop", "Impact", "SalivaLoop", "InkBurst", "InkHit",
            "MatchStart", "Countdown", "MatchEnd", "EngineLoop", "TireRollLoop",
            "ImpactFront", "ImpactSide", "ImpactRear", "ImpactHeavy"
        };

        [MenuItem("Tools/Bumper Cars/Audio/Install and Check Audio %#F4")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            StringBuilder report = new StringBuilder("Bumper Cars audio installation\n");
            CheckClips(report);
            BumperCarGameManager game = Object.FindObjectOfType<BumperCarGameManager>();
            Check(game != null, "The active gameplay scene has a game manager", report);
            if (game.GetComponent<BumperCarAudio>() == null)
                Undo.AddComponent<BumperCarAudio>(game.gameObject);
            EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
            Check(EditorSceneManager.SaveScene(game.gameObject.scene), "Saved the scene audio component", report);
            Save(report, "Logs/BumperCarAudioInstall.txt", true);
        }

        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.playModeStateChanged -= OnPlayState;
            EditorApplication.playModeStateChanged += OnPlayState;
        }

        [MenuItem("Tools/Bumper Cars/Audio/Validate Runtime Audio %#F3")]
        public static void ValidateRuntime()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            // HideAndDontSave captures from interrupted editor runs can survive
            // play-mode teardown. Remove only this validator's scene objects.
            foreach (BumperCarAudioCapture capture in Resources.FindObjectsOfTypeAll<BumperCarAudioCapture>())
                if (capture != null && !EditorUtility.IsPersistent(capture)) Object.DestroyImmediate(capture.gameObject);
            SessionState.SetBool(Pending, true);
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Pending, false);
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            new GameObject("Temporary audio validation") { hideFlags = HideFlags.HideAndDontSave }
                .AddComponent<BumperCarAudioCapture>();
        }

        internal static void CheckClips(StringBuilder report)
        {
            foreach (string name in ClipNames)
            {
                AudioClip clip = Resources.Load<AudioClip>("Audio/BumperCars/" + name);
                Check(clip != null && clip.length > 0.05f, name + " is a real playable audio asset", report);
                report.AppendLine("  " + name + ": " + clip.length.ToString("F2") + "s, " + clip.frequency + "Hz, " + clip.channels + " channels");
                if (name == "ArenaLoop")
                    Check(clip.channels == 2 && clip.length >= 30f, "Background music is a full stereo loop", report);
            }
        }

        internal static void Check(bool condition, string label, StringBuilder report)
        {
            report.AppendLine((condition ? "PASS: " : "FAIL: ") + label);
            if (!condition) throw new InvalidOperationException(label);
        }

        internal static void Save(StringBuilder report, string path, bool passed)
        {
            report.AppendLine("RESULT: " + (passed ? "PASS" : "FAIL"));
            Directory.CreateDirectory("Logs");
            File.WriteAllText(path, report.ToString(), Encoding.UTF8);
            if (passed) Debug.Log(report.ToString()); else Debug.LogError(report.ToString());
        }
    }

    public sealed class BumperCarAudioCapture : MonoBehaviour
    {
        private readonly StringBuilder report = new StringBuilder("Bumper Cars runtime audio validation\n");
        private BumperCarAudio service;
        private BumperCarGameManager game;
        private AudioSource music;
        private ToadSlowTrailSkill toadSkill;
        private BumperCarController toad;
        private bool toadWasEnabled;
        private bool finishing;
        private double deadline;
        private float originalTime;
        private readonly List<GameObject> fixtures = new List<GameObject>();
        private Vector3 originalPosition, originalVelocity, originalAngularVelocity;
        private Quaternion originalRotation;
        private bool originalGravity, movingFixture;

        private IEnumerator Start()
        {
            deadline = EditorApplication.timeSinceStartup + 45d;
            EditorApplication.update += Watchdog;
            yield return new WaitForSecondsRealtime(1.5f);
            if (!Guard(CheckStartup)) yield break;

            double dspBefore = AudioSettings.dspTime;
            float musicBefore = music.time;
            float maximumRms = 0f;
            float[] samples = new float[2048];
            float until = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < until)
            {
                AudioListener.GetOutputData(samples, 0);
                maximumRms = Mathf.Max(maximumRms, Rms(samples));
                music.GetOutputData(samples, 0);
                maximumRms = Mathf.Max(maximumRms, Rms(samples));
                yield return null;
            }
            if (!Guard(() =>
            {
                Check(AudioSettings.dspTime > dspBefore && music.time > musicBefore, "The music sample clock and audio DSP are advancing");
                report.AppendLine("Measured maximum output RMS: " + maximumRms.ToString("F6"));
                Check(maximumRms > 0.00001f, "The audio mixer produces a non-silent signal");
                toadSkill.enabled = false;
                BumperCarAudio.SetSaliva(toad, true);
            })) yield break;

            yield return new WaitForSecondsRealtime(0.35f);
            if (!Guard(() =>
            {
                Check(Sources().Any(s => IsClip(s, "SalivaLoop") && s.isPlaying && s.volume > 0.01f), "Holding the saliva skill plays its bubbling loop");
                BumperCarAudio.SetSaliva(toad, false);
            })) yield break;
            yield return new WaitForSecondsRealtime(0.7f);
            if (!Guard(() =>
            {
                Check(!Sources().Any(s => IsClip(s, "SalivaLoop") && s.isPlaying && s.volume > 0.01f), "Releasing saliva stops its loop cleanly");
                toadSkill.enabled = toadWasEnabled;
            })) yield break;

            yield return CheckDrivingAudio();
            if (finishing) yield break;
            yield return CheckImpactMix();
            if (finishing) yield break;
            yield return CheckPhysicalImpacts();
            if (finishing) yield break;

            HashSet<int> before = PlayingEffects();
            if (!Guard(() =>
            {
                BumperCarImpactFeedback impact = toad.GetComponentInChildren<BumperCarImpactFeedback>();
                Check(impact != null, "The toad has its normal collision feedback component");
                impact.Play(toad.transform.position, 0.7f);
            })) yield break;
            yield return new WaitForSecondsRealtime(0.05f);
            if (!Guard(() => Check(NewEffect(before), "The real collision feedback plays a sound"))) yield break;

            before = PlayingEffects();
            if (!Guard(() =>
            {
                RedCarInkRetreatSkill ink = Object.FindObjectOfType<RedCarInkRetreatSkill>();
                Check(ink != null, "The octopus ink skill is present");
                Invoke(ink, "Activate");
            })) yield break;
            yield return new WaitForSecondsRealtime(0.05f);
            if (!Guard(() => Check(NewEffect(before), "The real ink activation plays its burst sound"))) yield break;

            before = PlayingEffects();
            if (!Guard(() => InkScreenOverlay.ShowForPlayer(toad.Player, 0.5f, 0.2f))) yield break;
            yield return new WaitForSecondsRealtime(0.05f);
            if (!Guard(() => Check(NewEffect(before), "The actual ink hit overlay plays a wet splat"))) yield break;

            before = PlayingEffects();
            if (!Guard(() => SetRemainingTime(9.9f))) yield break;
            yield return new WaitForSecondsRealtime(0.05f);
            if (!Guard(() =>
            {
                Check(NewEffect(before), "The real match timer plays the final-seconds countdown");
                SetRemainingTime(originalTime);
            })) yield break;

            yield return CheckLethalImpact();
            if (finishing) yield break;
            yield return new WaitForSecondsRealtime(1.5f);
            if (!Guard(() =>
            {
                Check(!music.isPlaying || music.volume < 0.01f, "Music fades down when the match ends");
                Check(!Sources().Any(s => (IsClip(s, "EngineLoop") || IsClip(s, "TireRollLoop") || IsClip(s, "SalivaLoop")) && s.isPlaying && s.volume > 0.01f), "Vehicle loops stop after the match ends");
            })) yield break;
            Finish(true);
        }

        private void CheckStartup()
        {
            BumperCarAudioValidation.CheckClips(report);
            service = Object.FindObjectOfType<BumperCarAudio>();
            game = Object.FindObjectOfType<BumperCarGameManager>();
            Check(service != null && BumperCarAudio.IsInitialized, "The gameplay scene initializes one audio service");
            Check(Object.FindObjectsOfType<BumperCarAudio>().Length == 1, "Only one music service exists");
            Check(Object.FindObjectsOfType<AudioListener>().Count(l => l.enabled) == 1, "Split screen uses one active AudioListener");
            AudioSource[] sources = Sources();
            music = sources.FirstOrDefault(s => IsClip(s, "ArenaLoop"));
            Check(music != null && music.loop && music.isPlaying && music.volume > 0.01f, "The looping background music starts automatically");
            Check(sources.All(s => Mathf.Approximately(s.spatialBlend, 0f) && !s.playOnAwake && Mathf.Approximately(s.dopplerLevel, 0f)), "Music and effects use stable two-player 2D audio");
            Check(sources.Count(s => IsClip(s, "EngineLoop") && s.loop) == 2, "Both vehicles have motor loops");
            Check(sources.Count(s => IsClip(s, "TireRollLoop") && s.loop) == 2, "Both vehicles have separate tire rolling loops");
            toadSkill = Object.FindObjectOfType<ToadSlowTrailSkill>();
            Check(toadSkill != null, "The toad saliva skill is present");
            toad = toadSkill.GetComponent<BumperCarController>();
            toadWasEnabled = toadSkill.enabled;
            CheckAudioSectionGeometry();
            originalTime = (float)typeof(BumperCarGameManager).GetField("remainingTime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
            PropertyInfo mute = typeof(EditorUtility).GetProperty("audioMasterMute", BindingFlags.Static | BindingFlags.Public);
            if (mute != null && (bool)mute.GetValue(null)) report.AppendLine("NOTE: The Unity editor audio-mute setting is enabled; system volume was left unchanged.");
        }

        private void CheckAudioSectionGeometry()
        {
            BumperCarCollisionDamage dealer = toad.GetComponent<BumperCarCollisionDamage>();
            BoxCollider chassis = toad.GetComponent<BoxCollider>();
            MethodInfo classify = typeof(BumperCarCollisionDamage).GetMethod("GetAudioSection", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(dealer != null && chassis != null && classify != null, "The real toad exposes its chassis geometry and sound classifier");
            float forwardSign = Vector3.Dot(toad.DriveForwardDirection, toad.transform.forward) >= 0f ? 1f : -1f;
            Vector3[] offsets =
            {
                Vector3.forward * (chassis.size.z * 0.5f * forwardSign),
                Vector3.right * (chassis.size.x * 0.5f),
                Vector3.back * (chassis.size.z * 0.5f * forwardSign)
            };
            CarHitboxSection[] expected = { CarHitboxSection.Front, CarHitboxSection.Body, CarHitboxSection.Rear };
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3 world = toad.transform.TransformPoint(chassis.center + offsets[i]);
                CarHitboxSection actual = (CarHitboxSection)classify.Invoke(dealer, new object[] { world });
                Check(actual == expected[i], "The real toad's offset, scaled chassis correctly identifies its " + expected[i] + " surface");
            }
        }

        private IEnumerator CheckLethalImpact()
        {
            BumperCarController target = null;
            bool defeatEndedMatch = false;
            if (!Guard(() =>
            {
                ClearEffects();
                Vector3 center = new Vector3(1500f, 1100f, 1200f);
                target = CreateProbe("Audio lethal rear target", center, Quaternion.identity);
                target.Body.constraints = RigidbodyConstraints.FreezeAll;
                BumperCarHealth health = target.GetComponent<BumperCarHealth>();
                health.TakeDamage(health.MaxHealth - 1f);
                health.Defeated += defeated =>
                {
                    defeatEndedMatch = true;
                    Invoke(game, "EndGame", "AUDIO CHECK");
                };
                BumperCarController attacker = CreateProbe("Audio lethal rear attacker", center + Vector3.back * 5f, Quaternion.identity);
                attacker.Body.velocity = Vector3.forward * 8f;
                Physics.SyncTransforms();
            })) yield break;
            float until = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < until && !defeatEndedMatch) yield return null;
            yield return new WaitForEndOfFrame();
            if (!Guard(() =>
            {
                Check(defeatEndedMatch && target.GetComponent<BumperCarHealth>().IsDefeated,
                    "A real rear collision defeats the one-health target");
                Check((bool)typeof(BumperCarGameManager).GetField("gameEnded", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game),
                    "The lethal collision ends the real match through its defeat callback");
                Check(Sources().Any(s => IsClip(s, "MatchEnd") && s.isPlaying), "The real match ending plays its finish cue");
                Check(Sources().Any(s => IsClip(s, "ImpactRear") && s.isPlaying),
                    "The lethal rear impact remains audible after the match ends");
                Check(Sources().Count(s => IsSectionImpact(s) && s.isPlaying) == 1,
                    "The lethal collision retains exactly one section impact sound");
            })) yield break;
        }

        private IEnumerator CheckDrivingAudio()
        {
            AudioSource engine = null, tires = null;
            float idleVolume = 0f, idlePitch = 0f;
            if (!Guard(() =>
            {
                originalPosition = toad.Body.position;
                originalRotation = toad.Body.rotation;
                originalVelocity = toad.Body.velocity;
                originalAngularVelocity = toad.Body.angularVelocity;
                originalGravity = toad.Body.useGravity;
                movingFixture = true;
                toadSkill.enabled = false;
                GameObject platform = NewFixture("Audio driving platform");
                platform.transform.position = new Vector3(1000f, 999.5f, 1000f);
                platform.AddComponent<BoxCollider>().size = new Vector3(250f, 1f, 250f);
                toad.Body.position = new Vector3(1000f, 1003f, 1000f);
                toad.transform.position = toad.Body.position;
                Physics.SyncTransforms();
                Collider chassis = toad.GetComponents<Collider>().First(c => !c.isTrigger);
                Vector3 position = toad.Body.position;
                position.y += 1000.03f - chassis.bounds.min.y;
                toad.Body.position = position;
                toad.transform.position = position;
                toad.Body.velocity = Vector3.zero;
                toad.Body.angularVelocity = Vector3.zero;
                toad.Body.useGravity = false;
                Physics.SyncTransforms();
                engine = Sources().First(s => IsClip(s, "EngineLoop") && s.name.StartsWith(toad.name, StringComparison.Ordinal));
                tires = Sources().First(s => IsClip(s, "TireRollLoop") && s.name.StartsWith(toad.name, StringComparison.Ordinal));
            })) yield break;
            yield return new WaitForSecondsRealtime(0.4f);
            if (!Guard(() =>
            {
                idleVolume = engine.volume;
                idlePitch = engine.pitch;
                Check(tires.volume < 0.005f, "Stationary tires do not produce a rolling sound");
            })) yield break;
            float until = Time.realtimeSinceStartup + 0.8f;
            while (Time.realtimeSinceStartup < until)
            {
                toad.Body.velocity = toad.DriveForwardDirection * 30f;
                yield return null;
            }
            if (!Guard(() =>
            {
                Check(toad.IsOnGround, "The driving audio fixture places the real vehicle on a road surface");
                Check(engine.isPlaying && engine.volume > idleVolume + 0.025f && engine.pitch > idlePitch + 0.15f,
                    "Moving the real vehicle raises both engine loudness and pitch");
                Check(tires.isPlaying && tires.volume > 0.015f, "Driving on the road produces an audible rolling layer");
                report.AppendLine("Engine idle/moving gain: " + idleVolume.ToString("F3") + "/" + engine.volume.ToString("F3")
                    + "; pitch: " + idlePitch.ToString("F3") + "/" + engine.pitch.ToString("F3")
                    + "; tire gain: " + tires.volume.ToString("F3"));
            })) yield break;
            until = Time.realtimeSinceStartup + 0.65f;
            while (Time.realtimeSinceStartup < until)
            {
                toad.Body.velocity = Vector3.zero;
                yield return null;
            }
            if (!Guard(() =>
            {
                Check(tires.volume < 0.005f, "The rolling layer fades away when the vehicle stops");
                RestoreVehicle();
                ClearEffects();
            })) yield break;
        }

        private IEnumerator CheckImpactMix()
        {
            float lightGain = 0f, lightPitch = 0f;
            if (!Guard(() =>
            {
                ClearEffects();
                BumperCarAudio.PlayImpact(0.1f, toad, CarHitboxSection.Front, null, 710001);
            })) yield break;
            yield return new WaitForEndOfFrame();
            if (!Guard(() =>
            {
                AudioSource light = Sources().First(s => IsClip(s, "ImpactFront") && s.isPlaying);
                lightGain = light.volume;
                lightPitch = light.pitch;
                Check(!Sources().Any(s => IsClip(s, "ImpactHeavy") && s.isPlaying), "A light contact keeps a short, clean impact without the heavy layer");
                ClearEffects();
                BumperCarAudio.PlayImpact(0.95f, toad, CarHitboxSection.Front, null, 710002);
            })) yield break;
            yield return new WaitForEndOfFrame();
            if (!Guard(() =>
            {
                AudioSource heavy = Sources().First(s => IsClip(s, "ImpactFront") && s.isPlaying);
                Check(heavy.volume > lightGain + 0.1f && heavy.pitch < lightPitch - 0.1f,
                    "Higher damage raises the impact gain and lowers its pitch");
                Check(Sources().Any(s => IsClip(s, "ImpactHeavy") && s.isPlaying && s.volume > 0.05f),
                    "A high damage contact adds an audible low impact body");
                report.AppendLine("Light/heavy impact gain: " + lightGain.ToString("F3") + "/" + heavy.volume.ToString("F3")
                    + "; pitch: " + lightPitch.ToString("F3") + "/" + heavy.pitch.ToString("F3"));
                ClearEffects();
                BumperCarController other = Object.FindObjectsOfType<BumperCarController>().First(c => c != toad);
                BumperCarAudio.PlayImpact(0.1f, toad, CarHitboxSection.Front, other);
                BumperCarAudio.PlayImpact(0.85f, other, CarHitboxSection.Rear, toad);
            })) yield break;
            yield return new WaitForEndOfFrame();
            if (!Guard(() =>
            {
                Check(Sources().Count(s => IsSectionImpact(s) && s.isPlaying) == 1
                    && Sources().Any(s => IsClip(s, "ImpactRear") && s.isPlaying),
                    "Two callbacks for one vehicle pair merge into the strongest collision sound");
                ClearEffects();
                BumperCarAudio.PlayImpact(0.1f, toad, CarHitboxSection.Front, null, 710003);
                BumperCarAudio.PlayImpact(0.1f, toad, CarHitboxSection.Rear, null, 710004);
            })) yield break;
            yield return new WaitForEndOfFrame();
            if (!Guard(() =>
            {
                Check(Sources().Any(s => IsClip(s, "ImpactFront") && s.isPlaying)
                    && Sources().Any(s => IsClip(s, "ImpactRear") && s.isPlaying),
                    "Different surfaces struck in the same frame retain their own sounds");
                ClearEffects();
            })) yield break;
        }

        private IEnumerator CheckPhysicalImpacts()
        {
            CarHitboxSection[] sections = { CarHitboxSection.Front, CarHitboxSection.Body, CarHitboxSection.Rear };
            string[] clips = { "ImpactFront", "ImpactSide", "ImpactRear" };
            for (int i = 0; i < sections.Length; i++)
            {
                BumperCarController attacker = null, target = null;
                int sectionIndex = i;
                if (!Guard(() =>
                {
                    ClearEffects();
                    Vector3 center = new Vector3(1200f + sectionIndex * 30f, 1100f, 1200f);
                    target = CreateProbe("Audio target " + sections[sectionIndex], center, Quaternion.identity);
                    target.Body.constraints = RigidbodyConstraints.FreezeAll;
                    Vector3 direction = sections[sectionIndex] == CarHitboxSection.Body ? Vector3.left
                        : sections[sectionIndex] == CarHitboxSection.Front ? Vector3.back : Vector3.forward;
                    float distance = sections[sectionIndex] == CarHitboxSection.Body ? 4f : 5f;
                    attacker = CreateProbe("Audio attacker " + sections[sectionIndex], center - direction * distance,
                        Quaternion.LookRotation(direction, Vector3.up));
                    attacker.Body.velocity = direction * 8f;
                    Physics.SyncTransforms();
                })) yield break;
                float until = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < until && target != null
                    && target.GetComponent<BumperCarHealth>().CurrentHealth >= target.GetComponent<BumperCarHealth>().MaxHealth)
                    yield return null;
                yield return new WaitForEndOfFrame();
                if (!Guard(() =>
                {
                    Check(target.GetComponent<BumperCarHealth>().CurrentHealth < target.GetComponent<BumperCarHealth>().MaxHealth,
                        "The " + sections[sectionIndex] + " fixture causes a real damaging physics collision");
                    Check(Sources().Any(s => IsClip(s, clips[sectionIndex]) && s.isPlaying),
                        "A real " + sections[sectionIndex] + " hit selects " + clips[sectionIndex]);
                    Check(Sources().Count(s => IsSectionImpact(s) && s.isPlaying) == 1,
                        "The real " + sections[sectionIndex] + " collision produces one section sound");
                    Destroy(attacker.gameObject);
                    Destroy(target.gameObject);
                })) yield break;
            }

            if (!Guard(() =>
            {
                ClearEffects();
                for (int i = 0; i < 2; i++)
                {
                    Vector3 center = new Vector3(1400f + i * 20f, 1100f, 1200f);
                    GameObject wall = NewFixture("Audio wall " + i);
                    wall.transform.position = center;
                    wall.AddComponent<BoxCollider>().size = new Vector3(10f, 5f, 1f);
                    BumperCarController car = CreateProbe("Audio wall driver " + i, center + Vector3.back * 3.5f, Quaternion.identity);
                    car.Body.velocity = Vector3.forward * 10f;
                }
                Physics.SyncTransforms();
            })) yield break;
            float wallUntil = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < wallUntil
                && Sources().Count(s => IsClip(s, "ImpactFront") && s.isPlaying) < 2) yield return null;
            if (!Guard(() =>
            {
                Check(Sources().Count(s => IsClip(s, "ImpactFront") && s.isPlaying) == 2,
                    "Two simultaneous real wall impacts both play their front collision sound");
                ClearEffects();
                DestroyFixtures();
            })) yield break;
        }

        private BumperCarController CreateProbe(string label, Vector3 position, Quaternion rotation)
        {
            GameObject probe = NewFixture(label);
            probe.transform.SetPositionAndRotation(position, rotation);
            Rigidbody body = probe.AddComponent<Rigidbody>();
            body.useGravity = false;
            probe.AddComponent<BoxCollider>().size = new Vector3(2f, 1f, 4f);
            BumperCarController car = probe.AddComponent<BumperCarController>();
            typeof(BumperCarController).GetField("useNegativeZAsForward", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(car, false);
            typeof(BumperCarController).GetField("alignToGround", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(car, false);
            probe.AddComponent<BumperCarHealth>();
            probe.AddComponent<BumperCarCollisionDamage>();
            return car;
        }

        private GameObject NewFixture(string label)
        {
            GameObject fixture = new GameObject(label) { hideFlags = HideFlags.HideAndDontSave };
            fixtures.Add(fixture);
            return fixture;
        }

        private void RestoreVehicle()
        {
            if (!movingFixture || toad == null) return;
            movingFixture = false;
            toad.Body.position = originalPosition;
            toad.Body.rotation = originalRotation;
            toad.Body.velocity = originalVelocity;
            toad.Body.angularVelocity = originalAngularVelocity;
            toad.Body.useGravity = originalGravity;
            toadSkill.enabled = toadWasEnabled;
            Physics.SyncTransforms();
        }

        private void DestroyFixtures()
        {
            // Complete cleanup before ExitPlaymode can interrupt deferred Destroy.
            foreach (GameObject fixture in fixtures) if (fixture != null) DestroyImmediate(fixture);
            fixtures.Clear();
        }

        private void ClearEffects()
        {
            foreach (AudioSource source in Sources()) if (!source.loop) source.Stop();
        }

        private static bool IsSectionImpact(AudioSource source) => IsClip(source, "ImpactFront") || IsClip(source, "ImpactSide") || IsClip(source, "ImpactRear");

        private AudioSource[] Sources() => service.GetComponentsInChildren<AudioSource>(true);
        private static bool IsClip(AudioSource source, string name) => source.clip != null && source.clip.name == name;
        private HashSet<int> PlayingEffects() => new HashSet<int>(Sources().Where(s => !s.loop && s.isPlaying).Select(s => s.GetInstanceID()));
        private bool NewEffect(HashSet<int> before) => Sources().Any(s => !s.loop && s.isPlaying && !before.Contains(s.GetInstanceID()));
        private void SetRemainingTime(float value) => typeof(BumperCarGameManager).GetField("remainingTime", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(game, value);
        private static void Invoke(object target, string method, params object[] arguments) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);
        private static float Rms(float[] samples) { double sum = 0; foreach (float sample in samples) sum += sample * sample; return Mathf.Sqrt((float)(sum / samples.Length)); }
        private void Check(bool condition, string label) => BumperCarAudioValidation.Check(condition, label, report);
        private bool Guard(Action action) { try { action(); return true; } catch (Exception error) { report.AppendLine("FAIL: " + error); Finish(false); return false; } }

        private void Watchdog()
        {
            if (!finishing && EditorApplication.timeSinceStartup > deadline)
            {
                report.AppendLine("FAIL: Audio validation exceeded its 45-second deadline.");
                Finish(false);
            }
        }

        private void Finish(bool passed)
        {
            if (finishing) return;
            finishing = true;
            EditorApplication.update -= Watchdog;
            RestoreVehicle();
            DestroyFixtures();
            if (toad != null) BumperCarAudio.SetSaliva(toad, false);
            if (toadSkill != null) toadSkill.enabled = toadWasEnabled;
            BumperCarAudioValidation.Save(report, "Logs/BumperCarAudioValidation.txt", passed);
            DestroyImmediate(gameObject);
            EditorApplication.ExitPlaymode();
        }

        private void OnDestroy() { EditorApplication.update -= Watchdog; }
    }
}
