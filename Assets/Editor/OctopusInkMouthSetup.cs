using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BumperCars.Editor
{
    /// <summary>Authors the ink nozzle on the imported face, independent of FBX root scale.</summary>
    public static class OctopusInkMouthSetup
    {
        private const string FaceName = "pasted__Face-Low";
        private const string MouthName = "InkMouthPoint";
        // Imported face-local coordinates, verified against the FBX's protruding lip.
        // Front lip ends at z=.0202173; .0205 places the nozzle just outside it.
        private static readonly Vector3 FaceLocalMouth = new Vector3(0f, -0.002f, 0.0205f);

        public static Transform Configure(RedCarInkRetreatSkill skill)
        {
            if (skill == null) throw new ArgumentNullException(nameof(skill));
            BumperCarController controller = skill.GetComponent<BumperCarController>();
            if (controller == null)
                throw new InvalidOperationException("The octopus skill requires a BumperCarController.");
            Transform[] faces = skill.GetComponentsInChildren<Transform>(true)
                .Where(candidate => candidate.name == FaceName).ToArray();
            if (faces.Length != 1)
                throw new InvalidOperationException("Expected one imported octopus face named " + FaceName + "; found " + faces.Length + ".");

            Transform face = faces[0];
            Transform mouth = face.Find(MouthName);
            if (mouth == null)
            {
                GameObject mouthObject = new GameObject(MouthName);
                mouthObject.layer = skill.gameObject.layer;
                Undo.RegisterCreatedObjectUndo(mouthObject, "Create octopus mouth nozzle");
                mouth = mouthObject.transform;
                Undo.SetTransformParent(mouth, face, "Attach ink nozzle to octopus face");
            }

            Vector3 forward = controller.DriveForwardDirection;
            if (forward.sqrMagnitude < 0.5f)
                throw new InvalidOperationException("The octopus has no valid drive-forward direction.");
            Undo.RecordObject(mouth, "Position octopus ink nozzle on mouth");
            mouth.localPosition = FaceLocalMouth;
            mouth.localScale = Vector3.one;
            mouth.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            PrefabUtility.RecordPrefabInstancePropertyModifications(mouth);

            Undo.RecordObject(skill, "Bind ink spray to octopus mouth");
            SerializedObject skillData = new SerializedObject(skill);
            SerializedProperty nozzle = skillData.FindProperty("inkSpawnPoint");
            if (nozzle == null)
                throw new MissingFieldException(typeof(RedCarInkRetreatSkill).FullName, "inkSpawnPoint");
            nozzle.objectReferenceValue = mouth;
            skillData.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(skill);
            return mouth;
        }

        [MenuItem("Tools/Bumper Cars/VFX/Place Octopus Ink Mouth %#F7")]
        public static void PlaceOctopusInkMouth()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Place the octopus mouth nozzle in Edit mode.");

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
                throw new InvalidOperationException("Open the saved gameplay scene before placing the octopus mouth nozzle.");
            RedCarInkRetreatSkill[] skills = UnityEngine.Object.FindObjectsOfType<RedCarInkRetreatSkill>()
                .Where(candidate => candidate.gameObject.scene == scene && candidate.isActiveAndEnabled).ToArray();
            if (skills.Length != 1)
                throw new InvalidOperationException("Expected one active octopus skill in the current scene; found " + skills.Length + ".");

            RedCarInkRetreatSkill skill = skills[0];
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Move octopus ink spray to mouth");
            Transform mouth = Configure(skill);
            BumperCarController controller = skill.GetComponent<BumperCarController>();
            Vector3 rootLocalPosition = skill.transform.InverseTransformPoint(mouth.position);
            BoxCollider chassis = skill.GetComponent<BoxCollider>();
            Vector3 chassisCenter = chassis != null ? skill.transform.TransformPoint(chassis.center) : skill.transform.position;
            float aheadOfCenter = Vector3.Dot(mouth.position - chassisCenter, controller.DriveForwardDirection.normalized);
            float directionDot = Vector3.Dot(mouth.forward, controller.DriveForwardDirection.normalized);

            StringBuilder report = new StringBuilder();
            report.AppendLine("Octopus ink mouth placement validation");
            report.AppendLine("Scene: " + scene.path);
            report.AppendLine("Vehicle: " + skill.name);
            report.AppendLine("Parent face: " + mouth.parent.name);
            report.AppendLine("Face-local nozzle: " + Format(mouth.localPosition));
            report.AppendLine("Root-local nozzle: " + Format(rootLocalPosition));
            report.AppendLine("World nozzle: " + Format(mouth.position));
            report.AppendLine("World spray direction: " + Format(mouth.forward));
            report.AppendLine("Forward alignment dot: " + directionDot.ToString("F7", CultureInfo.InvariantCulture));
            report.AppendLine("Nozzle distance ahead of chassis center: " + aheadOfCenter.ToString("F7", CultureInfo.InvariantCulture) + "m");
            bool valid = mouth.parent.name == FaceName && rootLocalPosition.z > 0f && aheadOfCenter > 0f && directionDot > 0.999f;
            report.AppendLine(valid ? "PASS: The nozzle is outside the front mouth and faces drive-forward." : "FAIL: Mouth position or direction is inconsistent with the vehicle front.");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/OctopusInkMouthValidation.txt", report.ToString(), Encoding.UTF8);
            if (!valid)
                throw new InvalidOperationException(report.ToString());

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new IOException("The octopus mouth anchor was placed but the gameplay scene could not be saved.");
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log(report + "Saved the mouth anchor and inkSpawnPoint binding. The old rear anchor was left untouched.");
        }

        private static string Format(Vector3 vector)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:F7}, {1:F7}, {2:F7})", vector.x, vector.y, vector.z);
        }
    }
}
