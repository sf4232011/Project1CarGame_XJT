using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BumperCars.Editor
{
    public static class InkArenaExitValidation
    {
        private const string Pending = "BumperCars.InkArenaExitValidation.Pending";

        [MenuItem("Tools/Bumper Cars/VFX/Validate Arena Exit Ink %#F5")]
        public static void ValidateFixture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Scene original = SceneManager.GetActiveScene(), preview = default;
            UnityEngine.Random.State random = UnityEngine.Random.state;
            StringBuilder report = new StringBuilder("Arena-exit ink fixture validation\n");
            bool passed = false;
            try
            {
                preview = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(preview);
                Bounds bounds = new Bounds(new Vector3(0f, 1000f, 0f), new Vector3(400f, 4000f, 300f));
                InkFlightPath path = InkFlightPath.FromBounds(new Vector3(150f, 1000f, 0f), Vector3.left, 24f, bounds, 3f);
                Check(path.Distance > 350f && path.Duration > 14f, "Flight endpoint is beyond the opposite arena edge", report);
                SkillLiquidVfx.PlayInk(path.Origin, path.Direction, path.Distance, 7.888f, path.Duration);
                ParticleSystem core = FindCore(preview);
                Check(core != null, "Actual core particle emitter exists", report);
                ParticleSystem.Particle[] particles = new ParticleSystem.Particle[core.main.maxParticles];
                core.Simulate(3f, false, false, false);
                int count = core.GetParticles(particles);
                Check(count == 28, "All 28 main globs remain alive after three seconds", report);
                float distance = 0f;
                for (int i = 0; i < count; i++)
                {
                    distance += Vector3.Dot(particles[i].position - path.Origin, path.Direction);
                    Check(particles[i].GetCurrentColor(core).a > 204, "Glob " + i + " retains its opacity inside the arena", report);
                    Check(particles[i].GetCurrentSize(core) >= particles[i].startSize * 0.5f, "Glob " + i + " retains its size", report);
                }
                distance /= count;
                Check(distance > 70f && distance < 76f, "Mean travel after three seconds is " + distance.ToString("F3") + "m (including particle birth offsets and editor-frame advance)", report);
                Check(InsideXZ(path.ArenaBounds, path.Origin + path.Direction * distance), "The ink is still inside the field at three seconds", report);
                core.Simulate(path.Duration - 3.10f, false, false, false);
                count = core.GetParticles(particles);
                Check(count == 28, "All globs survive until the out-of-field endpoint", report);
                Check(!InsideXZ(path.ArenaBounds, particles[0].position), "Cleanup begins only after the packet is outside arena bounds", report);
                core.Simulate(0.2f, false, false, false);
                Check(core.particleCount == 0, "Particles are cleaned up after leaving the field", report);
                passed = true;
            }
            catch (Exception exception) { report.AppendLine("FAIL: " + exception.Message); }
            finally
            {
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                if (preview.IsValid() && preview.isLoaded) EditorSceneManager.CloseScene(preview, true);
                UnityEngine.Random.state = random;
                Save(report, "Logs/InkArenaExitValidation.txt", passed);
            }
        }

        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.playModeStateChanged -= OnPlayState;
            EditorApplication.playModeStateChanged += OnPlayState;
        }

        [MenuItem("Tools/Bumper Cars/VFX/Capture Arena Exit Ink %#F9")]
        public static void CaptureGameplay()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            SessionState.SetBool(Pending, true);
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Pending, false);
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            new GameObject("Temporary arena-exit ink validation") { hideFlags = HideFlags.HideAndDontSave }
                .AddComponent<InkArenaExitCapture>();
        }

        internal static ParticleSystem FindCore(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>())
                    if (system.name == "Ink / thick liquid jet") return system;
            return null;
        }

        internal static bool InsideXZ(Bounds bounds, Vector3 point)
        {
            return point.x >= bounds.min.x && point.x <= bounds.max.x && point.z >= bounds.min.z && point.z <= bounds.max.z;
        }

        internal static void Check(bool condition, string message, StringBuilder report)
        {
            report.AppendLine((condition ? "PASS: " : "FAIL: ") + message);
            if (!condition) throw new InvalidOperationException(message);
        }

        internal static void Save(StringBuilder report, string path, bool passed)
        {
            report.AppendLine("RESULT: " + (passed ? "PASS" : "FAIL"));
            Directory.CreateDirectory("Logs");
            File.WriteAllText(path, report.ToString(), Encoding.UTF8);
            if (passed) Debug.Log(report.ToString()); else Debug.LogError(report.ToString());
        }
    }

    public sealed class InkArenaExitCapture : MonoBehaviour
    {
        private readonly StringBuilder report = new StringBuilder("Arena-exit ink gameplay validation\n");
        private InkFlightPath path;
        private InkSprayHitbox hitbox;
        private ParticleSystem core;
        private float castTime;
        private double deadline;
        private bool finishing;

        private IEnumerator Start()
        {
            deadline = EditorApplication.timeSinceStartup + 60d;
            EditorApplication.update += Watchdog;
            yield return new WaitForSecondsRealtime(1f);
            if (!Guard(Begin)) yield break;
            yield return new WaitForSecondsRealtime(Mathf.Min(3f, path.Duration * 0.5f));
            if (!Guard(CheckTravel)) yield break;
            while (Time.time - castTime < path.Duration - 0.08f) yield return null;
            if (!Guard(CheckOutside)) yield break;
            yield return new WaitForSecondsRealtime(0.4f);
            if (!Guard(() =>
            {
                InkArenaExitValidation.Check(core == null || core.particleCount == 0, "Main ink particles are removed after the arena exit", report);
                InkArenaExitValidation.Check(hitbox == null, "Moving hitbox is also removed after the arena exit", report);
            })) yield break;
            Finish(true);
        }

        private void Begin()
        {
            RedCarInkRetreatSkill skill = Object.FindObjectOfType<RedCarInkRetreatSkill>();
            if (skill == null) throw new InvalidOperationException("No active octopus skill exists.");
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Vector3 origin = (Vector3)typeof(RedCarInkRetreatSkill).GetMethod("GetInkMouthPosition", flags).Invoke(skill, null);
            Vector3 direction = (Vector3)typeof(RedCarInkRetreatSkill).GetMethod("GetInkDirection", flags).Invoke(skill, null);
            SerializedProperty speedProperty = new SerializedObject(skill).FindProperty("inkFlightSpeed");
            path = InkFlightPath.Create(origin, direction, speedProperty != null ? speedProperty.floatValue : 24f);
            castTime = Time.time;
            typeof(RedCarInkRetreatSkill).GetMethod("Activate", flags).Invoke(skill, null);
            hitbox = Object.FindObjectOfType<InkSprayHitbox>();
            core = InkArenaExitValidation.FindCore(skill.gameObject.scene);
            InkArenaExitValidation.Check(hitbox != null && core != null, "The actual mouth skill creates its moving packet and VFX", report);
            report.AppendLine("Arena bounds: " + path.ArenaBounds + "; flight " + path.Distance.ToString("F2") + "m / " + path.Duration.ToString("F2") + "s");
            InkArenaExitValidation.Check(hitbox.transform.lossyScale.z < 5f, "The moving hitbox is a short packet rather than a field-length trigger", report);
        }

        private void CheckTravel()
        {
            float elapsed = Time.time - castTime;
            InkArenaExitValidation.Check(core.particleCount == 28 && hitbox != null, "The ink remains visible and hittable after " + elapsed.ToString("F2") + " seconds", report);
            Vector3 expected = path.Origin + path.Direction * (path.Speed * elapsed + 1f);
            InkArenaExitValidation.Check(Vector3.Distance(hitbox.transform.position, expected) < 2f, "The hitbox travels together with the ink packet", report);
            ParticleSystem.Particle[] particles = new ParticleSystem.Particle[core.main.maxParticles];
            int count = core.GetParticles(particles);
            float distance = 0f;
            for (int i = 0; i < count; i++) distance += Vector3.Dot(particles[i].position - path.Origin, path.Direction);
            distance /= count;
            InkArenaExitValidation.Check(Mathf.Abs(distance - path.Speed * elapsed) < 2f, "Live ink distance is " + distance.ToString("F2") + "m", report);
            CaptureFrame();
        }

        private void CheckOutside()
        {
            InkArenaExitValidation.Check(core.particleCount > 0 && hitbox != null, "The packet survives until just before its off-field cleanup", report);
            ParticleSystem.Particle[] particles = new ParticleSystem.Particle[core.main.maxParticles];
            core.GetParticles(particles);
            InkArenaExitValidation.Check(!InkArenaExitValidation.InsideXZ(path.ArenaBounds, particles[0].position), "The main ink packet reaches outside the playable field", report);
        }

        private void CaptureFrame()
        {
            GameObject cameraObject = new GameObject("Temporary ink flight view", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();
            RenderTexture target = new RenderTexture(1024, 768, 24);
            Texture2D pixels = new Texture2D(1024, 768, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.enabled = false;
                camera.transform.position = path.Origin + Vector3.Cross(Vector3.up, path.Direction) * 100f + path.Direction * 40f + Vector3.up * 50f;
                camera.transform.rotation = Quaternion.LookRotation(path.Origin + path.Direction * 35f - camera.transform.position, Vector3.up);
                camera.farClipPlane = 2000f;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0f, 0f, 1024f, 768f), 0, 0);
                pixels.Apply();
                File.WriteAllBytes("Logs/InkArenaExitGameplay.png", pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                target.Release();
                Object.Destroy(target); Object.Destroy(pixels); Object.Destroy(cameraObject);
            }
        }

        private bool Guard(Action action)
        {
            try { action(); return true; }
            catch (Exception exception) { report.AppendLine("FAIL: " + exception.Message); Finish(false); return false; }
        }

        private void Watchdog()
        {
            if (!finishing && EditorApplication.timeSinceStartup > deadline)
            {
                report.AppendLine("FAIL: Capture exceeded its 60-second deadline.");
                Finish(false);
            }
        }

        private void Finish(bool passed)
        {
            if (finishing) return;
            finishing = true;
            EditorApplication.update -= Watchdog;
            InkArenaExitValidation.Save(report, "Logs/InkArenaExitGameplayValidation.txt", passed);
            EditorApplication.ExitPlaymode();
        }

        private void OnDestroy() { EditorApplication.update -= Watchdog; }
    }
}
