using System.Collections;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;

/// <summary>
/// A first-level computer player. It gathers an available wand, shares
/// grey-shadow targets with the other CPUs, navigates around simple
/// obstructions, and attacks once in range.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
[RequireComponent(typeof(Rigidbody))]
public sealed class Unit1BotDuck : NetworkBehaviour
{
    [Header("AI Debug")]
    [Tooltip("只在 State 或 Target 改變時輸出決策與導航路徑資訊。")]
    [SerializeField] private bool debugAI;
    [SerializeField, Min(0.25f)] private float minimumStateDuration = 0.85f;
    [SerializeField, Min(1f)] private float targetSwitchThreshold = 12f;

    private enum BotAction
    {
        Wander,
        SeekWand,
        SeekShadow,
        AttackEnemy,
        Detour,
        JumpObstacle,
        Retreat,
        DefendCrystal,
        AttackCrystal,
        FollowAlly
    }

    private const float PatrolSpeed = 2.2f;
    private const float ChaseSpeed = 3.45f;
    private const float StopDistance = 0.32f;
    private const float AttackDistance = 1.45f;
    private const float AttackCooldown = 0.62f;
    private const float GuardianThreatDistance = 4.4f;
    private const float GuardianRetreatDistance = 5.4f;
    private const float SurvivalDistance = 4.7f;
    private const float CombatOrbitDistance = 1.14f;
    private const float CrystalAttackDistance = 2.45f;
    private const float CrystalDefenseRadius = 7.5f;
    private const float CrystalGuardDistance = 3.4f;
    private const int CriticalCrystalHealth = 3;
    private const float MatchVisualScale = 0.88f;
    private const int LowHealthThreshold = 1;
    private const float TurnSpeed = 540f;
    private const float TargetRefreshInterval = 0.35f;
    private const float UtilityRefreshInterval = 0.4f;
    private const float DamageImmunityDuration = 0.8f;
    private const float AutoJumpSpeed = 7f;
    private const float AutoJumpGravity = 24f;
    private const float AutoJumpMaximumObstacleHeight = 0.78f;
    private const float AutoJumpMaximumObstacleSpan = 1.55f;
    private const float AutoJumpClearance = 1.1f;
    private const float AutoJumpCooldown = 1.1f;
    private const float RecoveryDuration = 1.35f;
    public const int MaximumHealth = 3;

    private static readonly int SpeedProperty = Animator.StringToHash("Speed");
    private static readonly int GroundedProperty = Animator.StringToHash("Grounded");
    private static readonly int JumpProperty = Animator.StringToHash("Jump");
    private static readonly int IdleState = Animator.StringToHash("idle");
    private static readonly int WalkingState = Animator.StringToHash("Walking");
    private static readonly int JumpState = Animator.StringToHash("jump");
    private static readonly int WandAttackState = Animator.StringToHash("stand attack");

    [Networked] public int BotNumber { get; private set; }
    [Networked] public int FactionIndex { get; private set; }
    [Networked] public NetworkBool HasWand { get; private set; }
    [Networked] public int CurrentHealth { get; private set; }
    [Networked] public NetworkBool IsEliminated { get; private set; }
    [Networked] public Vector3 GravePosition { get; private set; }
    [Networked] public float GraveYaw { get; private set; }
    [Networked] public float GuardianShieldUntil { get; private set; }
    [Networked] private int ActionIndex { get; set; }

    private Rigidbody body;
    private Collider bodyCollider;
    private Animator animator;
    private DuckPlayerNameplate nameplate;
    private TextMeshPro factionLabel;
    private Unit1WandPickup heldWand;
    private Unit1WandPickup targetWand;
    private EnemyDuckAI targetEnemy;
    private FusionDuckPlayer targetPlayer;
    private Unit1BotDuck targetBot;
    private FactionCrystal targetCrystal;
    private Vector3 patrolOrigin;
    private Vector3 patrolTarget;
    private Vector3 detourTarget;
    private readonly List<Vector3> plannedRoute = new();
    private int plannedRouteIndex;
    private Vector3 plannedRouteDestination;
    private float nextRoutePlanAt;
    private int patrolStep;
    private int blockedMoveCount;
    private int currentAnimationState;
    private float wandAttackUntil;
    private float chooseNextPatrolAt;
    private float nextTargetRefreshAt;
    private float nextUtilityDecisionAt;
    private float nextAttackAt;
    private float detourUntil;
    private Vector3 recoveryDestination;
    private int recoverySide;
    private int recoveryAttempts;
    private int failedRecoveryCount;
    private float nextProgressCheckAt;
    private Vector3 lastProgressPosition;
    private int stalledRouteCount;
    private readonly Unit1NavigationMemory unreachableGoals = new();
    private int displayedBotNumber = -1;
    private int displayedFactionIndex = -1;
    private bool displayedHasWand;
    private int displayedActionIndex = -1;
    private int displayedCurrentHealth = -1;
    private bool displayedEliminated;
    private bool displayedShield;
    // In a shared room the master creates the CPU ducks.  Fusion normally
    // gives the spawning peer StateAuthority, but scene transitions can
    // briefly leave a spawned CPU as a proxy.  Without an explicit retry the
    // early authority guard in FixedUpdateNetwork would make that CPU stand
    // still forever.
    private bool stateAuthorityRequestIssued;
    private float nextAuthorityRequestAt;
    private float damageImmuneUntil;
    private float verticalSpeed;
    private float nextAutoJumpAt;
    private bool isGrounded;
    private bool matchVisualScaleApplied;
    private readonly RaycastHit[] groundProbeHits = new RaycastHit[8];
    private BotAction currentState;
    private float currentScore;
    private float stateEnterTime;
    private BotAction plannedAction = BotAction.Wander;
    private float plannedActionScore;
    private Transform lastDebugTarget;
    private BotAction lastDebugState = (BotAction)(-1);

    public bool HasGuardianShield => !IsEliminated && Time.time < GuardianShieldUntil;

    public void ConfigureBot(int botNumber)
    {
        if (HasStateAuthority)
        {
            BotNumber = Mathf.Max(1, botNumber);
        }
    }

    public void AssignFaction(int factionIndex)
    {
        if (HasStateAuthority)
        {
            FactionIndex = FactionTeam.Normalize(factionIndex);
        }
    }

    public override void Spawned()
    {
        body = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<Collider>();
        animator = GetComponentInChildren<Animator>(true);
        ApplyMatchVisualScale();
        patrolOrigin = transform.position;
        patrolTarget = patrolOrigin;
        lastProgressPosition = patrolOrigin;
        nextProgressCheckAt = Time.time + 0.5f;
        Unit1RouteScanner.PrimeMapScan(patrolOrigin, bodyCollider);
        if (HasStateAuthority)
        {
            CurrentHealth = MaximumHealth;
            IsEliminated = false;
            GravePosition = transform.position;
            GraveYaw = transform.eulerAngles.y;
            HasWand = false;
            GuardianShieldUntil = 0f;
        }
        ConfigureBody();
        isGrounded = IsGroundBelowFeet();
        EnsureMasterAuthority();
        EnsurePresentation();
        RefreshPresentation();
        DuckGraveMarker.GetOrAdd(gameObject).Apply(IsEliminated, GravePosition, GraveYaw);
    }

    public override void FixedUpdateNetwork()
    {
        if (body == null)
        {
            return;
        }

        // Only the room master is allowed to drive AI in this Shared-mode
        // game.  Requesting authority here makes the hand-off resilient to
        // a scene-load timing race, while every other client remains a pure
        // NetworkTransform replica.
        if (!HasStateAuthority)
        {
            EnsureMasterAuthority();
            return;
        }

        stateAuthorityRequestIssued = false;

        if (IsEliminated)
        {
            return;
        }

        if (Unit1GameDirector.IsMatchFinished())
        {
            PlayMovementAnimation(false);
            return;
        }

        AdvanceVerticalMotion();

        if (Time.time < wandAttackUntil)
        {
            PlayMovementAnimation(false);
            return;
        }

        RefreshHeldWand();
        if (!HasWand)
        {
            RefreshUnarmedPlan();
            RunWandSearch();
            return;
        }

        Unit1WandEffectProfile profile = heldWand != null
            ? heldWand.EffectProfile
            : Unit1WandPickup.GetEffectProfile(Unit1WandAbility.Swift);
        RefreshUtilityPlan(profile);
        ExecuteUtilityPlan(profile);
    }

