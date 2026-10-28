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

/// <summary>Actual free-moving downhill routes under the arcade controller.</summary>
public static class BumperCarRoadDrivingValidation
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo FixedUpdate = typeof(BumperCarController).GetMethod("FixedUpdate", Private);
    private static readonly FieldInfo Throttle = typeof(BumperCarController).GetField("throttleInput", Private);
    private static readonly FieldInfo Steering = typeof(BumperCarController).GetField("steerInput", Private);
    private const float Step = 0.02f;

    private sealed class Original
    {
        public BumperCarController car;
        public Vector3 position, velocity, angularVelocity;
        public Quaternion rotation;
        public bool kinematic;
        public Collider[] colliders;
        public bool[] enabled;
    }

    private sealed class Outcome
    {
        public bool passed, fell;
        public float seconds, minimumY = float.PositiveInfinity, maxUnsupported;
    }

    [MenuItem("Tools/Bumper Cars/Validate Driving Routes")]
    public static void ValidateMenu() => Validate();

    public static string Validate()
    {
        if (!EditorApplication.isPlaying || !EditorApplication.isPaused)
            throw new InvalidOperationException("Run driving validation in paused Play mode.");
        StringBuilder report = new StringBuilder("Bumper Cars free-moving road route validation\n");
        StringBuilder csv = new StringBuilder("car,trial,step,time,x,y,z,vx,vy,vz,headingX,headingZ,chassisBottomY,support,supportY,supportClearance,central,unsupportedSeconds\n");
        Stopwatch timer = Stopwatch.StartNew();
        SimulationMode oldMode = Physics.simulationMode;
        List<Original> originals = new List<Original>();
        List<GameObject> clones = new List<GameObject>();
        bool passed = true;
        try
        {
            BumperCarController[] cars = Object.FindObjectsOfType<BumperCarController>()
                .Where(c => c.name == "Toad" || c.name == "Octopus").ToArray();
            if (cars.Length != 2) throw new InvalidOperationException("Expected the real Toad and Octopus cars.");
            foreach (BumperCarController car in cars)
            {
                Collider[] colliders = car.GetComponentsInChildren<Collider>(true);
                Original original = new Original
                {
                    car = car, position = car.Body.position, rotation = car.Body.rotation,
                    velocity = car.Body.velocity, angularVelocity = car.Body.angularVelocity,
                    kinematic = car.Body.isKinematic, colliders = colliders, enabled = colliders.Select(c => c.enabled).ToArray()
                };
                originals.Add(original);
                car.Body.isKinematic = true;
                foreach (Collider collider in colliders) collider.enabled = false;
            }
            Physics.simulationMode = SimulationMode.Script;
            Physics.SyncTransforms();

            foreach (Original original in originals)
                foreach (bool topSpeed in new[] { false, true })
                {
                    BumperCarController car = CreateClone(original, clones);
                    string trial = topSpeed ? "top-speed-downhill" : "accelerating-downhill";
                    report.AppendLine("\n" + original.car.name + " " + trial + ": initial position=" + original.position.ToString("F3")
                        + ", heading=" + car.DriveForwardDirection.ToString("F3") + ", max speed=" + car.MaxDrivingSpeed.ToString("F2"));
                    for (int tick = 0; tick < 100; tick++) Tick(car, 0f, timer);
                    report.AppendLine("  Settled spawn=" + car.Body.position.ToString("F3")
                        + ", bottom=" + car.GetComponent<BoxCollider>().bounds.min.y.ToString("F3"));
                    if (topSpeed) car.Body.velocity = car.DriveForwardDirection * car.MaxDrivingSpeed;
                    Outcome outcome = Drive(car, original, trial, false, timer, report, csv);
                    passed &= outcome.passed;
                    // The same trajectory can return uphill without a teleport.
                    if (outcome.passed && !topSpeed)
                    {
                        Drive(car, original, "reverse-uphill", true, timer, report, csv);
                        report.AppendLine("INFO: Reverse/uphill result is observational and does not determine the forward driving suite result.");
                    }
                    Object.DestroyImmediate(car.gameObject);
                }
            foreach (Original original in originals)
            {
                foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
                    passed &= CheckArenaBoundary(original, clones, yaw, timer, report, csv);
                passed &= CheckSteeringAndImpact(original, clones, timer, report);
            }
        }
        catch (Exception error)
        {
            passed = false;
            report.AppendLine("FAIL: " + error);
        }
        finally
        {
            foreach (GameObject clone in clones) if (clone != null) Object.DestroyImmediate(clone);
            foreach (Original original in originals)
            {
                if (original.car == null) continue;
                for (int i = 0; i < original.colliders.Length; i++)
                    if (original.colliders[i] != null) original.colliders[i].enabled = original.enabled[i];
                original.car.Body.isKinematic = original.kinematic;
                if (!original.kinematic)
                {
                    original.car.Body.velocity = original.velocity;
                    original.car.Body.angularVelocity = original.angularVelocity;
                }
            }
            Physics.simulationMode = oldMode;
            Physics.SyncTransforms();
            report.AppendLine("\nHost elapsed=" + timer.Elapsed.TotalSeconds.ToString("F2") + "s");
            report.AppendLine("RESULT: " + (passed ? "PASS" : "FAIL"));
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/RoadDrivingValidation.txt", report.ToString(), Encoding.UTF8);
            File.WriteAllText("Logs/RoadDrivingTrajectory.csv", csv.ToString(), Encoding.UTF8);
        }
        if (passed) Debug.Log(report.ToString()); else Debug.LogError(report.ToString());
        return report.ToString();
    }

    private static BumperCarController CreateClone(Original original, List<GameObject> clones)
    {
        GameObject clone = Object.Instantiate(original.car.gameObject);
        clones.Add(clone);
        clone.name = "Temporary route " + original.car.name;
        clone.hideFlags = HideFlags.HideAndDontSave;
        foreach (MonoBehaviour script in clone.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
        foreach (Camera camera in clone.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
        foreach (AudioListener listener in clone.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
        foreach (AudioSource source in clone.GetComponentsInChildren<AudioSource>(true)) { source.Stop(); source.enabled = false; }
        Collider[] colliders = clone.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = original.enabled[i];
        BumperCarController car = clone.GetComponent<BumperCarController>();
        car.enabled = true;
        car.SetControlsEnabled(true);
        car.Body.isKinematic = false;
        car.Body.useGravity = true;
        car.Body.interpolation = RigidbodyInterpolation.None;
        car.Body.constraints = RigidbodyConstraints.FreezeRotation;
        clone.transform.SetPositionAndRotation(original.position, original.rotation);
        car.Body.position = original.position;
        car.Body.rotation = original.rotation;
        car.Body.velocity = Vector3.zero;
        car.Body.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
        return car;
    }

    private static BumperCarController CreateArenaClone(Original original, List<GameObject> clones, Vector2 xz,
        float yaw, Stopwatch timer)
    {
        BumperCarController car = CreateClone(original, clones);
        Vector3 position = new Vector3(xz.x, 40f, xz.y);
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        car.transform.SetPositionAndRotation(position, rotation);
        car.Body.position = position;
        car.Body.rotation = rotation;
        Physics.SyncTransforms();
        BoxCollider chassis = car.GetComponent<BoxCollider>();
        RaycastHit road = NearestSupport(car, chassis);
        if (road.collider == null) throw new InvalidOperationException("Arena test has no road beneath " + xz);
        position.y += road.point.y + 0.3f - chassis.bounds.min.y;
        car.transform.position = position;
        car.Body.position = position;
        Physics.SyncTransforms();
        for (int tick = 0; tick < 100; tick++) Tick(car, 0f, timer);
        return car;
    }

    private static bool CheckArenaBoundary(Original original, List<GameObject> clones, float yaw, Stopwatch timer,
        StringBuilder report, StringBuilder csv)
    {
        bool acrossX = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) > 0.5f;
        Vector2 start = new Vector2(25f, acrossX ? 50f : 0f);
        // The authored 405 rim is a legitimate obstacle before the south guard.
        // Start this explicit guard trial beyond it rather than crossing it.
        if (original.car.name == "Octopus" && Mathf.Abs(Mathf.DeltaAngle(yaw, 180f)) < 0.1f) start.y = -123f;
        BumperCarController car = CreateArenaClone(original, clones, start, yaw, timer);
        BumperCarRoadBoundaryProbe probe = car.gameObject.AddComponent<BumperCarRoadBoundaryProbe>();
        BoxCollider chassis = car.GetComponent<BoxCollider>();
        string trial = "arena-boundary-yaw" + yaw.ToString("F0");
        Vector3 direction = car.DriveForwardDirection;
        car.Body.velocity = direction * car.MaxDrivingSpeed;
        bool stayedInside = true, fell = false;
        float minimumY = float.PositiveInfinity, unsupported = 0f;
        for (int tick = 0; tick <= 400; tick++)
        {
            if (tick > 0) Tick(car, 1f, timer);
            Bounds bounds = chassis.bounds;
            minimumY = Mathf.Min(minimumY, bounds.min.y);
            stayedInside &= bounds.min.x >= -106.1f && bounds.max.x <= 162.1f
                && bounds.min.z >= -136.1f && bounds.max.z <= 134.1f;
            fell |= bounds.min.y < 20f;
            RaycastHit road = NearestSupport(car, chassis);
            float clearance = road.collider == null ? float.PositiveInfinity : bounds.min.y - road.point.y;
            unsupported = road.collider != null && clearance >= -0.6f && clearance <= 4f ? 0f : unsupported + Step;
            if (tick % 10 == 0 || fell)
                Csv(csv, original.car.name, trial, tick, car, bounds.min.y, road, clearance, true, unsupported);
            if (fell) break;
        }
        Bounds final = chassis.bounds;
        float boundaryDistance = direction.x > 0.5f ? Mathf.Abs(final.max.x - 161f)
            : direction.x < -0.5f ? Mathf.Abs(final.min.x + 105f)
            : direction.z > 0.5f ? Mathf.Abs(final.max.z - 133f) : Mathf.Abs(final.min.z + 135f);
        bool passed = !fell && stayedInside && probe.boundaryContact && boundaryDistance <= 3f;
        report.AppendLine((passed ? "PASS: " : "FAIL: ") + original.car.name + " " + trial
            + ", actual boundary contact=" + probe.boundaryContact + " (" + probe.lastBoundary + ")"
            + ", stayed inside footprint=" + stayedInside + ", final edge distance=" + boundaryDistance.ToString("F3")
            + "m, min chassisY=" + minimumY.ToString("F3") + ", final position=" + car.Body.position.ToString("F3"));
        Object.DestroyImmediate(car.gameObject);
        return passed;
    }

    private static bool CheckSteeringAndImpact(Original original, List<GameObject> clones, Stopwatch timer, StringBuilder report)
    {
        BumperCarController car = CreateArenaClone(original, clones, new Vector2(25f, 0f), 90f, timer);
        Vector3 heading = car.DriveForwardDirection;
        car.Body.velocity = heading * 12f;
        bool normalYawLocked = true;
        for (int tick = 0; tick < 100; tick++)
        {
            Tick(car, 0.05f, timer, 1f);
            normalYawLocked &= (car.Body.constraints & RigidbodyConstraints.FreezeRotationY) != 0;
        }
        float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(heading, car.DriveForwardDirection), -1f, 1f));
        bool steeringPassed = normalYawLocked && angle > 0.15f;
        report.AppendLine((steeringPassed ? "PASS: " : "FAIL: ") + original.car.name
            + " explicit steering under normal rotation constraints: angle=" + angle.ToString("F4")
            + "rad, native yaw stayed locked=" + normalYawLocked);

        car.ReceiveImpact(car.DriveForwardDirection * 5f, 0.5f);
        bool immediatelyUnlocked = (car.Body.constraints & RigidbodyConstraints.FreezeRotationY) == 0;
        float recovery = (float)typeof(BumperCarController).GetField("impactRecoveryTime", Private).GetValue(car);
        Tick(car, 0f, timer);
        bool firstTickUnlocked = recovery <= Step || (car.Body.constraints & RigidbodyConstraints.FreezeRotationY) == 0;
        for (int tick = 0; tick < Mathf.CeilToInt(recovery / Step) + 3; tick++) Tick(car, 0f, timer);
        bool restored = (car.Body.constraints & RigidbodyConstraints.FreezeRotationY) != 0;
        bool impactPassed = immediatelyUnlocked && firstTickUnlocked && restored;
        report.AppendLine((impactPassed ? "PASS: " : "FAIL: ") + original.car.name
            + " ReceiveImpact unlocks native yaw then restores it: immediate=" + immediatelyUnlocked
            + ", during recovery=" + firstTickUnlocked + ", restored=" + restored
            + ", recovery=" + recovery.ToString("F3") + "s");
        Object.DestroyImmediate(car.gameObject);
        return steeringPassed && impactPassed;
    }

    private static Outcome Drive(BumperCarController car, Original original, string trial, bool reverse,
        Stopwatch timer, StringBuilder report, StringBuilder csv)
    {
        Outcome outcome = new Outcome();
        BoxCollider chassis = car.GetComponent<BoxCollider>();
        int supportedTargetTicks = 0;
        float unsupported = 0f;
        bool entered = false;
        for (int tick = 0; tick <= 750; tick++)
        {
            if (tick > 0) Tick(car, reverse ? -1f : 1f, timer);
            Vector3 position = car.Body.position;
            float bottom = chassis.bounds.min.y;
            outcome.minimumY = Mathf.Min(outcome.minimumY, bottom);
            RaycastHit support = NearestSupport(car, chassis);
            float clearance = support.collider == null ? float.PositiveInfinity : bottom - support.point.y;
            bool supported = support.collider != null && clearance >= -0.6f && clearance <= 4f;
            unsupported = supported ? 0f : unsupported + Step;
            outcome.maxUnsupported = Mathf.Max(outcome.maxUnsupported, unsupported);
            bool central = position.x >= -75f && position.x <= 135f;
            bool deepCentral = original.position.x < -75f ? position.x >= -20f && position.x <= 135f
                : position.x <= 70f && position.x >= -75f;
            bool target = reverse
                ? (original.position.x < -75f ? position.x <= original.position.x + 2f : position.x >= original.position.x - 2f)
                    && position.y >= original.position.y - 1f
                : deepCentral;
            if (!entered && target)
            {
                entered = true;
                report.AppendLine("  " + trial + " reached target bounds at " + (tick * Step).ToString("F2")
                    + "s, position=" + position.ToString("F3") + ", support=" + (support.collider != null ? support.collider.name : "none")
                    + ", clearance=" + clearance.ToString("F3"));
            }
            supportedTargetTicks = target && supported ? supportedTargetTicks + 1 : 0;
            bool fell = bottom < 20f;
            bool arrived = supportedTargetTicks >= 20;
            if (tick % 10 == 0 || fell || arrived || tick == 750)
                Csv(csv, original.car.name, trial, tick, car, bottom, support, clearance, central, unsupported);
            if (fell || arrived || tick == 750)
            {
                outcome.passed = arrived && !fell;
                outcome.fell = fell;
                outcome.seconds = tick * Step;
                report.AppendLine((reverse ? "INFO: " : outcome.passed ? "PASS: " : "FAIL: ") + original.car.name + " " + trial
                    + ", " + (fell ? "fell below the arena" : arrived ? "arrived with road support" : "did not reach supported target in15s")
                    + ", elapsed=" + outcome.seconds.ToString("F2") + "s, position=" + position.ToString("F3")
                    + ", velocity=" + car.Body.velocity.ToString("F3") + ", min chassisY=" + outcome.minimumY.ToString("F3")
                    + ", max continuous >4m support loss=" + outcome.maxUnsupported.ToString("F2") + "s");
                return outcome;
            }
        }
        return outcome;
    }

    private static void Tick(BumperCarController car, float throttle, Stopwatch timer, float steer = 0f)
    {
        if (timer.Elapsed.TotalSeconds > 40d) throw new TimeoutException("Driving validation exceeded its40-second host limit.");
        Throttle.SetValue(car, throttle);
        Steering.SetValue(car, steer);
        FixedUpdate.Invoke(car, null);
        Physics.Simulate(Step);
    }

    private static RaycastHit NearestSupport(BumperCarController car, BoxCollider chassis)
    {
        Vector3 origin = new Vector3(chassis.bounds.center.x, chassis.bounds.max.y + 2f, chassis.bounds.center.z);
        return Physics.RaycastAll(origin, Vector3.down, 150f, ~0, QueryTriggerInteraction.Ignore)
            .Where(h => h.collider.attachedRigidbody == null && h.normal.y > 0.2f).OrderBy(h => h.distance).FirstOrDefault();
    }

    private static void Csv(StringBuilder csv, string carName, string trial, int tick, BumperCarController car,
        float bottom, RaycastHit support, float clearance, bool central, float unsupported)
    {
        Vector3 p = car.Body.position, v = car.Body.velocity, heading = car.DriveForwardDirection;
        string[] cells =
        {
            carName, trial, tick.ToString(), F(tick * Step), F(p.x), F(p.y), F(p.z), F(v.x), F(v.y), F(v.z),
            F(heading.x), F(heading.z), F(bottom), support.collider != null ? support.collider.name.Replace(',', '_') : "none",
            support.collider != null ? F(support.point.y) : "", F(clearance), central ? "1" : "0", F(unsupported)
        };
        csv.AppendLine(string.Join(",", cells));
    }

    private static string F(float value) => value.ToString("F4", CultureInfo.InvariantCulture);
}

public sealed class BumperCarRoadBoundaryProbe : MonoBehaviour
{
    public bool boundaryContact;
    public string lastBoundary = "none";
    private void OnCollisionEnter(Collision collision) => Record(collision);
    private void OnCollisionStay(Collision collision) => Record(collision);
    private void Record(Collision collision)
    {
        string name = collision.collider.name;
        if (name.IndexOf("Boundary", StringComparison.OrdinalIgnoreCase) < 0
            && name.IndexOf("Guard", StringComparison.OrdinalIgnoreCase) < 0) return;
        boundaryContact = true;
        lastBoundary = name;
    }
}
