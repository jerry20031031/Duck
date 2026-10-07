using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A deliberately small first-level attack. After picking up a wand, left click
/// damages the closest grey-shadow duck directly in front of the player.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DuckMover))]
public sealed class DuckWandAttack : MonoBehaviour
{
    public const float MinimumWandAttackWindup = 0.9f;
    private const float PostAttackRecovery = 0.08f;
    [SerializeField, Min(0.1f)] private float attackReach = 1.55f;
    [SerializeField, Min(0.1f)] private float attackRadius = 0.8f;
    [SerializeField, Min(0f)] private float attackCooldown = 0.45f;
    [SerializeField, Range(-1f, 1f)] private float minimumForwardDot = 0.1f;
    [SerializeField] private Color strikeColor = new Color(0.35f, 0.92f, 1f, 1f);

    private readonly Collider[] hitBuffer = new Collider[12];
    private DuckMover mover;
    private FusionDuckPlayer networkPlayer;
    private float nextAttackTime;
    private Coroutine pendingAttack;

    private void Awake()
    {
        mover = GetComponent<DuckMover>();
        networkPlayer = GetComponent<FusionDuckPlayer>();
    }

    private void Update()
    {
        // Every player's prefab exists on every client. Only its Fusion state
        // owner may turn local mouse input into a networked hit request.
        if (!CanAttack())
        {
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame || mover == null || !mover.HasWeapon || Time.time < nextAttackTime)
        {
            return;
        }

        Unit1WandEffectProfile profile = mover.HeldNetworkWand != null
            ? mover.HeldNetworkWand.EffectProfile
            : Unit1WandPickup.GetEffectProfile(Unit1WandAbility.Swift);
        float windup = mover.PlayWandAttackAnimation();
        if (windup <= 0f)
        {
            windup = ResolveWandAttackDuration(GetComponentInChildren<Animator>(true));
        }

        // Input starts the swing, not the hit. The cooldown begins after the
        // animation impact, so rapid clicking cannot skip the visible cast.
        float recovery = mover.HeldNetworkWand != null ? profile.AttackCooldown : attackCooldown;
        nextAttackTime = Time.time + windup + Mathf.Max(PostAttackRecovery, recovery);
        pendingAttack = StartCoroutine(ResolveAttackAfterWindup(profile, windup));
    }

    private IEnumerator ResolveAttackAfterWindup(Unit1WandEffectProfile profile, float windup)
    {
        yield return new WaitForSeconds(windup);
        pendingAttack = null;
        if (!CanAttack() || mover == null || !mover.HasWeapon)
        {
            yield break;
        }

        if (profile.Ability == Unit1WandAbility.Guardian)
        {
            ActivateGuardianShield(profile);
            yield break;
        }

        Strike(profile);
    }

    private bool CanAttack() => !SceneSettingsOverlay.IsOpen
        && !Unit1GameDirector.IsMatchFinished()
        && (networkPlayer == null || (networkPlayer.Object != null && networkPlayer.Object.IsValid
            && networkPlayer.HasStateAuthority && !networkPlayer.IsEliminated));

    private void OnDisable()
    {
        if (pendingAttack != null) StopCoroutine(pendingAttack);
        pendingAttack = null;
    }

    private void ActivateGuardianShield(Unit1WandEffectProfile profile)
    {
        FusionDuckPlayer player = networkPlayer;
        if (player != null
            && FactionCrystal.TryProtectNearestFriendlyCrystal(
                transform.position,
                player.FactionIndex,
                profile.ShieldDuration,
                1,
                out FactionCrystal protectedCrystal))
        {
            CreateGuardianAura(protectedCrystal.transform, profile.EffectColor, profile.ShieldDuration);
            return;
        }

        if (player == null || !player.ActivateGuardianShield(profile.ShieldDuration))
        {
            return;
        }

        CreateGuardianAura(transform, profile.EffectColor, profile.ShieldDuration);
    }