    public override void Render()
    {
        EnsurePresentation();
        if (!IsEliminated) RefreshPresentation();
        DuckGraveMarker.GetOrAdd(gameObject).Apply(IsEliminated, GravePosition, GraveYaw);
    }

    public void TakeDamage(int damage, Vector3 hitSource)
    {
        if (Object != null && !HasStateAuthority)
        {
            RPC_RequestDamage(damage, hitSource);
            return;
        }

        ApplyDamage(damage, hitSource);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDamage(int damage, Vector3 hitSource)
    {
        ApplyDamage(damage, hitSource);
    }

    private void ApplyDamage(int damage, Vector3 hitSource)
    {
        if (IsEliminated || damage <= 0 || HasGuardianShield || Time.time < damageImmuneUntil)
        {
            return;
        }

        damageImmuneUntil = Time.time + DamageImmunityDuration;
        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        if (CurrentHealth > 0)
        {
            return;
        }

        IsEliminated = true;
        GravePosition = DuckGraveMarker.FindGroundPosition(transform, bodyCollider);
        GraveYaw = transform.eulerAngles.y;
        HasWand = false;
        heldWand?.ReleaseIfHeldBy(Object);
        heldWand = null;
        targetWand = null;
        ClearCombatTarget();
        DuckGraveMarker.GetOrAdd(gameObject).Apply(true, GravePosition, GraveYaw);
    }

    private void LateUpdate()
    {
        if (factionLabel == null)
        {
            return;
        }

        Camera camera = Camera.main;
        if (camera == null || !camera.isActiveAndEnabled)
        {
            return;
        }

        Vector3 direction = factionLabel.transform.position - camera.transform.position;
        if (direction.sqrMagnitude > 0.001f)
        {
            factionLabel.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }

    private void ConfigureBody()
    {
        if (body == null)
        {
            return;
        }

        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void EnsureMasterAuthority()
    {
        if (HasStateAuthority
            || (stateAuthorityRequestIssued && Time.time < nextAuthorityRequestAt)
            || Runner == null
            || !Runner.IsSharedModeMasterClient
            || Object == null
            || !Object.IsValid)
        {
            return;
        }

        // Unit1CpuDuck inherits AllowStateAuthorityOverride from the player
        // prefab.  This is intentionally a request rather than a local
        // movement fallback: Fusion must confirm authority before the bot
        // writes a transform visible to the rest of the room.
        Object.RequestStateAuthority();
        stateAuthorityRequestIssued = true;
        nextAuthorityRequestAt = Time.time + 1f;
        Debug.Log("[UNIT1-DIAG CPU] requested StateAuthority | object=" + Object.Id, this);
    }

    private void RunWandSearch()
    {
        RefreshWandTarget();

        if (targetWand == null)
        {
            Patrol();
            return;
        }

        SetAction(BotAction.SeekWand);
        if (targetWand.CanBePickedUpBy(transform.position))
        {
            FaceTowards(targetWand.transform.position);
            if (targetWand.TryPickupByNetworkObject(Object))
            {
                heldWand = targetWand;
                targetWand = null;
                HasWand = true;
                nextTargetRefreshAt = nextUtilityDecisionAt = 0f;
                SetAction(BotAction.SeekShadow);
                return;
            }

            // Another duck won this wand during the same network tick.
            targetWand = null;
            return;
        }

        MoveTowards(targetWand.transform.position, ChaseSpeed, targetWand.NavigationArrivalDistance);
    }

    private void RefreshWandTarget()
    {
        bool currentValid = targetWand != null && targetWand.IsAvailable
            && !IsTemporarilyUnreachable(targetWand.transform.position);
        if (currentValid && Time.time < nextTargetRefreshAt) return;
        nextTargetRefreshAt = Time.time + TargetRefreshInterval;
        Unit1WandPickup candidate = FindBestAvailableWand();
        if (!currentValid || (candidate != null && candidate != targetWand
            && ShouldSwitchTarget(candidate.transform, targetWand.transform)))
        {
            if (targetWand != candidate)
            {
                detourUntil = 0f;
                Unit1RouteScanner.InvalidateRoute(plannedRoute, ref plannedRouteIndex, ref nextRoutePlanAt);
            }
            targetWand = candidate;
        }
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
        ClearCombatTarget();
    }

    /// <summary>
    /// 沒有手杖時只有取杖與巡邏兩種低階選項；仍透過同一份 Utility
    /// 計畫紀錄分數，方便在 Inspector Debug 中看出 AI 為何這樣做。
    /// </summary>
    private void RefreshUnarmedPlan()
    {
        if (Time.time < nextUtilityDecisionAt)
        {
            return;
        }

        nextUtilityDecisionAt = Time.time + UtilityRefreshInterval;
        RefreshWandTarget();
        if (targetWand == null)
        {
            SetUtilityPlan(BotAction.Wander, 6f, false);
            return;
        }

        float score = 68f - GetNavigationCost(targetWand.transform.position) * 1.15f;
        score -= targetWand.EffectProfile.Ability == Unit1WandAbility.Heavy ? 5f : 0f;
        SetUtilityPlan(BotAction.SeekWand, Mathf.Max(12f, score), false);
    }

    /// <summary>
    /// 戰術層只在固定間隔重新評分；移動與戰鬥完全由下方的執行層負責。
    /// 這讓 CPU 不會每個網路 tick 在取杖、打人、回防之間來回搶控制權。
    /// </summary>
    private void RefreshUtilityPlan(Unit1WandEffectProfile profile)
    {
        if (Time.time < nextUtilityDecisionAt)
        {
            return;
        }

        nextUtilityDecisionAt = Time.time + UtilityRefreshInterval;
        if (!HasCombatTarget() || Time.time >= nextTargetRefreshAt)
        {
            nextTargetRefreshAt = Time.time + TargetRefreshInterval;
            SelectBestCombatTarget();
        }

        if (targetCrystal == null || targetCrystal.IsDestroyed || !targetCrystal.CanBeDamagedBy(FactionIndex)
            || IsTemporarilyUnreachable(targetCrystal.transform.position))
        {
            targetCrystal = FindBestEnemyCrystal();
        }

        FactionCrystal friendlyCrystal = FactionCrystal.FindForFaction(FactionIndex);
        int crystalThreats = CountCrystalThreats(friendlyCrystal);
        int missingCrystalHealth = friendlyCrystal != null && !friendlyCrystal.IsDestroyed
            ? Mathf.Max(0, friendlyCrystal.MaximumHealth - friendlyCrystal.CurrentHealth)
            : 0;

        BotAction bestAction = BotAction.Wander;
        float bestScore = 8f;

        // 水晶附近有敵人或血量偏低時，防守分數會迅速超過一般進攻。
        if (friendlyCrystal != null && !friendlyCrystal.IsDestroyed
            && !IsTemporarilyUnreachable(friendlyCrystal.transform.position))
        {
            float distance = GetNavigationCost(friendlyCrystal.transform.position);
            float defendScore = crystalThreats * 27f + missingCrystalHealth * 10f - distance * 0.55f;
            if (profile.Ability == Unit1WandAbility.Guardian)
            {
                defendScore += 16f;
            }

            if (friendlyCrystal.CurrentHealth <= CriticalCrystalHealth)
            {
                defendScore += 22f;
            }

            ConsiderUtilityAction(BotAction.DefendCrystal, defendScore, ref bestAction, ref bestScore);
        }

        // 只剩一顆心時，除非能立刻收掉無杖的殘血敵人，否則撤退優先。
        if (CurrentHealth <= LowHealthThreshold && !HasGuardianShield)
        {
            float retreatDistance = friendlyCrystal != null && !friendlyCrystal.IsDestroyed
                ? GetNavigationCost(friendlyCrystal.transform.position)
                : 0f;
            float retreatScore = 92f - retreatDistance * 0.45f;
            ConsiderUtilityAction(BotAction.Retreat, retreatScore, ref bestAction, ref bestScore);
        }

        if (TryGetCombatTarget(out Transform combatTarget, out int health, out bool targetHasWand, out bool shielded))
        {
            float combatDistance = GetNavigationCost(combatTarget.position);
            float combatScore = 40f
                + (targetHasWand ? 15f : 0f)
                + (health <= 1 ? 12f : 0f)
                - (shielded ? 14f : 0f)
                - combatDistance * 0.45f;
            combatScore -= CountBotsTargeting(combatTarget) * 5f;
            ConsiderUtilityAction(BotAction.AttackEnemy, combatScore, ref bestAction, ref bestScore);
        }

        if (targetCrystal != null)
        {
            float assaultDistance = GetNavigationCost(targetCrystal.transform.position);
            float assaultScore = 25f
                + (profile.Ability == Unit1WandAbility.Heavy ? 20f : profile.Ability == Unit1WandAbility.Swift ? 8f : -18f)
                + Mathf.Max(0, 5 - targetCrystal.CurrentHealth) * 5f
                - assaultDistance * 0.38f
                - crystalThreats * 12f;
            ConsiderUtilityAction(BotAction.AttackCrystal, assaultScore, ref bestAction, ref bestScore);
        }

        if (profile.Ability == Unit1WandAbility.Guardian && friendlyCrystal != null && !friendlyCrystal.IsDestroyed
            && !IsTemporarilyUnreachable(friendlyCrystal.transform.position))
        {
            float followScore = 24f - GetNavigationCost(friendlyCrystal.transform.position) * 0.3f;
            ConsiderUtilityAction(BotAction.FollowAlly, followScore, ref bestAction, ref bestScore);
        }

        bool emergency = bestAction == BotAction.Retreat
            || (bestAction == BotAction.DefendCrystal
                && (crystalThreats > 0 || missingCrystalHealth >= 2));
        SetUtilityPlan(bestAction, bestScore, emergency);
    }

    private void ConsiderUtilityAction(BotAction action, float score, ref BotAction bestAction, ref float bestScore)
    {
        if (score > bestScore)
        {
            bestAction = action;
            bestScore = score;
        }
    }

    private void SetUtilityPlan(BotAction action, float score, bool emergency)
    {
        if (action == plannedAction)
        {
            plannedActionScore = score;
            return;
        }

        bool stateLockElapsed = Time.time >= stateEnterTime + minimumStateDuration;
        bool clearlyBetter = score >= plannedActionScore + targetSwitchThreshold;
        if (!emergency && !stateLockElapsed && !clearlyBetter)
        {
            return;
        }

        plannedAction = action;
        plannedActionScore = score;
    }

    private void ExecuteUtilityPlan(Unit1WandEffectProfile profile)
    {
        switch (plannedAction)
        {
            case BotAction.DefendCrystal:
                if (!RunCrystalDefense(profile))
                {
                    RunEnemyCombat(profile);
                }
                return;

            case BotAction.Retreat:
                RunSurvivalStrategy();
                return;

            case BotAction.AttackCrystal:
                RunCrystalAssault(profile);
                return;

            case BotAction.FollowAlly:
                RunGuardianStrategy(profile);
                return;

            case BotAction.AttackEnemy:
            case BotAction.SeekShadow:
                RunEnemyCombat(profile);
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
                && !shadow.IsDefeated
                && shadow.IsFactionEnemyOf(FactionIndex)
                && FlatDistance(shadow.transform.position, friendlyCrystal.transform.position) <= CrystalDefenseRadius)
            {
                threats++;
            }
        }

        FusionDuckPlayer[] players = FindObjectsByType<FusionDuckPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int index = 0; index < players.Length; index++)
        {
            FusionDuckPlayer player = players[index];
            if (player != null
                && !player.IsEliminated
                && FactionTeam.AreEnemies(FactionIndex, player.FactionIndex)
                && FlatDistance(player.transform.position, friendlyCrystal.transform.position) <= CrystalDefenseRadius)
            {
                threats++;
            }
        }

        Unit1BotDuck[] bots = FindObjectsByType<Unit1BotDuck>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int index = 0; index < bots.Length; index++)
        {
            Unit1BotDuck bot = bots[index];
            if (bot != null
                && bot != this
                && !bot.IsEliminated
                && FactionTeam.AreEnemies(FactionIndex, bot.FactionIndex)
                && FlatDistance(bot.transform.position, friendlyCrystal.transform.position) <= CrystalDefenseRadius)
            {
                threats++;
            }
        }

        return threats;
    }

