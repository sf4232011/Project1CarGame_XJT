using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BumperCars;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks the authored battle footprint with both cars' actual chassis dimensions.</summary>
public static class BumperCarBattleAreaValidation
{
    private const string ScenePath = "Assets/Scenes/SampleScene.scene";
    private const float Step = 0.02f;
    private const float Clearance = 0.15f;
    private static int checks;

    private sealed class Probe
    {
        public string name;
        public GameObject gameObject;
        public Rigidbody body;
        public BoxCollider chassis;
    }

    [MenuItem("Tools/Bumper Cars/Validate Battle Area")]
    public static void ValidateMenu()
    {
        if (SceneManager.GetActiveScene().path != ScenePath)
            throw new InvalidOperationException("Open SampleScene before validating the battle area.");
        RunBatch();
    }

    public static void RunBatch()
    {
        StringBuilder report = new StringBuilder("Bumper Cars battle-area validation\n");
        Scene temporary = default;
        List<Probe> probes = new List<Probe>();
        Exception failure = null;
        checks = 0;
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before configuring the battle area.");
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath);
            BumperCarRoadContinuitySetup.ConfigureBattleArea(scene);
            GameObject root = scene.GetRootGameObjects().Single(g => g.name == "Road Continuity Support");
            BumperCarBattleArea area = root.GetComponent<BumperCarBattleArea>();
            BumperCarController[] cars = scene.GetRootGameObjects()
                .SelectMany(g => g.GetComponentsInChildren<BumperCarController>(true))
                .Where(c => c.gameObject.activeInHierarchy && (c.name == "Toad" || c.name == "Octopus"))
                .OrderBy(c => c.name).ToArray();
            Check(cars.Length == 2, "Both authored cars are present", report);
            foreach (BumperCarController car in cars)
            {
                SerializedProperty binding = new SerializedObject(car).FindProperty("battleArea");
                if (binding == null) throw new InvalidOperationException("The controller has no serialized battleArea field.");
                Check(binding.objectReferenceValue == area, car.name + " is explicitly bound to the battle area", report);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Check(EditorSceneManager.SaveScene(scene), "Battle area and vehicle bindings are saved in SampleScene", report);

            // A local physics scene contains only test bodies. Simulating it cannot
            // move either authored car or the rest of the editor's open scene.
            temporary = EditorSceneManager.NewPreviewScene();
            PhysicsScene physics = temporary.GetPhysicsScene();
            Check(physics.IsValid() && !physics.Equals(Physics.defaultPhysicsScene),
                "Tests use an isolated preview physics scene", report);
            CheckGeometry(area, report);

            foreach (BumperCarController car in cars)
            {
                Probe probe = CreateProbe(car, temporary);
                probes.Add(probe);
                CheckFootprint(area, probe, report);
                CheckRecovery(area, probe, report);

                foreach (float yaw in new[] { 0f, 45f, 90f, 180f })
                {
                    string suffix = " yaw=" + yaw.ToString("F0");
                    CheckMotion(area, physics, probe, "north airborne/high-speed" + suffix,
                        new Vector2(28f, 108f), new Vector3(8f, 0f, 120f), yaw, report);
                    CheckMotion(area, physics, probe, "south airborne/reverse" + suffix,
                        new Vector2(28f, -110f), new Vector3(-8f, 0f, -120f), yaw, report);
                    CheckMotion(area, physics, probe, "east airborne/diagonal" + suffix,
                        new Vector2(138f, 55f), new Vector3(120f, 0f, 8f), yaw, report);
                    CheckMotion(area, physics, probe, "west airborne/diagonal" + suffix,
                        new Vector2(-82f, 55f), new Vector3(-120f, 0f, -8f), yaw, report);
                }

                CheckMotion(area, physics, probe, "Toad platform back edge", new Vector2(-140f, -28f),
                    new Vector3(-90f, 0f, 0f), 90f, report);
                CheckMotion(area, physics, probe, "Octopus platform back edge", new Vector2(191f, -1f),
                    new Vector3(90f, 0f, 0f), 90f, report);
                CheckMotion(area, physics, probe, "Toad ramp north side", new Vector2(-112f, -28f),
                    new Vector3(0f, 0f, 90f), 90f, report);
                CheckMotion(area, physics, probe, "Toad ramp south side", new Vector2(-112f, -28f),
                    new Vector3(0f, 0f, -90f), 90f, report);
                CheckMotion(area, physics, probe, "Octopus ramp north side", new Vector2(172f, -1f),
                    new Vector3(0f, 0f, 90f), 90f, report);
                CheckMotion(area, physics, probe, "Octopus ramp south side", new Vector2(172f, -1f),
                    new Vector3(0f, 0f, -90f), 90f, report);
                CheckPassage(area, physics, probe, "Toad platform/ramp/arena passage",
                    new Vector2(-140f, -28f), 40f, report);
                CheckPassage(area, physics, probe, "Octopus platform/ramp/arena passage",
                    new Vector2(191f, -1f), -40f, report);
                CheckControllerHooks(area, probe, report);
                Object.DestroyImmediate(probe.gameObject);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            report.AppendLine("FAIL: " + exception);
        }
        finally
        {
            foreach (Probe probe in probes)
                if (probe.gameObject != null) Object.DestroyImmediate(probe.gameObject);
            if (temporary.IsValid() && temporary.isLoaded) EditorSceneManager.ClosePreviewScene(temporary);
            report.AppendLine("Checks: " + checks);
            report.AppendLine("RESULT: " + (failure == null ? "PASS" : "FAIL"));
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/BattleAreaValidation.txt", report.ToString(), Encoding.UTF8);
        }
        if (failure != null)
        {
            Debug.LogError(report.ToString());
            throw new InvalidOperationException("Battle-area validation failed; see Logs/BattleAreaValidation.txt.", failure);
        }
        Debug.Log(report.ToString());
    }

