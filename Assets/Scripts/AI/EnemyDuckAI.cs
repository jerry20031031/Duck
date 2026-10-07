using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The first-level Grey Shadow. It can independently gather a wand, pursue a
/// target, and cast a telegraphed shadow spell; all decisions run on its
/// Fusion state authority.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
[RequireComponent(typeof(Rigidbody))]
public sealed class EnemyDuckAI : NetworkBehaviour
{
    [Header("AI Debug")]
    [Tooltip("只在灰影 State 或 Target 改變時輸出目前決策；Scene View 也會畫出路徑。")]
    [SerializeField] private bool debugAI;
    [Tooltip("比照 CPU，全圖選擇手杖／敵人並沿路換巡邏點，不受出生點半徑限制。")]
    [SerializeField] private bool useWholeMapObjectives = true;

    private enum EnemyState
    {
        Patrol,
        SeekWand,
        Chase,
        Cast,
        Stunned,
        Defeated
    }

    /// <summary>灰影的戰術選擇；實際走路與施法由各自的執行方法負責。</summary>
    private enum ShadowAction
    {
        Wander,
        SeekWand,
        AttackTarget,
        DefendCrystal,
        AttackCrystal,
        FollowAlly
    }

    [Header("Player target")]
    [SerializeField] private Transform player;
    [SerializeField] private bool onlyChasePlayerWithWand = true;
    [SerializeField, Min(0.5f)] private float detectionRange = 7f;
    [SerializeField, Min(0.25f)] private float stoppingDistance = 1.3f;

    [Header("Wand intelligence")]
    [SerializeField, Min(0.5f)] private float wandSearchRange = 18f;
    [SerializeField, Min(0.25f)] private float shadowSpellRange = 4.2f;
    [SerializeField, Min(0f)] private float shadowSpellCooldown = 1.15f;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float patrolSpeed = 2.2f;
    [SerializeField, Min(0f)] private float chaseSpeed = 3.45f;
    [SerializeField, Min(0f)] private float patrolRadius = 2.2f;
    [SerializeField, Min(0f)] private float turningSpeed = 540f;
    [SerializeField, Min(0f)] private float patrolWaitTime = 0.7f;
    private const float MatchVisualScale = 0.88f;
    private const float PreferredSpellDistanceFactor = 0.72f;
    private const float UtilityRefreshInterval = 0.45f;
    private const float MinimumActionDuration = 0.8f;
    private const float ActionSwitchThreshold = 10f;
    private const float StopDistance = 0.32f;
    private const float RecoveryDuration = 1.35f;
    private const float AutoJumpSpeed = 7f;
    private const float AutoJumpGravity = 24f;

    [Header("First-level health")]
    [SerializeField, Min(1)] private int maximumHealth = 2;
    [SerializeField, Min(0f)] private float hitStunTime = 0.38f;
    [SerializeField, Min(0f)] private float hitKnockback = 3.2f;
    [SerializeField, Min(0f)] private float hitDamageImmunityTime = 0.1f;
    [SerializeField, Min(0f)] private float defeatedDespawnDelay = 1.25f;

    [Header("Random skin and faction-marker appearance")]
    [SerializeField] private string enemyName = "灰影小鴨";
    [SerializeField] private Color bodyColor = new(0.95f, 0.78f, 0.3f, 1f);
    [SerializeField] private Color markerColor = new Color(1f, 0.2f, 0.15f, 1f);
    [SerializeField] private bool showEnemyMarker = true;
    [SerializeField] private GameObject[] stableDuckVisuals = Array.Empty<GameObject>();

    [Header("Events")]
    [SerializeField] private UnityEvent onDefeated;

    public event Action<EnemyDuckAI> Defeated;

    [Networked] public int FactionIndex { get; private set; }
    [Networked] public int CurrentHealth { get; private set; }
    [Networked] private int SkinColorIndex { get; set; }
    public bool IsDefeated => state == EnemyState.Defeated;
    public bool IsConverted => FactionTeam.IsAssigned(FactionIndex);
    public bool HasGuardianShield => !IsDefeated && Time.time < guardianShieldUntil;
    [Networked] public NetworkBool HasWand { get; private set; }

    private Rigidbody body;
    private Collider bodyCollider;
    private Animator animator;
    private DuckMover playerMover;
    private FusionDuckPlayer humanPlayer;
    private Unit1BotDuck cpuPlayer;
    private Unit1WandPickup heldWand;
    private Unit1WandPickup targetWand;
    private FactionCrystal targetCrystal;
    private TextMeshPro statusLabel;
    private Transform marker;
    private MeshFilter markerMeshFilter;
    private Material markerMaterial;
    private MaterialPropertyBlock materialProperties;
    private Vector3 homePosition;
    private Vector3 patrolTarget;
    private float nextPatrolTime;
    private float patrolTargetExpiresAt;
    private int patrolStep;
    private float stunnedUntil;
    private float hitDamageImmuneUntil;
    private float guardianShieldUntil;
    private float nextPlayerSearchTime;
    private float nextWandSearchTime;
    private float nextShadowSpellTime;
    private float nextUtilityDecisionAt;
    private float plannedActionSince;
    private float plannedActionScore;
    private Vector3 detourTarget;
    private readonly List<Vector3> plannedRoute = new();
    private int plannedRouteIndex;
    private Vector3 plannedRouteDestination;
    private float nextRoutePlanAt;
    private float detourUntil;
    private int blockedMoveCount;
    private Vector3 lastProgressPosition;
    private float nextProgressCheckAt;
    private int stalledRouteCount;
    private Vector3 recoveryDestination;
    private int recoverySide;
    private int recoveryAttempts;
    private int failedRecoveryCount;
    // Remember several failed goals: overwriting one failure with another
    // otherwise makes a shadow alternate between two unreachable wands.
    private readonly Unit1NavigationMemory unreachableGoals = new();
    private float verticalSpeed;
    private bool isGrounded = true;
    private float nextAutoJumpAt;
    private readonly RaycastHit[] groundProbeHits = new RaycastHit[16];
    private int currentAnimationState;
    private float wandAttackUntil;
    // Local generation invalidates all pre-conversion spells, without stopping
    // unrelated presentation coroutines such as the hit-marker flash.
    private int pendingCastRevision;
    private EnemyState state;
    private ShadowAction plannedAction = ShadowAction.Wander;
    private bool hasReportedAuthority;
    private bool reportedStateAuthority;
    private bool reportedBodyKinematic;
    private bool hasReportedFirstMove;
    private bool networkSpawned;
    private bool matchVisualScaleApplied;
    private int displayedFactionIndex = int.MinValue;
    private int displayedSkinColorIndex = int.MinValue;
    private EnemyState lastLoggedState = (EnemyState)(-1);
    private Transform lastLoggedTarget;

    private static readonly Color[] RandomSkinColors =
    {
        new(0.95f, 0.78f, 0.3f, 1f),
        new(0.96f, 0.9f, 0.76f, 1f),
        new(0.68f, 0.46f, 0.26f, 1f),
        new(0.82f, 0.62f, 0.42f, 1f),
        new(0.72f, 0.8f, 0.82f, 1f)
    };

    private static Mesh triangleMarkerMesh;
    private static Mesh squareMarkerMesh;
    private static Mesh circleMarkerMesh;
    private static Mesh diamondMarkerMesh;

    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int SpeedProperty = Animator.StringToHash("Speed");
    private static readonly int GroundedProperty = Animator.StringToHash("Grounded");
    private static readonly int JumpProperty = Animator.StringToHash("Jump");
    private static readonly int JumpState = Animator.StringToHash("jump");
    private static readonly int IdleState = Animator.StringToHash("idle");
    private static readonly int WalkingState = Animator.StringToHash("Walking");
    private static readonly int WandAttackState = Animator.StringToHash("stand attack");

    /// <summary>Called by the prefab builder with the known-stable people/1 and people/2 ducks.</summary>
    public void ConfigureStableDuckVisuals(GameObject[] visuals)
    {
        stableDuckVisuals = visuals ?? Array.Empty<GameObject>();
    }

    /// <summary>A neutral shadow is contestable by everyone; a converted one is only a valid enemy for another faction.</summary>
    public bool CanBeTargetedByFaction(int factionIndex)
    {
        return !IsDefeated && (!IsConverted || FactionTeam.AreEnemies(factionIndex, FactionIndex));
    }

    public bool IsFactionEnemyOf(int factionIndex)
    {
        return IsConverted && FactionTeam.AreEnemies(factionIndex, FactionIndex);
    }

