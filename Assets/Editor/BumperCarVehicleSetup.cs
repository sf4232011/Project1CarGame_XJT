using System;
using System.Linq;
using BumperCars;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit editor migration: no runtime object lookup or automatic scene changes.
public static class BumperCarVehicleSetup
{
    [MenuItem("Tools/Bumper Cars/Bind Octopus and Toad")]
    public static void BindVehicles()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before binding vehicles.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.scene")
            throw new InvalidOperationException("Open SampleScene before binding vehicles.");

        GameObject[] roots = scene.GetRootGameObjects();
        GameObject toad = roots.Single(g => g.name == "Toad");
        GameObject octopus = roots.Single(g => g.name == "Octopus");
        GameObject legacy = roots.Single(g => g.name == "Toad(blue)");
        var manager = roots.Single(g => g.name == "BumperCarGameManager").GetComponent<BumperCarGameManager>();
        var blueCamera = roots.Single(g => g.name == "BlueCamera").GetComponent<Camera>();
        var redCamera = roots.Single(g => g.name == "RedCamera").GetComponent<Camera>();
        var canvas = roots.Single(g => g.name == "Canvas").transform;
        var inkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/InkSprayHitbox.prefab").GetComponent<InkSprayHitbox>();
        var slowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/SlowZone.prefab").GetComponent<SlowZone>();

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Bind Octopus and Toad vehicles");
        // These imported meshes form both spawn platforms and their connecting
        // ramps. Without collision the cars fall through them to the old Plane.
        foreach (GameObject platform in roots.Where(g => g.name == "487" || g.name == "1485"))
        {
            foreach (MeshFilter mesh in platform.GetComponentsInChildren<MeshFilter>())
            {
                if (mesh.GetComponent<Collider>() != null) continue;
                var surface = Undo.AddComponent<MeshCollider>(mesh.gameObject);
                surface.sharedMesh = mesh.sharedMesh;
                surface.sharedMaterial = legacy.GetComponent<BoxCollider>().sharedMaterial;
            }
        }
        Undo.RegisterFullObjectHierarchyUndo(toad, "Bind Toad");
        Undo.RegisterFullObjectHierarchyUndo(octopus, "Bind Octopus");
        Bounds toadBounds = SetupCar(toad, legacy, BumperCarPlayer.Player1,
            new[] { "FrontTire_L", "FrontTire_R", "BackTire_Left", "BackTire_Right" });
        Bounds octopusBounds = SetupCar(octopus, legacy, BumperCarPlayer.Player2,
            new[] { "tire46", "tire48", "tire45", "tire47" });

        var toadController = toad.GetComponent<BumperCarController>();
        var octopusController = octopus.GetComponent<BumperCarController>();
        var trail = CopyOrAdd<ToadSlowTrailSkill>(toad, legacy);
        Bind(trail, "controller", toadController);
        Bind(trail, "slowZonePrefab", slowPrefab);
        Bind(trail, "spawnPoint", Point(toad.transform, "SlowTrailSpawn", new Vector3(toadBounds.center.x, toadBounds.min.y, toadBounds.min.z)));
        float toadWidth = toadBounds.size.x * toad.transform.lossyScale.x;
        SetFloat(trail, "spawnBackwardOffset", 1f);
        SetVector(trail, "zoneScaleMultiplier", new Vector3(toadWidth / 2.8f, 1f, 5f));

        var ink = Ensure<RedCarInkRetreatSkill>(octopus);
        float octopusWidth = octopusBounds.size.x * octopus.transform.lossyScale.x;
        float range = Mathf.Max(6f, octopusBounds.size.z * octopus.transform.lossyScale.z);
        Bind(ink, "controller", octopusController);
        Bind(ink, "inkSprayPrefab", inkPrefab);
        Bind(ink, "inkSpawnPoint", Point(octopus.transform, "InkSpawnPoint",
            new Vector3(octopusBounds.center.x, octopusBounds.center.y, octopusBounds.min.z - range * 0.5f / octopus.transform.lossyScale.z)));
        SetFloat(ink, "inkSprayWidth", octopusWidth * 1.2f);
        SetFloat(ink, "inkSprayRange", range);
        SetFloat(ink, "inkSprayHeight", Mathf.Max(2f, octopusBounds.size.y * octopus.transform.lossyScale.y));
        Bind(ink, "cooldownFill", canvas.Find("RedSkillCooldown/Fill").GetComponent<Image>());
        Bind(ink, "cooldownText", canvas.Find("RedSkillCooldown/Label").GetComponent<TMP_Text>());
        var cooldownRect = (RectTransform)canvas.Find("RedSkillCooldown");
        Undo.RecordObject(cooldownRect, "Place player 2 skill UI");
        cooldownRect.anchorMin = cooldownRect.anchorMax = new Vector2(0.75f, 0f);

