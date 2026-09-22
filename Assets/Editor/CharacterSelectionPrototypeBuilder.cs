using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class CharacterSelectionPrototypeBuilder
{
    private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
    private const string BigHallScenePath = "Assets/Scenes/BigHall.unity";
    private const string ControllerName = "Character Select Controller";
    private const string MainMenuControllerName = "Main Menu Controller";
    private const string PreviewRootName = "Character Preview Root";
    private const string PanelName = "Character Select Panel";
    private const float PreviewColliderRadius = 0.45f;
    private const float PreviewColliderHeight = 2.1f;
    private static readonly Vector3 PreviewColliderCenter = new Vector3(0f, 1.05f, 0f);

    private static readonly CharacterPrototypeData[] CharacterData =
    {
        new CharacterPrototypeData(
            "黃帽小鴨",
            "動作輕快、辨識度高。適合當第一個測試角色，用來確認選角流程和之後的玩家生成。",
            "Assets/people/1/00991177a1.fbx",
            "Assets/people/1/Meshy_AI_11_biped_Animation_Walking_withSkin.controller"),
        new CharacterPrototypeData(
            "藍帽小鴨",
            "穿著藍色帽T的角色版本。用來測試第二頁切換、模型啟用與停用是否正常。",
            "Assets/people/2/0917a2.fbx",
            "Assets/people/1/Meshy_AI_11_biped_Animation_Walking_withSkin.controller"),
        new CharacterPrototypeData(
            "探險小鴨",
            "偏冒險風格的角色版本。用來測試第三頁和之後繼續增加角色時的擴充方式。",
            "Assets/people/3/Meshy_AI_33_biped/Meshy_AI_33_biped_Animation_Walking_withSkin.fbx",
            string.Empty)
    };

    static CharacterSelectionPrototypeBuilder()
    {
        EditorApplication.delayCall += AutoSetupIfBigHallIsOpen;
    }

    [MenuItem("Duck/Setup Character Select Prototype")]
    public static void SetupCharacterSelectPrototype()
    {
        EnsureMainMenuLoadsBigHall();
        EnsureSceneInBuildSettings(MainMenuScenePath);
        EnsureSceneInBuildSettings(BigHallScenePath);

        Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.path != BigHallScenePath)
        {
            scene = EditorSceneManager.OpenScene(BigHallScenePath, OpenSceneMode.Single);
        }

        SetupScene(scene);
    }

    private static void AutoSetupIfBigHallIsOpen()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.path != BigHallScenePath)
        {
            return;
        }

        EnsureMainMenuLoadsBigHall();
        EnsureSceneInBuildSettings(MainMenuScenePath);
        EnsureSceneInBuildSettings(BigHallScenePath);

        if (GameObject.Find(ControllerName) == null)
        {
            SetupScene(scene);
            return;
        }

        if (RefreshExistingControllerReferences())
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }
    }

    private static void EnsureMainMenuLoadsBigHall()
    {
        Scene existingScene = SceneManager.GetSceneByPath(MainMenuScenePath);
        bool wasLoaded = existingScene.IsValid() && existingScene.isLoaded;
        Scene mainMenuScene = wasLoaded
            ? existingScene
            : EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Additive);

        GameObject controllerObject = FindGameObjectInScene(mainMenuScene, MainMenuControllerName);
        if (controllerObject == null)
        {
            controllerObject = new GameObject(MainMenuControllerName);
            SceneManager.MoveGameObjectToScene(controllerObject, mainMenuScene);
        }

        MainMenuController controller = controllerObject.GetComponent<MainMenuController>();
        if (controller == null)
        {
            controller = controllerObject.AddComponent<MainMenuController>();
        }

        SerializedObject serializedController = new SerializedObject(controller);
        serializedController.FindProperty("gameSceneName").stringValue = "BigHall";
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(mainMenuScene);
        EditorSceneManager.SaveScene(mainMenuScene);

        if (!wasLoaded)
        {
            EditorSceneManager.CloseScene(mainMenuScene, true);
        }
    }

    private static void EnsureSceneInBuildSettings(string scenePath)
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        for (int i = 0; i < scenes.Count; i++)
        {
            if (scenes[i].path == scenePath)
            {
                scenes[i].enabled = true;
                EditorBuildSettings.scenes = scenes.ToArray();
                return;
            }
        }

        scenes.Add(new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void SetupScene(Scene scene)
    {
        Canvas canvas = EnsureCanvas();
        EnsureEventSystem();
        HideLegacyThumbnails();

        List<GameObject> previewModels = BuildPreviewModels(scene);
        PrototypeUiRefs uiRefs = BuildUi(canvas.transform);
        CharacterSelectionController controller = EnsureController();
        ConfigureController(controller, previewModels, uiRefs);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    private static Canvas EnsureCanvas()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas != null)
        {
            return canvas;
        }

        GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform));
        SetLayerRecursively(canvasObject, LayerMask.NameToLayer("UI"));

        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<StandaloneInputModule>();
    }

    private static void HideLegacyThumbnails()
    {
        string[] thumbnailNames = { "RawImage (1)", "RawImage (2)", "RawImage (3)" };
        foreach (string thumbnailName in thumbnailNames)
        {
            GameObject thumbnail = GameObject.Find(thumbnailName);
            if (thumbnail != null)
            {
                thumbnail.SetActive(false);
            }
        }
    }

    private static List<GameObject> BuildPreviewModels(Scene scene)
    {
        GameObject previewRoot = GameObject.Find(PreviewRootName);
        if (previewRoot == null)
        {
            previewRoot = new GameObject(PreviewRootName);
        }

        previewRoot.transform.position = GetPreviewPosition();
        previewRoot.transform.rotation = Quaternion.identity;
        previewRoot.transform.localScale = Vector3.one;
        ClearChildren(previewRoot.transform);

        List<GameObject> previewModels = new List<GameObject>();
        for (int i = 0; i < CharacterData.Length; i++)
        {
            CharacterPrototypeData data = CharacterData[i];
            GameObject slot = new GameObject("Preview Slot " + (i + 1) + " - " + data.DisplayName);
            slot.transform.SetParent(previewRoot.transform, false);
            slot.transform.localPosition = Vector3.zero;
            slot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            slot.transform.localScale = Vector3.one;
            slot.AddComponent<PreviewTurntable>();

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(data.AssetPath);
            if (prefab != null)
            {
                GameObject model = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (model != null)
                {
                    model.name = data.DisplayName + " Model";
                    model.transform.SetParent(slot.transform, false);
                    model.transform.localPosition = Vector3.zero;
                    model.transform.localRotation = Quaternion.identity;
                    model.transform.localScale = Vector3.one;
                    FitModelOnSlot(model, slot.transform, 2.1f);
                }
            }

            EnsurePreviewCollider(slot);
            slot.SetActive(i == 0);
            previewModels.Add(slot);
        }

        return previewModels;
    }

    private static Vector3 GetPreviewPosition()
    {
        GameObject cylinder = GameObject.Find("Cylinder");
        if (cylinder == null)
        {
            return new Vector3(15.08f, 1.45f, -2.02f);
        }

        Renderer renderer = cylinder.GetComponent<Renderer>();
        if (renderer != null)
        {
            Bounds bounds = renderer.bounds;
            return new Vector3(bounds.center.x, bounds.max.y + 0.08f, bounds.center.z);
        }

        Transform transform = cylinder.transform;
        return transform.position + Vector3.up * (transform.lossyScale.y + 0.08f);
    }

    private static PrototypeUiRefs BuildUi(Transform canvasTransform)
    {
        Transform existingPanel = canvasTransform.Find(PanelName);
        if (existingPanel != null)
        {
            Object.DestroyImmediate(existingPanel.gameObject);
        }

        TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI/TmpFont/Fonts/NotoSansTC-Medium SDF.asset");

        GameObject panel = CreateUiObject(PanelName, canvasTransform);
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.07f, 0.06f, 0.05f, 0.78f);
        SetCenteredRect(panel.GetComponent<RectTransform>(), new Vector2(820f, 300f), new Vector2(0f, -320f));

        TextMeshProUGUI titleText = CreateText(panel.transform, "Character Name Text", fontAsset, 38, Color.white, TextAlignmentOptions.Center);
        SetCenteredRect(titleText.rectTransform, new Vector2(720f, 58f), new Vector2(0f, 105f));

        TextMeshProUGUI descriptionText = CreateText(panel.transform, "Character Description Text", fontAsset, 23, new Color(0.96f, 0.88f, 0.72f, 1f), TextAlignmentOptions.Top);
        SetCenteredRect(descriptionText.rectTransform, new Vector2(700f, 92f), new Vector2(0f, 35f));

        TextMeshProUGUI selectedText = CreateText(panel.transform, "Selected Character Text", fontAsset, 22, new Color(0.72f, 1f, 0.66f, 1f), TextAlignmentOptions.Center);
        SetCenteredRect(selectedText.rectTransform, new Vector2(620f, 34f), new Vector2(0f, -42f));

        TextMeshProUGUI pageText = CreateText(panel.transform, "Character Page Text", fontAsset, 20, new Color(0.92f, 0.86f, 0.74f, 1f), TextAlignmentOptions.Center);
        SetCenteredRect(pageText.rectTransform, new Vector2(120f, 36f), new Vector2(0f, -114f));

        Button previousButton = CreateButton(panel.transform, "Previous Page Button", "上一頁", fontAsset, new Vector2(-285f, -112f), new Vector2(150f, 54f));
        Button selectButton = CreateButton(panel.transform, "Select Character Button", "選擇此角色", fontAsset, new Vector2(0f, -112f), new Vector2(230f, 58f));
        Button nextButton = CreateButton(panel.transform, "Next Page Button", "下一頁", fontAsset, new Vector2(285f, -112f), new Vector2(150f, 54f));
        previousButton.gameObject.SetActive(false);
        nextButton.gameObject.SetActive(CharacterData.Length > 1);

        return new PrototypeUiRefs(panel, titleText, descriptionText, pageText, selectedText, previousButton, nextButton, selectButton);
    }

    private static CharacterSelectionController EnsureController()
    {
        GameObject controllerObject = GameObject.Find(ControllerName);
        if (controllerObject == null)
        {
            controllerObject = new GameObject(ControllerName);
        }

        CharacterSelectionController controller = controllerObject.GetComponent<CharacterSelectionController>();
        if (controller == null)
        {
            controller = controllerObject.AddComponent<CharacterSelectionController>();
        }

        return controller;
    }

    private static GameObject FindGameObjectInScene(Scene scene, string objectName)
    {
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            GameObject found = FindGameObjectInChildren(rootObject.transform, objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static GameObject FindGameObjectInChildren(Transform parent, string objectName)
    {
        if (parent.name == objectName)
        {
            return parent.gameObject;
        }

        foreach (Transform child in parent)
        {
            GameObject found = FindGameObjectInChildren(child, objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static void ConfigureController(CharacterSelectionController controller, IReadOnlyList<GameObject> previewModels, PrototypeUiRefs uiRefs)
    {
        SerializedObject serializedController = new SerializedObject(controller);

        serializedController.FindProperty("titleText").objectReferenceValue = uiRefs.TitleText;
        serializedController.FindProperty("descriptionText").objectReferenceValue = uiRefs.DescriptionText;
        serializedController.FindProperty("pageText").objectReferenceValue = uiRefs.PageText;
        serializedController.FindProperty("selectedText").objectReferenceValue = uiRefs.SelectedText;
        serializedController.FindProperty("previousButton").objectReferenceValue = uiRefs.PreviousButton;
        serializedController.FindProperty("nextButton").objectReferenceValue = uiRefs.NextButton;
        serializedController.FindProperty("selectButton").objectReferenceValue = uiRefs.SelectButton;
        serializedController.FindProperty("selectionPanel").objectReferenceValue = uiRefs.SelectionPanel;
        serializedController.FindProperty("previewRoot").objectReferenceValue = previewModels.Count > 0 ? previewModels[0].transform.parent.gameObject : null;
        serializedController.FindProperty("selectablePageCount").intValue = 2;
        serializedController.FindProperty("currentIndex").intValue = 0;

        SerializedProperty pagesProperty = serializedController.FindProperty("pages");
        pagesProperty.arraySize = CharacterData.Length;

        for (int i = 0; i < CharacterData.Length; i++)
        {
            SerializedProperty pageProperty = pagesProperty.GetArrayElementAtIndex(i);
            pageProperty.FindPropertyRelative("displayName").stringValue = CharacterData[i].DisplayName;
            pageProperty.FindPropertyRelative("description").stringValue = CharacterData[i].Description;
            pageProperty.FindPropertyRelative("model").objectReferenceValue = i < previewModels.Count ? previewModels[i] : null;
            pageProperty.FindPropertyRelative("animatorController").objectReferenceValue =
                string.IsNullOrEmpty(CharacterData[i].ControllerPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CharacterData[i].ControllerPath);
        }

        serializedController.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(controller);
    }

    private static bool RefreshExistingControllerReferences()
    {
        CharacterSelectionController controller = Object.FindFirstObjectByType<CharacterSelectionController>();
        if (controller == null)
        {
            return false;
        }

        SerializedObject serializedController = new SerializedObject(controller);
        bool changed = false;

        changed |= SetObjectReference(serializedController.FindProperty("selectionPanel"), GameObject.Find(PanelName));
        changed |= SetObjectReference(serializedController.FindProperty("previewRoot"), GameObject.Find(PreviewRootName));

        SerializedProperty selectablePageCountProperty = serializedController.FindProperty("selectablePageCount");
        if (selectablePageCountProperty.intValue != 2)
        {
            selectablePageCountProperty.intValue = 2;
            changed = true;
        }

        SerializedProperty pagesProperty = serializedController.FindProperty("pages");
        if (pagesProperty != null)
        {
            int count = Mathf.Min(pagesProperty.arraySize, CharacterData.Length);
            for (int i = 0; i < count; i++)
            {
                RuntimeAnimatorController animatorController = string.IsNullOrEmpty(CharacterData[i].ControllerPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CharacterData[i].ControllerPath);

                SerializedProperty animatorControllerProperty = pagesProperty
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative("animatorController");

                changed |= SetObjectReference(animatorControllerProperty, animatorController);

                SerializedProperty modelProperty = pagesProperty
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative("model");
                GameObject previewModel = modelProperty != null ? modelProperty.objectReferenceValue as GameObject : null;
                if (previewModel != null)
                {
                    changed |= EnsurePreviewCollider(previewModel);
                }
            }
        }

        if (changed)
        {
            serializedController.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
        }

        return changed;
    }

    private static bool EnsurePreviewCollider(GameObject player)
    {
        CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
        bool changed = false;
        if (capsule == null)
        {
            capsule = player.AddComponent<CapsuleCollider>();
            changed = true;
        }

        changed |= SetCapsule(capsule, 1, PreviewColliderCenter, PreviewColliderHeight, PreviewColliderRadius);
        if (changed)
        {
            EditorUtility.SetDirty(player);
        }

        return changed;
    }

    private static bool SetCapsule(CapsuleCollider capsule, int direction, Vector3 center, float height, float radius)
    {
        bool changed = false;

        if (capsule.direction != direction)
        {
            capsule.direction = direction;
            changed = true;
        }

        if (capsule.center != center)
        {
            capsule.center = center;
            changed = true;
        }

        if (!Mathf.Approximately(capsule.height, height))
        {
            capsule.height = height;
            changed = true;
        }

        if (!Mathf.Approximately(capsule.radius, radius))
        {
            capsule.radius = radius;
            changed = true;
        }

        if (capsule.isTrigger)
        {
            capsule.isTrigger = false;
            changed = true;
        }

        return changed;
    }

    private static bool SetObjectReference(SerializedProperty property, Object value)
    {
        if (property == null || property.objectReferenceValue == value)
        {
            return false;
        }

        property.objectReferenceValue = value;
        return true;
    }

    private static Button CreateButton(Transform parent, string name, string label, TMP_FontAsset fontAsset, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject buttonObject = CreateUiObject(name, parent);
        SetCenteredRect(buttonObject.GetComponent<RectTransform>(), size, anchoredPosition);

        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.92f, 0.68f, 0.28f, 1f);

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;

        ColorBlock colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = new Color(1f, 0.82f, 0.42f, 1f);
        colors.pressedColor = new Color(0.78f, 0.48f, 0.18f, 1f);
        colors.disabledColor = new Color(0.32f, 0.28f, 0.22f, 0.75f);
        button.colors = colors;

        TextMeshProUGUI labelText = CreateText(buttonObject.transform, "Label", fontAsset, 23, Color.black, TextAlignmentOptions.Center);
        Stretch(labelText.rectTransform);
        labelText.text = label;

        return button;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, TMP_FontAsset fontAsset, float fontSize, Color color, TextAlignmentOptions alignment)
    {
        GameObject textObject = CreateUiObject(name, parent);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = fontAsset;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject uiObject = new GameObject(name, typeof(RectTransform));
        uiObject.transform.SetParent(parent, false);
        SetLayerRecursively(uiObject, LayerMask.NameToLayer("UI"));
        return uiObject;
    }

    private static void FitModelOnSlot(GameObject model, Transform slot, float targetHeight)
    {
        Bounds bounds;
        if (!TryGetBounds(model, out bounds) || bounds.size.y <= 0.001f)
        {
            return;
        }

        float scaleFactor = targetHeight / bounds.size.y;
        model.transform.localScale *= scaleFactor;

        if (!TryGetBounds(model, out bounds))
        {
            return;
        }

        Vector3 targetPoint = slot.position;
        Vector3 boundsBaseCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        model.transform.position += targetPoint - boundsBaseCenter;
    }

    private static bool TryGetBounds(GameObject target, out Bounds bounds)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return true;
    }

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }
    }

    private static void SetCenteredRect(RectTransform rectTransform, Vector2 size, Vector2 anchoredPosition)
    {
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = size;
        rectTransform.anchoredPosition = anchoredPosition;
    }

    private static void Stretch(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        if (layer < 0)
        {
            return;
        }

        target.layer = layer;
        foreach (Transform child in target.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private readonly struct CharacterPrototypeData
    {
        public CharacterPrototypeData(string displayName, string description, string assetPath, string controllerPath)
        {
            DisplayName = displayName;
            Description = description;
            AssetPath = assetPath;
            ControllerPath = controllerPath;
        }

        public string DisplayName { get; }
        public string Description { get; }
        public string AssetPath { get; }
        public string ControllerPath { get; }
    }

    private readonly struct PrototypeUiRefs
    {
        public PrototypeUiRefs(
            GameObject selectionPanel,
            TMP_Text titleText,
            TMP_Text descriptionText,
            TMP_Text pageText,
            TMP_Text selectedText,
            Button previousButton,
            Button nextButton,
            Button selectButton)
        {
            SelectionPanel = selectionPanel;
            TitleText = titleText;
            DescriptionText = descriptionText;
            PageText = pageText;
            SelectedText = selectedText;
            PreviousButton = previousButton;
            NextButton = nextButton;
            SelectButton = selectButton;
        }

        public GameObject SelectionPanel { get; }
        public TMP_Text TitleText { get; }
        public TMP_Text DescriptionText { get; }
        public TMP_Text PageText { get; }
        public TMP_Text SelectedText { get; }
        public Button PreviousButton { get; }
        public Button NextButton { get; }
        public Button SelectButton { get; }
    }
}
