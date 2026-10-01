using Fusion;
using Fusion.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class FusionMultiplayerPrototypeBuilder
{
    private const string BigHallScenePath = "Assets/Scenes/BigHall.unity";
    private const string PlayerPrefabPath = "Assets/Prefabs/FusionDuckPlayer.prefab";
    private const string YellowDuckPath = "Assets/people/1/00991177a1.fbx";
    private const string BlueDuckPath = "Assets/people/2/0917a2.fbx";
    private const string AnimatorControllerPath = "Assets/people/1/Meshy_AI_11_biped_Animation_Walking_withSkin.controller";
    private const string NameplateFontPath = ChineseUiFontSetup.DynamicFontPath;
    private const string ConnectionPanelTexturePath = "Assets/UI/ed270f38-b350-40a7-87f1-84d59310bf59.png";
    private const string ConnectionButtonTexturePath = "Assets/UI/0484152f-c56c-4f00-b1cb-cc826bfc048f.png";
    private const string LauncherName = "Fusion Session Launcher";
    private const string PanelName = "Fusion Connection Panel";
    private const string ColorPlatformName = "Multiplayer Color Platform";
    private const string ReadyBoardAnchorName = "Lobby Ready Board Anchor";
    private const string ReadyBoardCanvasName = "Lobby Ready Board Canvas";
    private const string ColorPadMaterialFolder = "Assets/Materials/FusionColorPads";
    private const float PlayerHeight = 2.1f;
    private const float PlayerColliderHeight = 3.077536f;
    private const float PlayerColliderRadius = 0.58f;
    private const float PlayerColliderCenterY = 1.785727f;
    private static readonly Vector3 ColorPlatformPosition = new Vector3(15.08f, 1.2f, 4.5f);
    private static readonly Vector3 ReadyBoardAnchorPosition = new Vector3(15.08f, 3.45f, 8.5f);

    static FusionMultiplayerPrototypeBuilder()
    {
        EditorApplication.update += TryAutoSetupOnEditorUpdate;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.delayCall += EnsureReadyBoardCanvasInBigHall;
    }

    [MenuItem("Duck/Setup Fusion Multiplayer Prototype")]
    public static void SetupFusionMultiplayerPrototype()
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.path != BigHallScenePath)
        {
            scene = EditorSceneManager.OpenScene(BigHallScenePath, OpenSceneMode.Single);
        }

        BuildScene(scene);
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

        FusionSessionLauncher existingLauncher = Object.FindFirstObjectByType<FusionSessionLauncher>();
        if (existingLauncher != null && existingLauncher.SetupVersion >= FusionSessionLauncher.CurrentSetupVersion)
        {
            return;
        }

        BuildScene(scene);
    }

    private static void TryAutoSetupOnEditorUpdate()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            return;
        }

        Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.path != BigHallScenePath)
        {
            return;
        }

        EditorApplication.update -= TryAutoSetupOnEditorUpdate;
        AutoSetupIfBigHallIsOpen();
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.path == BigHallScenePath)
        {
            EditorApplication.delayCall += AutoSetupIfBigHallIsOpen;
        }
    }

    private static void BuildScene(Scene scene)
    {
        CharacterSelectionController selection = Object.FindFirstObjectByType<CharacterSelectionController>();
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (selection == null || canvas == null)
        {
            Debug.LogError("Character selection and its Canvas must exist before Fusion setup.");
            return;
        }

        NetworkObject playerPrefab = EnsurePlayerPrefab(selection);
        EnsureSinglePlayerNameplates(selection);
        FusionSessionLauncher launcher = EnsureLauncher();
        ConnectionPanelRefs panel = BuildConnectionPanel(canvas.transform);
        GameObject colorPlatform = BuildColorPlatform();
        BuildReadyBoardAnchor();
        BuildReadyBoardCanvas();
        launcher.Configure(
            selection,
            playerPrefab,
            panel.Root,
            panel.PlayerNameInput,
            panel.RoomNameInput,
            panel.StatusText,
            panel.SinglePlayerButton,
            panel.JoinRoomButton,
            colorPlatform);

        EditorUtility.SetDirty(launcher);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Fusion Shared Mode prototype is ready.");
    }

    private static void EnsureReadyBoardCanvasInBigHall()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        Scene previousActiveScene = EditorSceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(BigHallScenePath);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        if (!wasLoaded)
        {
            scene = EditorSceneManager.OpenScene(BigHallScenePath, OpenSceneMode.Additive);
        }

        EditorSceneManager.SetActiveScene(scene);
        bool changed = BuildReadyBoardAnchor() | BuildReadyBoardCanvas();
        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        if (!wasLoaded)
        {
            EditorSceneManager.CloseScene(scene, true);
        }

        if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
        {
            EditorSceneManager.SetActiveScene(previousActiveScene);
        }
    }

    private static bool BuildReadyBoardAnchor()
    {
        GameObject anchor = FindLoadedSceneObject(ReadyBoardAnchorName);
        if (anchor == null)
        {
            anchor = new GameObject(ReadyBoardAnchorName);
            // First-time default. After this, designers may place the anchor
            // on any wall without a future setup pass overwriting it.
            anchor.transform.SetPositionAndRotation(ReadyBoardAnchorPosition, Quaternion.Euler(0f, 180f, 0f));
            anchor.transform.localScale = Vector3.one;
            EditorUtility.SetDirty(anchor);
            return true;
        }

        return false;
    }

    private static bool BuildReadyBoardCanvas()
    {
        // This is intentionally a scene Canvas, not a runtime-made UI object.
        // Its transform and child layout stay editable in BigHall's Hierarchy.
        GameObject existingBoard = FindLoadedSceneObject(ReadyBoardCanvasName);
        if (existingBoard != null)
        {
            // Migrate the first purple prototype board once.  Once the wooden
            // parchment frame exists, keep the scene object untouched so its
            // placement and any designer adjustments remain editable.
            if (existingBoard.transform.Find("Ready Card/Wood Parchment Frame") != null)
            {
                return false;
            }

            Object.DestroyImmediate(existingBoard);
        }

        GameObject anchor = FindLoadedSceneObject(ReadyBoardAnchorName);
        GameObject board = new GameObject(
            ReadyBoardCanvasName,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        if (anchor != null)
        {
            board.transform.SetPositionAndRotation(anchor.transform.position, anchor.transform.rotation);
        }
        else
        {
            board.transform.SetPositionAndRotation(ReadyBoardAnchorPosition, Quaternion.Euler(0f, 180f, 0f));
        }

        board.transform.localScale = Vector3.one * 0.0095f;
        Canvas canvas = board.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        CanvasScaler scaler = board.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        TMP_FontAsset font = ChineseUiFontSetup.GetChineseFont() ?? TMP_Settings.defaultFontAsset;
        GameObject card = CreateUiObject("Ready Card", board.transform);
        SetCenteredRect(card.GetComponent<RectTransform>(), new Vector2(860f, 520f), Vector2.zero);

        GameObject frame = CreateUiObject("Wood Parchment Frame", card.transform);
        Stretch(frame.GetComponent<RectTransform>());
        RawImage frameImage = frame.AddComponent<RawImage>();
        frameImage.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ConnectionPanelTexturePath);
        frameImage.color = Color.white;
        frameImage.raycastTarget = false;

        Color titleColor = new Color(0.29f, 0.15f, 0.065f, 1f);
        Color labelColor = new Color(0.38f, 0.23f, 0.11f, 1f);
        TextMeshProUGUI title = CreateText(frame.transform, "Title", "多人房間・準備看板", font, 40, titleColor);
        title.fontStyle = FontStyles.Bold;
        SetCenteredRect(title.rectTransform, new Vector2(560f, 58f), new Vector2(-70f, 160f));

        TextMeshProUGUI subtitle = CreateText(frame.transform, "Subtitle", "集結小鴨，整備完畢後一起出發", font, 18, labelColor);
        SetCenteredRect(subtitle.rectTransform, new Vector2(590f, 34f), new Vector2(-70f, 112f));

        GameObject divider = CreateUiObject("Parchment Divider", frame.transform);
        SetCenteredRect(divider.GetComponent<RectTransform>(), new Vector2(520f, 3f), new Vector2(-70f, 82f));
        Image dividerImage = divider.AddComponent<Image>();
        dividerImage.color = new Color(0.52f, 0.31f, 0.14f, 0.5f);
        dividerImage.raycastTarget = false;

        TextMeshProUGUI statusLabel = CreateText(frame.transform, "Status Label", "隊伍準備狀態", font, 20, labelColor);
        statusLabel.fontStyle = FontStyles.Bold;
        SetCenteredRect(statusLabel.rectTransform, new Vector2(500f, 34f), new Vector2(-70f, 42f));

        TextMeshProUGUI status = CreateText(frame.transform, "Ready Status", "正在等待玩家…", font, 25, titleColor);
        SetCenteredRect(status.rectTransform, new Vector2(660f, 52f), new Vector2(-35f, -5f));

        Button readyButton = CreateButton(
            frame.transform,
            "Ready Button",
            "準備完成",
            font,
            new Vector2(-70f, -103f),
            new Vector2(390f, 76f));
        TMP_Text label = readyButton.transform.Find("Label")?.GetComponent<TMP_Text>();
        if (label != null)
        {
            label.fontSize = 27f;
        }

        TextMeshProUGUI footer = CreateText(frame.transform, "Footer", "全員準備後，房主將開啟前往第一關的傳送門", font, 16, labelColor);
        SetCenteredRect(footer.rectTransform, new Vector2(600f, 34f), new Vector2(-70f, -172f));

        // It activates only after a player joins a shared room.
        board.SetActive(false);
        EditorUtility.SetDirty(board);
        return true;
    }

    private static GameObject FindLoadedSceneObject(string objectName)
    {
        GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (GameObject candidate in objects)
        {
            if (candidate != null
                && candidate.name == objectName
                && candidate.scene.IsValid()
                && candidate.scene.isLoaded)
            {
                return candidate;
            }
        }

        return null;
    }

    private static NetworkObject EnsurePlayerPrefab(CharacterSelectionController selection)
    {
        EnsureFolder("Assets/Prefabs");
        GameObject root = new GameObject("FusionDuckPlayer");
        NetworkObject networkObject = root.AddComponent<NetworkObject>();
        // Each shared-room player owns and publishes only their own movement.
        // This lets a duck regain transform authority after the lobby scene is
        // unloaded instead of being corrected back by an old network snapshot.
        networkObject.Flags |= NetworkObjectFlags.AllowStateAuthorityOverride;
        NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
        // DuckMover owns the local dynamic Rigidbody.  Do not also let
        // NetworkTransform forecast that same physics body: the two systems
        // can apply competing corrections immediately after a scene load,
        // which looks like the player is being pulled or drifting.
        SerializedObject networkTransformSettings = new SerializedObject(networkTransform);
        SerializedProperty forecastEnabled = networkTransformSettings.FindProperty("PhysicsSettings.ForecastEnabled");
        if (forecastEnabled != null)
        {
            forecastEnabled.boolValue = false;
            networkTransformSettings.ApplyModifiedPropertiesWithoutUndo();
        }
        FusionDuckPlayer player = root.AddComponent<FusionDuckPlayer>();
        if (root.GetComponent<DuckMover>() == null)
        {
            root.AddComponent<DuckMover>();
        }

        if (root.GetComponent<DuckWandAttack>() == null)
        {
            root.AddComponent<DuckWandAttack>();
        }

        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
        ConfigurePlayerCollider(capsule);

        Rigidbody body = root.AddComponent<Rigidbody>();
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.constraints = RigidbodyConstraints.FreezeRotation;

        GameObject yellowPreview = GetCharacterPreview(selection, 0, out RuntimeAnimatorController yellowController);
        GameObject bluePreview = GetCharacterPreview(selection, 1, out RuntimeAnimatorController blueController);
        RuntimeAnimatorController fallbackController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimatorControllerPath);
        GameObject[] visuals =
        {
            CreateCharacterVisual(root.transform, "Yellow Duck Visual", yellowPreview, YellowDuckPath, yellowController ?? fallbackController),
            CreateCharacterVisual(root.transform, "Blue Duck Visual", bluePreview, BlueDuckPath, blueController ?? fallbackController)
        };
        visuals[0].SetActive(true);
        visuals[1].SetActive(false);
        player.ConfigureVisuals(
            visuals,
            new[] { FindBodyMaterialIndex(YellowDuckPath), FindBodyMaterialIndex(BlueDuckPath) });
        CreateNameplate(root.transform, AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(NameplateFontPath));

        PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.ImportAsset(PlayerPrefabPath, ImportAssetOptions.ForceUpdate);
        NetworkProjectConfigUtilities.RebuildPrefabTable();

        GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        return savedPrefab != null ? savedPrefab.GetComponent<NetworkObject>() : networkObject;
    }

    private static GameObject CreateCharacterVisual(
        Transform parent,
        string name,
        GameObject previewSlot,
        string modelPath,
        RuntimeAnimatorController animatorController)
    {
        GameObject visual = new GameObject(name);
        visual.transform.SetParent(parent, false);

        GameObject previewModel = FindPreviewModel(previewSlot);
        GameObject source = previewModel != null
            ? previewModel
            : AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (source == null)
        {
            Debug.LogError("Missing character model at " + modelPath);
            return visual;
        }

        GameObject model = previewModel != null
            ? Object.Instantiate(previewModel)
            : PrefabUtility.InstantiatePrefab(source) as GameObject;
        if (model == null)
        {
            return visual;
        }

        model.transform.SetParent(visual.transform, false);
        model.name = name + " Model";
        model.SetActive(true);

        if (previewModel != null)
        {
            // Preserve the exact model size, placement and material overrides shown in character select.
            model.transform.localPosition = previewModel.transform.localPosition;
            model.transform.localRotation = previewModel.transform.localRotation;
            model.transform.localScale = previewModel.transform.localScale;
        }
        else
        {
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            FitModelToHeight(model, PlayerHeight);
        }

        Animator animator = model.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            animator = model.AddComponent<Animator>();
        }

        animator.runtimeAnimatorController = animatorController;
        animator.applyRootMotion = false;
        return visual;
    }

    private static GameObject FindPreviewModel(GameObject previewSlot)
    {
        if (previewSlot == null)
        {
            return null;
        }

        for (int i = 0; i < previewSlot.transform.childCount; i++)
        {
            GameObject child = previewSlot.transform.GetChild(i).gameObject;
            if (child.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
            {
                return child;
            }
        }

        return null;
    }

    private static void EnsureSinglePlayerNameplates(CharacterSelectionController selection)
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(NameplateFontPath);
        for (int i = 0; i < 2; i++)
        {
            GameObject previewSlot = GetCharacterPreview(selection, i, out _);
            if (previewSlot == null)
            {
                continue;
            }

            CapsuleCollider capsule = previewSlot.GetComponent<CapsuleCollider>();
            capsule ??= previewSlot.AddComponent<CapsuleCollider>();
            ConfigurePlayerCollider(capsule);

            Transform existingLabel = previewSlot.transform.Find("Player Nameplate");
            if (existingLabel != null)
            {
                Object.DestroyImmediate(existingLabel.gameObject);
            }

            DuckPlayerNameplate existingNameplate = previewSlot.GetComponent<DuckPlayerNameplate>();
            if (existingNameplate != null)
            {
                Object.DestroyImmediate(existingNameplate);
            }

            CreateNameplate(previewSlot.transform, font);
        }
    }

    private static void CreateNameplate(Transform playerRoot, TMP_FontAsset font)
    {
        GameObject labelObject = new GameObject("Player Nameplate");
        labelObject.transform.SetParent(playerRoot, false);
        labelObject.transform.localPosition = Vector3.up * 2.45f;
        labelObject.transform.localScale = Vector3.one * 0.22f;

        TextMeshPro label = labelObject.AddComponent<TextMeshPro>();
        label.font = font;
        if (font != null)
        {
            label.fontSharedMaterial = font.material;
        }

        DuckPlayerNameplate nameplate = playerRoot.gameObject.GetComponent<DuckPlayerNameplate>();
        nameplate ??= playerRoot.gameObject.AddComponent<DuckPlayerNameplate>();
        nameplate.Configure(label);
        labelObject.SetActive(false);
    }

    private static void ConfigurePlayerCollider(CapsuleCollider capsule)
    {
        capsule.direction = 1;
        capsule.center = new Vector3(0f, PlayerColliderCenterY, 0f);
        capsule.height = PlayerColliderHeight;
        capsule.radius = PlayerColliderRadius;
        capsule.enabled = true;
        capsule.isTrigger = false;
    }

    private static GameObject GetCharacterPreview(
        CharacterSelectionController selection,
        int index,
        out RuntimeAnimatorController animatorController)
    {
        animatorController = null;
        if (selection == null)
        {
            return null;
        }

        SerializedObject serializedSelection = new SerializedObject(selection);
        SerializedProperty pages = serializedSelection.FindProperty("pages");
        if (pages == null || index < 0 || index >= pages.arraySize)
        {
            return null;
        }

        SerializedProperty page = pages.GetArrayElementAtIndex(index);
        animatorController = page.FindPropertyRelative("animatorController").objectReferenceValue as RuntimeAnimatorController;
        return page.FindPropertyRelative("model").objectReferenceValue as GameObject;
    }

    private static int FindBodyMaterialIndex(string modelPath)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        Renderer renderer = model != null ? model.GetComponentInChildren<Renderer>(true) : null;
        if (renderer == null)
        {
            return -1;
        }

        Material[] materials = renderer.sharedMaterials;
        for (int i = 0; i < materials.Length; i++)
        {
            string materialName = materials[i] != null ? materials[i].name : string.Empty;
            if (materialName.Equals("body", System.StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        Debug.LogWarning("No body material was found in " + modelPath + ". Skin color changes are disabled for this model.");
        return -1;
    }

    private static void FitModelToHeight(GameObject model, float targetHeight)
    {
        if (!TryGetBounds(model, out Bounds bounds) || bounds.size.y <= 0.001f)
        {
            return;
        }

        model.transform.localScale *= targetHeight / bounds.size.y;
        if (TryGetBounds(model, out bounds))
        {
            model.transform.position -= Vector3.up * bounds.min.y;
        }
    }

    private static bool TryGetBounds(GameObject target, out Bounds bounds)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        bounds = new Bounds();
        bool hasBounds = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (!hasBounds)
            {
                bounds = renderers[i].bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }

        return hasBounds;
    }

    private static FusionSessionLauncher EnsureLauncher()
    {
        GameObject launcherObject = GameObject.Find(LauncherName);
        if (launcherObject == null)
        {
            launcherObject = new GameObject(LauncherName);
        }

        FusionSessionLauncher launcher = launcherObject.GetComponent<FusionSessionLauncher>();
        return launcher != null ? launcher : launcherObject.AddComponent<FusionSessionLauncher>();
    }

    private static GameObject BuildColorPlatform()
    {
        Scene activeScene = EditorSceneManager.GetActiveScene();
        GameObject[] rootObjects = activeScene.GetRootGameObjects();
        Vector3 platformPosition = ColorPlatformPosition;
        Quaternion platformRotation = Quaternion.identity;
        Vector3 platformScale = Vector3.one;
        bool preservedTransform = false;
        for (int i = 0; i < rootObjects.Length; i++)
        {
            GameObject rootObject = rootObjects[i];
            if (rootObject.name == ColorPlatformName)
            {
                if (!preservedTransform)
                {
                    platformPosition = rootObject.transform.position;
                    platformRotation = rootObject.transform.rotation;
                    platformScale = rootObject.transform.localScale;
                    preservedTransform = true;
                }

                Object.DestroyImmediate(rootObject);
            }
        }

        EnsureFolder(ColorPadMaterialFolder);

        GameObject platform = new GameObject(ColorPlatformName);
        platform.transform.SetPositionAndRotation(platformPosition, platformRotation);
        platform.transform.localScale = platformScale;

        GameObject baseObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseObject.name = "Color Platform Base";
        baseObject.transform.SetParent(platform.transform, false);
        baseObject.transform.localScale = new Vector3(9.5f, 0.4f, 3.6f);
        baseObject.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial(
            ColorPadMaterialFolder + "/PlatformBase.mat",
            new Color(0.08f, 0.1f, 0.16f));

        string[] padNames = { "Original", "Red", "Blue", "Green", "Pink", "Purple" };
        float spacing = 1.45f;
        float startX = -spacing * (padNames.Length - 1) * 0.5f;

        for (int i = 0; i < padNames.Length; i++)
        {
            GameObject pad = new GameObject(padNames[i] + " Color Pad");
            pad.transform.SetParent(platform.transform, false);
            pad.transform.localPosition = new Vector3(startX + spacing * i, 0.2f, 0f);

            BoxCollider trigger = pad.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0.85f, 0f);
            trigger.size = new Vector3(1.25f, 1.7f, 2.8f);
            FusionColorPad colorPad = pad.AddComponent<FusionColorPad>();
            colorPad.Configure(i);

            GameObject padVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            padVisual.name = "Visual";
            padVisual.transform.SetParent(pad.transform, false);
            padVisual.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            padVisual.transform.localScale = new Vector3(1.25f, 0.08f, 2.8f);
            Object.DestroyImmediate(padVisual.GetComponent<Collider>());
            padVisual.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial(
                ColorPadMaterialFolder + "/" + padNames[i] + ".mat",
                FusionDuckPlayer.GetSkinColor(i));
        }

        CreateColorPlatformInstructions(platform.transform);

        platform.SetActive(false);
        return platform;
    }

    private static void CreateColorPlatformInstructions(Transform platform)
    {
        GameObject instructions = new GameObject("Color Instructions");
        instructions.transform.SetParent(platform, false);
        instructions.transform.localPosition = new Vector3(0f, 1.55f, 2.1f);
        instructions.transform.localRotation = Quaternion.identity;

        GameObject background = GameObject.CreatePrimitive(PrimitiveType.Cube);
        background.name = "Background";
        background.transform.SetParent(instructions.transform, false);
        background.transform.localScale = new Vector3(8.6f, 1.15f, 0.08f);
        Object.DestroyImmediate(background.GetComponent<Collider>());
        background.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial(
            ColorPadMaterialFolder + "/InstructionsBackground.mat",
            new Color(0.035f, 0.05f, 0.08f));

        GameObject textObject = new GameObject("Instructions Text");
        textObject.transform.SetParent(instructions.transform, false);
        textObject.transform.localPosition = new Vector3(0f, 0f, -0.055f);

        TextMeshPro text = textObject.AddComponent<TextMeshPro>();
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(NameplateFontPath);
        text.font = font;
        if (font != null)
        {
            text.fontSharedMaterial = font.material;
        }

        text.text = "踩色板更換身體顏色　｜　白色恢復原色";
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 3.2f;
        text.color = Color.white;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.rectTransform.sizeDelta = new Vector2(8.2f, 1f);
        text.raycastTarget = false;
    }

    private static Material GetOrCreateMaterial(string path, Color color)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.28f);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static ConnectionPanelRefs BuildConnectionPanel(Transform canvasTransform)
    {
        Transform existingPanel = canvasTransform.Find(PanelName);
        if (existingPanel != null)
        {
            Object.DestroyImmediate(existingPanel.gameObject);
        }

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(NameplateFontPath);
        Texture2D panelTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(ConnectionPanelTexturePath);
        GameObject panel = CreateUiObject(PanelName, canvasTransform);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        Stretch(panelRect);
        Image dimmer = panel.AddComponent<Image>();
        dimmer.color = new Color(0.035f, 0.025f, 0.015f, 0.56f);
        dimmer.raycastTarget = true;

        GameObject frame = CreateUiObject("Wood Parchment Frame", panel.transform);
        SetCenteredRect(frame.GetComponent<RectTransform>(), new Vector2(860f, 520f), Vector2.zero);
        RawImage frameImage = frame.AddComponent<RawImage>();
        frameImage.texture = panelTexture;
        frameImage.color = Color.white;
        frameImage.raycastTarget = false;

        Color titleColor = new Color(0.29f, 0.15f, 0.065f, 1f);
        Color labelColor = new Color(0.36f, 0.21f, 0.1f, 1f);
        TextMeshProUGUI title = CreateText(frame.transform, "Title", "開始遊戲", font, 40, titleColor);
        title.fontStyle = FontStyles.Bold;
        SetCenteredRect(title.rectTransform, new Vector2(620f, 58f), new Vector2(0f, 188f));

        TextMeshProUGUI help = CreateText(frame.transform, "Help", "輸入玩家名稱；多人遊戲請使用相同房間名稱", font, 19, labelColor);
        SetCenteredRect(help.rectTransform, new Vector2(650f, 38f), new Vector2(0f, 143f));

        TextMeshProUGUI nameLabel = CreateText(frame.transform, "Player Name Label", "玩家名稱", font, 19, labelColor);
        nameLabel.alignment = TextAlignmentOptions.MidlineLeft;
        SetCenteredRect(nameLabel.rectTransform, new Vector2(520f, 28f), new Vector2(0f, 101f));
        TMP_InputField playerNameInput = CreateTextInput(frame.transform, "Player Name Input", "輸入名字", string.Empty, font, new Vector2(0f, 64f), 16);

        TextMeshProUGUI roomLabel = CreateText(frame.transform, "Room Name Label", "多人房間名稱", font, 19, labelColor);
        roomLabel.alignment = TextAlignmentOptions.MidlineLeft;
        SetCenteredRect(roomLabel.rectTransform, new Vector2(520f, 28f), new Vector2(0f, 18f));
        TMP_InputField roomInput = CreateTextInput(frame.transform, "Room Name Input", "輸入或建立房間名稱", "魔法大亂鬥房間", font, new Vector2(0f, -19f), 32);

        Button joinButton = CreateButton(frame.transform, "Join Shared Room Button", "建立／加入房間", font, new Vector2(-150f, -104f), new Vector2(280f, 64f));
        Button singleButton = CreateButton(frame.transform, "Play Single Player Button", "單人開始", font, new Vector2(150f, -104f), new Vector2(240f, 64f));

        TextMeshProUGUI status = CreateText(frame.transform, "Connection Status", string.Empty, font, 18, new Color(0.65f, 0.18f, 0.08f, 1f));
        status.fontStyle = FontStyles.Bold;
        SetCenteredRect(status.rectTransform, new Vector2(650f, 42f), new Vector2(0f, -169f));
        panel.SetActive(false);

        return new ConnectionPanelRefs(panel, playerNameInput, roomInput, status, singleButton, joinButton);
    }

    private static TMP_InputField CreateTextInput(
        Transform parent,
        string objectName,
        string placeholderValue,
        string initialValue,
        TMP_FontAsset font,
        Vector2 position,
        int characterLimit)
    {
        GameObject inputObject = CreateUiObject(objectName, parent);
        SetCenteredRect(inputObject.GetComponent<RectTransform>(), new Vector2(520f, 52f), position);
        Image background = inputObject.AddComponent<Image>();
        background.color = new Color(1f, 0.965f, 0.86f, 1f);
        Outline border = inputObject.AddComponent<Outline>();
        border.effectColor = new Color(0.43f, 0.25f, 0.11f, 0.95f);
        border.effectDistance = new Vector2(2f, -2f);

        TMP_InputField input = inputObject.AddComponent<TMP_InputField>();
        GameObject viewport = CreateUiObject("Text Area", inputObject.transform);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect, 16f, 16f, 6f, 6f);
        viewport.AddComponent<RectMask2D>();

        TextMeshProUGUI text = CreateText(viewport.transform, "Text", string.Empty, font, 22, new Color(0.24f, 0.13f, 0.06f, 1f));
        text.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch(text.rectTransform);

        TextMeshProUGUI placeholder = CreateText(viewport.transform, "Placeholder", placeholderValue, font, 22, new Color(0.46f, 0.34f, 0.23f, 0.65f));
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        placeholder.fontStyle = FontStyles.Italic;
        Stretch(placeholder.rectTransform);

        input.textViewport = viewportRect;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.text = initialValue;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = characterLimit;
        return input;
    }

    private static Button CreateButton(Transform parent, string name, string label, TMP_FontAsset font, Vector2 position, Vector2 size)
    {
        GameObject buttonObject = CreateUiObject(name, parent);
        SetCenteredRect(buttonObject.GetComponent<RectTransform>(), size, position);
        RawImage image = buttonObject.AddComponent<RawImage>();
        image.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ConnectionButtonTexturePath);
        image.color = Color.white;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.87f, 0.62f, 1f);
        colors.pressedColor = new Color(0.86f, 0.66f, 0.4f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.55f, 0.48f, 0.4f, 0.72f);
        button.colors = colors;

        TextMeshProUGUI text = CreateText(buttonObject.transform, "Label", label, font, 22, new Color(0.28f, 0.14f, 0.055f, 1f));
        text.fontStyle = FontStyles.Bold;
        Stretch(text.rectTransform);
        return button;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, string value, TMP_FontAsset font, float fontSize, Color color)
    {
        GameObject textObject = CreateUiObject(name, parent);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.text = value;
        return text;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject uiObject = new GameObject(name, typeof(RectTransform));
        uiObject.transform.SetParent(parent, false);
        SetLayerRecursively(uiObject, LayerMask.NameToLayer("UI"));
        return uiObject;
    }

    private static void SetCenteredRect(RectTransform rect, Vector2 size, Vector2 position)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float bottom = 0f, float top = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        foreach (Transform child in target.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }

    private readonly struct ConnectionPanelRefs
    {
        public readonly GameObject Root;
        public readonly TMP_InputField PlayerNameInput;
        public readonly TMP_InputField RoomNameInput;
        public readonly TMP_Text StatusText;
        public readonly Button SinglePlayerButton;
        public readonly Button JoinRoomButton;

        public ConnectionPanelRefs(
            GameObject root,
            TMP_InputField playerNameInput,
            TMP_InputField roomNameInput,
            TMP_Text statusText,
            Button singlePlayerButton,
            Button joinRoomButton)
        {
            Root = root;
            PlayerNameInput = playerNameInput;
            RoomNameInput = roomNameInput;
            StatusText = statusText;
            SinglePlayerButton = singlePlayerButton;
            JoinRoomButton = joinRoomButton;
        }
    }
}
