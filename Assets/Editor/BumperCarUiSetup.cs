using BumperCars;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class BumperCarUiSetup
{
    private const string TargetScenePath = "Assets/Scenes/SampleScene.scene";
    private const string CooldownRootName = "RedSkillCooldown";
    private const string Player1OverlayName = "InkOverlay_Player1";
    private const string Player2OverlayName = "InkOverlay_Player2";

    [MenuItem("Tools/Bumper Cars/Setup Red Skill UI")]
    public static void SetupRedSkillUi()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != TargetScenePath && scene.name != "SampleScene")
        {
            Debug.LogWarning($"Open {TargetScenePath} before setting up the bumper-car UI.");
            return;
        }

        Canvas canvas = Object.FindObjectOfType<Canvas>();
        RedCarInkRetreatSkill redSkill = Object.FindObjectOfType<RedCarInkRetreatSkill>();
        if (canvas == null || redSkill == null)
        {
            Debug.LogError("Red skill UI setup needs a Canvas and a RedCarInkRetreatSkill in the active scene.");
            return;
        }

        InkScreenOverlay player1Overlay = CreateOrUpdateOverlay(canvas.transform, Player1OverlayName, BumperCarPlayer.Player1, 0f, 0.5f);
        InkScreenOverlay player2Overlay = CreateOrUpdateOverlay(canvas.transform, Player2OverlayName, BumperCarPlayer.Player2, 0.5f, 1f);
        player1Overlay.transform.SetSiblingIndex(0);
        player2Overlay.transform.SetSiblingIndex(1);

        CreateOrUpdateCooldown(canvas.transform, redSkill);

        EditorUtility.SetDirty(canvas.gameObject);
        EditorUtility.SetDirty(redSkill);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Red car cooldown UI and split-screen ink overlays are ready.");
    }

    private static InkScreenOverlay CreateOrUpdateOverlay(
        Transform canvasTransform,
        string objectName,
        BumperCarPlayer player,
        float anchorMinX,
        float anchorMaxX)
    {
        GameObject overlayObject = FindDirectChild(canvasTransform, objectName);
        if (overlayObject == null)
        {
            overlayObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup), typeof(InkScreenOverlay));
            Undo.RegisterCreatedObjectUndo(overlayObject, $"Create {objectName}");
            overlayObject.transform.SetParent(canvasTransform, false);
        }

        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(overlayObject);

        if (overlayObject.GetComponent<Image>() == null)
        {
            overlayObject.AddComponent<Image>();
        }

        if (overlayObject.GetComponent<CanvasGroup>() == null)
        {
            overlayObject.AddComponent<CanvasGroup>();
        }

        if (overlayObject.GetComponent<InkScreenOverlay>() == null)
        {
            overlayObject.AddComponent<InkScreenOverlay>();
        }

        RectTransform rect = overlayObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(anchorMinX, 0f);
        rect.anchorMax = new Vector2(anchorMaxX, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);

        Image image = overlayObject.GetComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;

        CanvasGroup group = overlayObject.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        InkScreenOverlay overlay = overlayObject.GetComponent<InkScreenOverlay>();
        SerializedObject serializedOverlay = new SerializedObject(overlay);
        serializedOverlay.FindProperty("affectedPlayer").enumValueIndex = (int)player;
        serializedOverlay.FindProperty("overlayGroup").objectReferenceValue = group;
        serializedOverlay.FindProperty("overlayImage").objectReferenceValue = image;
        serializedOverlay.FindProperty("maxAlpha").floatValue = 0.82f;
        serializedOverlay.ApplyModifiedPropertiesWithoutUndo();

        return overlay;
    }

    private static void CreateOrUpdateCooldown(Transform canvasTransform, RedCarInkRetreatSkill redSkill)
    {
        GameObject root = FindDirectChild(canvasTransform, CooldownRootName);
        if (root == null)
        {
            root = new GameObject(CooldownRootName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(root, "Create red skill cooldown UI");
            root.transform.SetParent(canvasTransform, false);
        }

        BumperCarController controller = redSkill.GetComponent<BumperCarController>();
        bool isPlayer2 = controller != null && controller.Player == BumperCarPlayer.Player2;

        RectTransform rootRect = root.GetComponent<RectTransform>();
        float anchorX = isPlayer2 ? 0.75f : 0.25f;
        rootRect.anchorMin = new Vector2(anchorX, 0f);
        rootRect.anchorMax = new Vector2(anchorX, 0f);
        rootRect.anchoredPosition = new Vector2(0f, 38f);
        rootRect.sizeDelta = new Vector2(250f, 30f);
        rootRect.pivot = new Vector2(0.5f, 0f);

        Image background = root.GetComponent<Image>();
        background.color = new Color(0.04f, 0.04f, 0.04f, 0.88f);
        background.raycastTarget = false;

        Image fill = GetOrCreateFill(root.transform);
        TMP_Text text = GetOrCreateText(root.transform);

        SerializedObject serializedSkill = new SerializedObject(redSkill);
        serializedSkill.FindProperty("cooldownFill").objectReferenceValue = fill;
        serializedSkill.FindProperty("cooldownText").objectReferenceValue = text;
        serializedSkill.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Image GetOrCreateFill(Transform parent)
    {
        GameObject fillObject = FindDirectChild(parent, "Fill");
        if (fillObject == null)
        {
            fillObject = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fillObject.transform.SetParent(parent, false);
        }

        RectTransform rect = fillObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(3f, 3f);
        rect.offsetMax = new Vector2(-3f, -3f);

        Image fill = fillObject.GetComponent<Image>();
        fill.color = new Color(0.95f, 0.2f, 0.12f, 1f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;
        fill.raycastTarget = false;
        return fill;
    }

    private static TMP_Text GetOrCreateText(Transform parent)
    {
        GameObject textObject = FindDirectChild(parent, "Label");
        if (textObject == null)
        {
            textObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
        }

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = "INK  READY";
        text.fontSize = 17f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject FindDirectChild(Transform parent, string objectName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == objectName)
            {
                return child.gameObject;
            }
        }

        return null;
    }
}