    private static void CheckGeometry(BumperCarBattleArea area, StringBuilder report)
    {
        Check(area.ContainsFootprint(new Bounds(new Vector3(28f, 100f, -1f), new Vector3(240f, 2f, 240f))),
            "The central fighting surface is available even above the physical walls", report);
        Check(area.ContainsFootprint(new Bounds(new Vector3(-105f, 100f, -28f), new Vector3(28f, 2f, 20f))),
            "A whole chassis can straddle the west arena/ramp seam", report);
        Check(area.ContainsFootprint(new Bounds(new Vector3(-121f, 100f, -28f), new Vector3(16f, 2f, 20f))),
            "A whole chassis can straddle the Toad ramp/platform seam", report);
        Check(area.ContainsFootprint(new Bounds(new Vector3(161f, 100f, -1f), new Vector3(20f, 2f, 18f))),
            "A whole chassis can straddle the east arena/ramp seam", report);
        Check(area.ContainsFootprint(new Bounds(new Vector3(180f, 100f, -1f), new Vector3(18f, 2f, 18f))),
            "A whole chassis can straddle the Octopus ramp/platform seam", report);
        Check(!area.ContainsFootprint(new Bounds(new Vector3(-107f, 100f, -8f), new Vector3(8f, 2f, 8f))),
            "The concave gap beside the west ramp is outside the battle area", report);
        Check(!area.ContainsFootprint(new Bounds(new Vector3(171f, 100f, 35f), Vector3.one)),
            "Empty ground beside the east ramp is outside the battle area", report);
        Check(!area.ContainsFootprint(new Bounds(new Vector3(28f, 100f, 132.8f), Vector3.one)),
            "A vehicle whose center is inside but whose body crosses the north edge is rejected", report);
        Check(!area.ContainsFootprint(new Bounds(new Vector3(28f, 100f, 132.45f), Vector3.one)),
            "The configured body clearance is enforced at the north edge", report);
    }

