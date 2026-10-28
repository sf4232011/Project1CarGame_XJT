using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BumperCars.Editor
{
    /// <summary>Renders the actual runtime effects without changing the gameplay scene.</summary>
    public static class SkillVfxValidation
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.scene";
        private const string GameplayCapturePending = "BumperCars.SkillVfxGameplayCapturePending";
        private const int PreviewLayer = 31;
        private static readonly string[] MaterialNames = { "SalivaDrop", "SalivaPuddle", "InkDrop", "InkMist" };

        [MenuItem("Tools/Bumper Cars/VFX/Validate and Render Preview %#F8")]
        public static void ValidateAndRenderPreview()
        {
            Run(false);
        }

        public static void RunBatch()
        {
            Run(true);
        }

        [InitializeOnLoadMethod]
        private static void RegisterGameplayCaptureCallback()
        {
            // Registration alone never starts play or captures anything. The menu sets
            // a SessionState flag so the user's request survives the play-mode reload.
            EditorApplication.playModeStateChanged -= OnCapturePlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnCapturePlayModeStateChanged;
        }

        [MenuItem("Tools/Bumper Cars/VFX/Capture Legacy Preview")]
        public static void CaptureSkillsInCurrentScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Start the gameplay capture in Edit mode. The current scene is restored automatically after capture.");
                return;
            }

            SessionState.SetBool(GameplayCapturePending, true);
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            EditorApplication.EnterPlaymode();
        }

        private static void OnCapturePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(GameplayCapturePending, false);
                return;
            }
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(GameplayCapturePending, false))
                return;

            SessionState.SetBool(GameplayCapturePending, false);
            GameObject capture = new GameObject("Temporary VFX gameplay capture");
            capture.hideFlags = HideFlags.HideAndDontSave;
            capture.AddComponent<SkillVfxGameplayCapture>();
        }

        private static void Run(bool batch)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Run the VFX preview in Edit mode so the live match is left untouched.");
                return;
            }
            Directory.CreateDirectory("Logs");
            StringBuilder report = new StringBuilder();
            report.AppendLine("Bumper Cars liquid VFX validation");
            report.AppendLine("Preview: actual runtime VFX on an isolated warm brown test stage; this is not the gameplay scene.");
            report.AppendLine("Editor: " + Application.unityVersion);
            Scene originalScene = SceneManager.GetActiveScene();
            if (batch && string.IsNullOrEmpty(originalScene.path))
            {
                originalScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            string originalSceneHash = HashFile(ScenePath);
            Scene previewScene = default;
            List<Object> transientAssets = new List<Object>();
            bool succeeded = false;
            try
            {
                Shader shader = Shader.Find("BumperCars/VFX/Stylized Liquid");
                Check(shader != null, "Liquid shader is included and discoverable", report);
                Check(!ShaderUtil.ShaderHasError(shader), "Liquid shader has no imported compiler errors", report);
                foreach (string name in MaterialNames)
                {
                    Material material = Resources.Load<Material>("VFX/" + name);
                    Check(material != null && material.shader == shader, "Resources/VFX/" + name + " references the liquid shader", report);
                }

                ValidatePrefab("Assets/Prefabs/SlowZone.prefab", typeof(SlowZone), report);
                ValidatePrefab("Assets/Prefabs/InkSprayHitbox.prefab", typeof(InkSprayHitbox), report);
                string sceneText = File.ReadAllText(ScenePath);
                Check(Regex.IsMatch(sceneText, @"slowZonePrefab:\s*\{fileID:\s*[1-9][0-9]*,"), "Scene retains its saliva prefab reference", report);
                Check(Regex.IsMatch(sceneText, @"inkSprayPrefab:\s*\{fileID:\s*[1-9][0-9]*,"), "Scene retains its ink prefab reference", report);

                previewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(previewScene);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.52f, 0.48f);
                RenderSettings.fog = false;
                BuildStage(transientAssets);

                // Existing gameplay roots are deliberately left untouched. The VFX manager
                // and all test objects are created in this temporary additive scene.
                for (int i = 0; i < 4; i++)
                {
                    GameObject puddle = new GameObject("Preview saliva puddle " + i);
                    puddle.transform.position = new Vector3(-3.0f + (i % 2) * 0.18f, 0.035f, 1.7f - i * 0.9f);
                    puddle.transform.localScale = new Vector3(2.8f, 0.05f, 1.2f);
                    SalivaPuddleVfx puddleVfx = puddle.AddComponent<SalivaPuddleVfx>();
                    puddleVfx.Initialize(8f);
                    typeof(SalivaPuddleVfx).GetField("age", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(puddleVfx, 0.35f);
                    InvokePrivate(puddleVfx, "UpdateSurface");
                }

                Vector3 salivaLanding = new Vector3(-3f, 0.065f, -1.15f);
                SkillLiquidVfx.PlaySaliva(new Vector3(-3f, 0.8f, 0.3f), salivaLanding, Vector3.back);
                SkillLiquidVfx.Bubble(salivaLanding + new Vector3(0.3f, 0.06f, 0.1f));
                SkillLiquidVfx.Bubble(salivaLanding + new Vector3(-0.45f, 0.07f, 0.1f));
                SkillLiquidVfx.PlayInk(new Vector3(2.7f, 0.8f, 2.2f), Vector3.back, 6f, 2.5f);

                List<ParticleSystem> systems = new List<ParticleSystem>();
                foreach (GameObject root in previewScene.GetRootGameObjects())
                {
                    SetLayerRecursively(root.transform);
                    systems.AddRange(root.GetComponentsInChildren<ParticleSystem>(true));
                }

                Check(systems.Count > 0, "Runtime APIs create real ParticleSystems", report);
                int totalParticles = 0;
                int maxParticleBudget = 0;
                foreach (ParticleSystem system in systems)
                {
                    Check(system.main.simulationSpace == ParticleSystemSimulationSpace.World,
                        system.name + " simulates in world space", report);
                    // Restart would erase manually emitted particles, so advance the current state.
                    system.Simulate(0.18f, false, false, true);
                    totalParticles += system.particleCount;
                    maxParticleBudget += system.main.maxParticles;
                }
                Check(totalParticles > 0, "Manually emitted particles survive preview simulation", report);
                report.AppendLine("Particle systems: " + systems.Count + "; visible particles: " + totalParticles + "; total configured cap: " + maxParticleBudget);

                Camera camera = BuildCamera();
                // Keep one camera, canvas and render target for both sides of the pixel
                // comparison. URP initializes shadow/light state on the first render.
                RenderTexture previewTarget = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                transientAssets.Add(previewTarget);
                camera.targetTexture = previewTarget;
                InkScreenOverlay inkOverlay = BuildOverlay(camera);
                inkOverlay.GetComponent<CanvasGroup>().alpha = 0f;
                Canvas.ForceUpdateCanvases();
                Color32[] baseline = RenderPreview(camera, previewTarget, "Logs/SkillVfxPreview.png", transientAssets, null, 3);
                Color32[] inkFrame = RenderPreview(camera, previewTarget, "Logs/SkillVfxInkOverlay.png", transientAssets, () =>
                {
                    InvokePrivate(inkOverlay, "LayoutSplatter");
                    InvokePrivate(inkOverlay, "AnimateSplatter", 0.35f);
                    inkOverlay.GetComponent<CanvasGroup>().alpha = 1f;
                    Canvas.ForceUpdateCanvases();
                });
                ValidateSplitScreenPixels(baseline, inkFrame, report);
                CanvasGroup inkGroup = inkOverlay.GetComponent<CanvasGroup>();
                Check(!inkGroup.blocksRaycasts && !inkGroup.interactable && !inkOverlay.GetComponent<Image>().raycastTarget,
                    "Ink overlay never captures player input", report);
                Check(inkOverlay.GetComponent<RectMask2D>() != null, "Ink splatter is clipped to its player's screen rect", report);
                InvokePrivate(inkOverlay, "OnDisable");
                Check(Mathf.Approximately(inkGroup.alpha, 0f), "Disabling the overlay clears stale ink", report);
                foreach (ParticleSystem system in systems)
                    system.Simulate(0.14f, false, false, true);
                RenderPreview(camera, previewTarget, "Logs/SkillVfxFlowPreview.png", transientAssets);
                Check(!ShaderUtil.ShaderHasError(shader), "Liquid shader still has no errors after GPU preview render", report);
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    report.AppendLine("Shader " + message.severity + ": " + message.message);
                report.AppendLine("PASS: Logs/SkillVfxPreview.png, Logs/SkillVfxFlowPreview.png and Logs/SkillVfxInkOverlay.png rendered at 1280 x 720.");
                succeeded = true;
            }
            catch (Exception exception)
            {
                report.AppendLine("FAIL: " + exception);
                Debug.LogException(exception);
            }
            finally
            {
                if (originalScene.IsValid() && originalScene.isLoaded)
                    SceneManager.SetActiveScene(originalScene);
                if (previewScene.IsValid() && previewScene.isLoaded)
                    EditorSceneManager.CloseScene(previewScene, true);
                foreach (Object transient in transientAssets)
                {
                    if (transient == null) continue;
                    if (transient is RenderTexture renderTexture) renderTexture.Release();
                    Object.DestroyImmediate(transient);
                }

                bool sceneUnchanged = HashFile(ScenePath) == originalSceneHash;
                report.AppendLine((sceneUnchanged ? "PASS" : "FAIL") + ": Gameplay scene file remains byte-for-byte unchanged.");
                succeeded &= sceneUnchanged;
                report.AppendLine("RESULT: " + (succeeded ? "PASS" : "FAIL"));
                File.WriteAllText("Logs/SkillVfxValidation.txt", report.ToString(), Encoding.UTF8);
            }
            if (succeeded)
                Debug.Log(report.ToString());
            else
                Debug.LogError(report.ToString());
            if (batch && !succeeded)
                EditorApplication.Exit(1);
        }

        private static void ValidatePrefab(string path, Type scriptType, StringBuilder report)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Check(prefab != null && prefab.GetComponent(scriptType) != null, path + " retains its gameplay component", report);
            MeshRenderer placeholder = prefab.GetComponent<MeshRenderer>();
            Check(placeholder == null || !placeholder.enabled, path + " hides its cube placeholder renderer", report);
            Collider collider = prefab.GetComponent<Collider>();
            Check(collider != null && collider.isTrigger && collider.enabled, path + " retains its active gameplay trigger", report);
        }

        private static void BuildStage(List<Object> transientAssets)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) lit = Shader.Find("Standard");
            Material[] plankMaterials = new Material[3];
            for (int i = 0; i < plankMaterials.Length; i++)
            {
                Material material = new Material(lit) { name = "Temporary warm wood " + i };
                Color color = new Color(0.38f + i * 0.035f, 0.23f + i * 0.023f, 0.115f + i * 0.014f);
                material.SetColor("_BaseColor", color);
                material.color = color;
                material.SetFloat("_Smoothness", 0.23f);
                plankMaterials[i] = material;
                transientAssets.Add(material);
            }
            for (int i = 0; i < 20; i++)
            {
                GameObject plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plank.name = "Temporary preview plank";
                plank.transform.position = new Vector3(0f, -0.12f, -4.5f + i * 0.53f);
                plank.transform.localScale = new Vector3(15f, 0.22f, 0.51f);
                plank.GetComponent<Renderer>().sharedMaterial = plankMaterials[i % plankMaterials.Length];
                Object.DestroyImmediate(plank.GetComponent<Collider>());
                plank.layer = PreviewLayer;
            }
            GameObject lightObject = new GameObject("Temporary warm key light", typeof(Light));
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.color = new Color(1f, 0.88f, 0.71f);
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << PreviewLayer;
            lightObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        }

        private static Camera BuildCamera()
        {
            GameObject cameraObject = new GameObject("Temporary VFX preview camera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();
            cameraObject.transform.position = new Vector3(0f, 9.2f, -11.5f);
            cameraObject.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0.4f, 0.5f) - cameraObject.transform.position);
            camera.fieldOfView = 45f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 60f;
            camera.backgroundColor = new Color(0.13f, 0.16f, 0.19f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = 1 << PreviewLayer;
            camera.allowHDR = true;
            camera.enabled = false;
            return camera;
        }

        private static InkScreenOverlay BuildOverlay(Camera camera)
        {
            GameObject canvasObject = new GameObject("Temporary ink preview canvas", typeof(RectTransform), typeof(Canvas));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 0.5f;
            canvas.sortingOrder = 100;
            GameObject overlayObject = new GameObject("Right player ink preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(CanvasGroup), typeof(Image));
            RectTransform rect = (RectTransform)overlayObject.transform;
            rect.SetParent(canvasObject.transform, false);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            overlayObject.GetComponent<Image>().color = new Color(0.045f, 0.018f, 0.085f, 1f);
            InkScreenOverlay overlay = overlayObject.AddComponent<InkScreenOverlay>();
            InvokePrivate(overlay, "Awake");
            SetLayerRecursively(canvasObject.transform);
            return overlay;
        }

        private static Color32[] RenderPreview(Camera camera, RenderTexture target, string path, List<Object> transientAssets, Action beforeRender = null, int warmupFrames = 0)
        {
            Texture2D pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            transientAssets.Add(pixels);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                beforeRender?.Invoke();
                for (int i = 0; i < warmupFrames; i++) camera.Render();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                return pixels.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private static void ValidateSplitScreenPixels(Color32[] baseline, Color32[] inkFrame, StringBuilder report)
        {
            int changedLeft = 0;
            int changedRight = 0;
            for (int y = 0; y < 720; y++)
            {
                for (int x = 0; x < 1280; x++)
                {
                    int index = y * 1280 + x;
                    Color32 a = baseline[index];
                    Color32 b = inkFrame[index];
                    int difference = Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b);
                    if (difference < 8) continue;
                    if (x < 638) changedLeft++;
                    else if (x > 642) changedRight++;
                }
            }
            report.AppendLine("Ink pixel comparison: left changed=" + changedLeft + "; right changed=" + changedRight);
            Check(changedLeft < 460, "Ink leaves the other player's half visually unchanged (<0.1% changed pixels)", report);
            Check(changedRight > 9200, "Ink visibly obscures the affected player's half (>2% changed pixels)", report);
        }

        private static void InvokePrivate(object target, string method, params object[] arguments)
        {
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            if (info == null) throw new MissingMethodException(target.GetType().Name, method);
            info.Invoke(target, arguments);
        }

        private static void SetLayerRecursively(Transform root)
        {
            root.gameObject.layer = PreviewLayer;
            for (int i = 0; i < root.childCount; i++) SetLayerRecursively(root.GetChild(i));
        }

        private static void Check(bool condition, string label, StringBuilder report)
        {
            report.AppendLine((condition ? "PASS: " : "FAIL: ") + label);
            if (!condition) throw new InvalidOperationException(label);
        }

        private static string HashFile(string path)
        {
            using (SHA256 hash = SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
        }
    }

    /// <summary>Transient Editor-only runner; never saved in the scene or included in builds.</summary>
    public sealed class SkillVfxGameplayCapture : MonoBehaviour
    {
        private double deadline;
        private bool exiting;
        private static RedCarInkRetreatSkill capturedOctopus;
        private static Vector3 castMouthPosition;
        private static Vector3 castDirection;
        private static Vector3 castChassisCenter;

        private System.Collections.IEnumerator Start()
        {
            deadline = EditorApplication.timeSinceStartup + 20d;
            EditorApplication.update += CheckCaptureTimeout;
            yield return new WaitForSecondsRealtime(1f);
            if (!TryActivateSkills())
            {
                FinishCapture();
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.18f);
            yield return new WaitForEndOfFrame();
            if (!CaptureFrame("Logs/SkillVfxGameplay.png"))
            {
                FinishCapture();
                yield break;
            }
            CaptureMouthFrame("Logs/OctopusInkMouth.png", true);
            yield return new WaitForSecondsRealtime(0.82f);
            yield return new WaitForEndOfFrame();
            CaptureFrame("Logs/SkillVfxGameplayLate.png");
            CaptureMouthFrame("Logs/OctopusInkMouthLate.png", false);
            yield return null;
            FinishCapture();
        }

        private static bool TryActivateSkills()
        {
            try
            {
                ToadSlowTrailSkill toad = Object.FindObjectOfType<ToadSlowTrailSkill>();
                RedCarInkRetreatSkill octopus = Object.FindObjectOfType<RedCarInkRetreatSkill>();
                if (toad == null || octopus == null)
                    throw new InvalidOperationException("The current scene needs active toad and octopus skill components for gameplay capture.");
                capturedOctopus = octopus;
                castMouthPosition = ReadSkillVector(octopus, "GetInkMouthPosition");
                castDirection = ReadSkillVector(octopus, "GetInkDirection").normalized;
                BoxCollider chassis = octopus.GetComponent<BoxCollider>();
                castChassisCenter = chassis != null ? octopus.transform.TransformPoint(chassis.center) : octopus.transform.position;
                typeof(ToadSlowTrailSkill).GetMethod("SpawnSlowZone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(toad, null);
                typeof(RedCarInkRetreatSkill).GetMethod("Activate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(octopus, null);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }

        private static Vector3 ReadSkillVector(RedCarInkRetreatSkill skill, string method)
        {
            MethodInfo info = typeof(RedCarInkRetreatSkill).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            if (info == null) throw new MissingMethodException(typeof(RedCarInkRetreatSkill).FullName, method);
            return (Vector3)info.Invoke(skill, null);
        }

        private static void CaptureMouthFrame(string path, bool validate)
        {
            Camera camera = null;
            GameObject cameraObject = null;
            RenderTexture target = null;
            Texture2D pixels = null;
            RenderTexture previous = RenderTexture.active;
            const string reportPath = "Logs/OctopusInkMouthGameplayValidation.txt";
            StringBuilder report = new StringBuilder();
            try
            {
                if (capturedOctopus == null)
                    throw new InvalidOperationException("The octopus skill disappeared before its mouth capture.");
                Vector3 mouth = ReadSkillVector(capturedOctopus, "GetInkMouthPosition");
                Vector3 direction = ReadSkillVector(capturedOctopus, "GetInkDirection").normalized;
                Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;

                if (validate)
                {
                    report.AppendLine("Octopus mouth spray gameplay validation");
                    BumperCarController controller = capturedOctopus.GetComponent<BumperCarController>();
                    float directionDot = Vector3.Dot(direction, controller.DriveForwardDirection.normalized);
                    float mouthAhead = Vector3.Dot(castMouthPosition - castChassisCenter, castDirection);
                    report.AppendLine("Cast mouth world position: " + castMouthPosition.ToString("F6"));
                    report.AppendLine("Mouth world position after recoil: " + mouth.ToString("F6"));
                    report.AppendLine("Forward alignment dot: " + directionDot.ToString("F6"));
                    report.AppendLine("Mouth ahead of chassis center: " + mouthAhead.ToString("F6") + "m");
                    RequireMouth(directionDot > 0.999f, "The ink direction matches the octopus front", report);
                    RequireMouth(mouthAhead > 0f, "The emission source is at the front mouth", report);

                    ParticleSystem core = null;
                    foreach (ParticleSystem system in Object.FindObjectsOfType<ParticleSystem>())
                        if (system.name == "Ink / thick liquid jet") { core = system; break; }
                    RequireMouth(core != null, "The real ink jet particle emitter exists", report);
                    ParticleSystem.Particle[] particles = new ParticleSystem.Particle[core.main.maxParticles];
                    int count = core.GetParticles(particles);
                    RequireMouth(count > 0, "The mouth jet has visible live particles", report);
                    float minimumForwardSpeed = float.PositiveInfinity;
                    float averageForwardDistance = 0f;
                    for (int i = 0; i < count; i++)
                    {
                        minimumForwardSpeed = Mathf.Min(minimumForwardSpeed, Vector3.Dot(particles[i].velocity, castDirection));
                        averageForwardDistance += Vector3.Dot(particles[i].position - castMouthPosition, castDirection);
                    }
                    averageForwardDistance /= count;
                    report.AppendLine("Ink particles: " + count + "; minimum forward speed " + minimumForwardSpeed.ToString("F6") + "m/s; average distance ahead " + averageForwardDistance.ToString("F6") + "m");
                    RequireMouth(minimumForwardSpeed > 0f && averageForwardDistance > 0f,
                        "Every sampled ink glob travels forward from the mouth", report);
                }

                cameraObject = new GameObject("Temporary octopus mouth closeup", typeof(Camera));
                cameraObject.hideFlags = HideFlags.HideAndDontSave;
                camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.11f, 0.13f, 0.16f, 1f);
                camera.fieldOfView = 52f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 2000f;
                camera.allowHDR = false;
                camera.transform.position = validate
                    ? castMouthPosition + right * 7f + direction * 8f + Vector3.up * 4f
                    : castMouthPosition + right * 25f + direction * 12f + Vector3.up * 15f;
                camera.transform.rotation = Quaternion.LookRotation(castMouthPosition + direction * (validate ? 1.5f : 5f) - camera.transform.position, Vector3.up);
                target = new RenderTexture(1024, 768, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels = new Texture2D(1024, 768, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0f, 0f, 1024f, 768f), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                report.AppendLine("PASS: Mouth closeup saved to " + path);
                if (validate) report.AppendLine("RESULT: PASS");
                if (validate) File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
                else File.AppendAllText(reportPath, report.ToString(), Encoding.UTF8);
                Debug.Log(report.ToString());
            }
            catch (Exception exception)
            {
                report.AppendLine("FAIL: " + exception.Message);
                report.AppendLine("RESULT: FAIL");
                File.AppendAllText(reportPath, report.ToString(), Encoding.UTF8);
                Debug.LogError(report.ToString());
            }
            finally
            {
                if (camera != null) camera.targetTexture = null;
                RenderTexture.active = previous;
                if (target != null) { target.Release(); Object.Destroy(target); }
                if (pixels != null) Object.Destroy(pixels);
                if (cameraObject != null) Object.Destroy(cameraObject);
            }
        }

        private static void RequireMouth(bool condition, string label, StringBuilder report)
        {
            report.AppendLine((condition ? "PASS: " : "FAIL: ") + label);
            if (!condition) throw new InvalidOperationException(label);
        }

        private static bool CaptureFrame(string path)
        {
            Texture2D screenshot = null;
            try
            {
                Directory.CreateDirectory("Logs");
                screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                if (screenshot == null || screenshot.width < 1)
                    throw new InvalidOperationException("The Game view did not provide a gameplay screenshot.");
                File.WriteAllBytes(path, screenshot.EncodeToPNG());
                Debug.Log("Gameplay VFX frame saved: " + Path.GetFullPath(path));
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
            finally
            {
                if (screenshot != null) Object.Destroy(screenshot);
            }
        }

        private void CheckCaptureTimeout()
        {
            if (!exiting && EditorApplication.timeSinceStartup > deadline)
            {
                Debug.LogWarning("VFX gameplay capture timed out waiting for the Game view; returning to Edit mode.");
                FinishCapture();
            }
        }

        private void FinishCapture()
        {
            if (exiting) return;
            exiting = true;
            EditorApplication.update -= CheckCaptureTimeout;
            EditorApplication.ExitPlaymode();
        }

        private void OnDestroy()
        {
            EditorApplication.update -= CheckCaptureTimeout;
        }
    }
}

