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
        // Root height places the Grey Shadow's capsule feet on UNIT1's
        // existing floor BoxCollider, rather than leaving it floating.
        new(0.52f, -0.334f, 32.73f), new(6.01f, -0.334f, 32.73f), new(11.72f, -0.334f, 32.73f),
        new(17.40f, -0.334f, 32.73f), new(27.20f, -0.334f, 32.73f), new(32.91f, -0.334f, 32.73f),
    };

    private static readonly WandDefinition[] Wands =
    {
        // Six equal-distance pickups: two of each visual style. They occupy
        // the north, west, east and southern main-road approaches rather than
        // spawning beside the player row.
        new("星光快攻杖 1", WeaponOnePath, WeaponOneMaterialPath, Unit1WandAbility.Swift, new Vector3(4.30f, 1.10f, 21.20f)),
        new("水晶守護杖 1", BlueWeaponPath, BlueWeaponMaterialPath, Unit1WandAbility.Guardian, new Vector3(10.10f, 1.10f, 29.10f)),
        new("虛空重擊杖 1", PurpleWeaponPath, PurpleWeaponMaterialPath, Unit1WandAbility.Heavy, new Vector3(17.10f, 1.10f, 24.60f)),
        new("星光快攻杖 2", WeaponOnePath, WeaponOneMaterialPath, Unit1WandAbility.Swift, new Vector3(23.90f, 1.10f, 31.00f)),
        new("水晶守護杖 2", BlueWeaponPath, BlueWeaponMaterialPath, Unit1WandAbility.Guardian, new Vector3(30.10f, 1.10f, 26.10f)),
        new("虛空重擊杖 2", PurpleWeaponPath, PurpleWeaponMaterialPath, Unit1WandAbility.Heavy, new Vector3(28.10f, 1.10f, 18.80f)),
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

    [MenuItem("Duck/建立多人準備大廳與 UNIT1 第一關")]
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

    private static void BuildUnit1(Scene unit1)
    {
        NetworkObject enemyPrefab = CreateGreyShadowDuckPrefab();
        NetworkObject cpuDuckPrefab = CreateCpuDuckPrefab();

        GameObject oldRoot = FindSceneObject(unit1, ContentRootName);
        if (oldRoot != null)
        {
            Object.DestroyImmediate(oldRoot);
        }

        GameObject root = new(ContentRootName);
        GameObject pointsRoot = new("Spawn Points");
        pointsRoot.transform.SetParent(root.transform, false);
        Transform[] playerPoints = CreatePoints(pointsRoot.transform, "Player Spawn", PlayerPositions, new Color(0.26f, 0.58f, 1f, 1f));
        Transform[] enemyPoints = CreatePoints(pointsRoot.transform, "Grey Shadow Spawn", EnemyPositions, new Color(0.65f, 0.32f, 1f, 1f));
        CreateWands(root.transform);

        GameObject directorObject = new(DirectorName);
        directorObject.transform.SetParent(root.transform, false);
        directorObject.AddComponent<NetworkObject>();
        Unit1GameDirector director = directorObject.AddComponent<Unit1GameDirector>();
        director.Configure(enemyPrefab, cpuDuckPrefab, playerPoints, enemyPoints, 3);

        EditorUtility.SetDirty(directorObject);
        EditorSceneManager.MarkSceneDirty(unit1);
        EditorSceneManager.SaveScene(unit1);
        AssetDatabase.SaveAssets();
        NetworkProjectConfigUtilities.RebuildPrefabTable();
        Selection.activeGameObject = directorObject;
        Debug.Log("UNIT1 multiplayer content is ready: 1–6 human players, CPU ducks filling matches to four, six material-equipped network wands, and the Grey Shadow Duck prefab.");
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

        DuckMover mover = root.GetComponent<DuckMover>();
        if (mover != null)
        {
            Object.DestroyImmediate(mover);
        }

        DuckWandAttack wandAttack = root.GetComponent<DuckWandAttack>();
        if (wandAttack != null)
        {
            Object.DestroyImmediate(wandAttack);
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
        Material bodyMaterial = GetOrCreateMaterial(BodyMaterialPath, Color.black, 0.15f);
        Material runeMaterial = GetOrCreateMaterial(RuneMaterialPath, new Color(0.65f, 0.32f, 1f, 1f), 0.45f);
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