    private Unit1WandPickup FindBestAvailableWand()
    {
        Unit1WandPickup[] wands = FindObjectsByType<Unit1WandPickup>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        Unit1WandPickup best = null;
        float bestScore = float.PositiveInfinity;
        bool needsProtection = CurrentHealth <= LowHealthThreshold && !HasGuardianShield;
        for (int i = 0; i < wands.Length; i++)
        {
            Unit1WandPickup candidate = wands[i];
            if (candidate == null || !candidate.IsAvailable || IsTemporarilyUnreachable(candidate.transform.position))
            {
                continue;
            }

            Unit1WandEffectProfile profile = candidate.EffectProfile;
            float score = GetNavigationCost(candidate.transform.position);
            score += CountBotsTargetingWand(candidate) * 5.5f;

            // CPU #1/#4 lead with heavy damage, #2/#5 sweep quickly, and
            // #3/#6 take a flexible role. Guardian Wands are deliberately
            // reserved for ducks that are about to be eliminated, so a full
            // health CPU does not waste the team's offence by hiding forever.
            if (profile.Ability == Unit1WandAbility.Guardian)
            {
                score += needsProtection ? -12f : 8.5f;
            }
            else if (profile.Ability == Unit1WandAbility.Heavy)
            {
                score += BotNumber % 3 == 1 ? -5.5f : -2.2f;
            }
            else
            {
                score += BotNumber % 3 == 2 ? -4.2f : -1.4f;
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private int CountBotsTargetingWand(Unit1WandPickup wand)
    {
        Unit1BotDuck[] bots = FindObjectsByType<Unit1BotDuck>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        int claimed = 0;
        for (int i = 0; i < bots.Length; i++)
        {
            if (bots[i] != null && bots[i] != this && bots[i].targetWand == wand)
            {
                claimed++;
            }
        }

        return claimed;
    }

    private void SelectNextPatrolPoint()
    {
        patrolStep++;
        Vector3 current = body != null ? body.position : transform.position;
        if (!Unit1RouteScanner.TryFindRoamingPoint(current, BotNumber + patrolStep * 137, out patrolTarget))
            patrolTarget = current;
        chooseNextPatrolAt = Time.time + Mathf.Max(6f, FlatDistance(current, patrolTarget) / PatrolSpeed + 4f);
        Unit1RouteScanner.InvalidateRoute(plannedRoute, ref plannedRouteIndex, ref nextRoutePlanAt);
    }

    private void RunEnemyCombat(Unit1WandEffectProfile profile)
    {
        if (profile.Ability == Unit1WandAbility.Guardian)
        {
            RunGuardianStrategy(profile);
            return;
        }

        if (!HasCombatTarget() || Time.time >= nextTargetRefreshAt)
        {
            nextTargetRefreshAt = Time.time + TargetRefreshInterval;
            SelectBestCombatTarget();
        }

        if (!HasCombatTarget())
        {
            RunCrystalAssault(profile);
            return;
        }

        AttackCurrentTarget(profile);
    }

    /// <summary>
    /// A Guardian Wand is a survival role, not an idle attacker. The CPU lets
    /// nearby wand-carrying Grey Shadows chase it, shields, then kites away so
    /// the offensive ducks have time to finish their own targets.
    /// </summary>
    private void RunGuardianStrategy(Unit1WandEffectProfile profile)
    {
        FactionCrystal friendlyCrystal = FactionCrystal.FindForFaction(FactionIndex);
        if (friendlyCrystal != null && IsTemporarilyUnreachable(friendlyCrystal.transform.position))
            friendlyCrystal = null;
        bool needsCrystalSupport = friendlyCrystal != null
            && !friendlyCrystal.IsDestroyed
            && friendlyCrystal.CurrentHealth < friendlyCrystal.MaximumHealth;
        if (needsCrystalSupport
            && FlatDistance(transform.position, friendlyCrystal.transform.position) > CrystalGuardDistance)
        {
            SetAction(BotAction.DefendCrystal);
            MoveTowards(friendlyCrystal.transform.position, ChaseSpeed, CrystalGuardDistance);
            return;
        }

        if (!HasCombatTarget() || Time.time >= nextTargetRefreshAt)
        {
            nextTargetRefreshAt = Time.time + TargetRefreshInterval;
            SelectBestCombatTarget();
        }

        if (!TryGetCombatTarget(out Transform target, out _, out _, out _))
        {
            if (friendlyCrystal != null && !friendlyCrystal.IsDestroyed)
            {
                SetAction(BotAction.FollowAlly);
                MoveTowards(friendlyCrystal.transform.position, PatrolSpeed, CrystalGuardDistance);
            }
            else
            {
                Patrol();
            }
            return;
        }

        float distance = FlatDistance(transform.position, target.position);
        if (distance > GuardianThreatDistance)
        {
            if (friendlyCrystal != null && !friendlyCrystal.IsDestroyed)
            {
                SetAction(BotAction.FollowAlly);
                MoveTowards(friendlyCrystal.transform.position, PatrolSpeed, CrystalGuardDistance);
            }
            else
            {
                Patrol();
            }
            return;
        }

        if (Time.time >= nextAttackAt)
        {
            float windup = PlayWandAttackAnimation();
            nextAttackAt = Time.time + windup + Mathf.Max(profile.AttackCooldown, 0.08f);
            StartCoroutine(ResolveGuardianAfterWindup(profile, windup));
            // A guardian cast is also a standing attack: do not let this one
            // simulation tick move the CPU away before its swing is visible.
            PlayMovementAnimation(false);
            return;
        }

        Vector3 away = transform.position - target.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.001f)
        {
            away = -transform.forward;
        }

        SetAction(BotAction.Retreat);
        MoveTowards(transform.position + away.normalized * GuardianRetreatDistance, ChaseSpeed, 0.15f);
    }

    private void RunSurvivalStrategy()
    {
        if (!HasCombatTarget() || Time.time >= nextTargetRefreshAt)
        {
            nextTargetRefreshAt = Time.time + TargetRefreshInterval;
            SelectBestCombatTarget();
        }

        if (!TryGetCombatTarget(out Transform target, out int targetHealth, out bool targetHasWand, out _))
        {
            FactionCrystal friendlyCrystal = FactionCrystal.FindForFaction(FactionIndex);
            if (friendlyCrystal != null && !friendlyCrystal.IsDestroyed
                && !IsTemporarilyUnreachable(friendlyCrystal.transform.position))
            {
                SetAction(BotAction.DefendCrystal);
                MoveTowards(friendlyCrystal.transform.position, PatrolSpeed, CrystalGuardDistance);
            }
            else
            {
                Patrol();
            }
            return;
        }

        float distance = FlatDistance(transform.position, target.position);
        // A vulnerable, unarmed one-hit opponent is a calculated finish
        // instead of a retreat. Every other situation preserves this CPU's
        // last life so its faction does not lose a defender for nothing.
        if (targetHealth <= 1 && !targetHasWand && distance <= AttackDistance)
        {
            Unit1WandEffectProfile profile = heldWand != null
                ? heldWand.EffectProfile
                : Unit1WandPickup.GetEffectProfile(Unit1WandAbility.Swift);
            AttackCurrentTarget(profile);
            return;
        }

        if (distance >= SurvivalDistance)
        {
            SetAction(BotAction.Retreat);
            PlayMovementAnimation(false);
            return;
        }

        // 撤退目的地必須是一個可經由地圖路線抵達的安全點；優先選己方
        // 水晶，而不是朝敵人反方向直走（那一側可能正好被房屋封住）。
        FactionCrystal retreatCrystal = FactionCrystal.FindForFaction(FactionIndex);
        SetAction(BotAction.Retreat);
        if (retreatCrystal != null && !retreatCrystal.IsDestroyed
            && !IsTemporarilyUnreachable(retreatCrystal.transform.position))
        {
            MoveTowards(retreatCrystal.transform.position, ChaseSpeed, CrystalGuardDistance);
            return;
        }

        Patrol();
    }

    private void SelectBestCombatTarget()
    {
        ClearCombatTarget();
        float bestScore = float.PositiveInfinity;

        EnemyDuckAI[] shadows = FindObjectsByType<EnemyDuckAI>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int index = 0; index < shadows.Length; index++)
        {
            EnemyDuckAI candidate = shadows[index];
            if (candidate == null || candidate.IsDefeated || !candidate.CanBeTargetedByFaction(FactionIndex))
            {
                continue;
            }

            if (IsTemporarilyUnreachable(candidate.transform.position)) continue;
            float score = ScoreCombatTarget(
                candidate.transform,
                candidate.CurrentHealth,
                candidate.HasWand,
                candidate.HasGuardianShield,
                CountBotsTargeting(candidate.transform));
            if (score < bestScore)
            {
                bestScore = score;
                targetEnemy = candidate;
            }
        }

        FusionDuckPlayer[] players = FindObjectsByType<FusionDuckPlayer>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int index = 0; index < players.Length; index++)
        {
            FusionDuckPlayer candidate = players[index];
            if (candidate == null || candidate.IsEliminated || !FactionTeam.AreEnemies(FactionIndex, candidate.FactionIndex))
            {
                continue;
            }

            if (IsTemporarilyUnreachable(candidate.transform.position)) continue;
            float score = ScoreCombatTarget(
                candidate.transform,
                candidate.CurrentHealth,
                candidate.HasWand,
                candidate.HasGuardianShield,
                CountBotsTargeting(candidate.transform));
            if (score < bestScore)
            {
                bestScore = score;
                targetEnemy = null;
                targetPlayer = candidate;
                targetBot = null;
            }
        }

        Unit1BotDuck[] bots = FindObjectsByType<Unit1BotDuck>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int index = 0; index < bots.Length; index++)
        {
            Unit1BotDuck candidate = bots[index];
            if (candidate == null || candidate == this || candidate.IsEliminated
                || !FactionTeam.AreEnemies(FactionIndex, candidate.FactionIndex))
            {
                continue;
            }

            if (IsTemporarilyUnreachable(candidate.transform.position)) continue;
            float score = ScoreCombatTarget(
                candidate.transform,
                candidate.CurrentHealth,
                candidate.HasWand,
                candidate.HasGuardianShield,
                CountBotsTargeting(candidate.transform));
            if (score < bestScore)
            {
                bestScore = score;
                targetEnemy = null;
                targetPlayer = null;
                targetBot = candidate;
            }
        }
    }

    private float ScoreCombatTarget(
        Transform candidate,
        int health,
        bool hasWand,
        bool shielded,
        int committedBots)
    {
        // Armed attackers and low-health targets deserve immediate attention;
        // the shared commitment penalty keeps allied CPUs from dog-piling.
        return GetNavigationCost(candidate.position)
            + committedBots * 5.5f
            + Mathf.Max(1, health) * 1.35f
            + (hasWand ? -14f : 0f)
            + (shielded ? 8.5f : 0f)
            + (IsTemporarilyUnreachable(candidate.position) ? 50f : 0f);
    }

    private int CountBotsTargeting(Transform candidate)
    {
        Unit1BotDuck[] bots = FindObjectsByType<Unit1BotDuck>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        int claimed = 0;
        for (int index = 0; index < bots.Length; index++)
        {
            Unit1BotDuck bot = bots[index];
            if (bot != null && bot != this && bot.GetCombatTargetTransform() == candidate)
            {
                claimed++;
            }
        }

        return claimed;
    }

    private bool RunCrystalDefense(Unit1WandEffectProfile profile)
    {
        FactionCrystal friendlyCrystal = FactionCrystal.FindForFaction(FactionIndex);
        if (friendlyCrystal == null || friendlyCrystal.IsDestroyed
            || IsTemporarilyUnreachable(friendlyCrystal.transform.position))
        {
            return false;
        }

        if (FindThreateningCrystalAttacker(friendlyCrystal))
        {
            if (profile.Ability == Unit1WandAbility.Guardian)
            {
                RunGuardianStrategy(profile);
            }
            else
            {
                AttackCurrentTarget(profile);
            }
            return true;
        }

        if (friendlyCrystal.CurrentHealth <= CriticalCrystalHealth)
        {
            SetAction(BotAction.DefendCrystal);
            MoveTowards(friendlyCrystal.transform.position, ChaseSpeed, CrystalGuardDistance);
            return true;
        }

        return false;
    }

    private bool FindThreateningCrystalAttacker(FactionCrystal friendlyCrystal)
    {
        ClearCombatTarget();
        float bestScore = float.PositiveInfinity;

        EnemyDuckAI[] shadows = FindObjectsByType<EnemyDuckAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int index = 0; index < shadows.Length; index++)
        {
            EnemyDuckAI candidate = shadows[index];
            if (candidate == null || candidate.IsDefeated || !candidate.IsFactionEnemyOf(FactionIndex)
                || IsTemporarilyUnreachable(candidate.transform.position)
                || FlatDistance(candidate.transform.position, friendlyCrystal.transform.position) > CrystalDefenseRadius)
            {
                continue;
            }

            float score = GetNavigationCost(candidate.transform.position) + (candidate.HasWand ? -12f : 0f);
            if (score < bestScore)
            {
                bestScore = score;
                targetEnemy = candidate;
            }
        }

        FusionDuckPlayer[] players = FindObjectsByType<FusionDuckPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int index = 0; index < players.Length; index++)
        {
            FusionDuckPlayer candidate = players[index];
            if (candidate == null || candidate.IsEliminated || !FactionTeam.AreEnemies(FactionIndex, candidate.FactionIndex)
                || IsTemporarilyUnreachable(candidate.transform.position)
                || FlatDistance(candidate.transform.position, friendlyCrystal.transform.position) > CrystalDefenseRadius)
            {
                continue;
            }

            float score = GetNavigationCost(candidate.transform.position) + (candidate.HasWand ? -12f : 0f);
            if (score < bestScore)
            {
                bestScore = score;
                targetEnemy = null;
                targetPlayer = candidate;
                targetBot = null;
            }
        }

        Unit1BotDuck[] bots = FindObjectsByType<Unit1BotDuck>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int index = 0; index < bots.Length; index++)
        {
            Unit1BotDuck candidate = bots[index];
            if (candidate == null || candidate == this || candidate.IsEliminated
                || !FactionTeam.AreEnemies(FactionIndex, candidate.FactionIndex)
                || IsTemporarilyUnreachable(candidate.transform.position)
                || FlatDistance(candidate.transform.position, friendlyCrystal.transform.position) > CrystalDefenseRadius)
            {
                continue;
            }

            float score = GetNavigationCost(candidate.transform.position) + (candidate.HasWand ? -12f : 0f);
            if (score < bestScore)
            {
                bestScore = score;
                targetEnemy = null;
                targetPlayer = null;
                targetBot = candidate;
            }
        }

        return HasCombatTarget();
    }

    private void RunCrystalAssault(Unit1WandEffectProfile profile)
    {
        if (profile.Ability == Unit1WandAbility.Guardian)
        {
            RunGuardianStrategy(profile);
            return;
        }

        if (targetCrystal == null || targetCrystal.IsDestroyed || !targetCrystal.CanBeDamagedBy(FactionIndex)
            || IsTemporarilyUnreachable(targetCrystal.transform.position))
        {
            nextTargetRefreshAt = Time.time + TargetRefreshInterval;
            targetCrystal = FindBestEnemyCrystal();
        }
        else if (Time.time >= nextTargetRefreshAt)
        {
            nextTargetRefreshAt = Time.time + TargetRefreshInterval;
            FactionCrystal candidate = FindBestEnemyCrystal();
            if (candidate != null && candidate != targetCrystal && ShouldSwitchTarget(candidate.transform, targetCrystal.transform))
            {
                targetCrystal = candidate;
            }
        }

        if (targetCrystal == null)
        {
            Patrol();
            return;
        }

        float distance = FlatDistance(transform.position, targetCrystal.transform.position);
        if (distance > CrystalAttackDistance)
        {
            SetAction(BotAction.AttackCrystal);
            MoveTowards(targetCrystal.transform.position, ChaseSpeed, CrystalAttackDistance);
            return;
        }

        FaceTowards(targetCrystal.transform.position);
        PlayMovementAnimation(false);
        SetAction(BotAction.AttackCrystal);
        if (Time.time < nextAttackAt)
        {
            return;
        }

        float windup = PlayWandAttackAnimation();
        nextAttackAt = Time.time + windup + Mathf.Max(profile.AttackCooldown, 0.08f);
        StartCoroutine(ResolveCrystalStrikeAfterWindup(targetCrystal, profile, windup));
    }

    private FactionCrystal FindBestEnemyCrystal()
    {
        FactionCrystal[] crystals = FindObjectsByType<FactionCrystal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
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

            float score = GetNavigationCost(candidate.transform.position)
                + candidate.CurrentHealth * 0.45f
                + (IsTemporarilyUnreachable(candidate.transform.position) ? 50f : 0f);
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private void AttackCurrentTarget(Unit1WandEffectProfile profile)
    {
        if (!TryGetCombatTarget(out Transform target, out _, out _, out _))
        {
            return;
        }

        float distance = FlatDistance(transform.position, target.position);
        if (distance > AttackDistance)
        {
            SetAction(BotAction.SeekShadow);
            MoveToCombatPosition(target);
            return;
        }

        FaceTowards(target.position);
        PlayMovementAnimation(false);
        SetAction(BotAction.AttackEnemy);
        if (Time.time < nextAttackAt)
        {
            return;
        }

        float windup = PlayWandAttackAnimation();
        nextAttackAt = Time.time + windup + Mathf.Max(profile.AttackCooldown, 0.08f) + (BotNumber % 2) * 0.08f;
        StartCoroutine(ResolveStrikeAfterWindup(targetEnemy, targetPlayer, targetBot, profile, windup));
    }

    private void MoveToCombatPosition(Transform enemy)
    {
        Vector3 radial = transform.position - enemy.position;
        radial.y = 0f;
        if (radial.sqrMagnitude < 0.001f)
        {
            float angle = (BotNumber * 79f) * Mathf.Deg2Rad;
            radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        radial.Normalize();
        float side = BotNumber % 2 == 0 ? 1f : -1f;
        Vector3 tangent = Vector3.Cross(Vector3.up, radial) * side;
        Vector3 position = enemy.position + radial * CombatOrbitDistance + tangent * 0.3f;
        MoveTowards(position, ChaseSpeed, 0.16f);
    }

    private bool HasCombatTarget()
    {
        return TryGetCombatTarget(out _, out _, out _, out _);
    }

    private Transform GetCombatTargetTransform()
    {
        return TryGetCombatTarget(out Transform target, out _, out _, out _) ? target : null;
    }

    private bool TryGetCombatTarget(
        out Transform target,
        out int health,
        out bool hasWand,
        out bool shielded)
    {
        target = null;
        health = 0;
        hasWand = false;
        shielded = false;
        if (targetEnemy != null && !targetEnemy.IsDefeated && targetEnemy.CanBeTargetedByFaction(FactionIndex)
            && !IsTemporarilyUnreachable(targetEnemy.transform.position))
        {
            target = targetEnemy.transform;
            health = targetEnemy.CurrentHealth;
            hasWand = targetEnemy.HasWand;
            shielded = targetEnemy.HasGuardianShield;
            return true;
        }

        if (targetPlayer != null && !targetPlayer.IsEliminated && FactionTeam.AreEnemies(FactionIndex, targetPlayer.FactionIndex)
            && !IsTemporarilyUnreachable(targetPlayer.transform.position))
        {
            target = targetPlayer.transform;
            health = targetPlayer.CurrentHealth;
            hasWand = targetPlayer.HasWand;
            shielded = targetPlayer.HasGuardianShield;
            return true;
        }

        if (targetBot != null && !targetBot.IsEliminated && FactionTeam.AreEnemies(FactionIndex, targetBot.FactionIndex)
            && !IsTemporarilyUnreachable(targetBot.transform.position))
        {
            target = targetBot.transform;
            health = targetBot.CurrentHealth;
            hasWand = targetBot.HasWand;
            shielded = targetBot.HasGuardianShield;
            return true;
        }

        ClearCombatTarget();
        return false;
    }

    private void ClearCombatTarget()
    {
        targetEnemy = null;
        targetPlayer = null;
        targetBot = null;
    }

    private IEnumerator ResolveStrikeAfterWindup(
        EnemyDuckAI enemy,
        FusionDuckPlayer player,
        Unit1BotDuck cpu,
        Unit1WandEffectProfile profile,
        float windup)
    {
        yield return new WaitForSeconds(windup);
        if (!HasStateAuthority
            || Unit1GameDirector.IsMatchFinished()
            || IsEliminated
            || !HasWand
            || (enemy == null && player == null && cpu == null))
        {
            yield break;
        }

        Vector3 strikeOrigin = transform.position + Vector3.up * 1.15f;
        Transform target = enemy != null ? enemy.transform : player != null ? player.transform : cpu.transform;
        if (FlatDistance(transform.position, target.position) > AttackDistance * 1.2f)
        {
            yield break;
        }

        bool hit = false;
        if (enemy != null && !enemy.IsDefeated)
        {
            hit = enemy.TakeWandHit(Object, transform.position, profile.Damage, profile.StunDuration, profile.KnockbackMultiplier);
        }
        else if (player != null && !player.IsEliminated && FactionTeam.AreEnemies(FactionIndex, player.FactionIndex))
        {
            player.TakeDamage(profile.Damage, transform.position);
            hit = true;
        }
        else if (cpu != null && !cpu.IsEliminated && FactionTeam.AreEnemies(FactionIndex, cpu.FactionIndex))
        {
            cpu.TakeDamage(profile.Damage, transform.position);
            hit = true;
        }

        if (hit)
        {
            Vector3 strikeEnd = target.position + Vector3.up * 1.1f;
            DuckWandAttack.CreateWandCast(strikeOrigin, strikeEnd, profile);
        }
    }

    private IEnumerator ResolveCrystalStrikeAfterWindup(FactionCrystal crystal, Unit1WandEffectProfile profile, float windup)
    {
        yield return new WaitForSeconds(windup);
        if (!HasStateAuthority
            || Unit1GameDirector.IsMatchFinished()
            || IsEliminated
            || !HasWand
            || crystal == null
            || !crystal.CanBeDamagedBy(FactionIndex)
            || FlatDistance(transform.position, crystal.transform.position) > CrystalAttackDistance * 1.2f)
        {
            yield break;
        }

        crystal.TakeDamage(Object, profile.Damage);
        DuckWandAttack.CreateWandCast(
            transform.position + Vector3.up * 1.15f,
            crystal.transform.position + Vector3.up * 1.25f,
            profile);
    }

    private IEnumerator ResolveGuardianAfterWindup(Unit1WandEffectProfile profile, float windup)
    {
        yield return new WaitForSeconds(windup);
        if (!HasStateAuthority || Unit1GameDirector.IsMatchFinished() || IsEliminated || !HasWand)
        {
            yield break;
        }

        if (FactionCrystal.TryProtectNearestFriendlyCrystal(
                transform.position,
                FactionIndex,
                profile.ShieldDuration,
                1,
                out FactionCrystal protectedCrystal))
        {
            DuckWandAttack.CreateGuardianAura(protectedCrystal.transform, profile.EffectColor, profile.ShieldDuration);
            yield break;
        }

        GuardianShieldUntil = Time.time + profile.ShieldDuration;
        DuckWandAttack.CreateGuardianAura(transform, profile.EffectColor, profile.ShieldDuration);
    }

    private void Patrol()
    {
        if (Time.time >= chooseNextPatrolAt
            || FlatDistance(transform.position, patrolTarget) <= StopDistance)
        {
            SelectNextPatrolPoint();
        }

        SetAction(BotAction.Wander);
        MoveTowards(patrolTarget, PatrolSpeed, StopDistance);
    }

    private void MoveTowards(Vector3 destination, float speed, float desiredDistance)
    {
        if (Time.time < detourUntil
            && FlatDistance(transform.position, detourTarget) <= StopDistance + 0.1f)
        {
            // A recovery point was reached. Resume the original objective and
            // let the normal planner take the next meaningful corner.
            detourUntil = 0f;
            recoveryAttempts = 0;
        }

        bool isDetouring = Time.time < detourUntil
            && FlatDistance(transform.position, detourTarget) > StopDistance;
        Vector3 intendedDestination = isDetouring ? detourTarget : destination;
        float destinationArrivalDistance = isDetouring ? StopDistance : desiredDistance;
        // Pickup/attack ranges apply to the objective, not to every A* corner.
        // Using the objective's wider interaction radius for every corner
        // would stop us before that corner's own arrival threshold forever.
        if (FlatDistance(body.position, intendedDestination) <= destinationArrivalDistance)
        {
            PlayMovementAnimation(false);
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
            // 沒有可行地圖路線時絕不能改走直線。目標會被
            // 暫時標記為不可達，下一輪 Utility 評估會挑選其他目標。
            MarkDestinationUnreachable(destination);
            if (currentState == BotAction.Wander) chooseNextPatrolAt = 0f;
            PlayMovementAnimation(false);
            return;
        }

        Vector3 direction = navigationTarget - body.position;
        direction.y = 0f;
        float distance = direction.magnitude;
        if (distance <= 0.001f)
        {
            PlayMovementAnimation(false);
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
        FaceDirection(normalizedDirection);
        PlayMovementAnimation(true);

        // A sweep can occasionally report clear while a kinematic collider is
        // pressed against scenery. Track actual progress as well, then force a
        // side route instead of letting the bot walk in place forever.
        if (Time.time >= nextProgressCheckAt)
        {
            float travelled = FlatDistance(body.position, lastProgressPosition);
            float minimumProgress = Mathf.Min(0.2f, speed * 0.45f * 0.25f);
            stalledRouteCount = travelled < minimumProgress ? stalledRouteCount + 1 : 0;
            if (!isDetouring && travelled >= 0.4f) failedRecoveryCount = 0;
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
            return;
        }

        // A short, static prop is quicker and more natural to clear with one
        // jump than to route around. Walls, large scenery and other ducks are
        // deliberately left to the route recovery logic below.
        if (TryStartObstacleJump(blockingHit, normalizedDirection))
        {
            Unit1RouteScanner.InvalidateRoute(plannedRoute, ref plannedRouteIndex, ref nextRoutePlanAt);
            detourUntil = 0f;
            return;
        }

        blockedMoveCount++;
        if (blockedMoveCount < 2)
        {
            return;
        }

        blockedMoveCount = 0;
        Unit1RouteScanner.InvalidateRoute(plannedRoute, ref plannedRouteIndex, ref nextRoutePlanAt);
        if (BeginRouteRecovery(destination))
        {
            SetAction(BotAction.Detour);
        }
        else
        {
            MarkDestinationUnreachable(destination);
            if (currentState == BotAction.Wander) chooseNextPatrolAt = 0f;
        }
    }

    private bool BeginRouteRecovery(Vector3 destination)
    {
        bool newObjective = recoverySide == 0
            || FlatDistance(destination, recoveryDestination) > 0.8f;
        if (newObjective)
        {
            recoveryDestination = destination;
            recoveryAttempts = 0;
            failedRecoveryCount = 0;
            recoverySide = (BotNumber + patrolStep) % 2 == 0 ? 1 : -1;
        }
        if (++failedRecoveryCount > 4) return false;

        // Keep taking the same side around one obstacle. Only after two
        // failed recovery routes do we try the other side, so the CPU cannot
        // ping-pong every simulation tick.
        if (recoveryAttempts >= 2)
        {
            recoverySide = -recoverySide;
            recoveryAttempts = 0;
        }

        if (!TryChooseRecoveryPoint(destination, recoverySide, out Vector3 recoveryPoint)
            && !TryChooseRecoveryPoint(destination, -recoverySide, out recoveryPoint))
        {
            return false;
        }

        detourTarget = recoveryPoint;
        detourUntil = Time.time + RecoveryDuration;
        recoveryDestination = destination;
        recoveryAttempts++;
        Unit1RouteScanner.InvalidateRoute(plannedRoute, ref plannedRouteIndex, ref nextRoutePlanAt);
        return true;
    }

    private bool TryChooseRecoveryPoint(Vector3 destination, int side, out Vector3 recoveryPoint)
    {
        recoveryPoint = body != null ? body.position : transform.position;
        if (body == null
            || !Unit1RouteScanner.TryFindRecoveryPoint(body.position, destination, side, out recoveryPoint))
        {
            return false;
        }

        Vector3 direction = recoveryPoint - body.position;
        direction.y = 0f;
        float distance = direction.magnitude;
        if (distance < 0.45f)
        {
            return false;
        }

        // Recovery uses exactly the same actor/floor filtering as movement.
        return !DuckKinematicMotor.TryGetBlockingHit(body,
            direction / distance,
            Mathf.Min(distance, 1.05f),
            true, out _);
    }

    private bool TryStartObstacleJump(RaycastHit blockingHit, Vector3 moveDirection)
    {
        Collider obstacle = blockingHit.collider;
        if (body == null
            || bodyCollider == null
            || obstacle == null
            || obstacle.isTrigger
            || obstacle.attachedRigidbody != null
            || !obstacle.gameObject.isStatic
            || !isGrounded
            || Time.time < nextAutoJumpAt)
        {
            return false;
        }

        float obstacleHeight = obstacle.bounds.max.y - bodyCollider.bounds.min.y;
        if (obstacleHeight < 0.06f || obstacleHeight > AutoJumpMaximumObstacleHeight)
        {
            return false;
        }

        Vector3 obstacleSize = obstacle.bounds.size;
        float obstacleSpan = Mathf.Abs(moveDirection.x) * obstacleSize.x
            + Mathf.Abs(moveDirection.z) * obstacleSize.z;
        if (obstacleSpan > AutoJumpMaximumObstacleSpan
            || DuckKinematicMotor.TryGetBlockingHit(body, Vector3.up, AutoJumpClearance, true, out _))
        {
            return false;
        }

        verticalSpeed = AutoJumpSpeed;
        isGrounded = false;
        nextAutoJumpAt = Time.time + AutoJumpCooldown;
        animator?.SetTrigger(JumpProperty);
        SetAction(BotAction.JumpObstacle);
        return true;
    }

    private void AdvanceVerticalMotion()
    {
        if (body == null)
        {
            return;
        }

        bool hasGroundBelow = IsGroundBelowFeet();
        if (hasGroundBelow && verticalSpeed <= 0.05f)
        {
            verticalSpeed = 0f;
            isGrounded = true;
            return;
        }

        isGrounded = false;
        verticalSpeed -= AutoJumpGravity * Runner.DeltaTime;
        bool blockedVertically = DuckKinematicMotor.Move(body, Vector3.up * verticalSpeed * Runner.DeltaTime,
            out _, ignoreDynamicBodies: true);
        if (blockedVertically)
        {
            verticalSpeed = 0f;
        }

        if (verticalSpeed <= 0f && IsGroundBelowFeet())
        {
            verticalSpeed = 0f;
            isGrounded = true;
        }
    }

    private bool IsGroundBelowFeet()
    {
        if (bodyCollider == null || verticalSpeed > 0.05f)
        {
            return false;
        }

        Bounds bounds = bodyCollider.bounds;
        float probeRadius = Mathf.Clamp(Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.7f, 0.08f, 0.38f);
        Vector3 origin = new(bounds.center.x, bounds.min.y + probeRadius + 0.03f, bounds.center.z);
        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            probeRadius,
            Vector3.down,
            groundProbeHits,
            0.2f,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        for (int index = 0; index < hitCount; index++)
        {
            Collider hitCollider = groundProbeHits[index].collider;
            if (hitCollider == null
                || hitCollider == bodyCollider
                || hitCollider.transform.IsChildOf(transform)
                || hitCollider.GetComponentInParent<NetworkObject>() != null)
            {
                continue;
            }

            if (Vector3.Dot(groundProbeHits[index].normal, Vector3.up) > 0.55f)
            {
                return true;
            }
        }

        return false;
    }

    private void MarkDestinationUnreachable(Vector3 destination)
    {
        unreachableGoals.Remember(destination, Time.time);
        Transform target = GetCombatTargetTransform();
        if (HasWand && target != null) unreachableGoals.Remember(target.position, Time.time);
        if (currentState == BotAction.AttackCrystal && targetCrystal != null)
        {
            unreachableGoals.Remember(targetCrystal.transform.position, Time.time);
            targetCrystal = null;
        }
        detourUntil = 0f;
        Unit1RouteScanner.InvalidateRoute(plannedRoute, ref plannedRouteIndex, ref nextRoutePlanAt);
        nextTargetRefreshAt = nextUtilityDecisionAt = 0f;
    }

    private bool IsTemporarilyUnreachable(Vector3 destination)
    {
        return unreachableGoals.IsUnreachable(destination, Time.time);
    }

    /// <summary>
    /// 戰術目標的距離估計。實際移動則一律由碰撞格點路徑決定，
    /// 因此不會為了追求目標而直線穿越建築物。
    /// </summary>
    private float GetNavigationCost(Vector3 destination)
    {
        if (IsTemporarilyUnreachable(destination))
        {
            return 1000f;
        }

        return FlatDistance(transform.position, destination);
    }

    /// <summary>
    /// 目標至少要比原目標好一個門檻才會轉向；
    /// 避免每 0.35 秒因小分差在兩把杖／兩個敵人間來回抖動。
    /// </summary>
    private bool ShouldSwitchTarget(Transform candidate, Transform current)
    {
        if (candidate == null)
        {
            return false;
        }

        if (Time.time < stateEnterTime + minimumStateDuration
            && currentState != BotAction.Retreat
            && currentState != BotAction.DefendCrystal)
        {
            return false;
        }

        if (current == null)
        {
            return true;
        }

        float currentCost = FlatDistance(transform.position, current.position);
        float candidateCost = FlatDistance(transform.position, candidate.position);
        return candidateCost + targetSwitchThreshold < currentCost;
    }

    private void FaceTowards(Vector3 position)
    {
        Vector3 direction = position - body.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f)
        {
            FaceDirection(direction.normalized);
        }
    }

    private void FaceDirection(Vector3 direction)
    {
        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        body.rotation = Quaternion.RotateTowards(body.rotation, targetRotation, TurnSpeed * Runner.DeltaTime);
    }

    private void SetAction(BotAction action)
    {
        if (ActionIndex != (int)action)
        {
            ActionIndex = (int)action;
            currentState = action;
            stateEnterTime = Time.time;
            currentScore = action == plannedAction ? plannedActionScore : GetDebugScore(action);
            LogAiDecisionIfChanged();
        }
    }

    private float GetDebugScore(BotAction action)
    {
        Transform target = GetDebugTarget();
        float pathCost = target != null ? FlatDistance(transform.position, target.position) : 0f;
        return action switch
        {
            BotAction.DefendCrystal => 80f + Mathf.Max(0, CriticalCrystalHealth - (FactionCrystal.FindForFaction(FactionIndex)?.CurrentHealth ?? CriticalCrystalHealth)) * 6f - pathCost,
            BotAction.Retreat => 92f - pathCost,
            BotAction.AttackCrystal => 66f - pathCost,
            BotAction.SeekWand => 54f - pathCost,
            BotAction.SeekShadow or BotAction.AttackEnemy => 48f - pathCost,
            _ => 10f - pathCost
        };
    }

    private Transform GetDebugTarget()
    {
        if (targetWand != null) return targetWand.transform;
        if (targetEnemy != null) return targetEnemy.transform;
        if (targetPlayer != null) return targetPlayer.transform;
        if (targetBot != null) return targetBot.transform;
        if (targetCrystal != null) return targetCrystal.transform;
        return null;
    }

    private void LogAiDecisionIfChanged()
    {
        Transform target = GetDebugTarget();
        if (!debugAI || (currentState == lastDebugState && target == lastDebugTarget))
        {
            return;
        }

        lastDebugTarget = target;
        lastDebugState = currentState;
        string path = target != null ? "Route " + FlatDistance(transform.position, target.position).ToString("0.0") + "m" : "None";
        Debug.Log("[UNIT1 AI] CPU_" + BotNumber
            + " State=" + currentState
            + " Target=" + (target != null ? target.name : "none")
            + " Score=" + currentScore.ToString("0")
            + " HP=" + CurrentHealth + "/" + MaximumHealth
            + " Wand=" + (heldWand != null ? heldWand.AbilityDisplayName : "none")
            + " Path=" + path
            + " minState=" + minimumStateDuration.ToString("0.00")
            + " switch=" + targetSwitchThreshold.ToString("0"), this);
    }

    private void OnDrawGizmosSelected()
    {
        if (!debugAI || plannedRoute == null || plannedRoute.Count == 0)
        {
            return;
        }

        Gizmos.color = Color.cyan;
        Vector3 from = transform.position + Vector3.up * 0.15f;
        for (int index = plannedRouteIndex; index < plannedRoute.Count; index++)
        {
            Vector3 to = plannedRoute[index] + Vector3.up * 0.15f;
            Gizmos.DrawLine(from, to);
            Gizmos.DrawSphere(to, 0.09f);
            from = to;
        }
    }

    private void PlayMovementAnimation(bool isWalking)
    {
        if (animator == null)
        {
            return;
        }

        if (Time.time < wandAttackUntil && animator.HasState(0, WandAttackState))
        {
            return;
        }

        animator.SetFloat(SpeedProperty, isWalking ? 0.75f : 0f);
        animator.SetBool(GroundedProperty, isGrounded);
        if (!isGrounded && animator.HasState(0, JumpState))
        {
            return;
        }

        int state = isWalking ? WalkingState : IdleState;
        if (currentAnimationState == state || !animator.HasState(0, state))
        {
            return;
        }

        currentAnimationState = state;
        animator.CrossFadeInFixedTime(state, 0.08f);
    }

    private float PlayWandAttackAnimation()
    {
        float duration = DuckWandAttack.ResolveWandAttackDuration(animator);
        if (animator == null || !animator.HasState(0, WandAttackState))
        {
            return duration;
        }

        // The clip is a non-looping standing strike. Keep locomotion from
        // immediately replacing it, then movement resumes naturally.
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

    private void EnsurePresentation()
    {
        nameplate ??= GetComponent<DuckPlayerNameplate>();
        if (nameplate == null)
        {
            nameplate = gameObject.AddComponent<DuckPlayerNameplate>();
        }

        if (factionLabel != null)
        {
            return;
        }

        GameObject labelObject = new("CPU Faction Label", typeof(TextMeshPro));
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition = Vector3.up * 3.9f;
        labelObject.transform.localScale = Vector3.one * 0.18f;
        factionLabel = labelObject.GetComponent<TextMeshPro>();
        factionLabel.font = TMP_Settings.defaultFontAsset;
        factionLabel.alignment = TextAlignmentOptions.Center;
        factionLabel.fontSize = 5.2f;
        factionLabel.outlineColor = new Color(0.04f, 0.05f, 0.09f, 1f);
        factionLabel.outlineWidth = 0.22f;
        factionLabel.textWrappingMode = TextWrappingModes.NoWrap;
        factionLabel.rectTransform.sizeDelta = new Vector2(21f, 4.8f);
    }

    private void RefreshPresentation()
    {
        if (BotNumber <= 0
            || (displayedBotNumber == BotNumber
                && displayedFactionIndex == FactionIndex
                && displayedHasWand == HasWand
                && displayedActionIndex == ActionIndex
                && displayedCurrentHealth == CurrentHealth
                && displayedEliminated == IsEliminated
                && displayedShield == HasGuardianShield))
        {
            return;
        }

        displayedBotNumber = BotNumber;
        displayedFactionIndex = FactionIndex;
        displayedHasWand = HasWand;
        displayedActionIndex = ActionIndex;
        displayedCurrentHealth = CurrentHealth;
        displayedEliminated = IsEliminated;
        displayedShield = HasGuardianShield;
        string factionName = FusionDuckPlayer.GetFactionName(FactionIndex);
        nameplate?.SetDisplayName("CPU " + BotNumber);
        if (factionLabel != null)
        {
            factionLabel.text = IsEliminated
                ? "✕ CPU 已陣亡"
                : FactionIndex > 0
                    ? "● " + factionName + "色 · " + GetActionName((BotAction)ActionIndex)
                      + "　♥" + CurrentHealth + (HasGuardianShield ? "　護盾" : string.Empty)
                      + "\n策略：" + GetStrategyName()
                    : "● CPU";
            factionLabel.color = IsEliminated
                ? new Color(0.42f, 0.42f, 0.42f)
                : FactionIndex > 0 ? FusionDuckPlayer.GetFactionColor(FactionIndex) : Color.white;
        }
    }

    private static string GetActionName(BotAction action)
    {
        return action switch
        {
            BotAction.SeekWand => "取杖",
            BotAction.SeekShadow => "尋找灰影／敵人",
            BotAction.AttackEnemy => "施法",
            BotAction.Detour => "重算路線",
            BotAction.JumpObstacle => "跳越障礙",
            BotAction.Retreat => "撤退",
            BotAction.DefendCrystal => "回防水晶",
            BotAction.AttackCrystal => "進攻水晶",
            BotAction.FollowAlly => "跟隨友軍",
            _ => "巡邏搜路"
        };
    }

    private string GetStrategyName()
    {
        if (HasGuardianShield || (heldWand != null && heldWand.EffectProfile.Ability == Unit1WandAbility.Guardian))
        {
            return "守護誘敵・拉開距離";
        }

        if (CurrentHealth <= LowHealthThreshold)
        {
            return "保存最後一心・避戰";
        }

        return (BotNumber % 3) switch
        {
            1 => HasWand ? "突擊分兵・優先壓制" : "突擊位・優先重擊杖",
            2 => HasWand ? "偵察側包・快速施壓" : "偵察位・搶快攻杖",
            _ => HasWand ? "支援補位・協同追擊" : "支援位・彈性取杖"
        };
    }

    private static float FlatDistance(Vector3 left, Vector3 right)
    {
        Vector3 difference = left - right;
        difference.y = 0f;
        return difference.magnitude;
    }
}