    private void Strike(Unit1WandEffectProfile profile)
    {
        Vector3 origin = transform.position + Vector3.up * 0.9f;
        Vector3 center = origin + transform.forward * attackReach;
        // Wands and faction crystals are non-blocking interaction targets.
        // Include their trigger colliders here while still filtering every
        // result below to a valid enemy or enemy-faction crystal.
        int hitCount = Physics.OverlapSphereNonAlloc(center, attackRadius, hitBuffer, ~0, QueryTriggerInteraction.Collide);

        FusionDuckPlayer attacker = networkPlayer;
        int attackerFaction = attacker != null ? attacker.FactionIndex : FactionTeam.Neutral;
        EnemyDuckAI closestEnemy = null;
        FusionDuckPlayer closestPlayer = null;
        Unit1BotDuck closestCpu = null;
        FactionCrystal closestCrystal = null;
        float closestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hitBuffer[i];
            if (hit == null)
            {
                continue;
            }

            Transform targetTransform = null;
            EnemyDuckAI enemy = hit.GetComponentInParent<EnemyDuckAI>();
            FusionDuckPlayer player = enemy == null ? hit.GetComponentInParent<FusionDuckPlayer>() : null;
            Unit1BotDuck cpu = enemy == null && player == null ? hit.GetComponentInParent<Unit1BotDuck>() : null;
            FactionCrystal crystal = enemy == null && player == null && cpu == null
                ? hit.GetComponentInParent<FactionCrystal>()
                : null;
            if (enemy != null && enemy.CanBeTargetedByFaction(attackerFaction))
            {
                targetTransform = enemy.transform;
            }
            else if (player != null
                     && player != attacker
                     && !player.IsEliminated
                     && FactionTeam.AreEnemies(attackerFaction, player.FactionIndex))
            {
                targetTransform = player.transform;
            }
            else if (cpu != null
                     && !cpu.IsEliminated
                     && FactionTeam.AreEnemies(attackerFaction, cpu.FactionIndex))
            {
                targetTransform = cpu.transform;
            }
            else if (crystal != null && crystal.CanBeDamagedBy(attackerFaction))
            {
                targetTransform = crystal.transform;
            }

            if (targetTransform == null)
            {
                continue;
            }

            Vector3 toTarget = targetTransform.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.001f || Vector3.Dot(transform.forward, toTarget.normalized) < minimumForwardDot)
            {
                continue;
            }

            float distance = toTarget.sqrMagnitude;
            if (distance < closestDistance)
            {
                closestEnemy = enemy;
                closestPlayer = player;
                closestCpu = cpu;
                closestCrystal = crystal;
                closestDistance = distance;
            }
        }

        Vector3 visualEnd = center;
        if (closestEnemy != null)
        {
            closestEnemy.TakeWandHit(
                attacker != null ? attacker.Object : null,
                transform.position,
                profile.Damage,
                profile.StunDuration,
                profile.KnockbackMultiplier);
            visualEnd = closestEnemy.transform.position + Vector3.up * 1.15f;
        }
        else if (closestPlayer != null)
        {
            closestPlayer.TakeDamage(profile.Damage, transform.position);
            visualEnd = closestPlayer.transform.position + Vector3.up * 1.15f;
        }
        else if (closestCpu != null)
        {
            closestCpu.TakeDamage(profile.Damage, transform.position);
            visualEnd = closestCpu.transform.position + Vector3.up * 1.15f;
        }
        else if (closestCrystal != null)
        {
            closestCrystal.TakeDamage(attacker != null ? attacker.Object : null, profile.Damage);
            visualEnd = closestCrystal.transform.position + Vector3.up * 1.25f;
        }

        if (mover.HeldNetworkWand != null)
        {
            CreateWandCast(origin, visualEnd, profile);
        }
        else
        {
            CreateStrikeFlash(origin, visualEnd, strikeColor);
        }
    }

    /// <summary>Shared spell cast for real players, CPU ducks, and Grey Shadows.</summary>
    public static void CreateWandCast(Vector3 origin, Vector3 end, Unit1WandEffectProfile profile)
    {
        Unit1WandVisualEffects.PlayCast(origin, end, profile);
    }

    /// <summary>Legacy/default quick spell entry point used by older scene content.</summary>
    public static void CreateStrikeFlash(Vector3 origin, Vector3 end, Color color)
    {
        Unit1WandEffectProfile fallback = Unit1WandPickup.GetEffectProfile(Unit1WandAbility.Swift);
        Unit1WandVisualEffects.PlayCast(
            origin,
            end,
            new Unit1WandEffectProfile(
                Unit1WandAbility.Swift,
                fallback.DisplayName,
                fallback.Description,
                fallback.AttackCooldown,
                fallback.Damage,
                fallback.StunDuration,
                fallback.KnockbackMultiplier,
                fallback.ShieldDuration,
                color));
    }

    /// <summary>Creates the visible, persistent Guardian Wand shield.</summary>
    public static void CreateGuardianAura(Transform owner, Color color, float duration)
    {
        Unit1WandVisualEffects.CreateGuardianAura(owner, color, duration);
    }

    /// <summary>
    /// Returns the actual attack-clip length when the active controller
    /// exposes it. Every duck falls back to the same readable windup so CPU,
    /// Grey Shadow, and player damage all land after the swing.
    /// </summary>
    public static float ResolveWandAttackDuration(Animator animator)
    {
        float duration = MinimumWandAttackWindup;
        RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
        if (controller == null)
        {
            return duration;
        }

        AnimationClip[] clips = controller.animationClips;
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip != null && clip.name.IndexOf("attack", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                duration = Mathf.Max(duration, clip.length);
            }
        }

        return duration;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(strikeColor.r, strikeColor.g, strikeColor.b, 0.28f);
        Vector3 origin = transform.position + Vector3.up * 0.9f;
        Gizmos.DrawWireSphere(origin + transform.forward * attackReach, attackRadius);
    }
}