    public override void Spawned()
    {
        networkSpawned = true;
        // Awake runs while Fusion is instantiating the prefab, before the
        // Runner has applied this enemy's requested spawn transform. Capture
        // the initial position here, not world origin (0,0,0). Whole-map
        // exploration picks later legs from the current position; the legacy
        // local patrol option still uses this spawn point as its origin.
        homePosition = transform.position;
        patrolTarget = homePosition;
        nextPatrolTime = Time.time;
        lastProgressPosition = body != null ? body.position : transform.position;
        nextProgressCheckAt = Time.time + 0.45f;
        Unit1RouteScanner.PrimeMapScan(homePosition, bodyCollider);

        ApplyStableVisualVariant();
        animator = GetComponentInChildren<Animator>();

        // The state owner advances a collision-aware kinematic body in the
        // Fusion tick. Every other client receives NetworkTransform snapshots.
        if (HasStateAuthority)
        {
            ConfigureBody();
            HasWand = false;
            FactionIndex = FactionTeam.Neutral;
            CurrentHealth = Mathf.Max(1, maximumHealth);
            SkinColorIndex = UnityEngine.Random.Range(0, RandomSkinColors.Length);
        }
        else if (body != null)
        {
            body.isKinematic = true;
            body.useGravity = false;
        }

        // Fusion has now bound the networked state, so this is the first safe
        // point to use SkinColorIndex. Awake only applies the prefab's local
        // fallback colour while the object is being constructed.
        ApplyRandomSkinAppearance();
        displayedSkinColorIndex = SkinColorIndex;
        ReportRuntimeState("spawned");
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<Collider>();
        animator = GetComponentInChildren<Animator>(true);
        homePosition = transform.position;
        patrolTarget = homePosition;

        // A DuckMover reads player keyboard input, so an enemy must never leave it enabled.
        DuckMover mover = GetComponent<DuckMover>();
        if (mover != null)
        {
            mover.enabled = false;
        }

        ConfigureBody();
        // A NetworkBehaviour's [Networked] properties are unavailable here.
        // Keep the prefab's serialized colour until Spawned applies the
        // synchronized random colour.
        ApplySkinColor();
        ApplyMatchVisualScale();
        CreateStatusVisuals();
        PlayMovementAnimation(false);
    }

    private void ApplyStableVisualVariant()
    {
        if (stableDuckVisuals == null || stableDuckVisuals.Length == 0)
        {
            return;
        }

        int selectedIndex = Object != null
            ? (int)(Object.Id.Raw % (uint)stableDuckVisuals.Length)
            : 0;
        for (int i = 0; i < stableDuckVisuals.Length; i++)
        {
            if (stableDuckVisuals[i] != null)
            {
                stableDuckVisuals[i].SetActive(i == selectedIndex);
            }
        }
    }

    private void OnEnable()
    {
        if (state == EnemyState.Defeated)
        {
            state = EnemyState.Patrol;
        }
    }

    private void OnDisable()
    {
        InvalidatePendingCasts();
    }

    public override void FixedUpdateNetwork()
    {
        ReportRuntimeState("network tick");
        if (!HasStateAuthority)
        {
            return;
        }

        if (Unit1GameDirector.IsMatchFinished())
        {
            StopMoving();
            return;
        }

        // Move directly in Fusion's simulation tick. Rigidbody velocity is
        // intentionally not used: NetworkTransform publishes transforms, not
        // an independently integrated Unity physics result.
        if (body == null)
        {
            return;
        }

        if (state == EnemyState.Defeated)
        {
            StopMoving();
            return;
        }

        AdvanceVerticalMotion();

        if (Time.time < wandAttackUntil)
        {
            StopMoving();
            return;
        }

        if (Time.time < stunnedUntil)
        {
            state = EnemyState.Stunned;
            StopMoving();
            return;
        }

        RefreshHeldWand();
        RefreshUtilityPlan();
        ExecuteUtilityPlan();
    }

    private void LateUpdate()
    {
        UpdateStatusVisuals();
        LogAiDecisionIfChanged();
    }

    /// <summary>
    /// Applies a wand hit from a networked duck. The final hitter's actual
    /// faction is resolved on this shadow's authority, which makes conversion
    /// authoritative instead of trusting a client-supplied team value.
    /// </summary>
    public bool TakeWandHit(
        NetworkObject attackerObject,
        Vector3 hitSource,
        int damage = 1,
        float stunDuration = 0f,
        float knockbackMultiplier = 1f)
    {
        NetworkId attackerObjectId = attackerObject != null ? attackerObject.Id : NetworkId.None;
        if (Object != null && !HasStateAuthority)
        {
            RPC_RequestWandHit(attackerObjectId, hitSource, damage, stunDuration, knockbackMultiplier);
            return true;
        }

        return ApplyWandHit(attackerObjectId, hitSource, damage, stunDuration, knockbackMultiplier);
    }

