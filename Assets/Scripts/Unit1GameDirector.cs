using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Scene-owned first-level coordinator. It never creates player ducks: the
/// players travelling from BigHall are positioned here, then the room master
/// creates the grey-shadow enemies from their prefab.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class Unit1GameDirector : NetworkBehaviour
{
    private const int MinimumHumanPlayers = 1;
    private const int MinimumBattleParticipants = 4;
    private const int MaximumFactionPlayers = 6;
    private const float FactionAssignmentDelay = 1.2f;

    private static readonly int[] FactionIndices = { 1, 2, 3, 4, 5, 6 };

    [SerializeField] private NetworkObject greyShadowDuckPrefab;
    [SerializeField] private NetworkObject cpuDuckPrefab;
    [SerializeField] private Transform[] playerSpawnPoints = System.Array.Empty<Transform>();
    [SerializeField] private Transform[] enemySpawnPoints = System.Array.Empty<Transform>();
    [SerializeField, Min(1)] private int enemyCount = 3;

    [Networked] public NetworkBool FactionsAssigned { get; private set; }
    [Networked] public NetworkBool CpuBotsSpawned { get; private set; }
    [Networked] public int CpuBotCount { get; private set; }
    [Networked] private int FactionShuffleSeed { get; set; }

    private bool enemiesSpawned;
    private TextMeshProUGUI objectiveText;
    private float factionAssignmentTime;

    public void Configure(
        NetworkObject enemyPrefab,
        NetworkObject botPrefab,
        Transform[] playerPoints,
        Transform[] enemyPoints,
        int count)
    {
        greyShadowDuckPrefab = enemyPrefab;
        cpuDuckPrefab = botPrefab;
        playerSpawnPoints = playerPoints ?? System.Array.Empty<Transform>();
        enemySpawnPoints = enemyPoints ?? System.Array.Empty<Transform>();
        enemyCount = Mathf.Max(1, count);
    }

    public override void Spawned()
    {
        Debug.Log("[UNIT1-DIAG Director] spawned | sharedMaster=" + Runner.IsSharedModeMasterClient);
        if (Runner.IsSharedModeMasterClient)
        {
            FactionShuffleSeed = UnityEngine.Random.Range(1, int.MaxValue);
            factionAssignmentTime = Time.time + FactionAssignmentDelay;
            SpawnEnemies();
        }

        CreateObjectiveHud();
    }

    public override void Render()
    {
        UpdateObjectiveHud();
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority && !FactionsAssigned && Time.time >= factionAssignmentTime)
        {
            TryAssignRandomFactions();
        }
    }

    private void SpawnEnemies()
    {
        if (enemiesSpawned || greyShadowDuckPrefab == null || enemySpawnPoints.Length == 0)
        {
            return;
        }

        enemiesSpawned = true;
        List<Transform> points = new();
        for (int i = 0; i < enemySpawnPoints.Length; i++)
        {
            if (enemySpawnPoints[i] != null)
            {
                points.Add(enemySpawnPoints[i]);
            }
        }

        for (int i = points.Count - 1; i > 0; i--)
        {
            int other = UnityEngine.Random.Range(0, i + 1);
            (points[i], points[other]) = (points[other], points[i]);
        }

        int count = Mathf.Min(enemyCount, points.Count);
        for (int i = 0; i < count; i++)
        {
            Transform point = points[i];
            NetworkObject enemy = Runner.Spawn(greyShadowDuckPrefab, point.position, point.rotation);
            if (enemy != null)
            {
                enemy.name = "灰影小鴨 " + (i + 1);
                Debug.Log("[UNIT1-DIAG Director] spawned enemy " + (i + 1) + " | object=" + enemy.Id);
            }
        }
    }

    private void CreateObjectiveHud()
    {
        if (objectiveText != null)
        {
            return;
        }

        GameObject canvasObject = new("UNIT1 Objective", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject labelObject = new("Objective", typeof(RectTransform));
        labelObject.transform.SetParent(canvasObject.transform, false);
        objectiveText = labelObject.AddComponent<TextMeshProUGUI>();
        objectiveText.font = TMP_Settings.defaultFontAsset;
        objectiveText.fontSize = 29f;
        objectiveText.alignment = TextAlignmentOptions.Center;
        objectiveText.color = Color.white;
        objectiveText.outlineColor = new Color(0.05f, 0.06f, 0.11f, 1f);
        objectiveText.outlineWidth = 0.2f;
        RectTransform rect = objectiveText.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -32f);
        rect.sizeDelta = new Vector2(960f, 72f);
    }

    private void UpdateObjectiveHud()
    {
        if (objectiveText == null)
        {
            return;
        }

        FusionDuckPlayer[] players = FindObjectsByType<FusionDuckPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        FusionDuckPlayer localPlayer = null;
        bool holdingWand = false;
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && players[i].HasStateAuthority)
            {
                localPlayer = players[i];
                holdingWand = players[i].HasWand;
                break;
            }
        }

        if (!FactionsAssigned)
        {
            objectiveText.text = "正在隨機抽取陣營　未滿 4 人會加入電腦鴨";
            return;
        }

        string faction = localPlayer != null ? localPlayer.FactionDisplayName + "色陣營" : "陣營同步中";
        int enemies = FindObjectsByType<EnemyDuckAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
        string cpuNote = CpuBotCount > 0 ? "　電腦鴨 " + CpuBotCount + " 隻已加入" : string.Empty;
        if (localPlayer != null && localPlayer.IsEliminated)
        {
            objectiveText.text = "你已陣亡　等待本局結束" + cpuNote;
            return;
        }

        string health = localPlayer != null
            ? "　生命 " + localPlayer.CurrentHealth + " / " + FusionDuckPlayer.MaximumHealth
            : string.Empty;
        string shield = localPlayer != null && localPlayer.HasGuardianShield
            ? "　護盾 " + localPlayer.GuardianShieldRemaining.ToString("0.0") + " 秒"
            : string.Empty;
        Unit1WandPickup heldWand = localPlayer != null
            ? localPlayer.GetComponent<DuckMover>()?.HeldNetworkWand
            : null;
        string wandHint = heldWand != null
            ? "　「" + heldWand.AbilityDisplayName + "」" + heldWand.AbilityDescription
            : string.Empty;
        objectiveText.text = !holdingWand
            ? "你是「" + faction + "」　靠近手杖後按 E 拾取" + health + cpuNote
            : "你是「" + faction + "」" + wandHint + "　剩餘灰影 " + enemies + " 隻" + health + shield + cpuNote;
    }

    private void TryAssignRandomFactions()
    {
        List<PlayerRef> players = new();
        int activePlayerCount = 0;
        foreach (PlayerRef playerRef in Runner.ActivePlayers)
        {
            activePlayerCount++;
            if (Runner.TryGetPlayerObject(playerRef, out NetworkObject playerObject)
                && playerObject != null
                && playerObject.GetComponent<FusionDuckPlayer>() != null)
            {
                // Each travelling duck must have handed StateAuthority to its
                // own player before that client can store its faction result.
                // Retry next tick rather than losing a remote player's draw.
                if (playerObject.StateAuthority != playerRef)
                {
                    return;
                }

                players.Add(playerRef);
            }
        }

        if (activePlayerCount < MinimumHumanPlayers
            || activePlayerCount > MaximumFactionPlayers
            || players.Count != activePlayerCount)
        {
            return;
        }

        int requestedCpuCount = Mathf.Max(0, MinimumBattleParticipants - activePlayerCount);
        if (!CpuBotsSpawned)
        {
            if (requestedCpuCount > 0 && cpuDuckPrefab == null)
            {
                Debug.LogError("[UNIT1] 缺少 CPU Duck prefab，無法補齊電腦鴨。", this);
                return;
            }

            CpuBotCount = requestedCpuCount;
            CpuBotsSpawned = true;
            SpawnCpuBots(requestedCpuCount, players);

            // Spawned objects finish appearing on the next network tick.
            // Waiting here prevents a CPU from missing its faction draw.
            if (requestedCpuCount > 0)
            {
                return;
            }
        }

        List<Unit1BotDuck> cpuBots = GetCpuBots();
        if (cpuBots.Count != CpuBotCount)
        {
            return;
        }

        players.Sort((left, right) => left.AsIndex.CompareTo(right.AsIndex));
        cpuBots.Sort((left, right) => left.BotNumber.CompareTo(right.BotNumber));
        int[] factionIndices = (int[])FactionIndices.Clone();
        System.Random random = new(FactionShuffleSeed);
        for (int i = factionIndices.Length - 1; i > 0; i--)
        {
            int other = random.Next(i + 1);
            (factionIndices[i], factionIndices[other]) = (factionIndices[other], factionIndices[i]);
        }

        for (int i = 0; i < players.Count; i++)
        {
            RPC_AssignRandomFaction(players[i], factionIndices[i]);
        }

        for (int i = 0; i < cpuBots.Count; i++)
        {
            cpuBots[i].AssignFaction(factionIndices[players.Count + i]);
        }

        FactionsAssigned = true;
        Debug.Log("[UNIT1] Random factions assigned for " + players.Count + " human players and " + cpuBots.Count + " CPU ducks.");
    }

    private void SpawnCpuBots(int count, List<PlayerRef> humanPlayers)
    {
        bool[] occupiedSpawnSlots = new bool[playerSpawnPoints.Length];
        for (int i = 0; i < humanPlayers.Count && occupiedSpawnSlots.Length > 0; i++)
        {
            int humanSlot = Mathf.Abs(humanPlayers[i].AsIndex) % occupiedSpawnSlots.Length;
            occupiedSpawnSlots[humanSlot] = true;
        }

        int nextSearchSlot = 0;
        for (int i = 0; i < count; i++)
        {
            int spawnIndex = FindOpenSpawnSlot(occupiedSpawnSlots, ref nextSearchSlot);
            Transform spawnPoint = spawnIndex >= 0 ? playerSpawnPoints[spawnIndex] : null;
            Vector3 position = spawnPoint != null
                ? spawnPoint.position
                : new Vector3(3f + i * 4f, 0f, 15f);
            Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

            NetworkObject botObject = Runner.Spawn(cpuDuckPrefab, position, rotation);
            Unit1BotDuck bot = botObject != null ? botObject.GetComponent<Unit1BotDuck>() : null;
            if (bot != null)
            {
                bot.ConfigureBot(i + 1);
            }
        }
    }

    private static int FindOpenSpawnSlot(bool[] occupiedSlots, ref int nextSearchSlot)
    {
        for (int offset = 0; offset < occupiedSlots.Length; offset++)
        {
            int slot = (nextSearchSlot + offset) % occupiedSlots.Length;
            if (occupiedSlots[slot])
            {
                continue;
            }

            occupiedSlots[slot] = true;
            nextSearchSlot = (slot + 1) % occupiedSlots.Length;
            return slot;
        }

        return -1;
    }

    private static List<Unit1BotDuck> GetCpuBots()
    {
        Unit1BotDuck[] found = FindObjectsByType<Unit1BotDuck>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        List<Unit1BotDuck> bots = new(found.Length);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null)
            {
                bots.Add(found[i]);
            }
        }

        return bots;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_AssignRandomFaction([RpcTarget] PlayerRef target, int factionIndex)
    {
        if (Runner == null
            || target != Runner.LocalPlayer
            || !Runner.TryGetPlayerObject(Runner.LocalPlayer, out NetworkObject localObject)
            || localObject == null)
        {
            return;
        }

        localObject.GetComponent<FusionDuckPlayer>()?.AssignRandomFaction(factionIndex);
    }
}
