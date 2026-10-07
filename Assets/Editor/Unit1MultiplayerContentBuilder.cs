using Fusion;
using Fusion.Editor;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Writes the multiplayer-ready first-level content into the real scenes.
/// It deliberately leaves the map's existing Duck1/Duck2/Duck3 objects alone:
/// those are map assets, not the network players or enemy template.
/// </summary>
[InitializeOnLoad]
public static class Unit1MultiplayerContentBuilder
{
    private const string Unit1ScenePath = "Assets/Scenes/UNIT1.unity";
    private const string EnemyPrefabPath = "Assets/Prefabs/GreyShadowDuck.prefab";
    private const string PlayerPrefabPath = "Assets/Prefabs/FusionDuckPlayer.prefab";
    private const string CpuDuckPrefabPath = "Assets/Prefabs/Unit1CpuDuck.prefab";
    private const string FactionCrystalPrefabPath = "Assets/Prefabs/FactionCrystal.prefab";
    private const string YellowPlayerVisualName = "Yellow Duck Visual";
    private const string BluePlayerVisualName = "Blue Duck Visual";
    // Fall back to these raw model assets only if the player prefab has not
    // yet been generated. Normal builds clone the player prefab visuals so
    // the enemy exactly matches the lobby character's scale and placement.
    private const string StableDuckOnePath = "Assets/people/1/00991177a1.fbx";
    private const string StableDuckTwoPath = "Assets/people/2/0917a2.fbx";
    private const string AnimatorControllerPath = "Assets/people/1/Meshy_AI_11_biped_Animation_Walking_withSkin.controller";
    private const string BodyMaterialPath = "Assets/Materials/Characters/GreyShadowDuckBody.mat";
    private const string RuneMaterialPath = "Assets/Materials/Characters/GreyShadowDuckRune.mat";
    private const string WeaponOnePath = "Assets/WEAPON/magic/weapon1.fbx";
    private const string BlueWeaponPath = "Assets/WEAPON/blue/blue.fbx";
    private const string PurpleWeaponPath = "Assets/WEAPON/purplr/purple.fbx";
    private const string WeaponOneMaterialPath = "Assets/WEAPON/magic/Materials/Meshy_AI_Star_Duckling_Wand_0901073553_texture.mat";
    private const string BlueWeaponMaterialPath = "Assets/WEAPON/blue/Materials/Meshy_AI_Crystal_Scepter_0914113032_texture.mat";
    private const string PurpleWeaponMaterialPath = "Assets/WEAPON/purplr/Materials/Meshy_AI_Violet_Voidstaff_0914113010_texture.mat";
    private const string ContentRootName = "UNIT1 Multiplayer Game Content";
    private const string DirectorName = "UNIT1 Game Director";
    private const string AutorunMarkerPath = "Assets/Editor/Unit1MultiplayerContent.autorun";
    private const float PlayerColliderHeight = 3.077536f;
    private const float PlayerColliderRadius = 0.58f;
    private const float PlayerColliderCenterY = 1.785727f;

    private readonly struct CrystalSpawnPointPose
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public CrystalSpawnPointPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    private static readonly Vector3[] PlayerPositions =
    {
        // These are centres of paved road pieces within the walled map—not
        // building footprints or the empty area outside the gate.
        new(0.58f, -0.107f, 15.29f), new(6.28f, -0.107f, 15.29f),
        new(11.97f, -0.107f, 15.29f), new(17.67f, -0.107f, 15.29f),
        new(23.37f, -0.107f, 15.29f), new(29.07f, -0.107f, 15.29f),
    };

    private static readonly Vector3[] EnemyPositions =
    {
        // Reference approaches for the director's random, flat, connected
        // spawn search. These are not six fixed enemy starting positions.
        new(1.05f, -0.334f, 26.00f), new(9.05f, -0.334f, 33.00f),
        new(10.05f, -0.334f, 21.00f), new(23.05f, -0.334f, 21.00f),
        new(24.05f, -0.334f, 32.00f), new(32.05f, -0.334f, 26.00f),
    };

    private static readonly Vector3[] FactionCrystalPositions =
    {
        // One base per physical map corner: north-west, north-east, then
        // south-east. These are the actual playable-map corners, not merely
        // points offset from the central routes. This keeps the crystals out
        // of the central
        // routes and makes travelling between bases meaningfully cross-map.
        new(1.35f, -0.11f, 36.60f),
        new(31.95f, -0.11f, 36.60f),
        new(31.95f, -0.11f, 15.50f),
    };

