using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Small editor shortcuts so the existing duck models can be reused for level one.</summary>
public static class FirstLevelDuckSetup
{
    private const string Unit1ScenePath = "Assets/Scenes/UNIT1.unity";
    private static readonly Vector3 PlayerStartPosition = new Vector3(-1.4f, 1.2f, -10f);
    private static readonly Vector3 WandPosition = new Vector3(-0.25f, 1.08f, -3.8f);
    private static readonly Vector3[] EnemySpawnPositions =
    {
        new Vector3(-8f, 1.2f, -3.5f),
        new Vector3(7.5f, 1.2f, -3f),
        new Vector3(-10f, 1.2f, 6.5f),
        new Vector3(9.5f, 1.2f, 7f),
        new Vector3(-6.5f, 1.2f, 14.5f),
        new Vector3(7f, 1.2f, 15f),
    };

    [MenuItem("Duck/第一關/建立 UNIT1 多人遊戲內容")]
    private static void BuildPlayableFirstLevel()
    {
        // Duck1/Duck2/Duck3 in UNIT1 are map dressing, never the actual players
        // or enemy source. The multiplayer builder creates separate runtime
        // players, the GreyShadowDuck prefab, and scene-owned spawn points.
        Unit1MultiplayerContentBuilder.BuildAll();
    }

    [MenuItem("Duck/第一關/把選取小鴨設為灰影敵人")]
    private static void MakeSelectedDuckAnEnemy()
    {
        GameObject selectedDuck = Selection.activeGameObject;
        if (selectedDuck == null)
        {
            EditorUtility.DisplayDialog("沒有選取小鴨", "請先在 Hierarchy 選取要當敵人的鴨子模型。", "知道了");
            return;
        }

        Undo.RecordObject(selectedDuck, "Set grey-shadow duck enemy");
        DuckMover mover = selectedDuck.GetComponent<DuckMover>();
        if (mover != null)
        {
            mover.enabled = false;
        }

        if (selectedDuck.GetComponent<Rigidbody>() == null)
        {
            Undo.AddComponent<Rigidbody>(selectedDuck);
        }

        if (selectedDuck.GetComponent<Collider>() == null)
        {
            Undo.AddComponent<CapsuleCollider>(selectedDuck);
        }

        if (selectedDuck.GetComponent<EnemyDuckAI>() == null)
        {
            Undo.AddComponent<EnemyDuckAI>(selectedDuck);
        }

        selectedDuck.name = "灰影小鴨 Enemy";
        EditorSceneManager.MarkSceneDirty(selectedDuck.scene);
        Selection.activeGameObject = selectedDuck;
    }

    [MenuItem("Duck/第一關/把選取小鴨設為玩家並加入手杖攻擊")]
    private static void GiveSelectedDuckWandAttack()
    {
        GameObject selectedDuck = Selection.activeGameObject;
        if (selectedDuck == null)
        {
            EditorUtility.DisplayDialog("沒有選取小鴨", "請先在 Hierarchy 選取玩家小鴨。", "知道了");
            return;
        }

        if (selectedDuck.GetComponent<DuckMover>() == null)
        {
            EditorUtility.DisplayDialog("找不到玩家移動元件", "這隻小鴨需要先有 DuckMover，才能在拿到手杖後攻擊。", "知道了");
            return;
        }

        if (selectedDuck.GetComponent<DuckWandAttack>() == null)
        {
            Undo.AddComponent<DuckWandAttack>(selectedDuck);
        }

        EditorSceneManager.MarkSceneDirty(selectedDuck.scene);
        Selection.activeGameObject = selectedDuck;
    }

    private static void ConfigurePlayer(GameObject player)
    {
        Undo.RecordObject(player, "Configure first level player");
        player.name = "Duck1";
        player.SetActive(true);
        player.transform.position = PlayerStartPosition;

        DuckMover mover = player.GetComponent<DuckMover>();
        mover ??= Undo.AddComponent<DuckMover>(player);
        mover.enabled = true;

        if (player.GetComponent<DuckWandAttack>() == null)
        {
            Undo.AddComponent<DuckWandAttack>(player);
        }
    }

    private static void ConfigureEnemyTemplate(GameObject enemyTemplate)
    {
        Undo.RecordObject(enemyTemplate, "Configure grey-shadow duck template");
        enemyTemplate.name = "Grey Shadow Duck Template";

        DuckMover mover = enemyTemplate.GetComponent<DuckMover>();
        if (mover != null)
        {
            mover.enabled = false;
        }

        if (enemyTemplate.GetComponent<Rigidbody>() == null)
        {
            Undo.AddComponent<Rigidbody>(enemyTemplate);
        }

        if (enemyTemplate.GetComponent<Collider>() == null)
        {
            Undo.AddComponent<CapsuleCollider>(enemyTemplate);
        }

        if (enemyTemplate.GetComponent<EnemyDuckAI>() == null)
        {
            Undo.AddComponent<EnemyDuckAI>(enemyTemplate);
        }

        // It is a source object only. The spawner creates active clones in play mode.
        enemyTemplate.SetActive(false);
    }

    private static void ConfigureWand(GameObject wand)
    {
        Undo.RecordObject(wand, "Place first level wand");
        wand.SetActive(true);
        wand.transform.position = WandPosition;
    }

    private static Transform[] CreateSpawnPoints(Scene scene)
    {
        GameObject oldRoot = FindSceneObject(scene, "First Level Enemy Spawn Points");
        if (oldRoot != null)
        {
            Undo.DestroyObjectImmediate(oldRoot);
        }

        GameObject root = new GameObject("First Level Enemy Spawn Points");
        Undo.RegisterCreatedObjectUndo(root, "Create enemy spawn points");
        Transform[] spawnPoints = new Transform[EnemySpawnPositions.Length];
        for (int i = 0; i < EnemySpawnPositions.Length; i++)
        {
            GameObject point = new GameObject("Enemy Spawn Point " + (i + 1));
            point.transform.SetParent(root.transform, false);
            point.transform.position = EnemySpawnPositions[i];
            point.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            spawnPoints[i] = point.transform;
        }

        return spawnPoints;
    }

    private static void CreateSpawner(Scene scene, GameObject enemyTemplate, Transform[] spawnPoints)
    {
        GameObject existingSpawner = FindSceneObject(scene, "First Level Enemy Spawner");
        if (existingSpawner != null)
        {
            Undo.DestroyObjectImmediate(existingSpawner);
        }

        GameObject spawnerObject = new GameObject("First Level Enemy Spawner");
        Undo.RegisterCreatedObjectUndo(spawnerObject, "Create enemy spawner");
        FirstLevelEnemySpawner spawner = spawnerObject.AddComponent<FirstLevelEnemySpawner>();
        spawner.Configure(enemyTemplate, spawnPoints, 3);
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
}
