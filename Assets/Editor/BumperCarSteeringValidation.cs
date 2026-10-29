using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BumperCars;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>Physical heading observations on an isolated flat road.</summary>
public static class BumperCarSteeringValidation
{
    private const float Dt = 0.02f;
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo Fixed = typeof(BumperCarController).GetMethod("FixedUpdate", Fields);
    private static readonly FieldInfo Throttle = typeof(BumperCarController).GetField("throttleInput", Fields);
    private static readonly FieldInfo Steer = typeof(BumperCarController).GetField("steerInput", Fields);

    private sealed class Original
    {
        public BumperCarController car;
        public bool kinematic;
        public Vector3 velocity, angularVelocity;
        public Collider[] colliders;
        public bool[] enabled;
    }

    private sealed class Trace
    {
        public string name;
        public int tick;
        public float heading, peakRate, maxRateStep, previousRate, dotSum, speedSum;
        public int samples;
    }

    // Snapshots from Logs/SteeringBackup-20261029/SampleScene.disk-before.scene.
    // This editor-only replay keeps a real physical before/after comparison
    // without introducing a legacy steering mode into the production controller.
    private sealed class Legacy
    {
        public float input, velocity;
        public bool toad;
        public float Degrees(BumperCarController car, float command)
        {
            input = Mathf.SmoothDamp(input, command, ref velocity, toad ? 0.05f : 0.08f, Mathf.Infinity, Dt);
            float speed = Mathf.Max(car.CurrentForwardSpeed, car.CurrentReverseSpeed);
            if (Mathf.Abs(input) < 0.01f || speed < (toad ? 0.4f : 0.5f)) return 0f;
            float factor = Mathf.Clamp01(speed / car.MaxDrivingSpeed);
            float influence = Mathf.Clamp01(factor * (toad ? 1.4f : 1f));
            float amount = Mathf.Clamp(input * (toad ? 1.3f : 1f), -1f, 1f);
            if (car.CurrentReverseSpeed > car.CurrentForwardSpeed) amount = -amount;
            float rate = amount * (toad ? 150f : 200f) * influence * Mathf.Lerp(1f, 0.45f, factor);
            return Mathf.Clamp(rate, toad ? -45f : -35f, toad ? 45f : 35f) * Dt;
        }
    }

    [MenuItem("Tools/Bumper Cars/Validate Steering Response")]
    public static void ValidateMenu() => Validate();