    private static readonly WandDefinition[] Wands =
    {
        // Keep the original six pickups, then spread one of each ability
        // further across the connected map for the six-shadow population.
        new("星光快攻杖 1", WeaponOnePath, WeaponOneMaterialPath, Unit1WandAbility.Swift, new Vector3(1.05f, 1.10f, 19.00f)),
        new("水晶守護杖 1", BlueWeaponPath, BlueWeaponMaterialPath, Unit1WandAbility.Guardian, new Vector3(16.55f, 1.10f, 19.00f)),
        new("虛空重擊杖 1", PurpleWeaponPath, PurpleWeaponMaterialPath, Unit1WandAbility.Heavy, new Vector3(32.05f, 1.10f, 19.00f)),
        new("星光快攻杖 2", WeaponOnePath, WeaponOneMaterialPath, Unit1WandAbility.Swift, new Vector3(1.05f, 1.10f, 33.00f)),
        new("水晶守護杖 2", BlueWeaponPath, BlueWeaponMaterialPath, Unit1WandAbility.Guardian, new Vector3(16.55f, 1.10f, 28.00f)),
        new("虛空重擊杖 2", PurpleWeaponPath, PurpleWeaponMaterialPath, Unit1WandAbility.Heavy, new Vector3(32.05f, 1.10f, 33.00f)),
        new("星光快攻杖 3", WeaponOnePath, WeaponOneMaterialPath, Unit1WandAbility.Swift, new Vector3(1.51f, 1.10f, 54.17f)),
        new("虛空重擊杖 3", PurpleWeaponPath, PurpleWeaponMaterialPath, Unit1WandAbility.Heavy, new Vector3(43.34f, 1.10f, 47.48f)),
        new("水晶守護杖 3", BlueWeaponPath, BlueWeaponMaterialPath, Unit1WandAbility.Guardian, new Vector3(26.61f, 1.10f, 94.32f)),
    };

    static Unit1MultiplayerContentBuilder()
    {
        EditorApplication.delayCall += BuildRequestedContent;
    }

    private static void BuildRequestedContent()
    {
        if (!File.Exists(AutorunMarkerPath))
        {
            return;
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.update -= BuildRequestedContentWhenIdle;
            EditorApplication.update += BuildRequestedContentWhenIdle;
            return;
        }

        AssetDatabase.DeleteAsset(AutorunMarkerPath);
        BuildAll();
    }

