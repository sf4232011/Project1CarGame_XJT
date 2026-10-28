using System.IO;
using UnityEditor;

/// <summary>Run free-moving cars through the road joins instead of only checking parked cars.</summary>
public static class BumperCarGroundingValidation
{
    [MenuItem("Tools/Bumper Cars/Validate Road Grounding %#F2")]
    public static void ValidateMenu() => Validate();

    public static string Validate()
    {
        string report = BumperCarRoadDrivingValidation.Validate();
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/BumperCarGroundingValidation.txt", report);
        return report;
    }
}