    public static string Validate()
    {
        if (!EditorApplication.isPlaying || !EditorApplication.isPaused)
            throw new InvalidOperationException("Steering validation requires paused Play mode.");
        StringBuilder report = new StringBuilder("Bumper Cars physical steering response validation\n");
        StringBuilder csv = new StringBuilder("car,case,t,input,speed,forwardSpeed,heading,yawRate,commandAngle,actualAngle,reportedYawRate,movementHeadingDot\n");
        Stopwatch timer = Stopwatch.StartNew();
        SimulationMode oldMode = Physics.simulationMode;
        List<Original> originals = new List<Original>();
        List<GameObject> owned = new List<GameObject>();
        PhysicMaterial material = null;
        bool passed = true;
        try
        {
            BumperCarController[] cars = Object.FindObjectsOfType<BumperCarController>()
                .Where(c => c.name == "Toad" || c.name == "Octopus").ToArray();
            if (cars.Length != 2) throw new InvalidOperationException("Both real cars are required.");
            foreach (BumperCarController car in cars)
            {
                Collider[] colliders = car.GetComponentsInChildren<Collider>(true);
                originals.Add(new Original { car = car, kinematic = car.Body.isKinematic, velocity = car.Body.velocity,
                    angularVelocity = car.Body.angularVelocity, colliders = colliders, enabled = colliders.Select(c => c.enabled).ToArray() });
                car.Body.isKinematic = true;
                foreach (Collider collider in colliders) collider.enabled = false;
            }
            material = new PhysicMaterial("Temporary steering road") { staticFriction = 0.1f, dynamicFriction = 0.1f,
                frictionCombine = PhysicMaterialCombine.Minimum, bounciness = 0f, hideFlags = HideFlags.HideAndDontSave };
            GameObject platform = new GameObject("Temporary isolated steering platform") { hideFlags = HideFlags.HideAndDontSave };
            owned.Add(platform);
            platform.transform.position = new Vector3(2000f, 999.5f, 2000f);
            BoxCollider ground = platform.AddComponent<BoxCollider>();
            ground.size = new Vector3(2000f, 1f, 2000f);
            ground.sharedMaterial = material;
            Physics.simulationMode = SimulationMode.Script;
            Physics.SyncTransforms();

            foreach (Original original in originals)
            {
                BumperCarController rest = Clone(original, owned, timer);
                Vector3 restHeading = rest.DriveForwardDirection;
                passed &= Check(Mathf.Abs(rest.MaxSteeringAngle - 90f) < 0.01f, original.car.name + " exposes the full 90-degree command range", report);
                foreach (float input in new[] { 0f, 0.05f, 0.1f, 0.25f, 1f, -1f, 0f })
                {
                    Trace trace = new Trace { name = "rest-command" };
                    for (int i = 0; i < 60; i++) Step(rest, 0f, input, trace, csv, timer);
                    float sensitivity = (float)typeof(BumperCarController).GetField("steerSensitivity", Fields).GetValue(rest);
                    float expected = Mathf.Clamp(input * sensitivity, -1f, 1f) * 90f;
                    passed &= Check(Mathf.Abs(rest.SteeringCommandAngle - expected) < 0.5f && Mathf.Abs(rest.SteeringCommandAngle) <= 90.01f,
                        original.car.name + " stationary input " + input + " gives command " + rest.SteeringCommandAngle.ToString("F3") + "deg", report);
                }
                passed &= Check(Vector3.Angle(restHeading, rest.DriveForwardDirection) < 0.1f,
                    original.car.name + " stationary steering does not rotate the chassis", report);
                Object.DestroyImmediate(rest.gameObject);

                float previous = 0f, fullLowRate = 0f, quarterRate = 0f;
                foreach (float input in new[] { 0.05f, 0.1f, 0.25f, 1f })
                {
                    BumperCarController car = Clone(original, owned, timer);
                    car.Body.velocity = car.DriveForwardDirection * 10f;
                    Trace trace = new Trace { name = "speed10-input" + input };
                    float total = 0f;
                    for (int i = 0; i < 90; i++)
                    {
                        float rate = Step(car, HoldSpeed(car, 10f, false), input, trace, csv, timer);
                        if (i >= 60) total += rate;
                    }
                    float mean = total / 30f;
                    passed &= Check(mean > Mathf.Max(0.5f, previous * 1.1f), original.car.name + " input " + input
                        + " produces progressively stronger physical yaw: " + mean.ToString("F3") + "deg/s", report);
                    report.AppendLine("  Mean planar speed=" + (trace.speedSum / trace.samples).ToString("F3")
                        + "m/s, movement/heading dot=" + (trace.dotSum / trace.samples).ToString("F3"));
                    previous = mean;
                    if (input == 0.25f) quarterRate = mean;
                    if (input == 1f) fullLowRate = trace.peakRate;
                    Object.DestroyImmediate(car.gameObject);
                }

                float lowTime = Ninety(original, owned, false, false, timer, report, csv, out float lowPeak);
                float oldLowTime = Ninety(original, owned, true, false, timer, report, csv, out _);
                float fastTime = Ninety(original, owned, false, true, timer, report, csv, out float fastPeak);
                Ninety(original, owned, true, true, timer, report, csv, out _);
                passed &= Check(lowTime <= 2.5f && fastTime <= 2.5f, original.car.name + " completes a real 90-degree heading change within2.5s at low speed and full drive", report);
                passed &= Check(lowTime < oldLowTime * 0.8f, original.car.name + " low-speed 90-degree turn is at least20% quicker than its old physical replay", report);
                passed &= Check(fastPeak < Mathf.Max(fullLowRate, lowPeak) && !float.IsNaN(fastPeak),
                    original.car.name + " high-speed peak yaw remains finite and below low-speed peak: " + fastPeak.ToString("F2") + "deg/s", report);

                passed &= Transitions(original, owned, timer, report, csv);
                BumperCarController reverse = Clone(original, owned, timer);
                reverse.Body.velocity = -reverse.DriveForwardDirection * 10f;
                Trace reverseTrace = new Trace { name = "reverse-speed10" };
                float reverseRate = 0f;
                for (int i = 0; i < 90; i++)
                {
                    float rate = Step(reverse, -HoldSpeed(reverse, 10f, true), 0.25f, reverseTrace, csv, timer);
                    if (i >= 60) reverseRate += rate / 30f;
                }
                passed &= Check(reverseRate < -0.5f && quarterRate > 0.5f, original.car.name
                    + " reverse steering turns the chassis in the opposite direction: " + reverseRate.ToString("F3") + "deg/s", report);
                Object.DestroyImmediate(reverse.gameObject);
            }
        }
        catch (Exception error) { passed = false; report.AppendLine("FAIL: " + error); }
        finally
        {
            foreach (GameObject obj in owned) if (obj != null) Object.DestroyImmediate(obj);
            if (material != null) Object.DestroyImmediate(material);
            foreach (Original original in originals)
            {
                if (original.car == null) continue;
                for (int i = 0; i < original.colliders.Length; i++) original.colliders[i].enabled = original.enabled[i];
                original.car.Body.isKinematic = original.kinematic;
                if (!original.kinematic) { original.car.Body.velocity = original.velocity; original.car.Body.angularVelocity = original.angularVelocity; }
            }
            Physics.simulationMode = oldMode;
            Physics.SyncTransforms();
            report.AppendLine("Host elapsed=" + timer.Elapsed.TotalSeconds.ToString("F2") + "s");
            report.AppendLine("RESULT: " + (passed ? "PASS" : "FAIL"));
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/BumperCarSteeringValidation.txt", report.ToString(), Encoding.UTF8);
            File.WriteAllText("Logs/BumperCarSteeringTrace.csv", csv.ToString(), Encoding.UTF8);
        }
        if (passed) Debug.Log(report.ToString()); else Debug.LogError(report.ToString());
        return report.ToString();
    }