        ConfigureCamera(blueCamera, toad, toadBounds, new Rect(0f, 0f, 0.5f, 1f));
        ConfigureCamera(redCamera, octopus, octopusBounds, new Rect(0.5f, 0f, 0.5f, 1f));
        BumperCarCameraMatching.Match(redCamera, blueCamera);
        // Preserve the authored cameras, but only the two gameplay cameras render.
        foreach (Camera camera in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)))
        {
            Undo.RecordObject(camera, "Configure split screen cameras");
            camera.enabled = camera == blueCamera || camera == redCamera;
        }
        foreach (AudioListener listener in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<AudioListener>(true)))
        {
            Undo.RecordObject(listener, "Use one audio listener");
            listener.enabled = listener.gameObject == blueCamera.gameObject;
        }
        Ensure<AudioListener>(blueCamera.gameObject).enabled = true;

        Bind(manager, "player1Controller", toadController);
        Bind(manager, "player1Health", toad.GetComponent<BumperCarHealth>());
        Bind(manager, "player2Controller", octopusController);
        Bind(manager, "player2Health", octopus.GetComponent<BumperCarHealth>());
        Bind(manager, "player1Camera", blueCamera);
        Bind(manager, "player2Camera", redCamera);
        var hud1 = canvas.Find("Player1Helath").GetComponent<BumperCarHudPanel>();
        var hud2 = canvas.Find("Player2Helath").GetComponent<BumperCarHudPanel>();
        Bind(hud1, "health", toad.GetComponent<BumperCarHealth>());
        Bind(hud2, "health", octopus.GetComponent<BumperCarHealth>());
        Bind(manager, "player1Hud", hud1);
        Bind(manager, "player2Hud", hud2);
        Undo.RecordObject(legacy, "Disable legacy vehicle input");
        legacy.SetActive(false);
        ValidateVehicles();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Undo.CollapseUndoOperations(group);
        Debug.Log("Bound Toad (WASD/F) and Octopus (arrows/period); scene saved.");
    }

    private static Bounds SetupCar(GameObject car, GameObject legacy, BumperCarPlayer player, string[] wheelNames)
    {
        Transform[] transforms = car.GetComponentsInChildren<Transform>(true);
        Transform[] wheels = wheelNames.Select(name => transforms.Single(t => t.name == name)).ToArray();
        Bounds bounds = MeshBounds(car.transform, wheels[0].GetComponent<MeshFilter>());
        foreach (Transform wheel in wheels.Skip(1)) bounds.Encapsulate(MeshBounds(car.transform, wheel.GetComponent<MeshFilter>()));
        // Use the tire footprint and real body meshes. Skinned renderer culling
        // bounds are deliberately oversized, so they are not collision geometry.
        foreach (MeshFilter mesh in car.GetComponentsInChildren<MeshFilter>(true).Where(m => !m.name.ToLowerInvariant().Contains("tire")))
        {
            Bounds b = MeshBounds(car.transform, mesh);
            bounds.Encapsulate(new Vector3(b.center.x, bounds.min.y, b.min.z));
            bounds.Encapsulate(new Vector3(b.center.x, bounds.max.y, b.max.z));
        }
        Vector3 size = bounds.size;
        size.y = Mathf.Max(size.y, 3f / car.transform.lossyScale.y);
        bounds = new Bounds(new Vector3(bounds.center.x, bounds.min.y + size.y * 0.5f, bounds.center.z), size);

        var body = CopyOrAdd<Rigidbody>(car, legacy);
        body.isKinematic = false;
        body.useGravity = true;
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var collider = Ensure<BoxCollider>(car);
        collider.center = bounds.center;
        collider.size = bounds.size;
        collider.sharedMaterial = legacy.GetComponent<BoxCollider>().sharedMaterial;
        collider.isTrigger = false;
        var controller = CopyOrAdd<BumperCarController>(car, legacy);
        var controllerData = new SerializedObject(controller);
        controllerData.FindProperty("player").enumValueIndex = (int)player;
        controllerData.FindProperty("useNegativeZAsForward").boolValue = false;
        controllerData.ApplyModifiedProperties();
        // Preserve the former red car's steering tuning from the previous scene.
        if (player == BumperCarPlayer.Player2)
        {
            SetFloat(controller, "turnSpeed", 200f);
            SetFloat(controller, "maxSteerAngle", 35f);
            SetFloat(controller, "steerSensitivity", 1f);
            SetFloat(controller, "minSpeedToTurn", 0.5f);
            SetFloat(controller, "turnSpeedFactor", 1f);
            SetFloat(controller, "steerSmoothTime", 0.08f);
            SetFloat(controller, "lateralGrip", 7f);
        }
        Bind(controller, "groundCheckOrigin", Point(car.transform, "GroundCheck",
            new Vector3(bounds.center.x, bounds.min.y + 0.15f / car.transform.lossyScale.y, bounds.center.z)));
        CopyOrAdd<BumperCarHealth>(car, legacy);
        var audio = CopyOrAdd<AudioSource>(car, legacy);
        audio.playOnAwake = false;
        audio.spatialBlend = 0f; // One listener serves both split screen players.
        audio.maxDistance = 200f;
        var feedback = CopyOrAdd<BumperCarImpactFeedback>(car, legacy);
        Bind(feedback, "audioSource", audio);
        var damage = CopyOrAdd<BumperCarCollisionDamage>(car, legacy);
        Bind(damage, "impactFeedback", feedback);
        SetFloat(damage, "fallbackFrontRearDistance", bounds.size.z * car.transform.lossyScale.z * 0.25f);
        for (int section = 0; section < 3; section++)
        {
            Transform hitbox = Point(car.transform, ((CarHitboxSection)section) + "HitBox", Vector3.zero);
            var box = Ensure<BoxCollider>(hitbox.gameObject);
            box.isTrigger = true;
            float fraction = section == 1 ? 0.6f : 0.2f;
            box.size = new Vector3(bounds.size.x * 1.02f, bounds.size.y * 1.02f, bounds.size.z * fraction);
            box.center = bounds.center + Vector3.forward * bounds.size.z * (section == 0 ? 0.4f : section == 2 ? -0.4f : 0f);
            var hit = Ensure<CarHitbox>(hitbox.gameObject);
            Bind(hit, "owner", damage);
            var data = new SerializedObject(hit);
            data.FindProperty("section").enumValueIndex = section;
            data.ApplyModifiedProperties();
        }
        var visuals = Ensure<BumperCarWheelVisuals>(car);
        Bind(visuals, "controller", controller);
        var wheelData = new SerializedObject(visuals);
        var array = wheelData.FindProperty("wheels");
        array.arraySize = wheels.Length;
        for (int i = 0; i < wheels.Length; i++)
        {
            var element = array.GetArrayElementAtIndex(i);
            var mesh = wheels[i].GetComponent<MeshFilter>();
            element.FindPropertyRelative("mesh").objectReferenceValue = wheels[i];
            element.FindPropertyRelative("steering").boolValue = i < 2;
            element.FindPropertyRelative("radius").floatValue = MeshBounds(car.transform, mesh).extents.y * car.transform.lossyScale.y;
            element.FindPropertyRelative("meshCenter").vector3Value = mesh.sharedMesh.bounds.center;
        }
        wheelData.ApplyModifiedProperties();
        return bounds;
    }

    private static Bounds MeshBounds(Transform root, MeshFilter mesh)
    {
        Bounds b = mesh.sharedMesh.bounds;
        Bounds result = new Bounds(root.InverseTransformPoint(mesh.transform.TransformPoint(b.center)), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            result.Encapsulate(root.InverseTransformPoint(mesh.transform.TransformPoint(corner)));
        }
        return result;
    }

    private static void ConfigureCamera(Camera camera, GameObject car, Bounds bounds, Rect viewport)
    {
        float length = bounds.size.z * car.transform.lossyScale.z;
        float height = bounds.size.y * car.transform.lossyScale.y;
        Vector3 offset = new Vector3(0f, Mathf.Max(9f, length * 0.7f), -Mathf.Max(16f, length * 1.8f));
        var follow = Ensure<BumperCarCameraFollow>(camera.gameObject);
        Bind(follow, "target", car.transform);
        SetVector(follow, "localOffset", offset);
        SetFloat(follow, "lookHeight", Mathf.Max(2f, height * 0.8f));
        Undo.RecordObject(camera, "Set split screen viewport");
        camera.rect = viewport;
        camera.farClipPlane = Mathf.Max(camera.farClipPlane, 1000f);
        Undo.RecordObject(camera.transform, "Place follow camera");
        camera.transform.position = car.transform.position + car.transform.rotation * offset;
        camera.transform.LookAt(car.transform.position + Vector3.up * Mathf.Max(2f, height * 0.8f));
    }

    private static T Ensure<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(target);
    }

    private static T CopyOrAdd<T>(GameObject target, GameObject source) where T : Component
    {
        T existing = target.GetComponent<T>();
        if (existing != null) return existing;
        T added = Ensure<T>(target);
        T original = source.GetComponent<T>();
        if (original != null) EditorUtility.CopySerialized(original, added);
        return added;
    }

    private static Transform Point(Transform parent, string name, Vector3 position)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            child = go.transform;
            child.SetParent(parent, false);
        }
        Undo.RecordObject(child, "Place " + name);
        child.localPosition = position;
        return child;
    }

    private static void Bind(Object target, string property, Object value)
    {
        var data = new SerializedObject(target);
        data.FindProperty(property).objectReferenceValue = value;
        data.ApplyModifiedProperties();
    }

    private static void SetFloat(Object target, string property, float value)
    {
        var data = new SerializedObject(target);
        data.FindProperty(property).floatValue = value;
        data.ApplyModifiedProperties();
    }

    private static void SetVector(Object target, string property, Vector3 value)
    {
        var data = new SerializedObject(target);
        data.FindProperty(property).vector3Value = value;
        data.ApplyModifiedProperties();
    }

    [MenuItem("Tools/Bumper Cars/Validate Octopus and Toad")]
    public static void ValidateVehicles()
    {
        var cars = Object.FindObjectsOfType<BumperCarController>();
        if (cars.Length != 2 || cars.Select(c => c.Player).Distinct().Count() != 2)
            throw new InvalidOperationException("Exactly two active vehicles with distinct players are required.");
        foreach (var car in cars)
        {
            if (car.GetComponent<Rigidbody>() == null || car.GetComponent<BoxCollider>() == null || car.GetComponent<BumperCarHealth>() == null
                || car.GetComponent<BumperCarCollisionDamage>() == null || car.GetComponent<BumperCarWheelVisuals>() == null)
                throw new InvalidOperationException(car.name + " is missing a vehicle component.");
            foreach (var hit in car.GetComponentsInChildren<CarHitbox>())
                if (hit.Owner != car.GetComponent<BumperCarCollisionDamage>()) throw new InvalidOperationException("Wrong hitbox owner: " + hit.name);
            RequireReferences(car, "groundCheckOrigin");
            RequireReferences(car.GetComponent<BumperCarCollisionDamage>(), "impactFeedback");
            RequireReferences(car.GetComponent<BumperCarImpactFeedback>(), "audioSource");
            var wheelData = new SerializedObject(car.GetComponent<BumperCarWheelVisuals>());
            var wheels = wheelData.FindProperty("wheels");
            if (wheels.arraySize != 4) throw new InvalidOperationException(car.name + " needs four wheels.");
            for (int i = 0; i < wheels.arraySize; i++)
                if (wheels.GetArrayElementAtIndex(i).FindPropertyRelative("mesh").objectReferenceValue == null)
                    throw new InvalidOperationException(car.name + " has an unbound wheel.");
            if (car.GetComponentsInChildren<CarHitbox>().Length != 3)
                throw new InvalidOperationException(car.name + " needs front/body/rear hitboxes.");
            foreach (Transform child in car.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) != 0)
                    throw new InvalidOperationException("Missing script on " + child.name);
        }
        RequireReferences(Object.FindObjectOfType<BumperCarGameManager>(), "player1Controller", "player1Health", "player2Controller", "player2Health",
            "player1Camera", "player2Camera", "player1Hud", "player2Hud", "timerText", "resultText");
        RequireReferences(Object.FindObjectOfType<RedCarInkRetreatSkill>(), "controller", "inkSprayPrefab", "inkSpawnPoint", "cooldownFill", "cooldownText");
        RequireReferences(Object.FindObjectOfType<ToadSlowTrailSkill>(), "controller", "slowZonePrefab", "spawnPoint", "chargeFill");
        foreach (var follow in Object.FindObjectsOfType<BumperCarCameraFollow>()) RequireReferences(follow, "target");
        foreach (var hud in Object.FindObjectsOfType<BumperCarHudPanel>()) RequireReferences(hud, "health", "nameText", "healthFill");
        foreach (var overlay in Object.FindObjectsOfType<InkScreenOverlay>()) RequireReferences(overlay, "overlayGroup", "overlayImage");
        if (Object.FindObjectsOfType<Camera>().Count(c => c.enabled) != 2 || Object.FindObjectsOfType<AudioListener>().Count(l => l.enabled) != 1)
            throw new InvalidOperationException("Two enabled cameras and one enabled audio listener are required.");
        Debug.Log("Vehicle bindings validated: two independent players, two cameras, one listener.");
    }

    private static void RequireReferences(Object component, params string[] properties)
    {
        if (component == null) throw new InvalidOperationException("Required component is missing.");
        var data = new SerializedObject(component);
        foreach (string property in properties)
            if (data.FindProperty(property).objectReferenceValue == null)
                throw new InvalidOperationException(component.name + "." + property + " is not bound.");
    }
}
