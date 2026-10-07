using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Spawns a small, random selection of grey-shadow ducks from designer-placed
/// spawn points. Points keep enemies off scenery and away from the player start.
/// </summary>
[DisallowMultipleComponent]
public sealed class FirstLevelEnemySpawner : MonoBehaviour
{
    [SerializeField] private GameObject enemyTemplate;
    [SerializeField] private Transform[] spawnPoints = System.Array.Empty<Transform>();
    [SerializeField, Min(1)] private int initialEnemyCount = 3;
    [SerializeField] private bool showObjective = true;

    private readonly List<EnemyDuckAI> livingEnemies = new List<EnemyDuckAI>();
    private DuckMover player;
    private TextMeshProUGUI objectiveText;
    private int defeatedEnemies;
    private int totalEnemies;

    /// <summary>Used by the Unity editor setup command.</summary>
    public void Configure(GameObject template, Transform[] points, int enemyCount)
    {
        enemyTemplate = template;
        spawnPoints = points ?? System.Array.Empty<Transform>();
        initialEnemyCount = Mathf.Max(1, enemyCount);
    }

    private void Start()
    {
        player = FindActivePlayer();
        CreateObjectiveHud();
        SpawnInitialEnemies();
        UpdateObjectiveText();
    }

    private void Update()
    {
        if (player == null)
        {
            player = FindActivePlayer();
        }

        UpdateObjectiveText();
    }

    private void SpawnInitialEnemies()
    {
        if (enemyTemplate == null)
        {
            Debug.LogError("FirstLevelEnemySpawner needs a grey-shadow duck template.", this);
            return;
        }

        List<Transform> validPoints = new List<Transform>();
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i] != null)
            {
                validPoints.Add(spawnPoints[i]);
            }
        }

        Shuffle(validPoints);
        totalEnemies = Mathf.Min(initialEnemyCount, validPoints.Count);

        for (int i = 0; i < totalEnemies; i++)
        {
            Transform point = validPoints[i];
            GameObject enemyObject = Instantiate(enemyTemplate, point.position, point.rotation);
            enemyObject.name = "灰影小鴨 " + (i + 1);
            enemyObject.SetActive(true);

            EnemyDuckAI enemy = enemyObject.GetComponent<EnemyDuckAI>();
            if (enemy == null)
            {
                Debug.LogError("The enemy template needs EnemyDuckAI.", enemyObject);
                Destroy(enemyObject);
                totalEnemies--;
                continue;
            }

            enemy.Defeated += HandleEnemyDefeated;
            livingEnemies.Add(enemy);
        }
    }

    private void HandleEnemyDefeated(EnemyDuckAI enemy)
    {
        enemy.Defeated -= HandleEnemyDefeated;
        livingEnemies.Remove(enemy);
        defeatedEnemies++;
        UpdateObjectiveText();
    }

    private void CreateObjectiveHud()
    {
        if (!showObjective)
        {
            return;
        }

        GameObject canvasObject = new GameObject("First Level HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject textObject = new GameObject("Objective", typeof(RectTransform));
        textObject.transform.SetParent(canvasObject.transform, false);
        objectiveText = textObject.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
        {
            objectiveText.font = TMP_Settings.defaultFontAsset;
        }

        objectiveText.alignment = TextAlignmentOptions.Center;
        objectiveText.fontSize = 30f;
        objectiveText.color = Color.white;
        objectiveText.outlineColor = new Color(0.05f, 0.07f, 0.1f, 1f);
        objectiveText.outlineWidth = 0.2f;

        RectTransform rect = objectiveText.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -32f);
        rect.sizeDelta = new Vector2(900f, 80f);
    }

    private void UpdateObjectiveText()
    {
        if (objectiveText == null)
        {
            return;
        }

        if (totalEnemies > 0 && defeatedEnemies >= totalEnemies)
        {
            objectiveText.text = "第一關完成！";
            return;
        }

        if (player == null || !player.HasWeapon)
        {
            objectiveText.text = "第一關：找到手杖";
            return;
        }

        objectiveText.text = "第一關：擊敗灰影小鴨 " + defeatedEnemies + " / " + totalEnemies;
    }

    private static void Shuffle(List<Transform> points)
    {
        for (int i = points.Count - 1; i > 0; i--)
        {
            int swapIndex = Random.Range(0, i + 1);
            (points[i], points[swapIndex]) = (points[swapIndex], points[i]);
        }
    }

    private static DuckMover FindActivePlayer()
    {
        DuckMover[] ducks = FindObjectsByType<DuckMover>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < ducks.Length; i++)
        {
            if (ducks[i] != null && ducks[i].isActiveAndEnabled)
            {
                return ducks[i];
            }
        }

        return null;
    }
}