    private static void CheckControllerHooks(BumperCarBattleArea area, Probe probe, StringBuilder report)
    {
        BumperCarController controller = probe.gameObject.AddComponent<BumperCarController>();
        var data = new SerializedObject(controller);
        data.FindProperty("battleArea").objectReferenceValue = area;
        data.ApplyModifiedPropertiesWithoutUndo();
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(BumperCarController).GetMethod("Awake", flags).Invoke(controller, null);
        foreach (string hook in new[] { "FixedUpdate", "LateUpdate" })
        {
            Place(area, probe, new Vector2(28f, 160f), 45f);
            probe.body.velocity = new Vector3(0f, 0f, 120f);
            typeof(BumperCarController).GetMethod(hook, flags).Invoke(controller, null);
            Check(area.ContainsFootprint(area.GetFootprint(probe.body, probe.chassis)),
                probe.name + " actual controller " + hook + " enforces its serialized area binding", report);
        }
    }

    private static Probe CreateProbe(BumperCarController original, Scene scene)
    {
        BoxCollider source = original.GetComponent<BoxCollider>();
        if (source == null) throw new InvalidOperationException(original.name + " is missing its chassis BoxCollider.");
        GameObject gameObject = new GameObject("Temporary battle probe " + original.name)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        SceneManager.MoveGameObjectToScene(gameObject, scene);
        gameObject.transform.localScale = original.transform.lossyScale;
        BoxCollider chassis = gameObject.AddComponent<BoxCollider>();
        chassis.center = source.center;
        chassis.size = source.size;
        chassis.contactOffset = source.contactOffset;
        Rigidbody body = gameObject.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.drag = 0f;
        body.angularDrag = 0f;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        return new Probe { name = original.name, gameObject = gameObject, body = body, chassis = chassis };
    }

    private static void Place(BumperCarBattleArea area, Probe probe, Vector2 center, float yaw)
    {
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        probe.gameObject.transform.SetPositionAndRotation(new Vector3(0f, 100f, 0f), rotation);
        probe.body.position = probe.gameObject.transform.position;
        probe.body.rotation = rotation;
        probe.body.velocity = Vector3.zero;
        probe.body.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
        Bounds footprint = area.GetFootprint(probe.body, probe.chassis);
        Vector3 position = probe.body.position + new Vector3(center.x - footprint.center.x, 0f, center.y - footprint.center.z);
        probe.gameObject.transform.position = position;
        probe.body.position = position;
        Physics.SyncTransforms();
    }

    private static void CheckFootprint(BumperCarBattleArea area, Probe probe, StringBuilder report)
    {
        foreach (Quaternion rotation in new[] { Quaternion.identity, Quaternion.Euler(0f, 45f, 0f), Quaternion.Euler(12f, 93f, -8f) })
        {
            probe.gameObject.transform.SetPositionAndRotation(new Vector3(28f, 100f, -1f), rotation);
            probe.body.position = probe.gameObject.transform.position;
            probe.body.rotation = rotation;
            Physics.SyncTransforms();
            Bounds expected = probe.chassis.bounds;
            Bounds actual = area.GetFootprint(probe.body, probe.chassis);
            Check(Vector3.Distance(expected.center, actual.center) < 0.002f && Vector3.Distance(expected.size, actual.size) < 0.002f,
                probe.name + " footprint matches the actual offset/scaled chassis at rotation " + rotation.eulerAngles, report);
        }
        Bounds bodyBounds = area.GetFootprint(probe.body, probe.chassis);
        report.AppendLine("INFO: " + probe.name + " rotated chassis size=" + bodyBounds.size.ToString("F3"));
    }

