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
    private float nextAttackTime;
    private Coroutine pendingAttack;

    private void Awake()
    {
        mover = GetComponent<DuckMover>();
    }

    private void Update()
    {
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
        if (mover == null || !mover.HasWeapon)
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

    private void ActivateGuardianShield(Unit1WandEffectProfile profile)
    {
        FusionDuckPlayer player = GetComponent<FusionDuckPlayer>();
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
        int hitCount = Physics.OverlapSphereNonAlloc(center, attackRadius, hitBuffer, ~0, QueryTriggerInteraction.Ignore);

        EnemyDuckAI closestEnemy = null;
        float closestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hitBuffer[i];
            if (hit == null)
            {
                continue;
            }

            EnemyDuckAI enemy = hit.GetComponentInParent<EnemyDuckAI>();
            if (enemy == null || enemy.IsDefeated)
            {
                continue;
            }

            Vector3 toEnemy = enemy.transform.position - transform.position;
            toEnemy.y = 0f;
            if (toEnemy.sqrMagnitude < 0.001f || Vector3.Dot(transform.forward, toEnemy.normalized) < minimumForwardDot)
            {
                continue;
            }

            float distance = toEnemy.sqrMagnitude;
            if (distance < closestDistance)
            {
                closestEnemy = enemy;
                closestDistance = distance;
            }
        }

        Vector3 visualEnd = center;
        if (closestEnemy != null)
        {
            closestEnemy.TakeWandHit(
                transform.position,
                profile.Damage,
                profile.StunDuration,
                profile.KnockbackMultiplier);
            visualEnd = closestEnemy.transform.position + Vector3.up * 1.15f;
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
