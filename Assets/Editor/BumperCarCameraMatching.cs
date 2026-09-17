using System;
using System.Collections.Generic;
using System.Linq;
using BumperCars;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BumperCarCameraMatching
{
    [MenuItem("Tools/Bumper Cars/Match Toad Camera to Octopus")]
    public static void MatchToadCamera()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before matching camera settings.");
        Camera reference = GameObject.Find("RedCamera").GetComponent<Camera>();
        Camera destination = GameObject.Find("BlueCamera").GetComponent<Camera>();
        Debug.Log(Match(reference, destination));
        EditorSceneManager.MarkSceneDirty(destination.gameObject.scene);
        EditorSceneManager.SaveScene(destination.gameObject.scene);
    }

    public static string Match(Camera reference, Camera destination)
    {
        var source = new SerializedObject(reference.GetComponent<BumperCarCameraFollow>());
        var target = new SerializedObject(destination.GetComponent<BumperCarCameraFollow>());
        var sourceCar = source.FindProperty("target").objectReferenceValue as Transform;
        var targetCar = target.FindProperty("target").objectReferenceValue as Transform;
        if (sourceCar == null || targetCar == null || reference == destination)
            throw new InvalidOperationException("Two different cameras with bound vehicles are required.");
        if (reference.orthographic)
            throw new InvalidOperationException("This framing matcher requires the perspective vehicle camera.");

        Vector3 sourcePosition = source.FindProperty("localOffset").vector3Value;
        Vector3 sourceLook = source.FindProperty("lookOffset").vector3Value
            + Vector3.up * source.FindProperty("lookHeight").floatValue;
        Quaternion orientation = Quaternion.LookRotation(sourceLook - sourcePosition, Vector3.up);
        Vector3[] sourcePoints = GetModelPoints(sourceCar);
        Vector3[] targetPoints = GetModelPoints(targetCar);
        float tangent = Mathf.Tan(reference.fieldOfView * Mathf.Deg2Rad * 0.5f);
        float aspect = reference.aspect;
        Rect referenceFrame = Project(sourcePoints, sourcePosition, orientation, tangent, aspect);
        Bounds bounds = new Bounds(targetPoints[0], Vector3.zero);
        foreach (Vector3 point in targetPoints) bounds.Encapsulate(point);
        Vector3 look = bounds.center;
        float scale = 2f;
        // Match projected diagonal and screen center. The different body shapes
        // cannot match both width and height exactly without changing perspective.
        for (int i = 0; i < 80; i++)
        {
            Vector3 position = look + (sourcePosition - sourceLook) * scale;
            Rect frame = Project(targetPoints, position, orientation, tangent, aspect);
            float depth = Vector3.Dot(bounds.center - position, orientation * Vector3.forward);
            Vector2 error = frame.center - referenceFrame.center;
            look += orientation * new Vector3(error.x * 2f * depth * tangent * aspect, error.y * 2f * depth * tangent, 0f);
            scale *= frame.size.magnitude / referenceFrame.size.magnitude;
        }
        Vector3 offset = look + (sourcePosition - sourceLook) * scale;
        Undo.RecordObject(destination, "Match Toad camera lens");
        destination.fieldOfView = reference.fieldOfView;
        destination.nearClipPlane = reference.nearClipPlane;
        destination.farClipPlane = reference.farClipPlane;
        target.FindProperty("localOffset").vector3Value = offset;
        target.FindProperty("lookHeight").floatValue = look.y;
        target.FindProperty("lookOffset").vector3Value = new Vector3(look.x, 0f, look.z);
        foreach (string property in new[] { "positionSmoothTime", "rotationSharpness", "shakeDecay" })
            target.FindProperty(property).floatValue = source.FindProperty(property).floatValue;
        target.FindProperty("maxShakeOffset").floatValue = source.FindProperty("maxShakeOffset").floatValue * scale;
        target.ApplyModifiedProperties();
        Undo.RecordObject(destination.transform, "Match Toad camera pose");
        destination.transform.SetPositionAndRotation(targetCar.position + targetCar.rotation * offset, targetCar.rotation * orientation);
        ConfigureFoliageVisibility(destination, targetCar, bounds);
        EditorUtility.SetDirty(destination);
        return $"Toad camera matched at scale {scale:F3}; offset {offset:F3}, look {look:F3}; "
            + $"Octopus frame {referenceFrame}, Toad frame {Project(targetPoints, offset, orientation, tangent, aspect)}.";
    }

    private static void ConfigureFoliageVisibility(Camera camera, Transform vehicle, Bounds bounds)
    {
        var visibility = camera.GetComponent<BumperCarCameraFoliageVisibility>();
        if (visibility == null) visibility = Undo.AddComponent<BumperCarCameraFoliageVisibility>(camera.gameObject);
        var data = new SerializedObject(visibility);
        data.FindProperty("target").objectReferenceValue = vehicle;
        data.FindProperty("targetHeight").floatValue = bounds.center.y;
        data.FindProperty("targetHalfWidth").floatValue = bounds.extents.x * 0.8f;
        Renderer[] foliage = vehicle.gameObject.scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true))
            .Where(r => r.name.StartsWith("Tree-", StringComparison.Ordinal) || r.name.StartsWith("Bush-", StringComparison.Ordinal)
                || r.name.StartsWith("Hedge-", StringComparison.Ordinal)).Cast<Renderer>().ToArray();
        var array = data.FindProperty("foliage");
        array.arraySize = foliage.Length;
        for (int i = 0; i < foliage.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = foliage[i];
        data.ApplyModifiedProperties();
    }

    public static Vector3[] GetModelPoints(Transform root)
    {
        var points = new List<Vector3>();
        Quaternion inverse = Quaternion.Inverse(root.rotation);
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled) continue;
            if (renderer is SkinnedMeshRenderer skin)
            {
                Mesh mesh = skin.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                BoneWeight[] weights = mesh.boneWeights;
                Matrix4x4[] bindposes = mesh.bindposes;
                Matrix4x4[] matrices = skin.bones.Select((bone, i) => bone.localToWorldMatrix * bindposes[i]).ToArray();
                for (int i = 0; i < vertices.Length; i++)
                {
                    BoneWeight weight = weights[i];
                    Vector3 v = vertices[i];
                    Vector3 world = matrices[weight.boneIndex0].MultiplyPoint3x4(v) * weight.weight0;
                    if (weight.weight1 > 0f) world += matrices[weight.boneIndex1].MultiplyPoint3x4(v) * weight.weight1;
                    if (weight.weight2 > 0f) world += matrices[weight.boneIndex2].MultiplyPoint3x4(v) * weight.weight2;
                    if (weight.weight3 > 0f) world += matrices[weight.boneIndex3].MultiplyPoint3x4(v) * weight.weight3;
                    points.Add(inverse * (world - root.position));
                }
            }
            else
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                foreach (Vector3 v in filter.sharedMesh.vertices)
                    points.Add(inverse * (renderer.transform.TransformPoint(v) - root.position));
            }
        }
        if (points.Count == 0) throw new InvalidOperationException(root.name + " has no visible vehicle mesh.");
        return points.ToArray();
    }

    private static Rect Project(Vector3[] points, Vector3 cameraPosition, Quaternion orientation, float tangent, float aspect)
    {
        Quaternion inverse = Quaternion.Inverse(orientation);
        Vector2 min = Vector2.one * float.PositiveInfinity;
        Vector2 max = Vector2.one * float.NegativeInfinity;
        foreach (Vector3 p in points)
        {
            Vector3 view = inverse * (p - cameraPosition);
            if (view.z <= 0f) throw new InvalidOperationException("Camera must be behind the complete vehicle mesh.");
            Vector2 v = new Vector2(0.5f + view.x / (2f * view.z * tangent * aspect), 0.5f + view.y / (2f * view.z * tangent));
            min = Vector2.Min(min, v);
            max = Vector2.Max(max, v);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
}
