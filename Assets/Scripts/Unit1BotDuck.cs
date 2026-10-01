using System.Collections;
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
    private enum BotAction
    {
        Patrol,
        SeekWand,
        ChaseEnemy,
        AttackEnemy,
        Detour,
        Retreat
    }

    private const float PatrolRadius = 4.2f;
    private const float PatrolSpeed = 2.2f;
    private const float ChaseSpeed = 3.45f;
    private const float StopDistance = 0.32f;
    // Wand roots float above the pavement, so this horizontal distance leaves
    // enough of the 1.5 m 3D pickup radius for their vertical offset.
    private const float WandPickupDistance = 0.78f;
    private const float AttackDistance = 1.45f;
    private const float AttackCooldown = 0.62f;
    private const float GuardianThreatDistance = 4.4f;
    private const float GuardianRetreatDistance = 5.4f;
    private const float SurvivalDistance = 4.7f;
    private const float CombatOrbitDistance = 1.14f;
    private const float MatchVisualScale = 0.88f;
    private const int LowHealthThreshold = 1;
    private const float TurnSpeed = 540f;
    private const float TargetRefreshInterval = 0.35f;
    private const float DamageImmunityDuration = 0.8f;
    public const int MaximumHealth = 3;

    private static readonly int SpeedProperty = Animator.StringToHash("Speed");
    private static readonly int GroundedProperty = Animator.StringToHash("Grounded");
    private static readonly int IdleState = Animator.StringToHash("idle");
    private static readonly int WalkingState = Animator.StringToHash("Walking");
    private static readonly int WandAttackState = Animator.StringToHash("stand attack");

    [Networked] public int BotNumber { get; private set; }
    [Networked] public int FactionIndex { get; private set; }
    [Networked] public NetworkBool HasWand { get; private set; }
    [Networked] public int CurrentHealth { get; private set; }
    [Networked] public NetworkBool IsEliminated { get; private set; }
    [Networked] public float GuardianShieldUntil { get; private set; }
    [Networked] private int ActionIndex { get; set; }

    private Rigidbody body;
    private Animator animator;
    private DuckPlayerNameplate nameplate;
    private TextMeshPro factionLabel;
    private Unit1WandPickup heldWand;
    private Unit1WandPickup targetWand;
    private EnemyDuckAI targetEnemy;
    private Vector3 patrolOrigin;
    private Vector3 patrolTarget;
    private Vector3 detourTarget;
    private int patrolStep;
    private int blockedMoveCount;
    private int currentAnimationState;
    private float wandAttackUntil;
    private float chooseNextPatrolAt;
    private float nextTargetRefreshAt;
    private float nextAttackAt;
    private float detourUntil;
    private float nextProgressCheckAt;
    private Vector3 lastProgressPosition;
    private int stalledRouteCount;
    private int displayedBotNumber = -1;
    private int displayedFactionIndex = -1;
    private bool displayedHasWand;
    private int displayedActionIndex = -1;
    private int displayedCurrentHealth = -1;
    private bool displayedEliminated;
    private bool displayedShield;
    private float damageImmuneUntil;
    private bool matchVisualScaleApplied;

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
            FactionIndex = Mathf.Clamp(factionIndex, 1, 6);
        }
    }

    public override void Spawned()
    {
        body = GetComponent<Rigidbody>();
        animator = GetComponentInChildren<Animator>(true);
        ApplyMatchVisualScale();
        patrolOrigin = transform.position;
        patrolTarget = patrolOrigin;
        lastProgressPosition = patrolOrigin;
        nextProgressCheckAt = Time.time + 0.5f;
        if (HasStateAuthority)
        {
            CurrentHealth = MaximumHealth;
            IsEliminated = false;
            HasWand = false;
            GuardianShieldUntil = 0f;
        }
        ConfigureBody();
        EnsurePresentation();
        RefreshPresentation();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || body == null)
        {
            return;
        }

        if (IsEliminated)
        {
            PlayMovementAnimation(false);
            return;
        }

        if (Time.time < wandAttackUntil)
        {
            PlayMovementAnimation(false);
            return;
        }

        RefreshHeldWand();
        if (!HasWand)
        {
            RunWandSearch();
            return;
        }

        // A one-heart CPU does not blindly trade its last life. Offensive
        // wand users kite a dangerous Shadow until it is safe to finish;
        // Guardian users still enter their shield behaviour below.
        bool hasGuardianWand = heldWand != null && heldWand.EffectProfile.Ability == Unit1WandAbility.Guardian;
        if (CurrentHealth <= LowHealthThreshold && !HasGuardianShield && !hasGuardianWand)
        {
            RunSurvivalStrategy();
            return;
        }

        RunEnemyCombat();
    }

    public override void Render()
    {
        EnsurePresentation();
        RefreshPresentation();
    }

    public void TakeDamage(int damage, Vector3 hitSource)
    {
        if (!HasStateAuthority || IsEliminated || damage <= 0 || HasGuardianShield || Time.time < damageImmuneUntil)
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
        HasWand = false;
        heldWand?.ReleaseIfHeldBy(Object);
        heldWand = null;
        targetWand = null;
        targetEnemy = null;
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

    private void RunWandSearch()
    {
        if (targetWand == null || !targetWand.IsAvailable || Time.time >= nextTargetRefreshAt)
        {
            nextTargetRefreshAt = Time.time + TargetRefreshInterval;
            targetWand = FindBestAvailableWand();
        }

        if (targetWand == null)
        {
            Patrol();
            return;
        }

        SetAction(BotAction.SeekWand);
        float distance = FlatDistance(transform.position, targetWand.transform.position);
        if (distance <= WandPickupDistance)
        {
            FaceTowards(targetWand.transform.position);
            if (targetWand.TryPickupByNetworkObject(Object))
            {
                heldWand = targetWand;
                targetWand = null;
                HasWand = true;
                nextTargetRefreshAt = 0f;
                SetAction(BotAction.ChaseEnemy);
                return;
            }

            // Another duck won this wand during the same network tick.
            targetWand = null;
            return;
        }

        MoveTowards(targetWand.transform.position, ChaseSpeed, WandPickupDistance);
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
        targetEnemy = null;
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
            if (candidate == null || !candidate.IsAvailable)
            {
                continue;
            }

            Unit1WandEffectProfile profile = candidate.EffectProfile;
            float score = FlatDistance(transform.position, candidate.transform.position);
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
        float angle = Mathf.Repeat(BotNumber * 79.7f + patrolStep * 137.5f, 360f) * Mathf.Deg2Rad;
        float radius = 1.4f + Mathf.Repeat(BotNumber * 0.73f + patrolStep * 0.91f, 1f) * (PatrolRadius - 1.4f);
        patrolTarget = patrolOrigin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
        chooseNextPatrolAt = Time.time + 2.2f + (BotNumber % 3) * 0.45f;
    }

    private void RunEnemyCombat()
    {
        Unit1WandEffectProfile profile = heldWand != null
            ? heldWand.EffectProfile
            : Unit1WandPickup.GetEffectProfile(Unit1WandAbility.Swift);
        if (profile.Ability == Unit1WandAbility.Guardian)
        {
            RunGuardianStrategy(profile);
            return;
        }

        if (targetEnemy == null || targetEnemy.IsDefeated || Time.time >= nextTargetRefreshAt)
        {
            nextTargetRefreshAt = Time.time + TargetRefreshInterval;
            targetEnemy = FindBestEnemy();
        }

        if (targetEnemy == null)
        {
            Patrol();
            return;
        }

        float distance = FlatDistance(transform.position, targetEnemy.transform.position);
        if (distance > AttackDistance)
        {
            SetAction(BotAction.ChaseEnemy);
            MoveToCombatPosition(targetEnemy);
            return;
        }

        FaceTowards(targetEnemy.transform.position);
        PlayMovementAnimation(false);
        SetAction(BotAction.AttackEnemy);
        if (Time.time < nextAttackAt)
        {
            return;
        }

        float windup = PlayWandAttackAnimation();
        nextAttackAt = Time.time + windup
            + Mathf.Max(heldWand != null ? profile.AttackCooldown : AttackCooldown, 0.08f)
            + (BotNumber % 2) * 0.08f;
        StartCoroutine(ResolveStrikeAfterWindup(targetEnemy, profile, windup));
    }

    /// <summary>
    /// A Guardian Wand is a survival role, not an idle attacker. The CPU lets
    /// nearby wand-carrying Grey Shadows chase it, shields, then kites away so
    /// the offensive ducks have time to finish their own targets.
    /// </summary>
    private void RunGuardianStrategy(Unit1WandEffectProfile profile)
    {
        if (targetEnemy == null || targetEnemy.IsDefeated || Time.time >= nextTargetRefreshAt)
        {
            nextTargetRefreshAt = Time.time + TargetRefreshInterval;
            targetEnemy = FindBestEnemy();
        }

        if (targetEnemy == null)
        {
            Patrol();
            return;
        }

        float distance = FlatDistance(transform.position, targetEnemy.transform.position);
        if (distance > GuardianThreatDistance)
        {
            Patrol();
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

        Vector3 away = transform.position - targetEnemy.transform.position;
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
        if (targetEnemy == null || targetEnemy.IsDefeated || Time.time >= nextTargetRefreshAt)
        {
            nextTargetRefreshAt = Time.time + TargetRefreshInterval;
            targetEnemy = FindBestEnemy();
        }

        if (targetEnemy == null)
        {
            Patrol();
            return;
        }

        float distance = FlatDistance(transform.position, targetEnemy.transform.position);
        // A stunned, unarmed, one-hit Shadow is a calculated finish instead
        // of a retreat. Every other situation preserves this CPU's last life.
        if (targetEnemy.CurrentHealth <= 1 && !targetEnemy.HasWand && distance <= AttackDistance)
        {
            RunEnemyCombat();
            return;
        }

        if (distance >= SurvivalDistance)
        {
            SetAction(BotAction.Retreat);
            PlayMovementAnimation(false);
            return;
        }

        Vector3 away = transform.position - targetEnemy.transform.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.001f)
        {
            float angle = (BotNumber * 67f) * Mathf.Deg2Rad;
            away = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        SetAction(BotAction.Retreat);
        MoveTowards(transform.position + away.normalized * SurvivalDistance, ChaseSpeed, 0.18f);
    }

    private EnemyDuckAI FindBestEnemy()
    {
        EnemyDuckAI[] enemies = FindObjectsByType<EnemyDuckAI>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        Unit1BotDuck[] bots = FindObjectsByType<Unit1BotDuck>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        EnemyDuckAI best = null;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyDuckAI candidate = enemies[i];
            if (candidate == null || candidate.IsDefeated)
            {
                continue;
            }

            int committedBots = 0;
            for (int botIndex = 0; botIndex < bots.Length; botIndex++)
            {
                if (bots[botIndex] != null && bots[botIndex] != this && bots[botIndex].targetEnemy == candidate)
                {
                    committedBots++;
                }
            }

            // Stop an armed Grey Shadow before it can damage a player. Then
            // distribute the remaining CPU ducks across different shadows
            // instead of stacking all of them on the nearest one.
            float score = FlatDistance(transform.position, candidate.transform.position)
                + committedBots * 5.5f
                + candidate.CurrentHealth * 1.35f
                + (candidate.HasWand ? -14f : 0f)
                + (candidate.HasGuardianShield ? 8.5f : 0f);
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private void MoveToCombatPosition(EnemyDuckAI enemy)
    {
        Vector3 radial = transform.position - enemy.transform.position;
        radial.y = 0f;
        if (radial.sqrMagnitude < 0.001f)
        {
            float angle = (BotNumber * 79f) * Mathf.Deg2Rad;
            radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        radial.Normalize();
        float side = BotNumber % 2 == 0 ? 1f : -1f;
        Vector3 tangent = Vector3.Cross(Vector3.up, radial) * side;
        Vector3 position = enemy.transform.position + radial * CombatOrbitDistance + tangent * 0.3f;
        MoveTowards(position, ChaseSpeed, 0.16f);
    }

    private IEnumerator ResolveStrikeAfterWindup(EnemyDuckAI target, Unit1WandEffectProfile profile, float windup)
    {
        yield return new WaitForSeconds(windup);
        if (!HasStateAuthority
            || IsEliminated
            || !HasWand
            || target == null
            || target.IsDefeated
            || FlatDistance(transform.position, target.transform.position) > AttackDistance * 1.2f)
        {
            yield break;
        }

        Vector3 strikeOrigin = transform.position + Vector3.up * 1.15f;
        Vector3 strikeEnd = target.transform.position + Vector3.up * 1.1f;
        if (target.TakeWandHit(
                transform.position,
                profile.Damage,
                profile.StunDuration,
                profile.KnockbackMultiplier))
        {
            DuckWandAttack.CreateWandCast(strikeOrigin, strikeEnd, profile);
        }
    }

    private IEnumerator ResolveGuardianAfterWindup(Unit1WandEffectProfile profile, float windup)
    {
        yield return new WaitForSeconds(windup);
        if (!HasStateAuthority || IsEliminated || !HasWand)
        {
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

        SetAction(BotAction.Patrol);
        MoveTowards(patrolTarget, PatrolSpeed, StopDistance);
    }

    private void MoveTowards(Vector3 destination, float speed, float desiredDistance)
    {
        bool isDetouring = Time.time < detourUntil
            && FlatDistance(transform.position, detourTarget) > StopDistance;
        Vector3 navigationTarget = isDetouring ? detourTarget : destination;
        Vector3 direction = navigationTarget - body.position;
        direction.y = 0f;
        float distance = direction.magnitude;
        if (distance <= desiredDistance)
        {
            PlayMovementAnimation(false);
            return;
        }

        Vector3 normalizedDirection = direction / distance;
        bool blocked = DuckKinematicMotor.Move(body, normalizedDirection * speed * Runner.DeltaTime);
        FaceDirection(normalizedDirection);
        PlayMovementAnimation(true);

        // A sweep can occasionally report clear while a kinematic collider is
        // pressed against scenery. Track actual progress as well, then force a
        // side route instead of letting the bot walk in place forever.
        if (Time.time >= nextProgressCheckAt)
        {
            float travelled = FlatDistance(body.position, lastProgressPosition);
            stalledRouteCount = travelled < 0.045f ? stalledRouteCount + 1 : 0;
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

        blockedMoveCount++;
        if (blockedMoveCount < 2)
        {
            return;
        }

        blockedMoveCount = 0;
        float side = (BotNumber + patrolStep) % 2 == 0 ? 1f : -1f;
        Vector3 sidestep = Vector3.Cross(Vector3.up, normalizedDirection) * side;
        detourTarget = body.position + sidestep * 2.35f + normalizedDirection * 0.5f;
        detourUntil = Time.time + 1.15f;
        SetAction(BotAction.Detour);
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
        animator.SetBool(GroundedProperty, true);
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
        factionLabel.rectTransform.sizeDelta = new Vector2(18f, 2.5f);
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
            BotAction.SeekWand => "找手杖",
            BotAction.ChaseEnemy => "追灰影",
            BotAction.AttackEnemy => "施法中",
            BotAction.Detour => "繞路",
            BotAction.Retreat => "護盾撤退",
            _ => "巡邏"
        };
    }

    private static float FlatDistance(Vector3 left, Vector3 right)
    {
        Vector3 difference = left - right;
        difference.y = 0f;
        return difference.magnitude;
    }
}