    private static void BuildRequestedContentWhenIdle()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        EditorApplication.update -= BuildRequestedContentWhenIdle;
        BuildRequestedContent();
    }

    public static void BuildAll()
    {
        Scene original = EditorSceneManager.GetActiveScene();
        FusionMultiplayerPrototypeBuilder.SetupFusionMultiplayerPrototype();
        Scene unit1 = EditorSceneManager.OpenScene(Unit1ScenePath, OpenSceneMode.Single);
        BuildUnit1(unit1);

        if (original.IsValid() && !string.IsNullOrEmpty(original.path) && original.path != Unit1ScenePath)
        {
            EditorSceneManager.OpenScene(original.path, OpenSceneMode.Single);
        }
    }

    /// <summary>Entry point for Unity batch mode, if it is ever needed.</summary>
    public static void BuildAllFromBatchMode()
    {
        BuildAll();
    }

    // A focused, idempotent upgrade for the existing authored scene. Never
    // run BuildAll here: it recreates the map's content and spawn markers.
    [MenuItem("Tools/UNIT1 Content/Apply 6 Shadows and 9 Wands")]
    public static void ApplySixShadowsAndNineWands()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new System.InvalidOperationException("Run after compilation in edit mode.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new System.InvalidOperationException("Unsaved scene changes: content upgrade cancelled.");
        for (int i = 6; i < Wands.Length; i++)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Wands[i].AssetPath) == null
                || AssetDatabase.LoadAssetAtPath<Material>(Wands[i].MaterialPath) == null)
                throw new System.InvalidOperationException("Missing wand model or material: " + Wands[i].Name);

        Scene previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(Unit1ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(Unit1ScenePath, OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            GameObject root = FindSceneObject(scene, ContentRootName);
            Transform wandRoot = root != null ? root.transform.Find("Hand Wands - E to Pick Up") : null;
            GameObject directorObject = FindSceneObject(scene, DirectorName);
            Unit1GameDirector director = directorObject != null ? directorObject.GetComponent<Unit1GameDirector>() : null;
            if (wandRoot == null || director == null)
                throw new System.InvalidOperationException("Existing UNIT1 content missing; no scene saved.");
            for (int i = 0; i < 6; i++)
                if (wandRoot.Find(Wands[i].Name)?.GetComponent<Unit1WandPickup>() == null)
                    throw new System.InvalidOperationException("Original wand missing: " + Wands[i].Name);
            for (int i = 6; i < Wands.Length; i++)
            {
                Transform existing = wandRoot.Find(Wands[i].Name);
                if (existing != null && existing.GetComponent<Unit1WandPickup>() == null)
                    throw new System.InvalidOperationException("Name collision: " + Wands[i].Name);
            }

            var serializedDirector = new SerializedObject(director);
            serializedDirector.FindProperty("enemyCount").intValue = 6;
            serializedDirector.ApplyModifiedPropertiesWithoutUndo();
            int added = 0;
            for (int i = 6; i < Wands.Length; i++)
            {
                if (wandRoot.Find(Wands[i].Name) != null) continue;
                CreateWand(wandRoot, Wands[i]);
                added++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new System.InvalidOperationException("Could not save UNIT1 density upgrade.");
            Debug.Log("[DENSITY-SETUP] six Grey Shadows, nine wands (three per ability); added=" + added
                + ". Existing six wands, map, crystals and spawn markers preserved.");
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void BuildUnit1(Scene unit1)
    {
        NetworkObject enemyPrefab = CreateGreyShadowDuckPrefab();
        NetworkObject cpuDuckPrefab = CreateCpuDuckPrefab();
        NetworkObject factionCrystalPrefab = CreateFactionCrystalPrefab();

        GameObject oldRoot = FindSceneObject(unit1, ContentRootName);
        CrystalSpawnPointPose[] crystalSpawnPointPoses = CaptureCrystalSpawnPointPoses(oldRoot);
        if (oldRoot != null)
        {
            Object.DestroyImmediate(oldRoot);
        }

        GameObject root = new(ContentRootName);
        GameObject pointsRoot = new("Spawn Points");
        pointsRoot.transform.SetParent(root.transform, false);
        Transform[] playerPoints = CreatePoints(pointsRoot.transform, "Player Spawn", PlayerPositions, new Color(0.26f, 0.58f, 1f, 1f));
        Transform[] enemyPoints = CreatePoints(pointsRoot.transform, "Grey Shadow Spawn", EnemyPositions, new Color(0.65f, 0.32f, 1f, 1f));
        Transform[] crystalPoints = CreatePoints(pointsRoot.transform, "Faction Crystal Base", FactionCrystalPositions, new Color(0.82f, 0.88f, 1f, 1f));
        RestoreCrystalSpawnPointPoses(crystalPoints, crystalSpawnPointPoses);
        CreateWands(root.transform);

        GameObject directorObject = new(DirectorName);
        directorObject.transform.SetParent(root.transform, false);
        directorObject.AddComponent<NetworkObject>();
        Unit1GameDirector director = directorObject.AddComponent<Unit1GameDirector>();
        director.Configure(enemyPrefab, cpuDuckPrefab, factionCrystalPrefab, playerPoints, enemyPoints, crystalPoints, 6);

        EditorUtility.SetDirty(directorObject);
        EditorSceneManager.MarkSceneDirty(unit1);
        EditorSceneManager.SaveScene(unit1);
        AssetDatabase.SaveAssets();
        NetworkProjectConfigUtilities.RebuildPrefabTable();
        Selection.activeGameObject = directorObject;
        Debug.Log("UNIT1 multiplayer content is ready: 1–6 human players, CPU ducks, nine network wands, three faction crystals, and six Grey Shadows.");
    }

    /// <summary>
    /// Crystal locations are laid out by the scene designer. Preserve their
    /// latest world transforms before a content rebuild destroys the generated
    /// root, then restore them on the replacement spawn-point objects.
    /// </summary>
    private static CrystalSpawnPointPose[] CaptureCrystalSpawnPointPoses(GameObject contentRoot)
    {
        if (contentRoot == null)
        {
            return null;
        }

        Transform pointsRoot = contentRoot.transform.Find("Spawn Points");
        if (pointsRoot == null)
        {
            return null;
        }

        CrystalSpawnPointPose[] poses = new CrystalSpawnPointPose[FactionTeam.Count];
        for (int index = 0; index < poses.Length; index++)
        {
            Transform point = pointsRoot.Find("Faction Crystal Base " + (index + 1));
            if (point == null)
            {
                return null;
            }

            poses[index] = new CrystalSpawnPointPose(point.position, point.rotation);
        }

        return poses;
    }

    private static void RestoreCrystalSpawnPointPoses(
        Transform[] crystalPoints,
        CrystalSpawnPointPose[] savedPoses)
    {
        if (crystalPoints == null || savedPoses == null)
        {
            return;
        }

        int count = Mathf.Min(crystalPoints.Length, savedPoses.Length);
        for (int index = 0; index < count; index++)
        {
            if (crystalPoints[index] != null)
            {
                crystalPoints[index].SetPositionAndRotation(
                    savedPoses[index].Position,
                    savedPoses[index].Rotation);
            }
        }
    }

    private static NetworkObject CreateFactionCrystalPrefab()
    {
        EnsureFolder("Assets/Prefabs");
        GameObject root = new("Faction Crystal", typeof(NetworkObject), typeof(NetworkTransform), typeof(FactionCrystal));
        SphereCollider collider = root.AddComponent<SphereCollider>();
        collider.center = new Vector3(0f, 1.15f, 0f);
        collider.radius = 1.35f;
        collider.isTrigger = true;

        PrefabUtility.SaveAsPrefabAsset(root, FactionCrystalPrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.ImportAsset(FactionCrystalPrefabPath, ImportAssetOptions.ForceUpdate);
        GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(FactionCrystalPrefabPath);
        return saved != null ? saved.GetComponent<NetworkObject>() : null;
    }

    private static NetworkObject CreateCpuDuckPrefab()
    {
        EnsureFolder("Assets/Prefabs");
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab == null)
        {
            throw new System.InvalidOperationException("找不到多人玩家 prefab：" + PlayerPrefabPath);
        }

        GameObject root = PrefabUtility.InstantiatePrefab(playerPrefab) as GameObject;
        if (root == null)
        {
            throw new System.InvalidOperationException("無法複製多人玩家 prefab 來建立 CPU 小鴨。");
        }

        root.name = "CPU Duck";
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        // The CPU keeps the same character art and collider as a player, but
        // removes all keyboard and local-profile behaviours. Unit1BotDuck is
        // the only state-authority movement writer for this prefab.
        FusionDuckPlayer humanController = root.GetComponent<FusionDuckPlayer>();
        if (humanController != null)
        {
            Object.DestroyImmediate(humanController);
        }

        DuckWandAttack wandAttack = root.GetComponent<DuckWandAttack>();
        if (wandAttack != null)
        {
            Object.DestroyImmediate(wandAttack);
        }

        // DuckWandAttack requires DuckMover. Remove the dependent attack
        // script first; otherwise Unity refuses to remove the player's input
        // mover and the CPU prefab can receive unintended local controls.
        DuckMover mover = root.GetComponent<DuckMover>();
        if (mover != null)
        {
            Object.DestroyImmediate(mover);
        }

        if (root.GetComponent<Unit1BotDuck>() == null)
        {
            root.AddComponent<Unit1BotDuck>();
        }

        PrefabUtility.SaveAsPrefabAsset(root, CpuDuckPrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.ImportAsset(CpuDuckPrefabPath, ImportAssetOptions.ForceUpdate);
        GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(CpuDuckPrefabPath);
        return saved != null ? saved.GetComponent<NetworkObject>() : null;
    }

    private static NetworkObject CreateGreyShadowDuckPrefab()
    {
        EnsureFolder("Assets/Prefabs");
        EnsureFolder("Assets/Materials/Characters");
        // Slot 0 is the duck's skin.  Keep the other slots on their original
        // textured materials so the bill, eyes, and all other duck details
        // retain their usual colours.
        Material bodyMaterial = GetOrCreateMaterial(BodyMaterialPath, Color.black, 0f);
        Material runeMaterial = GetOrCreateMaterial(RuneMaterialPath, new Color(0.65f, 0.32f, 1f, 1f), 0f);
        ConfigureMatteShadowMaterial(bodyMaterial);
        ConfigureMatteShadowMaterial(runeMaterial);
        if (runeMaterial.HasProperty("_EmissionColor"))
        {
            runeMaterial.EnableKeyword("_EMISSION");
            runeMaterial.SetColor("_EmissionColor", new Color(0.38f, 0.08f, 1f, 1f));
        }

        GameObject root = new("Grey Shadow Duck");
        NetworkObject networkObject = root.AddComponent<NetworkObject>();
        NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
        // Enemy AI owns a dynamic Rigidbody on its StateAuthority.  Letting
        // NetworkTransform also forecast its physics causes correction drift.
        SerializedObject networkTransformSettings = new(networkTransform);
        SerializedProperty forecastEnabled = networkTransformSettings.FindProperty("PhysicsSettings.ForecastEnabled");
        if (forecastEnabled != null)
        {
            forecastEnabled.boolValue = false;
            networkTransformSettings.ApplyModifiedPropertiesWithoutUndo();
        }
        EnemyDuckAI enemy = root.AddComponent<EnemyDuckAI>();

        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, PlayerColliderCenterY, 0f);
        capsule.height = PlayerColliderHeight;
        capsule.radius = PlayerColliderRadius;

        Rigidbody body = root.GetComponent<Rigidbody>();
        if (body == null)
        {
            body = root.AddComponent<Rigidbody>();
        }
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.constraints = RigidbodyConstraints.FreezeRotation;

        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        GameObject visualOne = CreateLobbySizedGreyVisual(
            root.transform,
            "Grey Shadow Duck - Stable 1",
            playerPrefab,
            YellowPlayerVisualName,
            StableDuckOnePath,
            bodyMaterial);
        GameObject visualTwo = CreateLobbySizedGreyVisual(
            root.transform,
            "Grey Shadow Duck - Stable 2",
            playerPrefab,
            BluePlayerVisualName,
            StableDuckTwoPath,
            bodyMaterial);
        visualOne.SetActive(true);
        visualTwo.SetActive(false);
        enemy.ConfigureStableDuckVisuals(new[] { visualOne, visualTwo });

        CreateRune(root.transform, "Violet Eye Left", new Vector3(-0.12f, 2.46f, 0.39f), runeMaterial);
        CreateRune(root.transform, "Violet Eye Right", new Vector3(0.12f, 2.46f, 0.39f), runeMaterial);
        CreateRune(root.transform, "Shadow Core", new Vector3(0f, 1.6f, -0.22f), runeMaterial, 0.16f);

        PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.ImportAsset(EnemyPrefabPath, ImportAssetOptions.ForceUpdate);
        GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        return saved != null ? saved.GetComponent<NetworkObject>() : networkObject;
    }

    private static GameObject CreateLobbySizedGreyVisual(
        Transform parent,
        string visualName,
        GameObject playerPrefab,
        string playerVisualName,
        string fallbackSourcePath,
        Material bodyMaterial)
    {
        Transform playerVisual = playerPrefab != null ? playerPrefab.transform.Find(playerVisualName) : null;
        GameObject visual;
        if (playerVisual != null)
        {
            // This visual was made from the BigHall character preview. Cloning
            // it retains its precise model scale, offset and orientation.
            visual = Object.Instantiate(playerVisual.gameObject);
        }
        else
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(fallbackSourcePath);
            if (source == null)
            {
                throw new System.InvalidOperationException("找不到穩定的小鴨模型：" + fallbackSourcePath);
            }

            visual = PrefabUtility.InstantiatePrefab(source) as GameObject;
            Animator animator = visual.GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimatorControllerPath);
                animator.applyRootMotion = false;
            }
        }

        visual.name = visualName;
        visual.transform.SetParent(parent, false);

        SkinnedMeshRenderer[] renderers = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = renderers[i];
            Material[] materials = renderer.sharedMaterials;
            // This character mesh uses slot 0 for its skin; slots 1 and 2
            // are the normal bill and feet/detail materials.
            const int skinMaterialIndex = 0;
            if (skinMaterialIndex < materials.Length)
            {
                materials[skinMaterialIndex] = bodyMaterial;
            }
            renderer.sharedMaterials = materials;
        }

        return visual;
    }

    private static void CreateRune(Transform parent, string runeName, Vector3 position, Material material, float size = 0.09f)
    {
        GameObject rune = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        rune.name = runeName;
        rune.transform.SetParent(parent, false);
        rune.transform.localPosition = position;
        rune.transform.localScale = Vector3.one * size;
        Collider collider = rune.GetComponent<Collider>();
        if (collider != null)
        {
            Object.DestroyImmediate(collider);
        }

        Renderer renderer = rune.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
    }

    private static Transform[] CreatePoints(Transform parent, string prefix, Vector3[] positions, Color color)
    {
        Transform[] points = new Transform[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            GameObject point = new(prefix + " " + (i + 1));
            point.transform.SetParent(parent, false);
            point.transform.position = positions[i];
            point.transform.rotation = Quaternion.Euler(0f, (i * 83f) % 360f, 0f);
            points[i] = point.transform;

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Editor Spawn Marker";
            marker.transform.SetParent(point.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            marker.transform.localScale = new Vector3(0.25f, 0.03f, 0.25f);
            Collider collider = marker.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }
            Renderer renderer = marker.GetComponent<Renderer>();
            Material markerMaterial = new(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            if (markerMaterial.HasProperty("_BaseColor")) markerMaterial.SetColor("_BaseColor", color);
            if (markerMaterial.HasProperty("_Color")) markerMaterial.SetColor("_Color", color);
            renderer.sharedMaterial = markerMaterial;
        }

        return points;
    }

    private static void CreateWands(Transform parent)
    {
        GameObject wandsRoot = new("Hand Wands - E to Pick Up");
        wandsRoot.transform.SetParent(parent, false);
        for (int i = 0; i < Wands.Length; i++)
        {
            CreateWand(wandsRoot.transform, Wands[i]);
        }
    }

    private static void CreateWand(Transform parent, WandDefinition definition)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(definition.AssetPath);
        if (source == null)
        {
            Debug.LogWarning("找不到手杖：" + definition.AssetPath);
            return;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(definition.MaterialPath);
        if (material == null)
        {
            Debug.LogWarning("找不到手杖材質：" + definition.MaterialPath);
            return;
        }

        GameObject wand = new(definition.Name, typeof(NetworkObject), typeof(Unit1WandPickup));
        wand.transform.SetParent(parent, false);
        wand.transform.position = definition.Position;

        GameObject visual = PrefabUtility.InstantiatePrefab(source) as GameObject;
        visual.name = "Wand Visual";
        visual.transform.SetParent(wand.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.Euler(-90f, 180f, -90f);
        ApplyWandMaterial(visual, material);

        SphereCollider pickupCollider = wand.AddComponent<SphereCollider>();
        pickupCollider.center = Vector3.zero;
        pickupCollider.radius = 0.85f;
        pickupCollider.isTrigger = true;
        wand.GetComponent<Unit1WandPickup>().Configure(visual.transform, definition.Ability);
    }

    private static void ApplyWandMaterial(GameObject visual, Material material)
    {
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            Material[] materials = renderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                materials[materialIndex] = material;
            }
            renderer.sharedMaterials = materials;
        }
    }

    private readonly struct WandDefinition
    {
        public WandDefinition(string name, string assetPath, string materialPath, Unit1WandAbility ability, Vector3 position)
        {
            Name = name;
            AssetPath = assetPath;
            MaterialPath = materialPath;
            Ability = ability;
            Position = position;
        }

        public string Name { get; }
        public string AssetPath { get; }
        public string MaterialPath { get; }
        public Unit1WandAbility Ability { get; }
        public Vector3 Position { get; }
    }

    private static Material GetOrCreateMaterial(string path, Color color, float smoothness)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void ConfigureMatteShadowMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        // Grey Shadows should read as ink-like shadow, not polished plastic.
        // Keep the rune's emission but remove every lit-material reflection.
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
        if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 0f);
        if (material.HasProperty("_GlossyReflections")) material.SetFloat("_GlossyReflections", 0f);
        EditorUtility.SetDirty(material);
    }

    private static GameObject FindSceneObject(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            GameObject found = FindSceneObject(root.transform, objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static GameObject FindSceneObject(Transform parent, string objectName)
    {
        if (parent.name == objectName)
        {
            return parent.gameObject;
        }

        foreach (Transform child in parent)
        {
            GameObject found = FindSceneObject(child, objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
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
}
