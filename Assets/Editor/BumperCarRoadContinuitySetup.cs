using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// The art contains open plank seams and separate ramp pieces. These closed,
// collision-only surfaces keep a fast car supported through those gaps.
public static class BumperCarRoadContinuitySetup
{
    private const string RootName = "Road Continuity Support";
    private const string MeshFolder = "Assets/Physics/RoadSupport";
    private const float Spacing = 0.5f;
    private const float Thickness = 1.5f;
    private const float SurfaceInset = 0.1f;
    private static PhysicMaterial drivingMaterial;

    private struct Triangle
    {
        public Vector3 a, ab, ac;
        public float denominator, minX, maxX, minZ, maxZ;
    }

    private static readonly Vector2[] ToadProfile =
    {
        new Vector2(-124f, 52.2f), new Vector2(-122f, 50.1f), new Vector2(-120f, 48.0f),
        new Vector2(-116f, 42.8f), new Vector2(-114f, 41.3f), new Vector2(-110f, 38.6f),
        new Vector2(-106f, 37.4f), new Vector2(-101f, 34.8f), new Vector2(-99f, 33.1f),
        new Vector2(-94f, 31.8f), new Vector2(-92f, 31.6f), new Vector2(-85f, 31.6f),
        new Vector2(-79f, 26.3f), new Vector2(-76f, 26.3f)
    };

    private static readonly Vector2[] OctopusProfile =
    {
        new Vector2(139f, 26.3f), new Vector2(143f, 26.3f), new Vector2(145f, 28.7f), new Vector2(151f, 31.2f),
        new Vector2(154f, 31.7f), new Vector2(159f, 33.85f), new Vector2(161f, 34.6f),
        new Vector2(165f, 36.7f), new Vector2(167f, 37.4f), new Vector2(170f, 40.4f),
        new Vector2(171f, 41.7f), new Vector2(174f, 45.2f),
        new Vector2(178f, 50.3f), new Vector2(179f, 51.3f), new Vector2(183f, 51.3f)
    };

    public static void Configure(Scene scene)
    {
        RequireScene(scene);
        EnsureAssetFolder();
        drivingMaterial = GetDrivingMaterial();
        Transform root = GetRoot(scene);
        GameObject arena = GetChild(root, "Central Arena Support");
        BoxCollider box = arena.GetComponent<BoxCollider>();
        if (box == null) box = Undo.AddComponent<BoxCollider>(arena);
        Undo.RecordObject(box, "Configure continuous arena support");
        box.center = new Vector3(28f, 25.8f, -1f);
        box.size = new Vector3(266f, 1.5f, 268f); // top 26.55; x[-105,161], z[-135,133]
        box.isTrigger = false;
        box.enabled = true;
        box.sharedMaterial = drivingMaterial;

        ConfigurePlatform(root, "Toad Spawn Support", new Vector3(-140f, 51.3f, -30.8f), new Vector3(40f, 2f, 58f));
        ConfigurePlatform(root, "Octopus Spawn Support", new Vector3(191f, 50.34f, -0.6f), new Vector3(24f, 2f, 46f));

        EnsureAssetFolder();
        BuildRoute(scene, root, "Central Driving Surface", "ArenaRoadSupport", -105f, 161f, -135f, 133f,
            "arena", "395", new[] { new Vector2(-105f, 26.65f), new Vector2(161f, 26.65f) }, false, true);
        BuildRoute(scene, root, "Toad Ramp Support", "ToadRampSupport", -124f, -76f, -46f, -10f,
            "slope1Toad", "1545", ToadProfile, true);
        BuildRoute(scene, root, "Octopus Ramp Support", "OctopusRampSupport", 139f, 183f, -15f, 13f,
            "octopslope2", "1485", OctopusProfile, false);
        BuildRoute(scene, root, "Toad South Guard", "ToadSouthGuard", -124f, -80f, -46.5f, -45.5f,
            "slope1Toad", "1545", ToadProfile, true, false, 4f, 4f);
        BuildRoute(scene, root, "Toad North Guard", "ToadNorthGuard", -124f, -80f, -10.5f, -9.5f,
            "slope1Toad", "1545", ToadProfile, true, false, 4f, 4f);
        BuildRoute(scene, root, "Octopus South Guard", "OctopusSouthGuard", 143f, 183f, -15.5f, -14.5f,
            "octopslope2", "1485", OctopusProfile, false, false, 4f, 4f);
        BuildRoute(scene, root, "Octopus North Guard", "OctopusNorthGuard", 143f, 183f, 12.5f, 13.5f,
            "octopslope2", "1485", OctopusProfile, false, false, 4f, 4f);
        ConfigureBoundaries(root);
        Physics.SyncTransforms();
        Validate(scene);
    }

