using System;
using System.Collections;
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
    private enum EnemyState
    {
        Patrol,
        SeekWand,
        Chase,
        Cast,
        Stunned,
        Defeated
    }

    [Header("Player target")]
    [SerializeField] private Transform player;
    [SerializeField] private bool onlyChasePlayerWithWand = true;
    [SerializeField, Min(0.5f)] private float detectionRange = 7f;
    [SerializeField, Min(0.25f)] private float stoppingDistance = 1.3f;

    [Header("Wand intelligence")]
    // Grey Shadows stand lower than the floating wand roots, so their
    // horizontal stopping distance must leave room for that vertical offset.
    [SerializeField, Min(0.25f)] private float wandPickupHorizontalDistance = 0.35f;
    [SerializeField, Min(0.5f)] private float wandSearchRange = 18f;
    [SerializeField, Min(0.25f)] private float shadowSpellRange = 4.2f;
    [SerializeField, Min(0f)] private float shadowSpellCooldown = 1.15f;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float patrolSpeed = 1.2f;
    [SerializeField, Min(0f)] private float chaseSpeed = 2f;
    [SerializeField, Min(0f)] private float patrolRadius = 2.2f;
    [SerializeField, Min(0f)] private float turningSpeed = 540f;
    [SerializeField, Min(0f)] private float patrolWaitTime = 0.7f;
    private const float MatchVisualScale = 0.88f;
    private const float PreferredSpellDistanceFactor = 0.72f;

    [Header("First-level health")]
    [SerializeField, Min(1)] private int maximumHealth = 2;
    [SerializeField, Min(0f)] private float hitStunTime = 0.38f;
    [SerializeField, Min(0f)] private float hitKnockback = 3.2f;
    [SerializeField, Min(0f)] private float hitDamageImmunityTime = 0.1f;
    [SerializeField, Min(0f)] private float defeatedDespawnDelay = 1.25f;

    [Header("Black-skin appearance")]
    [SerializeField] private string enemyName = "灰影小鴨";
    [SerializeField] private Color bodyColor = Color.black;
    [SerializeField] private Color markerColor = new Color(1f, 0.2f, 0.15f, 1f);
    [SerializeField] private bool showEnemyMarker = true;
    [SerializeField] private GameObject[] stableDuckVisuals = Array.Empty<GameObject>();

    [Header("Events")]
    [SerializeField] private UnityEvent onDefeated;

    public event Action<EnemyDuckAI> Defeated;

    public int CurrentHealth => currentHealth;
    public bool IsDefeated => state == EnemyState.Defeated;
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
    private TextMeshPro statusLabel;
    private Transform marker;
    private MaterialPropertyBlock materialProperties;
    private Vector3 homePosition;
    private Vector3 patrolTarget;
    private float nextPatrolTime;
    private float stunnedUntil;
    private float hitDamageImmuneUntil;
    private float guardianShieldUntil;
    private float nextPlayerSearchTime;
    private float nextWandSearchTime;
    private float nextShadowSpellTime;
    private Vector3 detourTarget;
    private float detourUntil;
    private int blockedMoveCount;
    private Vector3 lastProgressPosition;
    private float nextProgressCheckAt;
    private int stalledRouteCount;
    private int currentHealth;
    private int currentAnimationState;
    private float wandAttackUntil;
    private EnemyState state;
    private bool hasReportedAuthority;
    private bool reportedStateAuthority;
    private bool reportedBodyKinematic;
    private bool hasReportedFirstMove;
    private bool networkSpawned;
    private bool matchVisualScaleApplied;

    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int SpeedProperty = Animator.StringToHash("Speed");
    private static readonly int GroundedProperty = Animator.StringToHash("Grounded");
    private static readonly int IdleState = Animator.StringToHash("idle");
    private static readonly int WalkingState = Animator.StringToHash("Walking");
    private static readonly int WandAttackState = Animator.StringToHash("stand attack");

    /// <summary>Called by the prefab builder with the known-stable people/1 and people/2 ducks.</summary>
    public void ConfigureStableDuckVisuals(GameObject[] visuals)
    {
        stableDuckVisuals = visuals ?? Array.Empty<GameObject>();
    }

    public override void Spawned()
    {
        networkSpawned = true;
        // Awake runs while Fusion is instantiating the prefab, before the
        // Runner has applied this enemy's requested spawn transform. Capture
        // the patrol origin here so every enemy patrols around its own grey
        // shadow spawn point instead of walking toward world origin (0,0,0).
        homePosition = transform.position;
        patrolTarget = homePosition;
        nextPatrolTime = Time.time;
        lastProgressPosition = body != null ? body.position : transform.position;
        nextProgressCheckAt = Time.time + 0.45f;

        ApplyStableVisualVariant();
        animator = GetComponentInChildren<Animator>();

        // The state owner advances a collision-aware kinematic body in the
        // Fusion tick. Every other client receives NetworkTransform snapshots.
        if (HasStateAuthority)
        {
            ConfigureBody();
            HasWand = false;
        }
        else if (body != null)
        {
            body.isKinematic = true;
            body.useGravity = false;
        }

        ReportRuntimeState("spawned");
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<Collider>();
        animator = GetComponentInChildren<Animator>(true);
        homePosition = transform.position;
        patrolTarget = homePosition;
        currentHealth = Mathf.Max(1, maximumHealth);

        // A DuckMover reads player keyboard input, so an enemy must never leave it enabled.
        DuckMover mover = GetComponent<DuckMover>();
        if (mover != null)
        {
            mover.enabled = false;
        }

        ConfigureBody();
        ApplyGreyShadowAppearance();
        ApplyMatchVisualScale();
        CreateStatusVisuals();
        // Networked fields are not valid until Fusion has called Spawned().
        // The old call here raised an exception for every Grey Shadow as it
        // was created, before its behaviour tree could start.
        if (networkSpawned)
        {
            UpdateStatusVisuals();
        }
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
        if (currentHealth <= 0)
        {
            currentHealth = Mathf.Max(1, maximumHealth);
        }

        if (state == EnemyState.Defeated)
        {
            state = EnemyState.Patrol;
        }
    }

    public override void FixedUpdateNetwork()
    {
        ReportRuntimeState("network tick");
        if (!HasStateAuthority)
        {
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
        FindPlayerIfNeeded();
        if (!HasWand)
        {
            SeekWandOrPatrol();
            return;
        }

        UseWandAgainstTarget();
    }

    private void LateUpdate()
    {
        UpdateStatusVisuals();
    }

    /// <summary>Called by the player's wand attack. Returns false while stunned or defeated.</summary>
    public bool TakeWandHit(
        Vector3 hitSource,
        int damage = 1,
        float stunDuration = 0f,
        float knockbackMultiplier = 1f)
    {
        if (Object != null && !HasStateAuthority)
        {
            RPC_RequestWandHit(hitSource, damage, stunDuration, knockbackMultiplier);
            return true;
        }

        return ApplyWandHit(hitSource, damage, stunDuration, knockbackMultiplier);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestWandHit(Vector3 hitSource, int damage, float stunDuration, float knockbackMultiplier)
    {
        ApplyWandHit(hitSource, damage, stunDuration, knockbackMultiplier);
    }

    private bool ApplyWandHit(Vector3 hitSource, int damage, float stunDuration, float knockbackMultiplier)
    {
        if (IsDefeated || HasGuardianShield || Time.time < hitDamageImmuneUntil || damage <= 0)
        {
            return false;
        }

        currentHealth = Mathf.Max(0, currentHealth - damage);
        hitDamageImmuneUntil = Time.time + hitDamageImmunityTime;
        stunnedUntil = Time.time + Mathf.Max(hitStunTime, stunDuration);
        ApplyKnockback(hitSource, knockbackMultiplier);
        StartCoroutine(FlashMarker());

        if (currentHealth == 0)
        {
            Defeat();
        }

        return true;
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

        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.linearVelocity = Vector3.zero;
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
                || (requireTargetWand && !candidate.HasWeapon))
            {
                continue;
            }

            float distance = FlatDistance(transform.position, candidate.transform.position);
            if (distance > detectionRange)
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
                || (requireTargetWand && !candidate.HasWand))
            {
                continue;
            }

            float distance = FlatDistance(transform.position, candidate.transform.position);
            if (distance > detectionRange)
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

        bool targetHasWand = (playerMover != null && playerMover.HasWeapon)
            || (cpuPlayer != null && cpuPlayer.HasWand);
        if (!HasWand && onlyChasePlayerWithWand && !targetHasWand)
        {
            return false;
        }

        return FlatDistance(transform.position, player.position) <= detectionRange;
    }

    private void SeekWandOrPatrol()
    {
        if (targetWand == null || !targetWand.IsAvailable || Time.time >= nextWandSearchTime)
        {
            nextWandSearchTime = Time.time + 0.55f;
            targetWand = FindBestAvailableWand();
        }

        if (targetWand != null)
        {
            float distance = FlatDistance(transform.position, targetWand.transform.position);
            if (distance <= wandPickupHorizontalDistance)
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
            MoveTowards(targetWand.transform.position, chaseSpeed, wandPickupHorizontalDistance);
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

    private IEnumerator ResolveShadowCastAfterWindup(
        Transform target,
        FusionDuckPlayer targetPlayer,
        Unit1BotDuck targetCpu,
        Unit1WandEffectProfile profile,
        float windup)
    {
        yield return new WaitForSeconds(windup);
        if (!HasStateAuthority
            || IsDefeated
            || !HasWand
            || target == null
            || (targetPlayer != null && targetPlayer.IsEliminated)
            || (targetCpu != null && targetCpu.IsEliminated))
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
        yield return new WaitForSeconds(windup);
        if (!HasStateAuthority || IsDefeated || !HasWand)
        {
            yield break;
        }

        guardianShieldUntil = Time.time + profile.ShieldDuration;
        DuckWandAttack.CreateGuardianAura(transform, profile.EffectColor, profile.ShieldDuration);
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

            float distance = FlatDistance(transform.position, candidate.transform.position);
            if (distance > wandSearchRange)
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

    private float ScoreTarget(Transform candidate, bool candidateHasWand, int health, bool shielded)
    {
        float score = FlatDistance(transform.position, candidate.position)
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

        if (FlatDistance(transform.position, patrolTarget) <= 0.2f)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * patrolRadius;
            patrolTarget = homePosition + new Vector3(offset.x, 0f, offset.y);
            nextPatrolTime = Time.time + patrolWaitTime;
            StopMoving();
            return;
        }

        MoveTowards(patrolTarget, patrolSpeed, 0.1f);
    }

    private void MoveTowards(Vector3 destination, float speed, float desiredDistance)
    {
        bool detouring = Time.time < detourUntil
            && FlatDistance(transform.position, detourTarget) > 0.25f;
        Vector3 navigationTarget = detouring ? detourTarget : destination;
        Vector3 direction = Flatten(navigationTarget - transform.position);
        float distance = direction.magnitude;
        if (distance <= desiredDistance)
        {
            StopMoving();
            return;
        }

        Vector3 normalizedDirection = direction / distance;
        bool blocked = DuckKinematicMotor.Move(body, normalizedDirection * speed * Runner.DeltaTime);

        Quaternion desiredRotation = Quaternion.LookRotation(normalizedDirection, Vector3.up);
        body.rotation = Quaternion.RotateTowards(body.rotation, desiredRotation, turningSpeed * Runner.DeltaTime);
        PlayMovementAnimation(true);

        // Detect the case where a kinematic collider is pressed into scenery:
        // SweepTest can then appear clear while position never changes. Treat
        // two stalled samples as a blocked path and select a side route.
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
        }
        else
        {
            blockedMoveCount++;
            if (blockedMoveCount >= 2)
            {
                blockedMoveCount = 0;
                float side = ((Object != null ? Object.Id.Raw : 0u) & 1u) == 0u ? 1f : -1f;
                Vector3 sidestep = Vector3.Cross(Vector3.up, normalizedDirection) * side;
                detourTarget = body.position + sidestep * 2f + normalizedDirection * 0.45f;
                detourUntil = Time.time + 1.1f;
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
        if (body != null)
        {
            Vector3 velocity = body.linearVelocity;
            velocity.x = 0f;
            velocity.z = 0f;
            body.linearVelocity = velocity;
        }

        PlayMovementAnimation(false);
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
        animator.SetBool(GroundedProperty, true);

        int desiredState = isMoving ? WalkingState : IdleState;
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

    private void ApplyGreyShadowAppearance()
    {
        // This duck mesh reserves material slot 0 for its skin.  Do not
        // tint every renderer: doing so would also recolour the regular bill,
        // eyes, and the violet enemy markers.
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
        statusLabel.rectTransform.sizeDelta = new Vector2(18f, 5f);

        if (!showEnemyMarker)
        {
            return;
        }

        GameObject markerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        markerObject.name = "Enemy Alert Marker";
        markerObject.transform.SetParent(transform, false);
        markerObject.transform.localPosition = Vector3.up * (GetHeadHeight() + 0.34f);
        markerObject.transform.localScale = Vector3.one * 0.13f;
        Collider markerCollider = markerObject.GetComponent<Collider>();
        if (markerCollider != null)
        {
            markerCollider.enabled = false;
        }

        Renderer markerRenderer = markerObject.GetComponent<Renderer>();
        if (markerRenderer != null)
        {
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            material.name = "Enemy Marker Runtime Material";
            material.hideFlags = HideFlags.DontSave;
            if (material.HasProperty(BaseColorProperty))
            {
                material.SetColor(BaseColorProperty, markerColor);
            }
            if (material.HasProperty(ColorProperty))
            {
                material.SetColor(ColorProperty, markerColor);
            }
            markerRenderer.sharedMaterial = material;
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

        Camera camera = Camera.main;
        if (camera != null)
        {
            Vector3 direction = statusLabel.transform.position - camera.transform.position;
            if (direction.sqrMagnitude > 0.001f)
            {
                statusLabel.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
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

        string action = state switch
        {
            EnemyState.SeekWand => "找手杖",
            EnemyState.Chase => "追擊",
            EnemyState.Cast => "影法術",
            EnemyState.Stunned => "暈眩",
            _ => "巡邏"
        };
        statusLabel.text = enemyName + "  " + new string('♥', Mathf.Max(0, currentHealth))
            + (HasGuardianShield ? "　護盾" : string.Empty)
            + (HasWand ? "　影杖・" + action : "　" + action);
        if (marker != null)
        {
            marker.gameObject.SetActive(true);
            float pulse = 1f + Mathf.Sin(Time.time * 5f) * 0.12f;
            marker.localScale = Vector3.one * (0.13f * pulse);
        }
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

    private static float FlatDistance(Vector3 first, Vector3 second)
    {
        return Flatten(first - second).magnitude;
    }
}