    /// <summary>Convenience overload for editor diagnostics without a conversion owner.</summary>
    public bool TakeWandHit(
        Vector3 hitSource,
        int damage = 1,
        float stunDuration = 0f,
        float knockbackMultiplier = 1f)
    {
        return TakeWandHit(null, hitSource, damage, stunDuration, knockbackMultiplier);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestWandHit(
        NetworkId attackerObjectId,
        Vector3 hitSource,
        int damage,
        float stunDuration,
        float knockbackMultiplier)
    {
        ApplyWandHit(attackerObjectId, hitSource, damage, stunDuration, knockbackMultiplier);
    }

    private bool ApplyWandHit(
        NetworkId attackerObjectId,
        Vector3 hitSource,
        int damage,
        float stunDuration,
        float knockbackMultiplier)
    {
        if (IsDefeated
            || HasGuardianShield
            || Time.time < hitDamageImmuneUntil
            || damage <= 0
            || !TryGetAttackerFaction(attackerObjectId, out int attackerFaction)
            || (IsConverted && !FactionTeam.AreEnemies(FactionIndex, attackerFaction)))
        {
            return false;
        }

        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        hitDamageImmuneUntil = Time.time + hitDamageImmunityTime;
        stunnedUntil = Time.time + Mathf.Max(hitStunTime, stunDuration);
        ApplyKnockback(hitSource, knockbackMultiplier);
        StartCoroutine(FlashMarker());

        if (CurrentHealth == 0)
        {
            ConvertToFaction(attackerFaction);
        }

        return true;
    }

    private bool TryGetAttackerFaction(NetworkId attackerObjectId, out int attackerFaction)
    {
        attackerFaction = FactionTeam.Neutral;
        NetworkObject attacker = Runner != null && attackerObjectId != NetworkId.None
            ? Runner.FindObject(attackerObjectId)
            : null;
        if (attacker == null)
        {
            return false;
        }

        FusionDuckPlayer playerAttacker = attacker.GetComponent<FusionDuckPlayer>();
        if (playerAttacker != null)
        {
            attackerFaction = playerAttacker.FactionIndex;
        }
        else
        {
            Unit1BotDuck cpuAttacker = attacker.GetComponent<Unit1BotDuck>();
            if (cpuAttacker != null)
            {
                attackerFaction = cpuAttacker.FactionIndex;
            }
            else
            {
                EnemyDuckAI shadowAttacker = attacker.GetComponent<EnemyDuckAI>();
                attackerFaction = shadowAttacker != null ? shadowAttacker.FactionIndex : FactionTeam.Neutral;
            }
        }

        return FactionTeam.IsAssigned(attackerFaction);
    }

    private void ConvertToFaction(int factionIndex)
    {
        InvalidatePendingCasts();
        FactionIndex = FactionTeam.Normalize(factionIndex);
        CurrentHealth = Mathf.Max(1, maximumHealth);
        stunnedUntil = 0f;
        state = EnemyState.Patrol;
        // The old target/route belonged to a different faction. Reconsider
        // immediately, but retain the spell cooldown and current wand.
        player = null;
        playerMover = null;
        humanPlayer = null;
        cpuPlayer = null;
        targetCrystal = null;
        nextPlayerSearchTime = nextUtilityDecisionAt = 0f;
        plannedAction = ShadowAction.Wander;
        plannedActionScore = 0f;
        plannedActionSince = Time.time - MinimumActionDuration;
        detourUntil = 0f;
        Unit1RouteScanner.InvalidateRoute(plannedRoute, ref plannedRouteIndex, ref nextRoutePlanAt);
        ApplyFactionAppearance();
        DuckWandAttack.CreateGuardianAura(transform, FactionTeam.GetColor(FactionIndex), 0.75f);
        UpdateStatusVisuals();
    }

    [ContextMenu("Test Wand Hit")]
    private void TestWandHit()
    {
        TakeWandHit(transform.position - transform.forward * 2f);
    }

    private void ConfigureBody()
    {
        if (body == null)
        {
            return;
        }

        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
        }

        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void FindPlayerIfNeeded()
    {
        if (Time.time < nextPlayerSearchTime)
        {
            return;
        }

        nextPlayerSearchTime = Time.time + 0.75f;
        player = null;
        playerMover = null;
        humanPlayer = null;
        cpuPlayer = null;
        bool requireTargetWand = onlyChasePlayerWithWand && !HasWand;
        float bestScore = float.PositiveInfinity;
        DuckMover[] movers = FindObjectsByType<DuckMover>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < movers.Length; i++)
        {
            DuckMover candidate = movers[i];
            FusionDuckPlayer candidatePlayer = candidate != null
                ? candidate.GetComponent<FusionDuckPlayer>()
                : null;
            if (candidate == null
                || candidate.gameObject == gameObject
                || !candidate.isActiveAndEnabled
                || (candidatePlayer != null && candidatePlayer.IsEliminated)
                || (IsConverted && (candidatePlayer == null || !FactionTeam.AreEnemies(FactionIndex, candidatePlayer.FactionIndex)))
                || (requireTargetWand && !candidate.HasWeapon))
            {
                continue;
            }

            float distance = GetNavigationCost(candidate.transform.position);
            if (IsTemporarilyUnreachable(candidate.transform.position)
                || (!useWholeMapObjectives && distance > detectionRange))
            {
                continue;
            }

            int health = candidatePlayer != null ? candidatePlayer.CurrentHealth : 3;
            bool shielded = candidatePlayer != null && candidatePlayer.HasGuardianShield;
            float score = ScoreTarget(candidate.transform, candidate.HasWeapon, health, shielded);
            if (score < bestScore)
            {
                bestScore = score;
                playerMover = candidate;
                humanPlayer = candidatePlayer;
                cpuPlayer = null;
                player = candidate.transform;
            }
        }

        Unit1BotDuck[] bots = FindObjectsByType<Unit1BotDuck>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < bots.Length; i++)
        {
            Unit1BotDuck candidate = bots[i];
            if (candidate == null
                || !candidate.isActiveAndEnabled
                || candidate.IsEliminated
                || (IsConverted && !FactionTeam.AreEnemies(FactionIndex, candidate.FactionIndex))
                || (requireTargetWand && !candidate.HasWand))
            {
                continue;
            }

            float distance = GetNavigationCost(candidate.transform.position);
            if (IsTemporarilyUnreachable(candidate.transform.position)
                || (!useWholeMapObjectives && distance > detectionRange))
            {
                continue;
            }

            float score = ScoreTarget(
                candidate.transform,
                candidate.HasWand,
                candidate.CurrentHealth,
                candidate.HasGuardianShield);
            if (score < bestScore)
            {
                bestScore = score;
                playerMover = null;
                humanPlayer = null;
                cpuPlayer = candidate;
                player = candidate.transform;
            }
        }
    }

    private bool ShouldChasePlayer()
    {
        if (player == null)
        {
            return false;
        }

        if (humanPlayer != null && humanPlayer.IsEliminated)
        {
            return false;
        }

        if (cpuPlayer != null && cpuPlayer.IsEliminated)
        {
            return false;
        }

        if (IsConverted
            && ((humanPlayer != null && !FactionTeam.AreEnemies(FactionIndex, humanPlayer.FactionIndex))
                || (cpuPlayer != null && !FactionTeam.AreEnemies(FactionIndex, cpuPlayer.FactionIndex))))
        {
            return false;
        }

        bool targetHasWand = (playerMover != null && playerMover.HasWeapon)
            || (cpuPlayer != null && cpuPlayer.HasWand);
        if (!HasWand && onlyChasePlayerWithWand && !targetHasWand)
        {
            return false;
        }

        return !IsTemporarilyUnreachable(player.position)
            && (useWholeMapObjectives || FlatDistance(transform.position, player.position) <= detectionRange);
    }

    /// <summary>
    /// 每 0.45 秒觀察一次局勢，再挑選分數最高的戰術。這個方法不移動
    /// 角色；它只回答「現在該做什麼」，下方 ExecuteUtilityPlan 才負責行動。
    /// </summary>
    private void RefreshUtilityPlan()
    {
        if (Time.time < nextUtilityDecisionAt)
        {
            return;
        }

        nextUtilityDecisionAt = Time.time + UtilityRefreshInterval;
        FindPlayerIfNeeded();

        ShadowAction bestAction = ShadowAction.Wander;
        float bestScore = 6f;

        if (!HasWand)
        {
            RefreshWandTarget();
            if (targetWand != null)
            {
                float wandScore = 62f - GetNavigationCost(targetWand.transform.position) * 1.1f;
                wandScore -= CountShadowsTargetingWand(targetWand) * 5.2f;
                // Like an unarmed CPU, obtaining a wand is mandatory even if
                // it is far away. A negative distance score must not make a
                // distant shadow choose a tiny local patrol forever.
                SetUtilityPlan(ShadowAction.SeekWand, Mathf.Max(12f, wandScore), false);
                return;
            }
            if (ShouldChasePlayer())
            {
                // SeekWand's no-wand fallback follows a carrier; never cast a
                // free spell by selecting AttackTarget while still unarmed.
                SetUtilityPlan(ShadowAction.SeekWand, 12f, false);
                return;
            }

            SetUtilityPlan(bestAction, bestScore, false);
            return;
        }

        if (!IsConverted)
        {
            if (ShouldChasePlayer())
            {
                int health = humanPlayer != null ? humanPlayer.CurrentHealth : cpuPlayer != null ? cpuPlayer.CurrentHealth : 3;
                bool targetHasWand = (playerMover != null && playerMover.HasWeapon) || (cpuPlayer != null && cpuPlayer.HasWand);
                float combatScore = 42f
                    + (targetHasWand ? 14f : 0f)
                    + (health <= 1 ? 10f : 0f)
                    - GetNavigationCost(player.position) * 0.5f;
                ConsiderUtilityAction(ShadowAction.AttackTarget, combatScore, ref bestAction, ref bestScore);
            }

            SetUtilityPlan(bestAction, bestScore, false);
            return;
        }

        Unit1WandEffectProfile profile = heldWand != null
            ? heldWand.EffectProfile
            : Unit1WandPickup.GetEffectProfile(Unit1WandAbility.Swift);
        FactionCrystal friendlyCrystal = FactionCrystal.FindForFaction(FactionIndex);
        int crystalThreats = CountCrystalThreats(friendlyCrystal);
        int missingHealth = friendlyCrystal != null && !friendlyCrystal.IsDestroyed
            ? Mathf.Max(0, friendlyCrystal.MaximumHealth - friendlyCrystal.CurrentHealth)
            : 0;

        if (friendlyCrystal != null && !friendlyCrystal.IsDestroyed
            && !IsTemporarilyUnreachable(friendlyCrystal.transform.position))
        {
            float defendScore = crystalThreats * 26f + missingHealth * 10f
                - GetNavigationCost(friendlyCrystal.transform.position) * 0.45f;
            if (profile.Ability == Unit1WandAbility.Guardian)
            {
                defendScore += 18f;
            }

            ConsiderUtilityAction(ShadowAction.DefendCrystal, defendScore, ref bestAction, ref bestScore);
        }

        if (ShouldChasePlayer())
        {
            int health = humanPlayer != null ? humanPlayer.CurrentHealth : cpuPlayer != null ? cpuPlayer.CurrentHealth : 3;
            bool targetHasWand = (playerMover != null && playerMover.HasWeapon) || (cpuPlayer != null && cpuPlayer.HasWand);
            float combatScore = 38f
                + (targetHasWand ? 13f : 0f)
                + (health <= 1 ? 10f : 0f)
                - GetNavigationCost(player.position) * 0.45f;
            ConsiderUtilityAction(ShadowAction.AttackTarget, combatScore, ref bestAction, ref bestScore);
        }

        if (profile.Ability != Unit1WandAbility.Guardian)
        {
            if (targetCrystal == null || targetCrystal.IsDestroyed || !targetCrystal.CanBeDamagedBy(FactionIndex)
                || IsTemporarilyUnreachable(targetCrystal.transform.position))
            {
                targetCrystal = FindBestEnemyCrystal();
            }

            if (targetCrystal != null)
            {
                float assaultScore = 27f
                    + (profile.Ability == Unit1WandAbility.Heavy ? 18f : 7f)
                    + Mathf.Max(0, 5 - targetCrystal.CurrentHealth) * 5f
                    - GetNavigationCost(targetCrystal.transform.position) * 0.35f
                    - crystalThreats * 12f;
                ConsiderUtilityAction(ShadowAction.AttackCrystal, assaultScore, ref bestAction, ref bestScore);
            }
        }

        if (friendlyCrystal != null && !friendlyCrystal.IsDestroyed
            && !IsTemporarilyUnreachable(friendlyCrystal.transform.position))
        {
            float followScore = 18f - GetNavigationCost(friendlyCrystal.transform.position) * 0.25f;
            ConsiderUtilityAction(ShadowAction.FollowAlly, followScore, ref bestAction, ref bestScore);
        }

        bool emergency = bestAction == ShadowAction.DefendCrystal && (crystalThreats > 0 || missingHealth >= 2);
        SetUtilityPlan(bestAction, bestScore, emergency);
    }

    private void ConsiderUtilityAction(ShadowAction action, float score, ref ShadowAction bestAction, ref float bestScore)
    {
        if (score > bestScore)
        {
            bestAction = action;
            bestScore = score;
        }
    }

    private void SetUtilityPlan(ShadowAction action, float score, bool emergency)
    {
        if (action == plannedAction)
        {
            plannedActionScore = score;
            return;
        }

        bool enoughCommitment = Time.time >= plannedActionSince + MinimumActionDuration;
        bool clearlyBetter = score >= plannedActionScore + ActionSwitchThreshold;
        if (!emergency && !enoughCommitment && !clearlyBetter)
        {
            return;
        }

        plannedAction = action;
        plannedActionScore = score;
        plannedActionSince = Time.time;
    }

    private void ExecuteUtilityPlan()
    {
        Unit1WandEffectProfile profile = heldWand != null
            ? heldWand.EffectProfile
            : Unit1WandPickup.GetEffectProfile(Unit1WandAbility.Swift);

        switch (plannedAction)
        {
            case ShadowAction.SeekWand:
                SeekWandOrPatrol();
                return;

            case ShadowAction.AttackTarget:
                UseWandAgainstTarget();
                return;

            case ShadowAction.DefendCrystal:
                GuardFriendlyCrystal(FactionCrystal.FindForFaction(FactionIndex), profile);
                return;

            case ShadowAction.AttackCrystal:
                if (targetCrystal != null)
                {
                    AssaultCrystal(targetCrystal, profile);
                }
                else
                {
                    FollowFriendlyLeader(FactionCrystal.FindForFaction(FactionIndex));
                }
                return;

            case ShadowAction.FollowAlly:
                FollowFriendlyLeader(FactionCrystal.FindForFaction(FactionIndex));
                return;

            default:
                Patrol();
                return;
        }
    }

    private int CountCrystalThreats(FactionCrystal friendlyCrystal)
    {
        if (friendlyCrystal == null || friendlyCrystal.IsDestroyed)
        {
            return 0;
        }

        int threats = 0;
        EnemyDuckAI[] shadows = FindObjectsByType<EnemyDuckAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int index = 0; index < shadows.Length; index++)
        {
            EnemyDuckAI shadow = shadows[index];
            if (shadow != null
                && shadow != this
                && !shadow.IsDefeated
                && shadow.IsFactionEnemyOf(FactionIndex)
                && FlatDistance(shadow.transform.position, friendlyCrystal.transform.position) <= 7.5f)
            {
                threats++;
            }
        }

        FusionDuckPlayer[] players = FindObjectsByType<FusionDuckPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int index = 0; index < players.Length; index++)
        {
            FusionDuckPlayer candidate = players[index];
            if (candidate != null
                && !candidate.IsEliminated
                && FactionTeam.AreEnemies(FactionIndex, candidate.FactionIndex)
                && FlatDistance(candidate.transform.position, friendlyCrystal.transform.position) <= 7.5f)
            {
                threats++;
            }
        }

        Unit1BotDuck[] bots = FindObjectsByType<Unit1BotDuck>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int index = 0; index < bots.Length; index++)
        {
            Unit1BotDuck candidate = bots[index];
            if (candidate != null
                && !candidate.IsEliminated
                && FactionTeam.AreEnemies(FactionIndex, candidate.FactionIndex)
                && FlatDistance(candidate.transform.position, friendlyCrystal.transform.position) <= 7.5f)
            {
                threats++;
            }
        }

        return threats;
    }

    private void SeekWandOrPatrol()
    {
        // A committed SeekWand plan may survive briefly after pickup. Do not
        // let it claim a second wand while utility transitions to combat.
        if (HasWand)
        {
            nextUtilityDecisionAt = 0f;
            state = EnemyState.Patrol;
            Patrol();
            return;
        }
        if (targetWand == null || !targetWand.IsAvailable
            || IsTemporarilyUnreachable(targetWand.transform.position))
        {
            RefreshWandTarget();
        }

        if (targetWand != null)
        {
            // The shared pickup validates horizontal range and height. Take
            // a reachable wand as soon as that interaction range is valid,
            // rather than forcing the capsule into its centre/grid cell.
            if (targetWand.CanBePickedUpBy(transform.position))
            {
                FaceTowards(targetWand.transform.position);
                if (targetWand.TryPickupByNetworkObject(Object))
                {
                    heldWand = targetWand;
                    targetWand = null;
                    HasWand = true;
                    state = EnemyState.Chase;
                    return;
                }

                // A player or CPU won this pickup first. Rescan next tick.
                targetWand = null;
                return;
            }

            state = EnemyState.SeekWand;
            MoveTowards(targetWand.transform.position, chaseSpeed, targetWand.NavigationArrivalDistance);
            return;
        }

        // If every wand has been claimed, an unarmed shadow keeps the old
        // threatening behaviour and follows a nearby wand carrier.
        if (ShouldChasePlayer())
        {
            state = EnemyState.Chase;
            MoveTowards(player.position, chaseSpeed, stoppingDistance);
            return;
        }

        state = EnemyState.Patrol;
        Patrol();
    }

    private void UseWandAgainstTarget()
    {
        Unit1WandEffectProfile profile = heldWand != null
            ? heldWand.EffectProfile
            : Unit1WandPickup.GetEffectProfile(Unit1WandAbility.Swift);
        if (!ShouldChasePlayer())
        {
            state = EnemyState.Patrol;
            Patrol();
            return;
        }

        float distance = FlatDistance(transform.position, player.position);
        float preferredDistance = shadowSpellRange * PreferredSpellDistanceFactor;
        if (distance > shadowSpellRange * 0.88f || distance < Mathf.Min(stoppingDistance, preferredDistance * 0.55f))
        {
            state = EnemyState.Chase;
            MoveTowards(GetSpellPosition(preferredDistance), chaseSpeed, 0.2f);
            return;
        }

        state = EnemyState.Cast;
        FaceTowards(player.position);
        StopMoving();
        if (Time.time < nextShadowSpellTime)
        {
            return;
        }

        float windup = PlayWandAttackAnimation();
        float recovery = profile.Ability == Unit1WandAbility.Guardian
            ? Mathf.Max(shadowSpellCooldown, profile.AttackCooldown)
            : profile.AttackCooldown;
        nextShadowSpellTime = Time.time + windup + Mathf.Max(recovery, 0.08f);
        if (profile.Ability == Unit1WandAbility.Guardian)
        {
            StartCoroutine(ResolveGuardianCastAfterWindup(profile, windup));
            return;
        }

        StartCoroutine(ResolveShadowCastAfterWindup(player, humanPlayer, cpuPlayer, profile, windup));
    }

    /// <summary>
    /// A converted Grey Shadow stops behaving like a free-for-all monster. It
    /// first responds to an enemy near its own crystal, then follows nearby
    /// allies, and finally joins a push against the weakest reachable enemy
    /// crystal. The StateAuthority keeps this behaviour deterministic for all
    /// clients.
    /// </summary>
    private void RunFactionStrategy()
    {
        FindPlayerIfNeeded();
        if (!HasWand)
        {
            SeekWandOrPatrol();
            return;
        }

        Unit1WandEffectProfile profile = heldWand != null
            ? heldWand.EffectProfile
            : Unit1WandPickup.GetEffectProfile(Unit1WandAbility.Swift);
        FactionCrystal friendlyCrystal = FactionCrystal.FindForFaction(FactionIndex);
        if (ShouldChasePlayer())
        {
            UseWandAgainstTarget();
            return;
        }

        if (profile.Ability == Unit1WandAbility.Guardian)
        {
            GuardFriendlyCrystal(friendlyCrystal, profile);
            return;
        }

        if (targetCrystal == null || targetCrystal.IsDestroyed || !targetCrystal.CanBeDamagedBy(FactionIndex)
            || Time.time >= nextPlayerSearchTime)
        {
            targetCrystal = FindBestEnemyCrystal();
        }

        if (targetCrystal != null)
        {
            AssaultCrystal(targetCrystal, profile);
            return;
        }

        FollowFriendlyLeader(friendlyCrystal);
    }

    private void GuardFriendlyCrystal(FactionCrystal friendlyCrystal, Unit1WandEffectProfile profile)
    {
        if (friendlyCrystal == null || friendlyCrystal.IsDestroyed
            || IsTemporarilyUnreachable(friendlyCrystal.transform.position))
        {
            Patrol();
            return;
        }

        if (FlatDistance(transform.position, friendlyCrystal.transform.position) > 3.4f)
        {
            state = EnemyState.Chase;
            MoveTowards(friendlyCrystal.transform.position, chaseSpeed, 3.4f);
            return;
        }

        if (friendlyCrystal.CurrentHealth < friendlyCrystal.MaximumHealth && Time.time >= nextShadowSpellTime)
        {
            state = EnemyState.Cast;
            StopMoving();
            float windup = PlayWandAttackAnimation();
            nextShadowSpellTime = Time.time + windup + Mathf.Max(profile.AttackCooldown, 0.08f);
            StartCoroutine(ResolveGuardianCastAfterWindup(profile, windup));
            return;
        }

        FollowFriendlyLeader(friendlyCrystal);
    }

    private FactionCrystal FindBestEnemyCrystal()
    {
        FactionCrystal[] crystals = FindObjectsByType<FactionCrystal>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        FactionCrystal best = null;
        float bestScore = float.PositiveInfinity;
        for (int index = 0; index < crystals.Length; index++)
        {
            FactionCrystal candidate = crystals[index];
            if (candidate == null || candidate.IsDestroyed || !candidate.CanBeDamagedBy(FactionIndex)
                || IsTemporarilyUnreachable(candidate.transform.position))
            {
                continue;
            }

            float score = GetNavigationCost(candidate.transform.position) + candidate.CurrentHealth * 0.45f;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private void AssaultCrystal(FactionCrystal crystal, Unit1WandEffectProfile profile)
    {
        if (crystal == null || crystal.IsDestroyed || IsTemporarilyUnreachable(crystal.transform.position))
        {
            targetCrystal = null;
            nextUtilityDecisionAt = 0f;
            Patrol();
            return;
        }
        const float attackDistance = 2.45f;
        if (FlatDistance(transform.position, crystal.transform.position) > attackDistance)
        {
            state = EnemyState.Chase;
            MoveTowards(crystal.transform.position, chaseSpeed, attackDistance);
            return;
        }

        state = EnemyState.Cast;
        FaceTowards(crystal.transform.position);
        StopMoving();
        if (Time.time < nextShadowSpellTime)
        {
            return;
        }

        float windup = PlayWandAttackAnimation();
        nextShadowSpellTime = Time.time + windup + Mathf.Max(profile.AttackCooldown, 0.08f);
        StartCoroutine(ResolveCrystalCastAfterWindup(crystal, profile, windup));
    }

    private IEnumerator ResolveCrystalCastAfterWindup(FactionCrystal crystal, Unit1WandEffectProfile profile, float windup)
    {
        int revision = pendingCastRevision;
        yield return new WaitForSeconds(windup);
        if (!CanResolvePendingCast(revision)
            || !IsConverted
            || crystal == null
            || !crystal.CanBeDamagedBy(FactionIndex)
            || FlatDistance(transform.position, crystal.transform.position) > 3.1f)
        {
            yield break;
        }

        crystal.TakeDamage(Object, profile.Damage);
        DuckWandAttack.CreateWandCast(
            transform.position + Vector3.up * 1.2f,
            crystal.transform.position + Vector3.up * 1.25f,
            profile);
    }

    private void FollowFriendlyLeader(FactionCrystal friendlyCrystal)
    {
        Transform leader = null;
        float bestDistance = float.PositiveInfinity;
        FusionDuckPlayer[] players = FindObjectsByType<FusionDuckPlayer>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int index = 0; index < players.Length; index++)
        {
            FusionDuckPlayer candidate = players[index];
            if (candidate == null || candidate.IsEliminated || !FactionTeam.AreAllies(FactionIndex, candidate.FactionIndex)
                || IsTemporarilyUnreachable(candidate.transform.position))
            {
                continue;
            }

            float distance = GetNavigationCost(candidate.transform.position) - (candidate.HasWand ? 2.5f : 0f);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                leader = candidate.transform;
            }
        }

        Unit1BotDuck[] bots = FindObjectsByType<Unit1BotDuck>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int index = 0; index < bots.Length; index++)
        {
            Unit1BotDuck candidate = bots[index];
            if (candidate == null || candidate.IsEliminated || !FactionTeam.AreAllies(FactionIndex, candidate.FactionIndex)
                || IsTemporarilyUnreachable(candidate.transform.position))
            {
                continue;
            }

            float distance = GetNavigationCost(candidate.transform.position) - (candidate.HasWand ? 2.5f : 0f);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                leader = candidate.transform;
            }
        }

        if (leader != null)
        {
            state = EnemyState.Chase;
            MoveTowards(leader.position, patrolSpeed + 0.55f, 2.15f);
        }
        else if (friendlyCrystal != null && !friendlyCrystal.IsDestroyed
            && !IsTemporarilyUnreachable(friendlyCrystal.transform.position))
        {
            state = EnemyState.Patrol;
            MoveTowards(friendlyCrystal.transform.position, patrolSpeed, 3.4f);
        }
        else
        {
            Patrol();
        }
    }

    private IEnumerator ResolveShadowCastAfterWindup(
        Transform target,
        FusionDuckPlayer targetPlayer,
        Unit1BotDuck targetCpu,
        Unit1WandEffectProfile profile,
        float windup)
    {
        int revision = pendingCastRevision;
        yield return new WaitForSeconds(windup);
        if (!CanResolvePendingCast(revision)
            || target == null
            || (targetPlayer != null && targetPlayer.IsEliminated)
            || (targetCpu != null && targetCpu.IsEliminated))
        {
            yield break;
        }

        // A target can also change team while we wind up, independently of
        // our own conversion. Never resolve an old hostile plan against an ally.
        int targetFaction = targetPlayer != null ? targetPlayer.FactionIndex
            : targetCpu != null ? targetCpu.FactionIndex : FactionTeam.Neutral;
        if (IsConverted && !FactionTeam.AreEnemies(FactionIndex, targetFaction))
        {
            yield break;
        }

        Vector3 origin = transform.position + Vector3.up * 1.2f;
        Vector3 end = target.position + Vector3.up * 1.2f;
        DuckWandAttack.CreateWandCast(origin, end, profile);
        if (FlatDistance(transform.position, target.position) > shadowSpellRange * 1.15f)
        {
            yield break;
        }

        if (targetPlayer != null)
        {
            targetPlayer.TakeDamage(profile.Damage, transform.position);
        }
        else
        {
            targetCpu?.TakeDamage(profile.Damage, transform.position);
        }
    }

    private IEnumerator ResolveGuardianCastAfterWindup(Unit1WandEffectProfile profile, float windup)
    {
        int revision = pendingCastRevision;
        yield return new WaitForSeconds(windup);
        if (!CanResolvePendingCast(revision))
        {
            yield break;
        }

        if (IsConverted
            && FactionCrystal.TryProtectNearestFriendlyCrystal(
                transform.position,
                FactionIndex,
                profile.ShieldDuration,
                1,
                out FactionCrystal protectedCrystal))
        {
            DuckWandAttack.CreateGuardianAura(protectedCrystal.transform, profile.EffectColor, profile.ShieldDuration);
            yield break;
        }

        guardianShieldUntil = Time.time + profile.ShieldDuration;
        DuckWandAttack.CreateGuardianAura(transform, profile.EffectColor, profile.ShieldDuration);
    }

    private void InvalidatePendingCasts()
    {
        unchecked { pendingCastRevision++; }
        wandAttackUntil = 0f;
    }

    private bool CanResolvePendingCast(int revision)
    {
        return revision == pendingCastRevision
            && isActiveAndEnabled
            && Object != null && Object.IsValid
            && HasStateAuthority && !IsDefeated && HasWand
            && !Unit1GameDirector.IsMatchFinished();
    }

    private void RefreshHeldWand()
    {
        if (!HasWand)
        {
            return;
        }

        if (heldWand != null && heldWand.IsHeldBy(Object))
        {
            return;
        }

        Unit1WandPickup[] wands = FindObjectsByType<Unit1WandPickup>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int i = 0; i < wands.Length; i++)
        {
            if (wands[i] != null && wands[i].IsHeldBy(Object))
            {
                heldWand = wands[i];
                return;
            }
        }

        heldWand = null;
        HasWand = false;
    }

    private Unit1WandPickup FindBestAvailableWand()
    {
        Unit1WandPickup[] wands = FindObjectsByType<Unit1WandPickup>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        Unit1WandPickup best = null;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < wands.Length; i++)
        {
            Unit1WandPickup candidate = wands[i];
            if (candidate == null || !candidate.IsAvailable)
            {
                continue;
            }

            float distance = GetNavigationCost(candidate.transform.position);
            if (IsTemporarilyUnreachable(candidate.transform.position)
                || (!useWholeMapObjectives && distance > wandSearchRange))
            {
                continue;
            }

            Unit1WandEffectProfile profile = candidate.EffectProfile;
            int role = GetTacticalRole();
            float score = distance + CountShadowsTargetingWand(candidate) * 5.2f;
            if (profile.Ability == Unit1WandAbility.Heavy)
            {
                score += role == 0 ? -5.5f : -1.4f;
            }
            else if (profile.Ability == Unit1WandAbility.Swift)
            {
                score += role == 1 ? -4.2f : -0.8f;
            }
            else
            {
                // One Shadow deliberately becomes a shielded distraction;
                // the others prefer damage wands.
                score += role == 2 ? -3.8f : 4.2f;
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private void RefreshWandTarget()
    {
        bool currentValid = targetWand != null && targetWand.IsAvailable
            && !IsTemporarilyUnreachable(targetWand.transform.position);
        if (currentValid && Time.time < nextWandSearchTime) return;
        nextWandSearchTime = Time.time + 0.55f;
        Unit1WandPickup candidate = FindBestAvailableWand();
        if (!currentValid || (candidate != null
            && GetNavigationCost(candidate.transform.position) + ActionSwitchThreshold
                < GetNavigationCost(targetWand.transform.position)))
        {
            if (targetWand != candidate)
            {
                detourUntil = 0f;
                Unit1RouteScanner.InvalidateRoute(plannedRoute, ref plannedRouteIndex, ref nextRoutePlanAt);
            }
            targetWand = candidate;
        }
    }

    private float ScoreTarget(Transform candidate, bool candidateHasWand, int health, bool shielded)
    {
        float score = GetNavigationCost(candidate.position)
            + CountShadowsTargeting(candidate) * 4.5f
            + Mathf.Max(1, health) * 1.15f
            + (candidateHasWand ? -11f : 3.5f)
            + (shielded ? 8.5f : 0f);

        // Heavy wand holders finish a vulnerable target; swift holders put
        // pressure on wand carriers. This makes a pack split jobs instead of
        // every grey duck tunnelling on the nearest body.
        Unit1WandAbility ability = heldWand != null
            ? heldWand.EffectProfile.Ability
            : Unit1WandAbility.Swift;
        if (ability == Unit1WandAbility.Heavy && health <= 1)
        {
            score -= 4.5f;
        }
        else if (ability == Unit1WandAbility.Swift && candidateHasWand)
        {
            score -= 2.5f;
        }

        return score;
    }

    private int CountShadowsTargeting(Transform candidate)
    {
        EnemyDuckAI[] shadows = FindObjectsByType<EnemyDuckAI>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        int claimed = 0;
        for (int i = 0; i < shadows.Length; i++)
        {
            if (shadows[i] != null
                && shadows[i] != this
                && !shadows[i].IsDefeated
                && shadows[i].player == candidate)
            {
                claimed++;
            }
        }

        return claimed;
    }

    private int CountShadowsTargetingWand(Unit1WandPickup candidate)
    {
        EnemyDuckAI[] shadows = FindObjectsByType<EnemyDuckAI>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        int claimed = 0;
        for (int i = 0; i < shadows.Length; i++)
        {
            if (shadows[i] != null
                && shadows[i] != this
                && !shadows[i].IsDefeated
                && shadows[i].targetWand == candidate)
            {
                claimed++;
            }
        }

        return claimed;
    }

    private int GetTacticalRole()
    {
        int identity = Object != null ? (int)Object.Id.Raw : GetInstanceID();
        return Mathf.Abs(identity) % 3;
    }

    private Vector3 GetSpellPosition(float preferredDistance)
    {
        Vector3 radial = Flatten(transform.position - player.position);
        if (radial.sqrMagnitude < 0.001f)
        {
            float angle = GetTacticalRole() * 120f * Mathf.Deg2Rad;
            radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        radial.Normalize();
        float side = GetTacticalRole() == 1 ? -1f : 1f;
        Vector3 tangent = Vector3.Cross(Vector3.up, radial) * side;
        return player.position + radial * preferredDistance + tangent * 0.38f;
    }

    private void FaceTowards(Vector3 position)
    {
        Vector3 direction = Flatten(position - transform.position);
        if (direction.sqrMagnitude < 0.001f || body == null)
        {
            return;
        }

        Quaternion desiredRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        body.rotation = Quaternion.RotateTowards(body.rotation, desiredRotation, turningSpeed * Runner.DeltaTime);
    }

    private void Patrol()
    {
        if (Time.time < nextPatrolTime)
        {
            StopMoving();
            return;
        }

        if (FlatDistance(body.position, patrolTarget) <= StopDistance
            || Time.time >= patrolTargetExpiresAt)
        {
            patrolStep++;
            int identity = Object != null ? (int)Object.Id.Raw : GetInstanceID();
            if (useWholeMapObjectives)
            {
                if (!Unit1RouteScanner.TryFindRoamingPoint(body.position,
                    identity + patrolStep * 137, out patrolTarget))
                {
                    patrolTarget = body.position;
                }
            }
            else
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * patrolRadius;
                Vector3 intendedPoint = homePosition + new Vector3(offset.x, 0f, offset.y);
                patrolTarget = Unit1RouteScanner.ClampToWalkablePoint(intendedPoint, body.position.y);
            }
            patrolTargetExpiresAt = Time.time + Mathf.Max(6f,
                FlatDistance(body.position, patrolTarget) / Mathf.Max(patrolSpeed, 0.1f) + 4f);
            Unit1RouteScanner.InvalidateRoute(plannedRoute, ref plannedRouteIndex, ref nextRoutePlanAt);
            nextPatrolTime = Time.time + patrolWaitTime;
            StopMoving();
            return;
        }

        state = EnemyState.Patrol;
        MoveTowards(patrolTarget, patrolSpeed, StopDistance);
    }

    private void MoveTowards(Vector3 destination, float speed, float desiredDistance)
    {
        if (Time.time < detourUntil && FlatDistance(body.position, detourTarget) <= StopDistance)
        {
            detourUntil = 0f;
            recoveryAttempts = 0;
        }
        bool detouring = Time.time < detourUntil
            && FlatDistance(body.position, detourTarget) > StopDistance;
        Vector3 intendedDestination = detouring ? detourTarget : destination;
        float destinationArrivalDistance = detouring ? StopDistance : desiredDistance;
        // Tactical stopping ranges belong to the final target. Intermediate
        // route corners must be reached closely enough for the scanner to
        // advance, even when defending/attacking from several metres away.
        if (FlatDistance(body.position, intendedDestination) <= destinationArrivalDistance)
        {
            StopMoving();
            return;
        }

        bool hasRoute = Unit1RouteScanner.TryGetNextWaypoint(
            body.position,
            intendedDestination,
            destinationArrivalDistance,
            plannedRoute,
            ref plannedRouteIndex,
            ref plannedRouteDestination,
            ref nextRoutePlanAt,
            out Vector3 navigationTarget);
        if (!hasRoute)
        {
            MarkDestinationUnreachable(destination);
            detourUntil = 0f;
            if (plannedAction == ShadowAction.Wander)
            {
                // A failed patrol goal cannot remain active forever. Retry
                // another goal after a short pause instead of walking through
                // an obstacle or repeatedly requesting the same empty route.
                patrolTarget = body.position;
                nextPatrolTime = Time.time + Mathf.Min(patrolWaitTime, 0.35f);
                Unit1RouteScanner.InvalidateRoute(plannedRoute, ref plannedRouteIndex, ref nextRoutePlanAt);
            }
            StopMoving();
            return;
        }

        Vector3 direction = Flatten(navigationTarget - body.position);
        float distance = direction.magnitude;
        if (distance <= 0.001f)
        {
            StopMoving();
            return;
        }

        Vector3 normalizedDirection = direction / distance;
        float tickDeltaTime = Runner != null && Runner.DeltaTime > 0.0001f
            ? Runner.DeltaTime
            : Time.fixedDeltaTime;
        bool blocked = DuckKinematicMotor.Move(
            body,
            normalizedDirection * Mathf.Min(speed * tickDeltaTime, distance),
            out RaycastHit blockingHit,
            ignoreDynamicBodies: true);

        Quaternion desiredRotation = Quaternion.LookRotation(normalizedDirection, Vector3.up);
        body.rotation = Quaternion.RotateTowards(body.rotation, desiredRotation, turningSpeed * tickDeltaTime);
        PlayMovementAnimation(true);

        // Detect the case where a kinematic collider is pressed into scenery:
        // SweepTest can then appear clear while position never changes. Treat
        // two stalled samples as a blocked path and select a side route.
        if (Time.time >= nextProgressCheckAt)
        {
            float travelled = FlatDistance(body.position, lastProgressPosition);
            float minimumProgress = Mathf.Min(0.2f, speed * 0.45f * 0.25f);
            stalledRouteCount = travelled < minimumProgress ? stalledRouteCount + 1 : 0;
            if (!detouring && travelled >= 0.4f) failedRecoveryCount = 0;
            lastProgressPosition = body.position;
            nextProgressCheckAt = Time.time + 0.45f;
            if (stalledRouteCount >= 2)
            {
                blocked = true;
                stalledRouteCount = 0;
            }
        }

        if (!blocked)
        {
            blockedMoveCount = 0;
        }
        else
        {
            if (TryStartObstacleJump(blockingHit, normalizedDirection))
            {
                Unit1RouteScanner.InvalidateRoute(plannedRoute, ref plannedRouteIndex, ref nextRoutePlanAt);
                detourUntil = 0f;
                return;
            }
            blockedMoveCount++;
            if (blockedMoveCount >= 2)
            {
                blockedMoveCount = 0;
                Unit1RouteScanner.InvalidateRoute(plannedRoute, ref plannedRouteIndex, ref nextRoutePlanAt);
                if (!BeginRouteRecovery(destination))
                {
                    MarkDestinationUnreachable(destination);
                    if (plannedAction == ShadowAction.Wander) patrolTargetExpiresAt = 0f;
                }
            }
        }

        if (!hasReportedFirstMove)
        {
            hasReportedFirstMove = true;
            Debug.Log(
                "[UNIT1-DIAG Enemy] patrol/chase movement started"
                + " | object=" + (Object != null ? Object.Id.ToString() : "none")
                + " | speed=" + speed
                + " | destination=" + destination);
        }
    }

    private bool BeginRouteRecovery(Vector3 destination)
    {
        if (recoverySide == 0 || FlatDistance(destination, recoveryDestination) > 0.8f)
        {
            recoveryDestination = destination;
            recoveryAttempts = 0;
            failedRecoveryCount = 0;
            recoverySide = ((Object != null ? Object.Id.Raw : 0u) & 1u) == 0u ? 1 : -1;
        }
        // A nominally clear side point is not proof that the real capsule
        // escaped. Bound repeated recoveries, then choose another objective.
        if (++failedRecoveryCount > 4) return false;
        if (recoveryAttempts >= 2)
        {
            recoverySide = -recoverySide;
            recoveryAttempts = 0;
        }
        if (!Unit1RouteScanner.TryFindRecoveryPoint(body.position, destination, recoverySide, out Vector3 point)
            && !Unit1RouteScanner.TryFindRecoveryPoint(body.position, destination, -recoverySide, out point))
        {
            return false;
        }
        detourTarget = point;
        detourUntil = Time.time + RecoveryDuration;
        recoveryAttempts++;
        return true;
    }

    private bool TryStartObstacleJump(RaycastHit blockingHit, Vector3 direction)
    {
        Collider obstacle = blockingHit.collider;
        if (bodyCollider == null || obstacle == null || obstacle.isTrigger
            || obstacle.attachedRigidbody != null || !obstacle.gameObject.isStatic
            || !isGrounded || Time.time < nextAutoJumpAt) return false;
        float height = obstacle.bounds.max.y - bodyCollider.bounds.min.y;
        Vector3 size = obstacle.bounds.size;
        float span = Mathf.Abs(direction.x) * size.x + Mathf.Abs(direction.z) * size.z;
        // Match the CPU's jump envelope. Buildings and walls are routed around.
        if (height < 0.06f || height > 0.78f || span > 1.55f
            || DuckKinematicMotor.TryGetBlockingHit(body, Vector3.up, 1.1f, true, out _)) return false;
        verticalSpeed = AutoJumpSpeed;
        isGrounded = false;
        nextAutoJumpAt = Time.time + 1.1f;
        animator?.SetTrigger(JumpProperty);
        return true;
    }

    private void AdvanceVerticalMotion()
    {
        float dt = Runner != null && Runner.DeltaTime > 0.0001f ? Runner.DeltaTime : Time.fixedDeltaTime;
        if (verticalSpeed <= 0.05f && IsGroundBelowFeet())
        {
            verticalSpeed = 0f;
            isGrounded = true;
            return;
        }
        isGrounded = false;
        verticalSpeed -= AutoJumpGravity * dt;
        if (DuckKinematicMotor.Move(body, Vector3.up * verticalSpeed * dt, out _, ignoreDynamicBodies: true))
            verticalSpeed = 0f;
        if (verticalSpeed <= 0f && IsGroundBelowFeet())
        {
            verticalSpeed = 0f;
            isGrounded = true;
        }
    }

    private bool IsGroundBelowFeet()
    {
        if (bodyCollider == null || verticalSpeed > 0.05f) return false;
        Bounds bounds = bodyCollider.bounds;
        float radius = Mathf.Clamp(Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.7f, 0.08f, 0.38f);
        Vector3 origin = new(bounds.center.x, bounds.min.y + radius + 0.03f, bounds.center.z);
        int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, groundProbeHits,
            0.2f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = groundProbeHits[i].collider;
            if (hit == null || hit == bodyCollider || hit.transform.IsChildOf(transform)
                || hit.GetComponentInParent<NetworkObject>() != null) continue;
            if (Vector3.Dot(groundProbeHits[i].normal, Vector3.up) > 0.55f) return true;
        }
        return false;
    }

    private void MarkDestinationUnreachable(Vector3 destination)
    {
        RememberUnreachableGoal(destination);
        // Combat routes end at a spell-position offset, not at the enemy.
        // Exclude the semantic target too, or utility immediately picks the
        // same enemy and regenerates the same unreachable firing position.
        if (plannedAction == ShadowAction.AttackTarget && player != null)
            RememberUnreachableGoal(player.position);
        if (plannedAction == ShadowAction.AttackCrystal && targetCrystal != null)
        {
            RememberUnreachableGoal(targetCrystal.transform.position);
            targetCrystal = null;
        }
        detourUntil = 0f;
        Unit1RouteScanner.InvalidateRoute(plannedRoute, ref plannedRouteIndex, ref nextRoutePlanAt);
        nextWandSearchTime = nextPlayerSearchTime = nextUtilityDecisionAt = 0f;
    }

    private void RememberUnreachableGoal(Vector3 destination)
    {
        unreachableGoals.Remember(destination, Time.time);
    }

    private bool IsTemporarilyUnreachable(Vector3 destination)
    {
        return unreachableGoals.IsUnreachable(destination, Time.time);
    }

    private void ReportRuntimeState(string source)
    {
        bool bodyKinematic = body != null && body.isKinematic;
        if (hasReportedAuthority
            && reportedStateAuthority == HasStateAuthority
            && reportedBodyKinematic == bodyKinematic)
        {
            return;
        }

        hasReportedAuthority = true;
        reportedStateAuthority = HasStateAuthority;
        reportedBodyKinematic = bodyKinematic;
        Debug.Log(
            "[UNIT1-DIAG Enemy] " + source
            + " | object=" + (Object != null ? Object.Id.ToString() : "none")
            + " | stateAuthority=" + HasStateAuthority
            + " | kinematic=" + bodyKinematic
            + " | gravity=" + (body != null && body.useGravity));
    }

    private void StopMoving()
    {
        if (body != null && !body.isKinematic)
        {
            Vector3 velocity = body.linearVelocity;
            velocity.x = 0f;
            velocity.z = 0f;
            body.linearVelocity = velocity;
        }

        PlayMovementAnimation(false);
    }

    private void LogAiDecisionIfChanged()
    {
        if (!debugAI || (lastLoggedState == state && lastLoggedTarget == player))
        {
            return;
        }

        lastLoggedState = state;
        lastLoggedTarget = player;
        Transform target = player != null ? player : targetWand != null ? targetWand.transform : targetCrystal != null ? targetCrystal.transform : null;
        string path = target != null ? "Route " + FlatDistance(transform.position, target.position).ToString("0.0") + "m" : "None";
        Debug.Log("[UNIT1 AI] Shadow_" + (Object != null ? Object.Id.ToString() : name)
            + " State=" + state
            + " Plan=" + plannedAction
            + " Score=" + plannedActionScore.ToString("0")
            + " Target=" + (target != null ? target.name : "none")
            + " HP=" + CurrentHealth + "/" + maximumHealth
            + " Wand=" + (heldWand != null ? heldWand.AbilityDisplayName : "none")
            + " Path=" + path, this);
    }

    private void OnDrawGizmosSelected()
    {
        if (!debugAI || plannedRoute == null || plannedRoute.Count == 0)
        {
            return;
        }

        Gizmos.color = new Color(0.72f, 0.35f, 1f);
        Vector3 from = transform.position + Vector3.up * 0.15f;
        for (int index = plannedRouteIndex; index < plannedRoute.Count; index++)
        {
            Vector3 to = plannedRoute[index] + Vector3.up * 0.15f;
            Gizmos.DrawLine(from, to);
            Gizmos.DrawSphere(to, 0.09f);
            from = to;
        }
    }

    private void PlayMovementAnimation(bool isMoving)
    {
        if (animator == null)
        {
            return;
        }

        if (Time.time < wandAttackUntil && animator.HasState(0, WandAttackState))
        {
            return;
        }

        animator.SetFloat(SpeedProperty, isMoving ? 0.5f : 0f);
        animator.SetBool(GroundedProperty, isGrounded);

        int desiredState = !isGrounded && verticalSpeed > 0.1f && animator.HasState(0, JumpState)
            ? JumpState : isMoving ? WalkingState : IdleState;
        if (desiredState == currentAnimationState || !animator.HasState(0, desiredState))
        {
            return;
        }

        currentAnimationState = desiredState;
        animator.CrossFadeInFixedTime(desiredState, 0.08f);
    }

    private float PlayWandAttackAnimation()
    {
        float duration = DuckWandAttack.ResolveWandAttackDuration(animator);
        if (animator == null || !animator.HasState(0, WandAttackState))
        {
            return duration;
        }

        wandAttackUntil = Time.time + duration;
        currentAnimationState = WandAttackState;
        animator.CrossFadeInFixedTime(WandAttackState, 0.06f);
        return duration;
    }

    private void ApplyMatchVisualScale()
    {
        if (matchVisualScaleApplied)
        {
            return;
        }

        Animator[] animators = GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            Animator candidate = animators[i];
            if (candidate == null)
            {
                continue;
            }

            Animator parentAnimator = candidate.transform.parent != null
                ? candidate.transform.parent.GetComponentInParent<Animator>()
                : null;
            if (parentAnimator == null)
            {
                candidate.transform.localScale *= MatchVisualScale;
            }
        }

        matchVisualScaleApplied = true;
    }

    private void ApplyKnockback(Vector3 hitSource, float multiplier)
    {
        if (body == null)
        {
            return;
        }

        Vector3 direction = Flatten(transform.position - hitSource);
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = -transform.forward;
        }

        // Keep knockback compatible with the same kinematic collision motor.
        DuckKinematicMotor.Move(body, direction.normalized * hitKnockback * Mathf.Max(0f, multiplier) * 0.12f);
    }

    private void Defeat()
    {
        state = EnemyState.Defeated;
        StopMoving();
        if (body != null)
        {
            body.isKinematic = true;
        }

        if (bodyCollider != null)
        {
            bodyCollider.enabled = false;
        }

        Defeated?.Invoke(this);
        onDefeated?.Invoke();
        UpdateStatusVisuals();

        if (Object != null && Runner != null)
        {
            Runner.Despawn(Object);
        }
        else if (defeatedDespawnDelay > 0f)
        {
            StartCoroutine(DespawnAfterDelay());
        }
    }

    private IEnumerator DespawnAfterDelay()
    {
        yield return new WaitForSeconds(defeatedDespawnDelay);
        gameObject.SetActive(false);
    }

    private IEnumerator FlashMarker()
    {
        if (marker == null)
        {
            yield break;
        }

        Vector3 originalScale = marker.localScale;
        marker.localScale = originalScale * 1.65f;
        yield return new WaitForSeconds(0.12f);
        if (marker != null)
        {
            marker.localScale = originalScale;
        }
    }

    private void ApplyRandomSkinAppearance()
    {
        // This guard prevents future prefab-initialization code from reading a
        // networked property before Fusion has called Spawned().
        if (!networkSpawned || RandomSkinColors.Length == 0)
        {
            ApplySkinColor();
            return;
        }

        int index = Mathf.Abs(SkinColorIndex) % RandomSkinColors.Length;
        bodyColor = RandomSkinColors[index];
        ApplySkinColor();
    }

    private void ApplySkinColor()
    {
        // This duck mesh reserves material slot 0 for its skin. Do not tint
        // every renderer: doing so would also recolour the bill and eyes.
        const int skinMaterialIndex = 0;
        SkinnedMeshRenderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        materialProperties ??= new MaterialPropertyBlock();

        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            SkinnedMeshRenderer renderer = renderers[rendererIndex];
            Material[] materials = renderer.sharedMaterials;
            if (skinMaterialIndex >= materials.Length)
            {
                continue;
            }

            Material skinMaterial = materials[skinMaterialIndex];
            if (skinMaterial == null || (!skinMaterial.HasProperty(BaseColorProperty) && !skinMaterial.HasProperty(ColorProperty)))
            {
                continue;
            }

            materialProperties.Clear();
            materialProperties.SetColor(BaseColorProperty, bodyColor);
            materialProperties.SetColor(ColorProperty, bodyColor);
            renderer.SetPropertyBlock(materialProperties, skinMaterialIndex);
        }
    }

    private void ApplyFactionAppearance()
    {
        markerColor = IsConverted ? FactionTeam.GetColor(FactionIndex) : new Color(1f, 0.2f, 0.15f, 1f);
        if (statusLabel != null)
        {
            statusLabel.color = markerColor;
        }

        if (marker == null)
        {
            return;
        }

        if (markerMaterial != null)
        {
            if (markerMaterial.HasProperty(BaseColorProperty)) markerMaterial.SetColor(BaseColorProperty, markerColor);
            if (markerMaterial.HasProperty(ColorProperty)) markerMaterial.SetColor(ColorProperty, markerColor);
        }

        if (markerMeshFilter != null)
        {
            markerMeshFilter.sharedMesh = GetFactionMarkerMesh(FactionIndex);
        }
    }

    private void CreateStatusVisuals()
    {
        GameObject labelObject = new GameObject("Enemy Status");
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition = Vector3.up * GetHeadHeight();
        labelObject.transform.localScale = Vector3.one * 0.18f;
        statusLabel = labelObject.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null)
        {
            statusLabel.font = TMP_Settings.defaultFontAsset;
            statusLabel.fontSharedMaterial = TMP_Settings.defaultFontAsset.material;
        }
        statusLabel.alignment = TextAlignmentOptions.Center;
        statusLabel.fontSize = 6f;
        statusLabel.color = markerColor;
        statusLabel.outlineColor = Color.black;
        statusLabel.outlineWidth = 0.2f;
        statusLabel.textWrappingMode = TextWrappingModes.NoWrap;
        statusLabel.rectTransform.sizeDelta = new Vector2(21f, 6.5f);

        if (!showEnemyMarker)
        {
            return;
        }

        GameObject markerObject = new("Faction Marker");
        markerObject.transform.SetParent(transform, false);
        markerObject.transform.localPosition = Vector3.up * (GetHeadHeight() + 0.34f);
        markerObject.transform.localScale = Vector3.one * 0.18f;
        markerMeshFilter = markerObject.AddComponent<MeshFilter>();
        markerMeshFilter.sharedMesh = GetFactionMarkerMesh(FactionTeam.Neutral);
        MeshRenderer markerRenderer = markerObject.AddComponent<MeshRenderer>();
        if (markerRenderer != null)
        {
            markerMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            markerMaterial.name = "Faction Marker Runtime Material";
            markerMaterial.hideFlags = HideFlags.DontSave;
            if (markerMaterial.HasProperty(BaseColorProperty))
            {
                markerMaterial.SetColor(BaseColorProperty, markerColor);
            }
            if (markerMaterial.HasProperty(ColorProperty))
            {
                markerMaterial.SetColor(ColorProperty, markerColor);
            }
            markerRenderer.sharedMaterial = markerMaterial;
        }

        marker = markerObject.transform;
    }

    private void UpdateStatusVisuals()
    {
        if (statusLabel == null)
        {
            return;
        }

        if (!networkSpawned)
        {
            statusLabel.text = enemyName + "　準備中";
            return;
        }

        if (displayedFactionIndex != FactionIndex)
        {
            displayedFactionIndex = FactionIndex;
            ApplyFactionAppearance();
        }

        if (displayedSkinColorIndex != SkinColorIndex)
        {
            displayedSkinColorIndex = SkinColorIndex;
            ApplyRandomSkinAppearance();
        }

        Camera camera = Camera.main;
        if (camera != null)
        {
            Vector3 direction = statusLabel.transform.position - camera.transform.position;
            if (direction.sqrMagnitude > 0.001f)
            {
                statusLabel.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                if (marker != null)
                {
                    marker.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                }
            }
        }

        if (IsDefeated)
        {
            statusLabel.text = "擊敗";
            if (marker != null)
            {
                marker.gameObject.SetActive(false);
            }
            return;
        }

        string action = GetActionName();
        string displayName = IsConverted
            ? GetFactionMarkerSymbol(FactionIndex) + " " + FactionTeam.GetName(FactionIndex) + "方轉化鴨"
            : "◇ " + enemyName;
        statusLabel.text = displayName + "  " + new string('♥', Mathf.Max(0, CurrentHealth))
            + (HasGuardianShield ? "　護盾" : string.Empty)
            + (HasWand ? "　影杖・" + action : "　" + action)
            + "\n策略：" + GetStrategyName();
        if (marker != null)
        {
            marker.gameObject.SetActive(true);
            float pulse = 1f + Mathf.Sin(Time.time * 5f) * 0.12f;
            marker.localScale = Vector3.one * (0.18f * pulse);
        }
    }

    private static string GetFactionMarkerSymbol(int factionIndex)
    {
        return factionIndex switch
        {
            FactionTeam.Red => "▲",
            FactionTeam.Blue => "■",
            FactionTeam.Green => "●",
            _ => "◇"
        };
    }

    private static Mesh GetFactionMarkerMesh(int factionIndex)
    {
        return factionIndex switch
        {
            FactionTeam.Red => triangleMarkerMesh ??= CreatePolygonMarkerMesh("Triangle Faction Marker", 3, 90f),
            FactionTeam.Blue => squareMarkerMesh ??= CreatePolygonMarkerMesh("Square Faction Marker", 4, 45f),
            FactionTeam.Green => circleMarkerMesh ??= CreatePolygonMarkerMesh("Circle Faction Marker", 20, 90f),
            _ => diamondMarkerMesh ??= CreatePolygonMarkerMesh("Neutral Faction Marker", 4, 90f)
        };
    }

    private static Mesh CreatePolygonMarkerMesh(string meshName, int sides, float startAngleDegrees)
    {
        Vector3[] vertices = new Vector3[sides + 1];
        int[] triangles = new int[sides * 3];
        vertices[0] = Vector3.zero;
        for (int index = 0; index < sides; index++)
        {
            float angle = (startAngleDegrees + 360f * index / sides) * Mathf.Deg2Rad;
            vertices[index + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
        }

        for (int index = 0; index < sides; index++)
        {
            int next = index == sides - 1 ? 1 : index + 2;
            int triangle = index * 3;
            triangles[triangle] = 0;
            triangles[triangle + 1] = next;
            triangles[triangle + 2] = index + 1;
        }

        Mesh mesh = new()
        {
            name = meshName,
            vertices = vertices,
            triangles = triangles,
            hideFlags = HideFlags.DontSave
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    private string GetActionName()
    {
        return state switch
        {
            _ when IsConverted && state == EnemyState.Chase => "護衛／推進",
            EnemyState.SeekWand => "取杖",
            EnemyState.Chase => "追擊",
            EnemyState.Cast => "影法術",
            EnemyState.Stunned => "暈眩",
            _ => "巡邏搜路"
        };
    }

    private string GetStrategyName()
    {
        return state switch
        {
            _ when IsConverted && state == EnemyState.Chase => "跟隨隊友・前往目標",
            _ when IsConverted && state == EnemyState.Cast => "守晶／破晶・施放手杖",
            EnemyState.SeekWand => "爭奪補給・搶先武裝",
            EnemyState.Chase when HasWand => "維持射程・逼迫持杖者",
            EnemyState.Chase => "封鎖持杖者・伺機奪杖",
            EnemyState.Cast => "遠距壓制・保留安全距離",
            EnemyState.Stunned => "受創重整・暫停評估",
            _ when HasWand => "巡防施法點・等待目標",
            _ => "巡邏搜杖・掌握路線"
        };
    }

    private float GetHeadHeight()
    {
        CapsuleCollider capsule = GetComponent<CapsuleCollider>();
        return capsule != null ? capsule.center.y + capsule.height * 0.5f + 0.34f : 2.4f;
    }

    private static Vector3 Flatten(Vector3 direction)
    {
        direction.y = 0f;
        return direction;
    }

    /// <summary>灰影選杖、選人與轉化後選水晶的距離成本。</summary>
    private float GetNavigationCost(Vector3 destination)
    {
        return IsTemporarilyUnreachable(destination) ? 1000f : FlatDistance(transform.position, destination);
    }

    private static float FlatDistance(Vector3 first, Vector3 second)
    {
        return Flatten(first - second).magnitude;
    }
}