    public static void Validate(Scene scene)
    {
        RequireScene(scene);
        GameObject root = scene.GetRootGameObjects().Single(g => g.name == RootName);
        if (root.GetComponentsInChildren<Renderer>(true).Length != 0 || root.GetComponent<Rigidbody>() != null)
            throw new InvalidOperationException("Road support must remain invisible static collision.");
        BoxCollider arena = root.transform.Find("Central Arena Support").GetComponent<BoxCollider>();
        if (arena == null || !arena.enabled || arena.isTrigger || Mathf.Abs(arena.bounds.max.y - 26.55f) > 0.01f)
            throw new InvalidOperationException("Continuous arena support is missing or at the wrong height.");
        foreach (string name in new[] { "Central Driving Surface", "Toad Ramp Support", "Octopus Ramp Support" })
        {
            MeshCollider surface = root.transform.Find(name).GetComponent<MeshCollider>();
            if (surface == null || !surface.enabled || surface.isTrigger || surface.convex
                || surface.attachedRigidbody != null || surface.sharedMesh == null
                || !AssetDatabase.GetAssetPath(surface.sharedMesh).StartsWith(MeshFolder + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("Missing persistent closed ramp support: " + name);
            Mesh mesh = surface.sharedMesh;
            if (mesh.vertexCount < 100 || mesh.triangles.Length < 300 || mesh.bounds.size.y < Thickness)
                throw new InvalidOperationException("Incomplete ramp support geometry: " + name);
        }
    }

    private static void BuildRoute(Scene scene, Transform root, string objectName, string assetName,
        float minX, float maxX, float minZ, float maxZ, string rampGroup, string platformMesh,
        Vector2[] profile, bool toad, bool arena = false, float verticalOffset = 0f, float thickness = Thickness)
    {
        List<Triangle> art = ReadTopTriangles(scene, rampGroup, platformMesh);
        if (art.Count == 0) throw new InvalidOperationException("No visible route geometry: " + rampGroup);
        float spacing = arena ? 2f : Spacing;
        int columns = Mathf.RoundToInt((maxX - minX) / spacing) + 1;
        int rows = Mathf.RoundToInt((maxZ - minZ) / spacing) + 1;
        int count = columns * rows;
        Vector3[] vertices = new Vector3[count * 2];
        List<int> triangles = new List<int>((columns - 1) * (rows - 1) * 12 + (columns + rows) * 12);
        for (int x = 0; x < columns; x++)
            for (int z = 0; z < rows; z++)
            {
                float worldX = minX + x * spacing, worldZ = minZ + z * spacing;
                float height;
                if (arena)
                {
                    height = SampleTop(art, worldX, worldZ, out height)
                        ? Mathf.Clamp(height - SurfaceInset, 26.65f, 28.6f) : 26.65f;
                }
                // A continuous longitudinal profile replaces plank-end walls
                // and transverse ridges that were launching the cars sideways.
                else height = ProfileHeight(profile, worldX) + verticalOffset;
                int i = x * rows + z;
                vertices[i] = new Vector3(worldX, height, worldZ);
                vertices[i + count] = vertices[i] - Vector3.up * thickness;
            }

        if (arena)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                Vector3[] previous = (Vector3[])vertices.Clone();
                for (int x = 1; x < columns - 1; x++)
                    for (int z = 1; z < rows - 1; z++)
                    {
                        int i = x * rows + z;
                        float height = previous[i].y * 0.6f
                            + (previous[i - rows].y + previous[i + rows].y + previous[i - 1].y + previous[i + 1].y) * 0.1f;
                        vertices[i].y = height;
                        vertices[i + count].y = height - Thickness;
                    }
            }
        }

        for (int x = 0; x < columns - 1; x++)
            for (int z = 0; z < rows - 1; z++)
            {
                int a = x * rows + z, b = (x + 1) * rows + z, c = a + 1, d = b + 1;
                AddQuad(triangles, a, c, d, b); // upward top
                AddQuad(triangles, a + count, b + count, d + count, c + count);
            }
        for (int x = 0; x < columns - 1; x++)
        {
            int a = x * rows, b = (x + 1) * rows;
            AddQuad(triangles, a, b, b + count, a + count);
            a += rows - 1; b += rows - 1;
            AddQuad(triangles, a, a + count, b + count, b);
        }
        for (int z = 0; z < rows - 1; z++)
        {
            int a = z, b = z + 1;
            AddQuad(triangles, a, a + count, b + count, b);
            a += (columns - 1) * rows; b += (columns - 1) * rows;
            AddQuad(triangles, a, b, b + count, a + count);
        }

        string path = MeshFolder + "/" + assetName + ".asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new InvalidOperationException("Road mesh path is occupied by another asset: " + path);
            mesh = new Mesh { name = assetName };
            AssetDatabase.CreateAsset(mesh, path);
        }
        else Undo.RegisterCompleteObjectUndo(mesh, "Update continuous road support mesh");
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        EditorUtility.SetDirty(mesh);
        AssetDatabase.SaveAssetIfDirty(mesh);

