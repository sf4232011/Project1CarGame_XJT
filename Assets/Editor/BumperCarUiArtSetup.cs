using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BumperCars;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class BumperCarUiArtSetup
{
    private const string ArtFolder = "Assets/Art/UI/";
    private const string ScenePath = "Assets/Scenes/SampleScene.scene";
    private const float PanelScale = 0.58f;

    // Source rectangles are measured in the original 956 x 429 art canvas,
    // with a top-left origin. Related layers share the same coordinate space.
    private static readonly Dictionary<string, Rect> ArtRects = new Dictionary<string, Rect>
    {
        { "Toad-panel.PNG", new Rect(77, 86, 806, 270) },
        { "Blood-toad.PNG", new Rect(390, 222, 464, 27) },
        { "Octopus-panel.PNG", new Rect(122, 97, 789, 264) },
        { "Blood-Octopus.PNG", new Rect(155, 229, 460, 25) },
        { "Timer.PNG", new Rect(470, 110, 427, 163) },
        { "Toad-Ulti.PNG", new Rect(546, 48, 355, 354) },
        { "ToadUlti-loading.PNG", new Rect(546, 48, 355, 354) },
        { "Octopus-Ulti.PNG", new Rect(546, 58, 355, 354) },
        { "OctopusUlti-Loading.PNG", new Rect(546, 58, 355, 354) }
    };

    [MenuItem("Tools/Bumper Cars/Apply UI Art")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath)
            throw new InvalidOperationException("Open SampleScene in edit mode before applying the UI art.");

        BumperCarGameManager manager = Object.FindObjectOfType<BumperCarGameManager>();
        if (manager == null) throw new InvalidOperationException("No BumperCarGameManager in the active scene.");
        SerializedObject game = new SerializedObject(manager);
        BumperCarHudPanel leftHud = game.FindProperty("player1Hud").objectReferenceValue as BumperCarHudPanel;
        BumperCarHudPanel rightHud = game.FindProperty("player2Hud").objectReferenceValue as BumperCarHudPanel;
        TMP_Text timer = game.FindProperty("timerText").objectReferenceValue as TMP_Text;
        if (leftHud == null || rightHud == null || timer == null)
            throw new InvalidOperationException("The game manager needs both HUD panels and its timer reference.");
        Canvas canvas = timer.GetComponentInParent<Canvas>();
        if (canvas == null) throw new InvalidOperationException("The timer must belong to a Canvas.");
        foreach (string filename in ArtRects.Keys)
            if (!File.Exists(ArtFolder + filename)) throw new FileNotFoundException(ArtFolder + filename);

        Dictionary<string, Sprite> art = ImportArt();
        Undo.SetCurrentGroupName("Replace bumper car UI art");
        Undo.RegisterFullObjectHierarchyUndo(canvas.gameObject, "Replace bumper car UI art");

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        SetupHud(leftHud, art, false);
        SetupHud(rightHud, art, true);
        SetupTimer(canvas.transform, timer, art["Timer.PNG"], game.FindProperty("matchDuration").floatValue);
        SetupSkills(canvas.transform, art);

        TMP_Text result = game.FindProperty("resultText").objectReferenceValue as TMP_Text;
        if (result != null)
        {
            SetRect(result.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 180));
            result.fontSize = 52;
            result.alignment = TextAlignmentOptions.Center;
            result.raycastTarget = false;
            result.transform.SetAsLastSibling();
        }

        EditorUtility.SetDirty(canvas.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Failed to save the updated UI scene.");
        Debug.Log("UI art applied: two character panels, two health bars, two skill bases/rings and timer. All nine Art/UI sprites are bound.");
    }

    private static Dictionary<string, Sprite> ImportArt()
    {
        var sprites = new Dictionary<string, Sprite>();
        foreach (var entry in ArtRects)
        {
            string path = ArtFolder + entry.Key;
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Cannot import UI texture: " + path);
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            if (width != 956 || height != 429)
                throw new InvalidOperationException("UI art source size changed; update the crop rectangles: " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 100;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            Rect topRect = entry.Value;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
#pragma warning disable 618
            importer.spritesheet = new[]
            {
                new SpriteMetaData
                {
                    name = Path.GetFileNameWithoutExtension(path) + "_UI",
                    rect = new Rect(topRect.x, height - topRect.yMax, topRect.width, topRect.height),
                    alignment = (int)SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f)
                }
            };
#pragma warning restore 618
            importer.SaveAndReimport();
            Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().SingleOrDefault();
            if (sprite == null) throw new InvalidOperationException("Missing imported UI sprite: " + path);
            sprites.Add(entry.Key, sprite);
        }
        return sprites;
    }

    private static void SetupHud(BumperCarHudPanel hud, Dictionary<string, Sprite> art, bool right)
    {
        string panelFile = right ? "Octopus-panel.PNG" : "Toad-panel.PNG";
        string bloodFile = right ? "Blood-Octopus.PNG" : "Blood-toad.PNG";
        Rect panelSource = ArtRects[panelFile];
        Rect bloodSource = ArtRects[bloodFile];
        RectTransform panel = (RectTransform)hud.transform;
        SetRect(panel, new Vector2(right ? 1 : 0, 1), new Vector2(right ? 1 : 0, 1),
            new Vector2(right ? -28 : 28, -22), panelSource.size * PanelScale);

        Image background = hud.GetComponent<Image>();
        if (background == null) background = Undo.AddComponent<Image>(hud.gameObject);
        SetImage(background, art[panelFile]);

        SerializedObject data = new SerializedObject(hud);
        Image fill = data.FindProperty("healthFill").objectReferenceValue as Image;
        if (fill == null) throw new InvalidOperationException("HUD has no health fill: " + hud.name);
        Image track = GetImage(panel, "HealthTrack");
        SetImage(track, art[bloodFile]);
        track.color = new Color(0.12f, 0.21f, 0.26f, 0.85f);
        Vector2 barPosition = new Vector2(bloodSource.x - panelSource.x, -(bloodSource.y - panelSource.y)) * PanelScale;
        Vector2 barSize = bloodSource.size * PanelScale;
        SetRect(track.rectTransform, new Vector2(0, 1), new Vector2(0, 1), barPosition, barSize);
        track.transform.SetAsFirstSibling();
        SetRect(fill.rectTransform, new Vector2(0, 1), new Vector2(0, 1), barPosition, barSize);
        SetImage(fill, art[bloodFile]);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = right ? (int)Image.OriginHorizontal.Right : (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1;
        fill.transform.SetAsLastSibling();
        TMP_Text oldName = data.FindProperty("nameText").objectReferenceValue as TMP_Text;
        if (oldName != null) oldName.gameObject.SetActive(false); // Names are part of the supplied panel artwork.
        data.FindProperty("preserveArtworkColors").boolValue = true;
        data.ApplyModifiedProperties();
        EditorUtility.SetDirty(hud);
    }

    private static void SetupTimer(Transform canvas, TMP_Text timer, Sprite sprite, float matchDuration)
    {
        Image background = GetImage(canvas, "TimerArt");
        SetImage(background, sprite);
        SetRect(background.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(230, 87.8f));
        timer.transform.SetParent(background.transform, false);
        Stretch(timer.rectTransform);
        timer.rectTransform.offsetMin = new Vector2(16, 8);
        timer.rectTransform.offsetMax = new Vector2(-16, -8);
        timer.fontSize = 42;
        timer.fontStyle = FontStyles.Bold;
        timer.alignment = TextAlignmentOptions.Center;
        timer.color = new Color(0.025f, 0.09f, 0.13f, 1);
        timer.raycastTarget = false;
        int seconds = Mathf.CeilToInt(Mathf.Max(0, matchDuration));
        timer.text = $"{seconds / 60:00}:{seconds % 60:00}";
    }

    private static void SetupSkills(Transform canvas, Dictionary<string, Sprite> art)
    {
        ToadSlowTrailSkill[] toads = Object.FindObjectsOfType<ToadSlowTrailSkill>(true);
        RedCarInkRetreatSkill[] octopuses = Object.FindObjectsOfType<RedCarInkRetreatSkill>(true);
        if (toads.Length == 0 || octopuses.Length == 0)
            throw new InvalidOperationException("Both vehicle skill components are required.");
        Transform toadRoot = canvas.Find("SkillFill");
        if (toadRoot == null) toadRoot = GetImage(canvas, "SkillFill").transform;
        Transform octopusRoot = canvas.Find("RedSkillCooldown");
        if (octopusRoot == null) octopusRoot = GetImage(canvas, "RedSkillCooldown").transform;

        Image toadRing = SetupSkill(toadRoot, art["Toad-Ulti.PNG"], art["ToadUlti-loading.PNG"], 0.25f, "F", "100 / 100", out TMP_Text toadText);
        Image octopusRing = SetupSkill(octopusRoot, art["Octopus-Ulti.PNG"], art["OctopusUlti-Loading.PNG"], 0.75f, ".", "INK [.]  READY", out TMP_Text octopusText);
        foreach (ToadSlowTrailSkill skill in toads)
        {
            Undo.RecordObject(skill, "Bind toad UI art");
            var data = new SerializedObject(skill);
            data.FindProperty("chargeFill").objectReferenceValue = toadRing;
            data.FindProperty("chargeText").objectReferenceValue = toadText;
            data.FindProperty("preserveArtworkColors").boolValue = true;
            data.ApplyModifiedProperties();
        }
        foreach (RedCarInkRetreatSkill skill in octopuses)
        {
            Undo.RecordObject(skill, "Bind octopus UI art");
            var data = new SerializedObject(skill);
            data.FindProperty("cooldownFill").objectReferenceValue = octopusRing;
            data.FindProperty("cooldownText").objectReferenceValue = octopusText;
            data.FindProperty("preserveArtworkColors").boolValue = true;
            data.ApplyModifiedProperties();
        }
    }

    private static Image SetupSkill(Transform root, Sprite disk, Sprite ring, float anchorX, string key, string initialText, out TMP_Text label)
    {
        SetRect((RectTransform)root, new Vector2(anchorX, 0), new Vector2(0.5f, 0), new Vector2(0, 52), new Vector2(116, 116));
        Image background = root.GetComponent<Image>();
        if (background == null) background = Undo.AddComponent<Image>(root.gameObject);
        SetImage(background, disk);

        // Reuse the old fill so existing inspector references remain recognizable.
        Transform oldFill = root.Find(root.name == "SkillFill" ? "Image" : "Fill");
        Image progress = oldFill != null ? oldFill.GetComponent<Image>() : GetImage(root, "ProgressRing");
        Stretch(progress.rectTransform);
        SetImage(progress, ring);
        progress.type = Image.Type.Filled;
        progress.fillMethod = Image.FillMethod.Radial360;
        progress.fillOrigin = (int)Image.Origin360.Top;
        progress.fillClockwise = true;
        progress.fillAmount = 1;
        progress.transform.SetAsFirstSibling();

        TMP_Text button = GetText(root, "KeyLabel");
        Stretch(button.rectTransform);
        button.text = key;
        button.fontSize = 38;
        button.fontStyle = FontStyles.Bold;
        button.color = new Color(0.86f, 0.97f, 1, 1);
        label = GetText(root, "Label");
        SetRect(label.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, -7), new Vector2(260, 34));
        label.fontSize = 20;
        label.text = initialText;
        label.color = new Color(0.86f, 0.97f, 1, 1);
        return progress;
    }

    private static Image GetImage(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            var created = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(created, "Create " + name);
            created.transform.SetParent(parent, false);
            created.layer = 5;
            child = created.transform;
        }
        Image image = child.GetComponent<Image>();
        return image != null ? image : Undo.AddComponent<Image>(child.gameObject);
    }

    private static TMP_Text GetText(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            var created = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            Undo.RegisterCreatedObjectUndo(created, "Create " + name);
            created.transform.SetParent(parent, false);
            created.layer = 5;
            child = created.transform;
        }
        TMP_Text text = child.GetComponent<TMP_Text>();
        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        return text;
    }

    private static void SetImage(Image image, Sprite sprite)
    {
        image.enabled = true;
        image.sprite = sprite;
        image.overrideSprite = null;
        image.type = Image.Type.Simple;
        image.color = Color.white;
        image.raycastTarget = false;
        image.preserveAspect = false;
    }

    private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
