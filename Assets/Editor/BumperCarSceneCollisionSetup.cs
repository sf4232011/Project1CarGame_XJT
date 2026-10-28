using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Keep collision on the authored road meshes instead of the old elevated proxies.
public static class BumperCarSceneCollisionSetup
{
    private static readonly HashSet<string> RoadGroups = new HashSet<string>
    {
        "487", "1485", "platform1", "platform2", "slope1Toad", "octopslope2"
    };

    [MenuItem("Tools/Bumper Cars/Fix Road Collisions")]
    public static void FixRoadCollisions()
    {
        Scene scene = SceneManager.GetActiveScene();
        RequireEditScene(scene);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Match road collisions to visible meshes");
        string report = Configure(scene);
        Validate(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Could not save the corrected road collisions.");
        Undo.CollapseUndoOperations(group);
        Debug.Log(report + "\nRoad collision checks passed; scene saved.");
    }

    /// <summary>Idempotent scene repair; the caller decides when to save.</summary>
    public static string Configure(Scene scene)
    {
        RequireEditScene(scene);
        // Check the identities before changing any collider. These are two known
        // legacy proxies, not every empty object or every box in the scene.
        BoxCollider arenaProxy = FindKnownProxy(scene, "GameObject", new Vector3(17.8f, 23.3f, -0.05f));
        BoxCollider rampProxy = FindKnownProxy(scene, "GameObject (1)", new Vector3(-107.4f, 39.4f, -29.2f));
        MeshFilter arena = FindArenaMesh(scene);
        MeshFilter[] roadMeshes = GetRoadMeshes(scene);
        foreach (MeshFilter mesh in roadMeshes)
            if (mesh.GetComponent<MeshCollider>() != null && mesh.GetComponentInParent<Rigidbody>() != null)
                throw new InvalidOperationException("A road mesh unexpectedly has a Rigidbody: " + mesh.name);

        string before = SupportingLevels(scene);
        StringBuilder report = new StringBuilder("Road collision repair\n");
        DisableProxy(arenaProxy, report);
        DisableProxy(rampProxy, report);

        BoxCollider oldArenaBox = arena.GetComponent<BoxCollider>();
        if (oldArenaBox != null && oldArenaBox.enabled)
        {
            Undo.RecordObject(oldArenaBox, "Disable the oversized arena box");
            oldArenaBox.enabled = false;
            report.AppendLine("Disabled 487/395 bounding box; the visible board mesh supplies contact.");
        }
        MeshCollider arenaSurface = arena.GetComponent<MeshCollider>();
        if (arenaSurface == null)
        {
            arenaSurface = Undo.AddComponent<MeshCollider>(arena.gameObject);
            if (oldArenaBox != null) arenaSurface.sharedMaterial = oldArenaBox.sharedMaterial;
            report.AppendLine("Added the exact 487/395 mesh collision surface.");
        }
        ConfigureMesh(arena, arenaSurface);

        // Existing platforms and ramps already have mesh collision. Preserve
        // their coverage and repair their bindings without adding collision to
        // decorative meshes that never had a collider.
        foreach (MeshFilter mesh in roadMeshes)
        {
            MeshCollider surface = mesh.GetComponent<MeshCollider>();
            if (surface == null) continue;
            if (UsesContinuousDrivingSurface(mesh))
            {
                Undo.RecordObject(surface, "Replace jagged plank contact with continuous driving surface");
                surface.enabled = false;
            }
            else ConfigureMesh(mesh, surface);
        }
        BumperCarRoadContinuitySetup.Configure(scene);
        Physics.SyncTransforms();
        report.AppendLine("Supporting levels before: " + before);
        report.AppendLine("Supporting levels after:  " + SupportingLevels(scene));
        return report.ToString();
    }

    public static void Validate(Scene scene)
    {
        RequireEditScene(scene);
        foreach (string name in new[] { "GameObject", "GameObject (1)" })
        {
            GameObject root = scene.GetRootGameObjects().SingleOrDefault(g => g.name == name && g.GetComponent<BoxCollider>() != null);
            if (root != null && root.GetComponents<BoxCollider>().Any(c => c.enabled && !c.isTrigger))
                throw new InvalidOperationException("The elevated legacy road proxy is still enabled: " + name);
        }
        MeshFilter arena = FindArenaMesh(scene);
        if (arena.GetComponents<BoxCollider>().Any(c => c.enabled && !c.isTrigger))
            throw new InvalidOperationException("487/395 still uses its oversized bounding box.");
        MeshCollider arenaSurface = arena.GetComponent<MeshCollider>();
        if (arenaSurface == null)
            throw new InvalidOperationException("487/395 is missing its mesh collision surface.");
        foreach (MeshFilter mesh in GetRoadMeshes(scene))
        {
            MeshCollider surface = mesh.GetComponent<MeshCollider>();
            if (surface == null) continue;
            if (surface.enabled == UsesContinuousDrivingSurface(mesh) || surface.isTrigger || surface.convex || surface.sharedMesh != mesh.sharedMesh
                || surface.attachedRigidbody != null)
                throw new InvalidOperationException("The road collider does not match its visible static mesh: " + mesh.name);
        }
        BumperCarRoadContinuitySetup.Validate(scene);
    }

    private static bool UsesContinuousDrivingSurface(MeshFilter mesh)
    {
        if (mesh.name == "395") return true;
        for (Transform parent = mesh.transform; parent != null; parent = parent.parent)
            if (parent.name == "slope1Toad" || parent.name == "octopslope2"
                || parent.name == "platform1" || parent.name == "platform2") return true;
        return false;
    }

    private static void RequireEditScene(Scene scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before repairing road collisions.");
        if (!scene.IsValid() || !scene.isLoaded || scene.path != "Assets/Scenes/SampleScene.scene")
            throw new InvalidOperationException("Open SampleScene before repairing road collisions.");
    }

    private static BoxCollider FindKnownProxy(Scene scene, string name, Vector3 expectedPosition)
    {
        GameObject root = scene.GetRootGameObjects().SingleOrDefault(g => g.name == name && g.GetComponent<BoxCollider>() != null);
        if (root == null) return null;
        BoxCollider[] boxes = root.GetComponents<BoxCollider>();
        if (boxes.Length != 1 || root.GetComponentsInChildren<Renderer>(true).Length != 0
            || root.GetComponentsInChildren<MeshFilter>(true).Length != 0
            || Vector3.Distance(root.transform.position, expectedPosition) > 0.5f
            || Vector3.Distance(boxes[0].size, new Vector3(248.18f, 9.29f, 254.94f)) > 0.15f
            || Vector3.Distance(boxes[0].center, new Vector3(21.16f, 4.14f, 3.34f)) > 0.15f)
            throw new InvalidOperationException("The legacy proxy identity changed; inspect it before disabling: " + name);
        return boxes[0];
    }

    private static MeshFilter FindArenaMesh(Scene scene)
    {
        GameObject root = scene.GetRootGameObjects().Single(g => g.name == "487");
        MeshFilter mesh = root.GetComponentsInChildren<MeshFilter>(true).Single(m => m.name == "395");
        if (mesh.sharedMesh == null || mesh.GetComponent<Renderer>() == null || mesh.GetComponentInParent<Rigidbody>() != null)
            throw new InvalidOperationException("487/395 is not the expected visible static arena mesh.");
        return mesh;
    }

    private static MeshFilter[] GetRoadMeshes(Scene scene)
    {
        return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Where(t => RoadGroups.Contains(t.name))
            .SelectMany(t => t.GetComponentsInChildren<MeshFilter>(true))
            .Where(m => m.sharedMesh != null).Distinct().ToArray();
    }

    private static void DisableProxy(BoxCollider proxy, StringBuilder report)
    {
        if (proxy == null || !proxy.enabled) return;
        Undo.RecordObject(proxy, "Disable an elevated legacy road proxy");
        proxy.enabled = false;
        report.AppendLine("Disabled the invisible " + proxy.name + " collision proxy.");
    }

    private static void ConfigureMesh(MeshFilter mesh, MeshCollider surface)
    {
        if (surface.enabled && !surface.isTrigger && !surface.convex && surface.sharedMesh == mesh.sharedMesh) return;
        Undo.RecordObject(surface, "Match road collider to its visible mesh");
        surface.enabled = true;
        surface.isTrigger = false;
        surface.convex = false;
        surface.sharedMesh = mesh.sharedMesh;
    }

    private static string SupportingLevels(Scene scene)
    {
        Collider[] surfaces = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Collider>())
            .Where(c => c.enabled && !c.isTrigger && c.attachedRigidbody == null).ToArray();
        return "arena (30,0): " + SupportHeight(surfaces, 30f, 0f)
            + "; toad ramp (-95,-29): " + SupportHeight(surfaces, -95f, -29f);
    }

    private static string SupportHeight(Collider[] surfaces, float x, float z)
    {
        Ray ray = new Ray(new Vector3(x, 100f, z), Vector3.down);
        float nearest = float.PositiveInfinity;
        string label = "no surface";
        foreach (Collider surface in surfaces)
            if (surface.Raycast(ray, out RaycastHit hit, 150f) && hit.distance < nearest)
            {
                nearest = hit.distance;
                label = hit.point.y.ToString("F3") + " (" + surface.name + ")";
            }
        return label;
    }
}
