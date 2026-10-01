using Fusion;
using UnityEngine;

/// <summary>The gameplay role of a first-level wand, independent of its model.</summary>
public enum Unit1WandAbility
{
    Swift,
    Heavy,
    Guardian
}

/// <summary>Shared combat values so humans, CPU ducks, and Grey Shadows use a wand consistently.</summary>
public readonly struct Unit1WandEffectProfile
{
    public Unit1WandEffectProfile(
        Unit1WandAbility ability,
        string displayName,
        string description,
        float attackCooldown,
        int damage,
        float stunDuration,
        float knockbackMultiplier,
        float shieldDuration,
        Color effectColor)
    {
        Ability = ability;
        DisplayName = displayName;
        Description = description;
        AttackCooldown = attackCooldown;
        Damage = damage;
        StunDuration = stunDuration;
        KnockbackMultiplier = knockbackMultiplier;
        ShieldDuration = shieldDuration;
        EffectColor = effectColor;
    }

    public Unit1WandAbility Ability { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public float AttackCooldown { get; }
    public int Damage { get; }
    public float StunDuration { get; }
    public float KnockbackMultiplier { get; }
    public float ShieldDuration { get; }
    public Color EffectColor { get; }
}

/// <summary>
/// A scene-owned wand whose holder is synchronized by Fusion. The visual is
/// attached locally to the holder's hand on every client while the NetworkObject
/// root remains in the scene, which avoids transform-parent conflicts.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class Unit1WandPickup : NetworkBehaviour
{
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Unit1WandAbility ability = Unit1WandAbility.Swift;
    [SerializeField, Min(0.25f)] private float pickupDistance = 1.5f;
    [SerializeField] private Vector3 heldLocalPosition = new(0.08f, 0.03f, 0.02f);
    [SerializeField] private Vector3 heldLocalEulerAngles = new(-90f, 180f, -90f);

    /// <summary>
    /// The duck currently carrying this wand. NetworkId (rather than
    /// PlayerRef) deliberately supports both a real player duck and a CPU
    /// duck, which has no PlayerRef of its own.
    /// </summary>
    [Networked] public NetworkId HolderObjectId { get; private set; }

    private Collider[] pickupColliders = System.Array.Empty<Collider>();
    private Transform originalParent;
    private Vector3 visualLocalPosition;
    private Quaternion visualLocalRotation;
    private Vector3 visualWorldScale = Vector3.one;
    private DuckMover renderedHolderMover;
    private NetworkId renderedHolderObjectId = NetworkId.None;

    public bool IsAvailable => HolderObjectId == NetworkId.None;
    public Unit1WandAbility Ability => ResolveAbility();
    public Unit1WandEffectProfile EffectProfile => GetEffectProfile(Ability);
    public string AbilityDisplayName => EffectProfile.DisplayName;
    public string AbilityDescription => EffectProfile.Description;

    public void Configure(Transform newVisualRoot)
    {
        visualRoot = newVisualRoot;
    }

    public void Configure(Transform newVisualRoot, Unit1WandAbility newAbility)
    {
        visualRoot = newVisualRoot;
        ability = newAbility;
    }

    public static Unit1WandEffectProfile GetEffectProfile(Unit1WandAbility wandAbility)
    {
        return wandAbility switch
        {
            Unit1WandAbility.Heavy => new Unit1WandEffectProfile(
                wandAbility,
                "重擊杖",
                "左鍵重擊：2 傷害、長暈眩與強擊退",
                0.95f,
                2,
                1.35f,
                2.6f,
                0f,
                new Color(0.72f, 0.30f, 1f, 1f)),
            Unit1WandAbility.Guardian => new Unit1WandEffectProfile(
                wandAbility,
                "守護杖",
                "左鍵展開 3 秒護盾，期間不受傷害",
                3.5f,
                0,
                0f,
                0f,
                3f,
                new Color(0.22f, 0.76f, 1f, 1f)),
            _ => new Unit1WandEffectProfile(
                Unit1WandAbility.Swift,
                "快攻杖",
                "左鍵快速施放：低傷害、冷卻最短",
                0.24f,
                1,
                0.18f,
                0.65f,
                0f,
                new Color(1f, 0.82f, 0.24f, 1f))
        };
    }

    // Existing UNIT1 scenes already contain the old model names. This fallback
    // lets them acquire the correct gameplay role immediately, even before the
    // editor's content builder has rebuilt and serialized the new enum value.
    private Unit1WandAbility ResolveAbility()
    {
        string objectName = gameObject.name;
        if (objectName.Contains("水晶"))
        {
            return Unit1WandAbility.Guardian;
        }

        if (objectName.Contains("虛空"))
        {
            return Unit1WandAbility.Heavy;
        }

        return ability;
    }

    public override void Spawned()
    {
        pickupColliders = GetComponents<Collider>();
        originalParent = transform;
        if (visualRoot != null)
        {
            visualLocalPosition = visualRoot.localPosition;
            visualLocalRotation = visualRoot.localRotation;
            visualWorldScale = visualRoot.lossyScale;
        }
    }

    public override void Render()
    {
        NetworkObject holderObject = Runner != null && HolderObjectId != NetworkId.None
            ? Runner.FindObject(HolderObjectId)
            : null;
        bool hasHolder = holderObject != null;

        if (!hasHolder)
        {
            ReleasePresentation();
            return;
        }

        if (renderedHolderObjectId != HolderObjectId)
        {
            renderedHolderMover?.SetNetworkWandHeld(this, false);
            renderedHolderMover = holderObject.GetComponent<DuckMover>();
            renderedHolderObjectId = HolderObjectId;
        }

        AttachVisual(holderObject.transform);
        renderedHolderMover?.SetNetworkWandHeld(this, true);
        SetPickupCollidersEnabled(false);
    }

    public bool CanBePickedUpBy(Vector3 playerPosition)
    {
        return IsAvailable
            && (transform.position - playerPosition).sqrMagnitude <= pickupDistance * pickupDistance;
    }

    public void RequestPickup()
    {
        if (Runner == null || !Runner.IsRunning || !IsAvailable)
        {
            return;
        }

        RPC_RequestPickup(Runner.LocalPlayer);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestPickup(PlayerRef requestingPlayer)
    {
        if (!IsAvailable
            || Runner == null
            || !Runner.TryGetPlayerObject(requestingPlayer, out NetworkObject playerObject)
            || playerObject == null
            || (playerObject.transform.position - transform.position).sqrMagnitude > pickupDistance * pickupDistance)
        {
            return;
        }

        HolderObjectId = playerObject.Id;
    }

    /// <summary>
    /// Called by a state-authoritative non-human duck once it has reached the
    /// wand. CPU ducks and Grey Shadows both use this same holder path.
    /// </summary>
    public bool TryPickupByNetworkObject(NetworkObject holderObject)
    {
        if (!HasStateAuthority
            || !IsAvailable
            || holderObject == null
            || (holderObject.transform.position - transform.position).sqrMagnitude > pickupDistance * pickupDistance)
        {
            return false;
        }

        HolderObjectId = holderObject.Id;
        return true;
    }

    public bool IsHeldBy(NetworkObject holderObject)
    {
        return holderObject != null && HolderObjectId == holderObject.Id;
    }

    /// <summary>Lets a defeated local player return a scene wand to the map.</summary>
    public void RequestRelease()
    {
        if (Runner == null || !Runner.IsRunning || HolderObjectId == NetworkId.None)
        {
            return;
        }

        RPC_RequestRelease(Runner.LocalPlayer);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestRelease(PlayerRef requestingPlayer)
    {
        if (Runner != null && Runner.TryGetPlayerObject(requestingPlayer, out NetworkObject playerObject))
        {
            ReleaseIfHeldBy(playerObject);
        }
    }

    /// <summary>State-authoritative release path for CPU ducks and Grey Shadows.</summary>
    public bool ReleaseIfHeldBy(NetworkObject holderObject)
    {
        if (!HasStateAuthority || !IsHeldBy(holderObject))
        {
            return false;
        }

        HolderObjectId = NetworkId.None;
        return true;
    }

    private void AttachVisual(Transform playerRoot)
    {
        if (visualRoot == null)
        {
            return;
        }

        Animator animator = playerRoot.GetComponentInChildren<Animator>();
        Transform hand = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        if (hand == null)
        {
            return;
        }

        if (visualRoot.parent != hand)
        {
            visualRoot.SetParent(hand, false);
        }

        visualRoot.localPosition = heldLocalPosition;
        visualRoot.localRotation = Quaternion.Euler(heldLocalEulerAngles);
        SetWorldScale(visualRoot, visualWorldScale);
    }

    private void ReleasePresentation()
    {
        if (renderedHolderObjectId != NetworkId.None)
        {
            renderedHolderMover?.SetNetworkWandHeld(this, false);
            renderedHolderMover = null;
            renderedHolderObjectId = NetworkId.None;
        }

        if (visualRoot != null && visualRoot.parent != originalParent)
        {
            visualRoot.SetParent(originalParent, false);
            visualRoot.localPosition = visualLocalPosition;
            visualRoot.localRotation = visualLocalRotation;
            SetWorldScale(visualRoot, visualWorldScale);
        }

        SetPickupCollidersEnabled(true);
    }

    private void SetPickupCollidersEnabled(bool enabled)
    {
        for (int i = 0; i < pickupColliders.Length; i++)
        {
            if (pickupColliders[i] != null)
            {
                pickupColliders[i].enabled = enabled;
            }
        }
    }

    private static void SetWorldScale(Transform target, Vector3 worldScale)
    {
        Transform parent = target.parent;
        if (parent == null)
        {
            target.localScale = worldScale;
            return;
        }

        Vector3 parentScale = parent.lossyScale;
        target.localScale = new Vector3(
            SafeDivide(worldScale.x, parentScale.x),
            SafeDivide(worldScale.y, parentScale.y),
            SafeDivide(worldScale.z, parentScale.z));
    }

    private static float SafeDivide(float value, float divisor)
    {
        return Mathf.Approximately(divisor, 0f) ? value : value / divisor;
    }
}
