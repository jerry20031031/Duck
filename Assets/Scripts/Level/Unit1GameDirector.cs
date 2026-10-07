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

    [SerializeField] private NetworkObject greyShadowDuckPrefab;
    [SerializeField] private NetworkObject cpuDuckPrefab;
    [SerializeField] private NetworkObject factionCrystalPrefab;
    [SerializeField] private Transform[] playerSpawnPoints = System.Array.Empty<Transform>();
    [SerializeField] private Transform[] enemySpawnPoints = System.Array.Empty<Transform>();
    [SerializeField] private Transform[] factionCrystalSpawnPoints = System.Array.Empty<Transform>();
    [SerializeField, Min(1)] private int enemyCount = 6;
    [SerializeField, Min(2f)] private float minimumNpcSpawnSpacing = 8f;

    [Networked] public NetworkBool FactionsAssigned { get; private set; }
    [Networked] public NetworkBool CpuBotsSpawned { get; private set; }
    [Networked] public int CpuBotCount { get; private set; }
    [Networked] public NetworkBool MatchObjectivesReady { get; private set; }
    [Networked] public NetworkBool MatchFinished { get; private set; }
    [Networked] public NetworkBool IsDraw { get; private set; }
    [Networked] public int WinningFactionIndex { get; private set; }
    [Networked] private int FactionShuffleSeed { get; set; }

    private bool enemiesSpawned;
    private bool crystalsSpawned;
    private TextMeshProUGUI objectiveText;
    private GameObject resultPanel;
    private TextMeshProUGUI resultText;
    private float factionAssignmentTime;

    public static bool IsMatchFinished()
    {
        Unit1GameDirector director = FindFirstObjectByType<Unit1GameDirector>();
        // A late-joining client can see the Unity scene object one frame
        // before Fusion attaches it to its runner. Networked properties are
        // unavailable in that interval, so treat the match as still active
        // rather than throwing and halting every AI update.
        return director != null
            && director.Object != null
            && director.Object.IsValid
            && director.MatchFinished;
    }

    public void Configure(
        NetworkObject enemyPrefab,
        NetworkObject botPrefab,
        NetworkObject crystalPrefab,
        Transform[] playerPoints,
        Transform[] enemyPoints,
        Transform[] crystalPoints,
        int count)
    {
        greyShadowDuckPrefab = enemyPrefab;
        cpuDuckPrefab = botPrefab;
        factionCrystalPrefab = crystalPrefab;
        playerSpawnPoints = playerPoints ?? System.Array.Empty<Transform>();
        enemySpawnPoints = enemyPoints ?? System.Array.Empty<Transform>();
        factionCrystalSpawnPoints = crystalPoints ?? System.Array.Empty<Transform>();
        enemyCount = Mathf.Max(1, count);
    }

    public override void Spawned()
    {
        Debug.Log("[UNIT1-DIAG Director] spawned | sharedMaster=" + Runner.IsSharedModeMasterClient);
        if (Runner.IsSharedModeMasterClient)
        {
            FactionShuffleSeed = UnityEngine.Random.Range(1, int.MaxValue);
            factionAssignmentTime = Time.time + FactionAssignmentDelay;
            MatchObjectivesReady = false;
            SpawnEnemies();
            SpawnFactionCrystals();
        }

        CreateObjectiveHud();
    }

    public override void Render()
    {
        UpdateObjectiveHud();
        UpdateResultHud();
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority && !FactionsAssigned && Time.time >= factionAssignmentTime)
        {
            TryAssignRandomFactions();
        }

        if (HasStateAuthority && FactionsAssigned && !MatchObjectivesReady)
        {
            MatchObjectivesReady = AreInitialObjectivesReady();
            if (MatchObjectivesReady)
            {
                Debug.Log("[UNIT1] Opening state synchronized; CPU ducks may begin their AI.");
            }
        }

        if (HasStateAuthority && MatchObjectivesReady && !MatchFinished)
        {
            EvaluateMatchEnd();
        }
    }

    private void SpawnEnemies()
    {
        if (enemiesSpawned || greyShadowDuckPrefab == null)
        {
            return;
        }

        enemiesSpawned = true;
        List<Vector3> references = GetNpcSpawnReferences();
        List<Vector3> occupied = GetOccupiedNpcSpawnPositions();
        CapsuleCollider shape = greyShadowDuckPrefab.GetComponent<CapsuleCollider>();
        for (int i = 0; i < enemyCount; i++)
        {
            if (!Unit1RouteScanner.TryFindSpawnPoint(shape, references, occupied, minimumNpcSpawnSpacing, out Vector3 position))
            {
                Debug.LogWarning("[UNIT1] 找不到安全平地，略過灰影生成；不使用未驗證的座標。", this);
                break;
            }
            NetworkObject enemy = Runner.Spawn(greyShadowDuckPrefab, position, RandomSpawnRotation());
            if (enemy != null)
            {
                occupied.Add(position);
                enemy.name = "灰影小鴨 " + (i + 1);
                Debug.Log("[UNIT1-DIAG Director] spawned enemy " + (i + 1) + " | object=" + enemy.Id + " | randomFlat=" + position);
            }
        }
    }

    /// <summary>
    /// Markers define connected playable areas, not the final NPC positions.
    /// Keep human spawn locations reserved even before travelling players arrive.
    /// </summary>
    private List<Vector3> GetNpcSpawnReferences()
    {
        List<Vector3> references = new();
        foreach (Transform point in playerSpawnPoints) if (point != null) references.Add(point.position);
        foreach (Transform point in enemySpawnPoints) if (point != null) references.Add(point.position);
        return references;
    }

    private List<Vector3> GetOccupiedNpcSpawnPositions()
    {
        List<Vector3> occupied = new();
        foreach (Transform point in playerSpawnPoints) if (point != null) occupied.Add(point.position);
        foreach (FusionDuckPlayer player in FindObjectsByType<FusionDuckPlayer>(FindObjectsSortMode.None))
            occupied.Add(player.transform.position);
        foreach (Unit1BotDuck bot in FindObjectsByType<Unit1BotDuck>(FindObjectsSortMode.None))
            occupied.Add(bot.transform.position);
        foreach (EnemyDuckAI grey in FindObjectsByType<EnemyDuckAI>(FindObjectsSortMode.None))
            occupied.Add(grey.transform.position);
        foreach (Transform point in factionCrystalSpawnPoints) if (point != null) occupied.Add(point.position);
        return occupied;
    }

    private static Quaternion RandomSpawnRotation() => Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

    private void SpawnFactionCrystals()
    {
        if (crystalsSpawned || factionCrystalPrefab == null || factionCrystalSpawnPoints.Length == 0)
        {
            return;
        }

        crystalsSpawned = true;
        int crystalCount = Mathf.Min(FactionTeam.Count, factionCrystalSpawnPoints.Length);
        // The transform locations belong to the scene designer. Only the
        // faction assigned to each location is shuffled per match.
        int[] factionOrder = FactionTeam.CreateShuffledFactionOrder(FactionShuffleSeed);
        for (int index = 0; index < crystalCount; index++)
        {
            Transform spawnPoint = factionCrystalSpawnPoints[index];
            if (spawnPoint == null)
            {
                continue;
            }

            NetworkObject crystalObject = Runner.Spawn(
                factionCrystalPrefab,
                spawnPoint.position,
                spawnPoint.rotation);
            FactionCrystal crystal = crystalObject != null ? crystalObject.GetComponent<FactionCrystal>() : null;
            if (crystal == null)
            {
                continue;
            }

            int factionIndex = factionOrder[index];
            crystal.name = FactionTeam.GetName(factionIndex) + "方水晶";
            crystal.AssignFaction(factionIndex);
            Debug.Log("[UNIT1] spawned " + crystal.name + " | object=" + crystalObject.Id);
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

        CreateResultHud(canvasObject.transform);
    }

    private void CreateResultHud(Transform canvasRoot)
    {
        resultPanel = new GameObject("UNIT1 Match Result", typeof(RectTransform), typeof(Image));
        resultPanel.transform.SetParent(canvasRoot, false);
        Image panelImage = resultPanel.GetComponent<Image>();
        panelImage.color = new Color(0.025f, 0.04f, 0.09f, 0.91f);
        RectTransform panelRect = resultPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(670f, 300f);

        GameObject textObject = new("Result Text", typeof(RectTransform));
        textObject.transform.SetParent(resultPanel.transform, false);
        resultText = textObject.AddComponent<TextMeshProUGUI>();
        resultText.font = TMP_Settings.defaultFontAsset;
        resultText.fontSize = 34f;
        resultText.alignment = TextAlignmentOptions.Center;
        resultText.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        resultText.outlineWidth = 0.18f;
        RectTransform textRect = resultText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(34f, 28f);
        textRect.offsetMax = new Vector2(-34f, -28f);
        resultPanel.SetActive(false);
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

        if (MatchFinished)
        {
            objectiveText.text = IsDraw
                ? "本局平手　多方陣營同時淘汰"
                : "「" + FactionTeam.GetName(WinningFactionIndex) + "色陣營」獲勝！　對局已結算";
            return;
        }

        if (!MatchObjectivesReady)
        {
            objectiveText.text = "正在同步水晶與電腦鴨…";
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
        FactionCrystal friendlyCrystal = localPlayer != null
            ? FactionCrystal.FindForFaction(localPlayer.FactionIndex)
            : null;
        string crystalStatus = friendlyCrystal == null
            ? "　水晶同步中"
            : friendlyCrystal.IsDestroyed
                ? "　己方水晶已毀壞"
                : "　水晶 " + friendlyCrystal.CurrentHealth + " / " + friendlyCrystal.MaximumHealth
                  + (friendlyCrystal.HasShield ? "　水晶護盾" : string.Empty);
        Unit1WandPickup heldWand = localPlayer != null
            ? localPlayer.GetComponent<DuckMover>()?.HeldNetworkWand
            : null;
        string wandHint = heldWand != null
            ? "　「" + heldWand.AbilityDisplayName + "」" + heldWand.AbilityDescription
            : string.Empty;
        objectiveText.text = !holdingWand
            ? "你是「" + faction + "」　靠近手杖後按 E 拾取" + crystalStatus + health + cpuNote
            : "你是「" + faction + "」" + wandHint + "　剩餘灰影 " + enemies + " 隻" + crystalStatus + health + shield + cpuNote;
    }

    private void UpdateResultHud()
    {
        if (resultPanel == null || resultText == null)
        {
            return;
        }

        if (resultPanel.activeSelf != MatchFinished)
        {
            resultPanel.SetActive(MatchFinished);
        }
        if (!MatchFinished)
        {
            return;
        }

        int livingCoreDucks = 0;
        FusionDuckPlayer[] players = FindObjectsByType<FusionDuckPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int index = 0; index < players.Length; index++)
        {
            if (players[index] != null && !players[index].IsEliminated)
            {
                livingCoreDucks++;
            }
        }
        Unit1BotDuck[] bots = FindObjectsByType<Unit1BotDuck>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int index = 0; index < bots.Length; index++)
        {
            if (bots[index] != null && !bots[index].IsEliminated)
            {
                livingCoreDucks++;
            }
        }

        int destroyedCrystals = 0;
        FactionCrystal[] crystals = FindObjectsByType<FactionCrystal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int index = 0; index < crystals.Length; index++)
        {
            if (crystals[index] != null && crystals[index].IsDestroyed)
            {
                destroyedCrystals++;
            }
        }

        int winningShadows = 0;
        EnemyDuckAI[] shadows = FindObjectsByType<EnemyDuckAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int index = 0; index < shadows.Length; index++)
        {
            if (shadows[index] != null && shadows[index].FactionIndex == WinningFactionIndex)
            {
                winningShadows++;
            }
        }

        if (IsDraw)
        {
            resultText.color = Color.white;
            resultText.text = "本局平手\n\n多方陣營同時淘汰\n已摧毀水晶　" + destroyedCrystals + "　／　" + FactionTeam.Count;
            return;
        }

        resultText.color = FactionTeam.GetColor(WinningFactionIndex);
        resultText.text = "「" + FactionTeam.GetName(WinningFactionIndex) + "色陣營」獲勝！\n\n"
            + "存活真人／CPU　" + livingCoreDucks + "\n"
            + "已摧毀水晶　" + destroyedCrystals + "　　轉化鴨　" + winningShadows;
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

            CpuBotCount = SpawnCpuBots(requestedCpuCount, players);
            CpuBotsSpawned = true;

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
        int[] factionIndices = FactionTeam.CreateBalancedAssignments(
            players.Count,
            cpuBots.Count,
            FactionShuffleSeed);

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

    /// <summary>
    /// Do not evaluate victory on the same tick that teams are assigned.
    /// Network-spawned crystals can still expose their default faction or
    /// health for one replication tick, which previously made the director
    /// declare an instant winner and freeze every CPU before it moved.
    /// </summary>
    private static bool AreInitialObjectivesReady()
    {
        for (int faction = FactionTeam.Red; faction <= FactionTeam.Count; faction++)
        {
            FactionCrystal crystal = FactionCrystal.FindForFaction(faction);
            if (crystal == null || crystal.IsDestroyed || crystal.CurrentHealth <= 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A faction survives only while it still has an intact crystal and at
    /// least one living human or CPU duck. Converted shadows deliberately do
    /// not postpone elimination: they are support units, not match lives.
    /// </summary>
    private void EvaluateMatchEnd()
    {
        bool[] participating = new bool[FactionTeam.Count + 1];
        bool[] hasLivingCoreDuck = new bool[FactionTeam.Count + 1];

        FusionDuckPlayer[] players = FindObjectsByType<FusionDuckPlayer>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int index = 0; index < players.Length; index++)
        {
            FusionDuckPlayer player = players[index];
            if (player == null || !FactionTeam.IsAssigned(player.FactionIndex))
            {
                continue;
            }

            participating[player.FactionIndex] = true;
            if (!player.IsEliminated)
            {
                hasLivingCoreDuck[player.FactionIndex] = true;
            }
        }

        Unit1BotDuck[] bots = FindObjectsByType<Unit1BotDuck>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int index = 0; index < bots.Length; index++)
        {
            Unit1BotDuck bot = bots[index];
            if (bot == null || !FactionTeam.IsAssigned(bot.FactionIndex))
            {
                continue;
            }

            participating[bot.FactionIndex] = true;
            if (!bot.IsEliminated)
            {
                hasLivingCoreDuck[bot.FactionIndex] = true;
            }
        }

        int survivorCount = 0;
        int survivingFaction = FactionTeam.Neutral;
        int participantCount = 0;
        for (int faction = FactionTeam.Red; faction <= FactionTeam.Count; faction++)
        {
            if (!participating[faction])
            {
                continue;
            }

            participantCount++;
            FactionCrystal crystal = FactionCrystal.FindForFaction(faction);
            bool survives = crystal != null && !crystal.IsDestroyed && hasLivingCoreDuck[faction];
            if (survives)
            {
                survivorCount++;
                survivingFaction = faction;
            }
        }

        // Do not end a room while late-spawned network objects have not yet
        // received their faction assignments or crystal replicas.
        if (participantCount < 2 || survivorCount > 1)
        {
            return;
        }

        MatchFinished = true;
        IsDraw = survivorCount == 0;
        WinningFactionIndex = survivorCount == 1 ? survivingFaction : FactionTeam.Neutral;
        Debug.Log(IsDraw
            ? "[UNIT1] Match ended in a draw."
            : "[UNIT1] Match winner: " + FactionTeam.GetName(WinningFactionIndex) + " faction.");
    }

    private int SpawnCpuBots(int count, List<PlayerRef> humanPlayers)
    {
        if (cpuDuckPrefab == null) return 0;
        List<Vector3> references = GetNpcSpawnReferences();
        List<Vector3> occupied = GetOccupiedNpcSpawnPositions();
        foreach (PlayerRef human in humanPlayers)
            if (Runner.TryGetPlayerObject(human, out NetworkObject player) && player != null)
                occupied.Add(player.transform.position);
        CapsuleCollider shape = cpuDuckPrefab.GetComponent<CapsuleCollider>();
        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            if (!Unit1RouteScanner.TryFindSpawnPoint(shape, references, occupied, minimumNpcSpawnSpacing, out Vector3 position))
            {
                Debug.LogWarning("[UNIT1] 找不到安全平地，略過 CPU 生成；不使用未驗證的座標。", this);
                break;
            }
            NetworkObject botObject = Runner.Spawn(cpuDuckPrefab, position, RandomSpawnRotation());
            Unit1BotDuck bot = botObject != null ? botObject.GetComponent<Unit1BotDuck>() : null;
            if (bot != null)
            {
                occupied.Add(position);
                bot.ConfigureBot(++spawned);
                Debug.Log("[UNIT1-DIAG Director] spawned CPU " + spawned + " | object=" + botObject.Id + " | randomFlat=" + position);
            }
        }
        return spawned;
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