    private static float Ninety(Original original, List<GameObject> owned, bool old, bool fast, Stopwatch timer,
        StringBuilder report, StringBuilder csv, out float peak)
    {
        BumperCarController car = Clone(original, owned, timer);
        car.Body.velocity = car.DriveForwardDirection * (fast ? car.MaxDrivingSpeed : 10f);
        Trace trace = new Trace { name = (old ? "legacy" : "new") + (fast ? "-full-drive-90" : "-speed10-90") };
        Legacy legacy = old ? new Legacy { toad = original.car.name == "Toad" } : null;
        float elapsed = float.PositiveInfinity;
        for (int i = 0; i < 400; i++)
        {
            Step(car, fast ? 1f : HoldSpeed(car, 10f, false), 1f, trace, csv, timer, legacy);
            if (trace.heading < 90f) continue;
            elapsed = (i + 1) * Dt;
            break;
        }
        peak = trace.peakRate;
        report.AppendLine("  " + original.car.name + " " + trace.name + ": time=" + elapsed.ToString("F3")
            + "s, peak physical yaw=" + peak.ToString("F3") + "deg/s, mean speed=" + (trace.speedSum / trace.samples).ToString("F3")
            + "m/s, movement/heading dot=" + (trace.dotSum / trace.samples).ToString("F3"));
        Object.DestroyImmediate(car.gameObject);
        return elapsed;
    }

    private static bool Transitions(Original original, List<GameObject> owned, Stopwatch timer, StringBuilder report, StringBuilder csv)
    {
        BumperCarController car = Clone(original, owned, timer);
        car.Body.velocity = car.DriveForwardDirection * 10f;
        Trace trace = new Trace { name = "press-release-reverse" };
        for (int i = 0; i < 60; i++) Step(car, HoldSpeed(car, 10f, false), 1f, trace, csv, timer);
        for (int i = 0; i < 25; i++) Step(car, HoldSpeed(car, 10f, false), 0f, trace, csv, timer);
        bool release = Mathf.Abs(car.SteeringYawRate) < 0.5f && Mathf.Abs(trace.previousRate) < 0.5f;
        for (int i = 0; i < 40; i++) Step(car, HoldSpeed(car, 10f, false), 1f, trace, csv, timer);
        float beforeReverse = trace.previousRate;
        float firstReverse = Step(car, HoldSpeed(car, 10f, false), -1f, trace, csv, timer);
        for (int i = 0; i < 49; i++) Step(car, HoldSpeed(car, 10f, false), -1f, trace, csv, timer);
        bool reversed = firstReverse * beforeReverse >= 0f && trace.previousRate < -0.5f;
        bool smooth = trace.maxRateStep <= (720f + 90f) * Dt;
        bool passed = Check(release && reversed && smooth, original.car.name + " release settles within0.5s and reversal stays smooth; max observed yaw-rate step="
            + trace.maxRateStep.ToString("F3") + "deg/s, released=" + release + ", reversed=" + reversed, report);
        car.SetControlsEnabled(false);
        Vector3 heading = car.DriveForwardDirection;
        for (int i = 0; i < 25; i++) Step(car, 0f, 0f, trace, csv, timer);
        passed &= Check(Vector3.Angle(heading, car.DriveForwardDirection) < 0.1f && Mathf.Abs(car.SteeringYawRate) < 0.001f,
            original.car.name + " disabling controls clears residual steering immediately", report);
        Object.DestroyImmediate(car.gameObject);
        return passed;
    }