    private static void CheckRecovery(BumperCarBattleArea area, Probe probe, StringBuilder report)
    {
        foreach (bool inward in new[] { false, true })
        {
            Place(area, probe, new Vector2(28f, 155f), 45f);
            float height = probe.body.position.y;
            Vector3 velocity = new Vector3(7f, 3f, inward ? -20f : 20f);
            probe.body.velocity = velocity;
            area.Constrain(probe.body, probe.chassis);
            Bounds corrected = area.GetFootprint(probe.body, probe.chassis);
            Check(area.ContainsFootprint(corrected), probe.name + " outside placement is returned fully inside", report);
            Check(Mathf.Abs(probe.body.position.y - height) < 0.0001f,
                probe.name + " outside recovery preserves height", report);
            Vector3 expected = inward ? velocity : new Vector3(7f, 3f, 0f);
            Check(Vector3.Distance(probe.body.velocity, expected) < 0.002f,
                probe.name + (inward ? " recovery preserves inward and tangential velocity" : " recovery removes only outward velocity"), report);
            Check(Mathf.Abs(corrected.max.z + Clearance - 133f) < 0.002f,
                probe.name + " returns to the nearest north boundary with body clearance", report);
        }
        Place(area, probe, new Vector2(-500f, 500f), 45f);
        float farHeight = probe.body.position.y;
        area.Constrain(probe.body, probe.chassis);
        Check(area.ContainsFootprint(area.GetFootprint(probe.body, probe.chassis))
            && Mathf.Abs(probe.body.position.y - farHeight) < 0.0001f,
            probe.name + " remote teleport recovers inside without changing height", report);
    }

    private static void CheckMotion(BumperCarBattleArea area, PhysicsScene physics, Probe probe,
        string trial, Vector2 start, Vector3 drive, float yaw, StringBuilder report)
    {
        Place(area, probe, start, yaw);
        Check(area.ContainsFootprint(area.GetFootprint(probe.body, probe.chassis)),
            probe.name + " " + trial + " begins fully inside", report);
        Vector3 initial = probe.body.position;
        bool corrected = false;
        for (int tick = 0; tick < 100; tick++)
        {
            // Repeatedly commanding the outside direction represents held throttle,
            // recoil, or knockback. The post-simulation call is the render-frame guard.
            probe.body.velocity = drive;
            corrected |= area.Constrain(probe.body, probe.chassis);
            physics.Simulate(Step);
            corrected |= area.Constrain(probe.body, probe.chassis);
            if (!area.ContainsFootprint(area.GetFootprint(probe.body, probe.chassis)))
                throw new InvalidOperationException(probe.name + " left the battle area during " + trial + ", tick " + tick);
        }
        Check(corrected && Mathf.Abs(probe.body.position.y - initial.y) < 0.002f,
            probe.name + " " + trial + " stays inside for 100 physics steps above wall height", report);
        if (Mathf.Abs(drive.x) > 0f && Mathf.Abs(drive.z) > 0f)
        {
            float tangentTravel = Mathf.Abs(drive.x) > Mathf.Abs(drive.z)
                ? Mathf.Abs(probe.body.position.z - initial.z) : Mathf.Abs(probe.body.position.x - initial.x);
            Check(tangentTravel > 5f, probe.name + " " + trial + " still slides along the edge", report);
        }
    }

    private static void CheckPassage(BumperCarBattleArea area, PhysicsScene physics, Probe probe,
        string trial, Vector2 start, float speed, StringBuilder report)
    {
        Place(area, probe, start, 90f);
        Vector3 initial = probe.body.position;
        bool changed = false;
        for (int tick = 0; tick < 200; tick++)
        {
            probe.body.velocity = new Vector3(speed, 0f, 0f);
            changed |= area.Constrain(probe.body, probe.chassis);
            physics.Simulate(Step);
            changed |= area.Constrain(probe.body, probe.chassis);
            if (!area.ContainsFootprint(area.GetFootprint(probe.body, probe.chassis)))
                throw new InvalidOperationException(probe.name + " left the playable union in " + trial);
        }
        Vector3 expected = initial + new Vector3(speed * Step * 200f, 0f, 0f);
        Check(!changed && Vector3.Distance(probe.body.position, expected) < 0.02f,
            probe.name + " " + trial + " crosses both seams without clipping or losing speed", report);
    }

    private static void Check(bool condition, string message, StringBuilder report)
    {
        checks++;
        report.AppendLine((condition ? "PASS: " : "FAIL: ") + message);
        if (!condition) throw new InvalidOperationException(message);
    }
}
