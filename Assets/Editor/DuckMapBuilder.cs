using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DuckMapBuilder
{
    private const string GeneratedRootName = "Generated Map";
    private const string UnitScenePath = "Assets/Scenes/UNIT1.unity";
    private const string AutoRunMarkerPath = "Assets/Editor/DuckMapBuilder.autorun";

    private static readonly Vector3 MapCenter = Vector3.zero;

    private sealed class AssetRefs
    {
        public GameObject Terrain;
        public GameObject GrassTile;
        public GameObject GrassMound;
        public GameObject Road;
        public GameObject Lantern;
        public GameObject Crate;
        public GameObject Gate;
        public GameObject House;
        public GameObject MountainHole;
        public GameObject Tower;
        public GameObject Tree;
    }

    [MenuItem("Duck/Build Complete Map")]
    public static void BuildCompleteMapInCurrentScene()
    {
        BuildMap(EditorSceneManager.GetActiveScene());
    }

    [InitializeOnLoadMethod]
    private static void BuildPendingMapAfterReload()
    {
        EditorApplication.delayCall += TryRunPendingAutoBuild;
    }

    [MenuItem("Duck/Rebuild UNIT1 Map")]
    public static void RebuildUnit1Scene()
    {
        Scene scene = EditorSceneManager.OpenScene(UnitScenePath, OpenSceneMode.Single);
        BuildMap(scene);
    }

    public static void BuildUnit1SceneFromBatchMode()
    {
        RebuildUnit1Scene();
    }

    private static void TryRunPendingAutoBuild()
    {
        if (!File.Exists(AutoRunMarkerPath))
        {
            return;
        }

        Scene activeScene = EditorSceneManager.GetActiveScene();
        string activePath = activeScene.path.Replace('\\', '/');
        if (!string.Equals(activePath, UnitScenePath, StringComparison.OrdinalIgnoreCase))
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("Duck map auto-build was cancelled while waiting to open UNIT1.");
                return;
            }

            activeScene = EditorSceneManager.OpenScene(UnitScenePath, OpenSceneMode.Single);
        }

        try
        {
            File.Delete(AutoRunMarkerPath);
            BuildMap(activeScene);
            AssetDatabase.Refresh();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void BuildMap(Scene scene)
    {
        if (!scene.IsValid())
        {
            throw new InvalidOperationException("No valid scene is open.");
        }

        AssetRefs assets = LoadAssets();
        GameObject root = RecreateRoot();

        Transform groundRoot = CreateGroup(root.transform, "Ground");
        Transform roadRoot = CreateGroup(root.transform, "Roads");
        Transform buildingRoot = CreateGroup(root.transform, "Buildings");
        Transform natureRoot = CreateGroup(root.transform, "Nature");
        Transform propRoot = CreateGroup(root.transform, "Props");

        float grassStep = Mathf.Max(GetFootprint(assets.GrassTile).x * 1.65f, 5.5f);
        float roadStep = Mathf.Max(GetFootprint(assets.Road).y * 1.2f, 3.25f);
        float mapRadius = grassStep * 4f;

        PlaceGrassField(assets, groundRoot, grassStep);
        PlaceLandmarks(assets, groundRoot, buildingRoot, mapRadius);
        PlaceRoadNetwork(assets, roadRoot, roadStep);
        PlaceVillage(assets, buildingRoot);
        PlaceForestRing(assets, natureRoot, mapRadius);
        PlaceLanternsAndCrates(assets, propRoot, roadStep);
        AddSafetyWalls(root.transform, mapRadius + grassStep * 0.6f);
        AdjustPlayerStartPositions();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Duck map generated successfully.");
    }

    private static AssetRefs LoadAssets()
    {
        return new AssetRefs
        {
            Terrain = LoadRequired("Assets/ELSETHING/2TERRAIN/Meshy_AI_Grassy_Terrace_Platfo_0901072127_texture_fbx/TERRAIN.fbx"),
            GrassTile = LoadRequired("Assets/ELSETHING/Grass/Meshy_AI_Grassy_Tile_0901061403_texture_fbx/Meshy_AI_Grassy_Tile_0901061403_texture_fbx/grass.fbx"),
            GrassMound = LoadRequired("Assets/ELSETHING/Grass/Meshy_AI_Mossy_Mound_0901064635_texture_fbx/grass22.fbx"),
            Road = LoadRequired("Assets/ELSETHING/ROAD/ROAD.fbx"),
            Lantern = LoadRequired("Assets/ELSETHING/ROADLIGHT/ROADLIGHT.fbx"),
            Crate = LoadRequired("Assets/ELSETHING/BOX/BOX.fbx"),
            Gate = LoadRequired("Assets/ELSETHING/GATE/gate.fbx"),
            House = LoadRequired("Assets/ELSETHING/HOUSE/HOUSE.fbx"),
            MountainHole = LoadRequired("Assets/ELSETHING/MOUTAINHOLE/Meshy_AI_Lanternlit_Grotto_0901070139_texture_fbx/MOUNTAINHOLE.fbx"),
            Tower = LoadRequired("Assets/ELSETHING/TOWER/Meshy_AI_Duckling_Keep_0902075733_texture_fbx/tower.fbx"),
            Tree = LoadRequired("Assets/ELSETHING/TREE/Meshy_AI_Clay_Tree_0830073255_texture_fbx/TREE.fbx"),
        };
    }

    private static GameObject LoadRequired(string path)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null)
        {
            throw new InvalidOperationException("Missing map asset: " + path);
        }

        return asset;
    }

    private static GameObject RecreateRoot()
    {
        GameObject oldRoot = GameObject.Find(GeneratedRootName);
        if (oldRoot != null)
        {
            UnityEngine.Object.DestroyImmediate(oldRoot);
        }

        GameObject root = new GameObject(GeneratedRootName);
        root.transform.position = MapCenter;
        return root;
    }

    private static Transform CreateGroup(Transform parent, string name)
    {
        GameObject group = new GameObject(name);
        group.transform.SetParent(parent, false);
        return group.transform;
    }

    private static void PlaceGrassField(AssetRefs assets, Transform parent, float step)
    {
        int index = 0;
        for (int x = -4; x <= 4; x++)
        {
            for (int z = -4; z <= 4; z++)
            {
                Vector3 position = new Vector3(x * step, -0.03f, z * step);
                float yaw = ((x + z) & 1) == 0 ? 0f : 90f;
                PlacePrefab(assets.GrassTile, parent, "Grass Tile " + index, position, yaw, Vector3.one * 1.55f, true);
                index++;
            }
        }

        PlacePrefab(assets.Terrain, parent, "Central Terrace", new Vector3(0f, 0f, 0f), 0f, Vector3.one * 1.35f, true);
    }

    private static void PlaceRoadNetwork(AssetRefs assets, Transform parent, float step)
    {
        int index = 0;
        for (int z = -7; z <= 7; z++)
        {
            PlacePrefab(assets.Road, parent, "Main Road " + index, new Vector3(0f, 0.04f, z * step), 0f, Vector3.one * 1.05f, true);
            index++;
        }

        for (int x = -5; x <= 5; x++)
        {
            if (x == 0)
            {
                continue;
            }

            PlacePrefab(assets.Road, parent, "Cross Road " + index, new Vector3(x * step, 0.05f, 0f), 90f, Vector3.one * 1.05f, true);
            index++;
        }
    }

    private static void PlaceLandmarks(AssetRefs assets, Transform groundRoot, Transform buildingRoot, float mapRadius)
    {
        PlacePrefab(assets.Gate, buildingRoot, "South Gate", new Vector3(0f, 0.05f, -mapRadius * 0.8f), 0f, Vector3.one * 1.25f, true);
        PlacePrefab(assets.MountainHole, buildingRoot, "North Grotto", new Vector3(0f, 0.05f, mapRadius * 0.82f), 180f, Vector3.one * 1.45f, true);
        PlacePrefab(assets.Tower, buildingRoot, "East Tower", new Vector3(mapRadius * 0.68f, 0.05f, mapRadius * 0.36f), 235f, Vector3.one * 1.2f, true);
        PlacePrefab(assets.GrassMound, groundRoot, "West Mossy Mound", new Vector3(-mapRadius * 0.65f, 0.02f, mapRadius * 0.28f), 35f, Vector3.one * 1.15f, true);
    }

    private static void PlaceVillage(AssetRefs assets, Transform parent)
    {
        Vector3[] housePositions =
        {
            new Vector3(-14f, 0.05f, -7f),
            new Vector3(14f, 0.05f, -6f),
            new Vector3(-17f, 0.05f, 8f),
            new Vector3(17f, 0.05f, 9f),
            new Vector3(-8f, 0.05f, 15f),
            new Vector3(9f, 0.05f, 16f),
        };

        float[] rotations = { 45f, 315f, 105f, 250f, 150f, 210f };
        for (int i = 0; i < housePositions.Length; i++)
        {
            PlacePrefab(assets.House, parent, "Duck Cottage " + (i + 1), housePositions[i], rotations[i], Vector3.one * 1.15f, true);
        }
    }

    private static void PlaceForestRing(AssetRefs assets, Transform parent, float mapRadius)
    {
        int index = 0;
        const int count = 42;
        for (int i = 0; i < count; i++)
        {
            float angle = i * 137.5f;
            float radians = angle * Mathf.Deg2Rad;
            float radius = mapRadius * (0.72f + 0.25f * Stable01(i * 17 + 3));
            Vector3 position = new Vector3(Mathf.Cos(radians) * radius, 0.05f, Mathf.Sin(radians) * radius);

            if (Mathf.Abs(position.x) < 4f && Mathf.Abs(position.z) > mapRadius * 0.65f)
            {
                position.x += Mathf.Sign(position.x + 0.01f) * 5f;
            }

            float scale = Mathf.Lerp(0.8f, 1.35f, Stable01(i * 29 + 11));
            PlacePrefab(assets.Tree, parent, "Tree " + index, position, angle + 90f, Vector3.one * scale, true);
            index++;
        }
    }

    private static void PlaceLanternsAndCrates(AssetRefs assets, Transform parent, float roadStep)
    {
        int lanternIndex = 0;
        for (int z = -6; z <= 6; z += 2)
        {
            PlacePrefab(assets.Lantern, parent, "Left Lantern " + lanternIndex, new Vector3(-2.6f, 0.08f, z * roadStep), 20f, Vector3.one * 0.95f, true);
            PlacePrefab(assets.Lantern, parent, "Right Lantern " + lanternIndex, new Vector3(2.6f, 0.08f, z * roadStep), 340f, Vector3.one * 0.95f, true);
            lanternIndex++;
        }

        Vector3[] cratePositions =
        {
            new Vector3(-10.5f, 0.08f, -9.5f),
            new Vector3(-12.2f, 0.08f, -8.2f),
            new Vector3(11.2f, 0.08f, -8.6f),
            new Vector3(13.1f, 0.08f, -7.4f),
            new Vector3(-16.2f, 0.08f, 12.4f),
            new Vector3(15.6f, 0.08f, 12.8f),
            new Vector3(6.5f, 0.08f, 19.2f),
            new Vector3(-6.8f, 0.08f, 18.6f),
        };

        for (int i = 0; i < cratePositions.Length; i++)
        {
            float yaw = 37f * i;
            float scale = i % 3 == 0 ? 1.15f : 0.95f;
            PlacePrefab(assets.Crate, parent, "Crate " + (i + 1), cratePositions[i], yaw, Vector3.one * scale, true);
        }
    }

    private static GameObject PlacePrefab(GameObject asset, Transform parent, string name, Vector3 position, float yaw, Vector3 scale, bool addColliders)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        instance.name = name;
        instance.transform.SetParent(parent, false);
        instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        instance.transform.localScale = scale;
        SetStaticRecursively(instance);

        if (addColliders)
        {
            AddMeshColliders(instance);
        }

        return instance;
    }

    private static Vector2 GetFootprint(GameObject asset)
    {
        GameObject temp = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        Bounds bounds = CalculateBounds(temp);
        UnityEngine.Object.DestroyImmediate(temp);

        float x = Mathf.Max(bounds.size.x, 1f);
        float z = Mathf.Max(bounds.size.z, 1f);
        return new Vector2(x, z);
    }

    private static Bounds CalculateBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return new Bounds(root.transform.position, Vector3.one);
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    private static void AddMeshColliders(GameObject root)
    {
        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>();
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null)
            {
                continue;
            }

            MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
        }
    }

    private static void SetStaticRecursively(GameObject root)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            transforms[i].gameObject.isStatic = true;
        }
    }

    private static void AddSafetyWalls(Transform parent, float radius)
    {
        Transform wallRoot = CreateGroup(parent, "Safety Walls");
        CreateWall(wallRoot, "North Wall", new Vector3(0f, 2f, radius), new Vector3(radius * 2f, 4f, 1f));
        CreateWall(wallRoot, "South Wall", new Vector3(0f, 2f, -radius), new Vector3(radius * 2f, 4f, 1f));
        CreateWall(wallRoot, "East Wall", new Vector3(radius, 2f, 0f), new Vector3(1f, 4f, radius * 2f));
        CreateWall(wallRoot, "West Wall", new Vector3(-radius, 2f, 0f), new Vector3(1f, 4f, radius * 2f));
    }

    private static void CreateWall(Transform parent, string name, Vector3 position, Vector3 scale)
    {
        GameObject wall = new GameObject(name);
        wall.transform.SetParent(parent, false);
        wall.transform.position = position;
        wall.transform.localScale = scale;
        BoxCollider collider = wall.AddComponent<BoxCollider>();
        collider.size = Vector3.one;
        wall.isStatic = true;
    }

    private static void AdjustPlayerStartPositions()
    {
        Dictionary<string, Vector3> starts = new Dictionary<string, Vector3>
        {
            { "Duck1", new Vector3(-1.4f, 1.2f, -10f) },
            { "Duck2", new Vector3(0f, 1.2f, -10.8f) },
            { "Duck3", new Vector3(1.4f, 1.2f, -10f) },
        };

        foreach (KeyValuePair<string, Vector3> start in starts)
        {
            GameObject duck = GameObject.Find(start.Key);
            if (duck != null)
            {
                duck.transform.position = start.Value;
            }
        }
    }

    private static float Stable01(int seed)
    {
        unchecked
        {
            uint value = (uint)seed;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            return (value & 0xffff) / 65535f;
        }
    }
}