    private static BumperCarController Clone(Original original, List<GameObject> owned, Stopwatch timer)
    {
        GameObject obj = Object.Instantiate(original.car.gameObject);
        obj.name = "Temporary steering " + original.car.name;
        obj.hideFlags = HideFlags.HideAndDontSave;
        owned.Add(obj);
        foreach (MonoBehaviour script in obj.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
        foreach (Camera camera in obj.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
        foreach (AudioListener listener in obj.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
        foreach (AudioSource source in obj.GetComponentsInChildren<AudioSource>(true)) { source.Stop(); source.enabled = false; }
        Collider[] colliders = obj.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = original.enabled[i];
        BumperCarController car = obj.GetComponent<BumperCarController>();
        car.enabled = true;
        car.SetControlsEnabled(true);
        car.Body.isKinematic = false;
        car.Body.interpolation = RigidbodyInterpolation.None;
        car.transform.SetPositionAndRotation(new Vector3(2000f, 1006f, 2000f), Quaternion.identity);
        car.Body.position = car.transform.position;
        car.Body.rotation = car.transform.rotation;
        Physics.SyncTransforms();
        Vector3 position = car.Body.position + Vector3.up * (1000.2f - car.GetComponent<BoxCollider>().bounds.min.y);
        car.transform.position = position;
        car.Body.position = position;
        car.Body.velocity = Vector3.zero;
        car.Body.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
        for (int i = 0; i < 60; i++)
        {
            if (timer.Elapsed.TotalSeconds > 40d) throw new TimeoutException("Steering validation exceeded40s.");
            Throttle.SetValue(car, 0f); Steer.SetValue(car, 0f);
            Fixed.Invoke(car, null); Physics.Simulate(Dt);
        }
        return car;
    }

    private static float Step(BumperCarController car, float throttle, float input, Trace trace, StringBuilder csv, Stopwatch timer, Legacy legacy = null)
    {
        if (timer.Elapsed.TotalSeconds > 40d) throw new TimeoutException("Steering validation exceeded40s.");
        Vector3 heading = car.DriveForwardDirection;
        Throttle.SetValue(car, car.ControlsEnabled ? throttle : 0f);
        Steer.SetValue(car, car.ControlsEnabled && legacy == null ? input : 0f);
        Fixed.Invoke(car, null);
        if (legacy != null) car.Body.MoveRotation(Quaternion.AngleAxis(legacy.Degrees(car, input), Vector3.up) * car.Body.rotation);
        Physics.Simulate(Dt);
        // atan2 preserves sub-hundredth-degree motion. SignedAngle's acos can
        // quantize a gentle release to alternating zero/one deg/s samples.
        Vector3 nextHeading = car.DriveForwardDirection;
        float rate = Mathf.Atan2(Vector3.Dot(Vector3.up, Vector3.Cross(heading, nextHeading)),
            Vector3.Dot(heading, nextHeading)) * Mathf.Rad2Deg / Dt;
        trace.heading += rate * Dt;
        trace.peakRate = Mathf.Max(trace.peakRate, Mathf.Abs(rate));
        trace.maxRateStep = Mathf.Max(trace.maxRateStep, Mathf.Abs(rate - trace.previousRate));
        trace.previousRate = rate;
        Vector3 planar = Vector3.ProjectOnPlane(car.Body.velocity, Vector3.up);
        float dot = planar.sqrMagnitude > 0.01f ? Vector3.Dot(planar.normalized, car.DriveForwardDirection) : 1f;
        trace.dotSum += dot;
        trace.speedSum += planar.magnitude;
        trace.samples++;
        csv.AppendLine(string.Join(",", car.name.Replace("Temporary steering ", ""), trace.name, F(trace.tick++ * Dt), F(input), F(planar.magnitude),
            F(car.CurrentForwardSpeed), F(trace.heading), F(rate), F(car.SteeringCommandAngle), F(car.SteeringAngle), F(car.SteeringYawRate), F(dot)));
        return rate;
    }

    private static float HoldSpeed(BumperCarController car, float target, bool reverse)
    {
        float current = reverse ? car.CurrentReverseSpeed : car.CurrentForwardSpeed;
        return Mathf.Clamp(0.1f + (target - current) * 0.07f, 0f, 1f);
    }

    private static bool Check(bool condition, string label, StringBuilder report)
    {
        report.AppendLine((condition ? "PASS: " : "FAIL: ") + label);
        return condition;
    }

    private static string F(float value) => value.ToString("F4", CultureInfo.InvariantCulture);
}