        GameObject child = GetChild(root, objectName);
        MeshCollider collider = child.GetComponent<MeshCollider>();
        if (collider == null) collider = Undo.AddComponent<MeshCollider>(child);
        Undo.RecordObject(collider, "Configure continuous closed ramp collision");
        collider.sharedMesh = null;
        collider.convex = false;
        collider.isTrigger = false;
        collider.sharedMesh = mesh;
        collider.enabled = true;
        collider.sharedMaterial = drivingMaterial;
    }

    private static List<Triangle> ReadTopTriangles(Scene scene, string rampGroup, string platformMesh)
    {
        List<Triangle> result = new List<Triangle>();
        foreach (MeshFilter filter in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshFilter>()))
        {
            bool plank = filter.name == platformMesh
                || rampGroup == "arena" && filter.name == "405";
            for (Transform t = filter.transform; t != null; t = t.parent)
                if (t.name == rampGroup && filter.GetComponent<MeshCollider>() != null) plank = true;
            if (!plank || filter.sharedMesh == null) continue;
            Vector3[] vertices = filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
            int[] indices = filter.sharedMesh.triangles;
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
                Vector3 ab = b - a, ac = c - a;
                if (Vector3.Cross(ab, ac).normalized.y < 0.35f) continue;
                result.Add(new Triangle
                {
                    a = a, ab = ab, ac = ac, denominator = ab.x * ac.z - ab.z * ac.x,
                    minX = Mathf.Min(a.x, b.x, c.x), maxX = Mathf.Max(a.x, b.x, c.x),
                    minZ = Mathf.Min(a.z, b.z, c.z), maxZ = Mathf.Max(a.z, b.z, c.z)
                });
            }
        }
        return result;
    }

    private static bool SampleTop(List<Triangle> triangles, float x, float z, out float height)
    {
        height = float.NegativeInfinity;
        foreach (Triangle t in triangles)
        {
            if (x < t.minX || x > t.maxX || z < t.minZ || z > t.maxZ || Mathf.Abs(t.denominator) < 0.0000001f) continue;
            float dx = x - t.a.x, dz = z - t.a.z;
            float u = (dx * t.ac.z - dz * t.ac.x) / t.denominator;
            float v = (t.ab.x * dz - t.ab.z * dx) / t.denominator;
            if (u < 0f || v < 0f || u + v > 1f) continue;
            height = Mathf.Max(height, t.a.y + u * t.ab.y + v * t.ac.y);
        }
        return !float.IsNegativeInfinity(height);
    }

    private static float ProfileHeight(Vector2[] profile, float x)
    {
        for (int i = 1; i < profile.Length; i++)
            if (x <= profile[i].x)
                return Mathf.Lerp(profile[i - 1].y, profile[i].y, Mathf.InverseLerp(profile[i - 1].x, profile[i].x, x));
        return profile[profile.Length - 1].y;
    }

    private static void AddQuad(List<int> triangles, int a, int b, int c, int d)
    {
        triangles.Add(a); triangles.Add(b); triangles.Add(c);
        triangles.Add(a); triangles.Add(c); triangles.Add(d);
    }

    private static void ConfigurePlatform(Transform root, string name, Vector3 center, Vector3 size)
    {
        GameObject child = GetChild(root, name);
        BoxCollider surface = child.GetComponent<BoxCollider>();
        if (surface == null) surface = Undo.AddComponent<BoxCollider>(child);
        Undo.RecordObject(surface, "Configure stable spawn platform contact");
        surface.center = center;
        surface.size = size;
        surface.isTrigger = false;
        surface.enabled = true;
        surface.sharedMaterial = drivingMaterial;
    }

    private static PhysicMaterial GetDrivingMaterial()
    {
        string path = MeshFolder + "/DrivingSurface.physicMaterial";
        PhysicMaterial material = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(path);
        if (material == null)
        {
            material = new PhysicMaterial("Arcade Driving Surface");
            AssetDatabase.CreateAsset(material, path);
        }
        Undo.RecordObject(material, "Use controller-managed road grip");
        material.staticFriction = 0.1f;
        material.dynamicFriction = 0.1f;
        material.frictionCombine = PhysicMaterialCombine.Minimum;
        material.bounciness = 0f;
        material.bounceCombine = PhysicMaterialCombine.Minimum;
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }

    private static void ConfigureBoundaries(Transform root)
    {
        ConfigurePlatform(root, "Arena North Boundary", new Vector3(28f, 28.55f, 133f), new Vector3(266f, 4f, 1f));
        ConfigurePlatform(root, "Arena South Boundary", new Vector3(28f, 28.55f, -135f), new Vector3(266f, 4f, 1f));
        ConfigurePlatform(root, "Arena West Lower Boundary", new Vector3(-105f, 28.55f, -90.5f), new Vector3(1f, 4f, 89f));
        ConfigurePlatform(root, "Arena West Upper Boundary", new Vector3(-105f, 28.55f, 61.5f), new Vector3(1f, 4f, 143f));
        ConfigurePlatform(root, "Arena East Lower Boundary", new Vector3(161f, 28.55f, -75f), new Vector3(1f, 4f, 120f));
        ConfigurePlatform(root, "Arena East Upper Boundary", new Vector3(161f, 28.55f, 73f), new Vector3(1f, 4f, 120f));

        ConfigurePlatform(root, "Toad Platform Back Guard", new Vector3(-159.5f, 54.3f, -30.8f), new Vector3(1f, 4f, 58f));
        ConfigurePlatform(root, "Toad Platform North Guard", new Vector3(-140f, 54.3f, -2.1f), new Vector3(40f, 4f, 1f));
        ConfigurePlatform(root, "Toad Platform South Guard", new Vector3(-140f, 54.3f, -59.5f), new Vector3(40f, 4f, 1f));
        ConfigurePlatform(root, "Toad Exit South Guard", new Vector3(-120f, 54.3f, -52.8f), new Vector3(1f, 4f, 13.4f));
        ConfigurePlatform(root, "Toad Exit North Guard", new Vector3(-120f, 54.3f, -6.05f), new Vector3(1f, 4f, 8.9f));
        ConfigurePlatform(root, "Octopus Platform Back Guard", new Vector3(202.5f, 53.34f, -0.6f), new Vector3(1f, 4f, 46f));
        ConfigurePlatform(root, "Octopus Platform North Guard", new Vector3(191f, 53.34f, 22f), new Vector3(24f, 4f, 1f));
        ConfigurePlatform(root, "Octopus Platform South Guard", new Vector3(191f, 53.34f, -23f), new Vector3(24f, 4f, 1f));
        ConfigurePlatform(root, "Octopus Exit South Guard", new Vector3(179f, 53.34f, -19f), new Vector3(1f, 4f, 8f));
        ConfigurePlatform(root, "Octopus Exit North Guard", new Vector3(179f, 53.34f, 17.5f), new Vector3(1f, 4f, 9f));
    }

    private static Transform GetRoot(Scene scene)
    {
        GameObject root = scene.GetRootGameObjects().SingleOrDefault(g => g.name == RootName);
        if (root == null)
        {
            root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "Create continuous road support");
        }
        if (root.GetComponentsInChildren<Renderer>(true).Length != 0)
            throw new InvalidOperationException("The road support name is occupied by visible scene content.");
        Undo.RecordObject(root.transform, "Keep road support in world coordinates");
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root.transform.localScale = Vector3.one;
        return root.transform;
    }

    private static GameObject GetChild(Transform root, string name)
    {
        Transform child = root.Find(name);
        if (child == null)
        {
            GameObject created = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(created, "Create continuous road collision");
            created.transform.SetParent(root, false);
            child = created.transform;
        }
        Undo.RecordObject(child.gameObject, "Keep road support collision active");
        child.gameObject.SetActive(true);
        Undo.RecordObject(child.gameObject, "Configure road support layer");
        child.gameObject.layer = 0;
        Undo.RecordObject(child, "Keep road support coordinates fixed");
        child.localPosition = Vector3.zero;
        child.localRotation = Quaternion.identity;
        child.localScale = Vector3.one;
        return child.gameObject;
    }

    private static void EnsureAssetFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Physics")) AssetDatabase.CreateFolder("Assets", "Physics");
        if (!AssetDatabase.IsValidFolder(MeshFolder)) AssetDatabase.CreateFolder("Assets/Physics", "RoadSupport");
    }

    private static void RequireScene(Scene scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.IsValid() || !scene.isLoaded
            || scene.path != "Assets/Scenes/SampleScene.scene")
            throw new InvalidOperationException("Open SampleScene in Edit mode to configure continuous road support.");
    }
}
