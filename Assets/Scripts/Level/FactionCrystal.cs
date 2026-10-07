using System.Collections;
using Fusion;
using TMPro;
using UnityEngine;

/// <summary>
/// A networked base objective for one UNIT1 faction. It accepts damage only
/// from an assigned enemy faction and broadcasts every meaningful state change
/// so each team can react before its crystal is destroyed.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class FactionCrystal : NetworkBehaviour
{
    public const int DefaultMaximumHealth = 10;
    private const float MaximumValidatedAttackDistance = 3.35f;

    [SerializeField, Min(1)] private int maximumHealth = DefaultMaximumHealth;
    [SerializeField, Min(1f)] private float guardianProtectionRange = 6.25f;

    [Networked] public int FactionIndex { get; private set; }
    [Networked] public int CurrentHealth { get; private set; }
    [Networked] public float ShieldUntil { get; private set; }
    [Networked] public NetworkBool IsDestroyed { get; private set; }

    private Renderer[] crystalRenderers = System.Array.Empty<Renderer>();
    private Renderer[] baseRenderers = System.Array.Empty<Renderer>();
    private Renderer[] shieldRenderers = System.Array.Empty<Renderer>();
    private Renderer[] friendlyBeaconRenderers = System.Array.Empty<Renderer>();
    private GameObject crystalVisualRoot;
    private GameObject shieldVisualRoot;
    private GameObject friendlyBeaconRoot;
    private Light crystalLight;
    private Light friendlyBeaconLight;
    private TextMeshPro statusLabel;
    private Collider targetCollider;
    private Vector3 visualRestScale = Vector3.one;
    private float impactPulseUntil;
    private int displayedFaction = -1;
    private int displayedHealth = -1;
    private bool displayedShield;
    private bool displayedDestroyed;
    private int displayedBeaconFaction = -1;

    public int MaximumHealth => maximumHealth;
    public bool HasShield => !IsDestroyed && Time.time < ShieldUntil;
    public float ShieldRemaining => HasShield ? ShieldUntil - Time.time : 0f;

    public bool CanBeDamagedBy(int attackerFaction)
    {
        return !IsDestroyed && FactionTeam.AreEnemies(attackerFaction, FactionIndex);
    }

    public override void Spawned()
    {
        CreateVisuals();
        if (HasStateAuthority)
        {
            CurrentHealth = maximumHealth;
            ShieldUntil = 0f;
            IsDestroyed = false;
        }

        RefreshPresentation(true);
    }

    public override void Render()
    {
        RefreshPresentation(false);
    }

    /// <summary>Called by UNIT1's shared-mode master immediately after spawning.</summary>
    public void AssignFaction(int factionIndex)
    {
        if (!HasStateAuthority)
        {
            return;
        }

        FactionIndex = FactionTeam.Normalize(factionIndex);
    }

    /// <summary>
    /// Requests an enemy wand's crystal damage from this object's authority.
    /// The authority resolves this NetworkObject again, rather than trusting a
    /// faction value supplied by the attacking client.
    /// </summary>
    public void TakeDamage(NetworkObject attackerObject, int damage)
    {
        if (attackerObject == null)
        {
            return;
        }

        if (Object != null && !HasStateAuthority)
        {
            RPC_RequestDamage(attackerObject.Id, damage);
            return;
        }

        ApplyDamage(attackerObject.Id, damage);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDamage(NetworkId attackerObjectId, int damage)
    {
        ApplyDamage(attackerObjectId, damage);
    }

    private void ApplyDamage(NetworkId attackerObjectId, int damage)
    {
        if (!TryGetValidAttackerFaction(attackerObjectId, out int attackerFaction))
        {
            return;
        }

        if (!CanBeDamagedBy(attackerFaction) || damage <= 0 || HasShield)
        {
            return;
        }

        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        bool destroyedNow = CurrentHealth == 0;
        if (destroyedNow)
        {
            IsDestroyed = true;
        }

        RPC_NotifyCrystalState(FactionIndex, CurrentHealth, maximumHealth, IsDestroyed, false);
    }

    private bool TryGetValidAttackerFaction(NetworkId attackerObjectId, out int attackerFaction)
    {
        attackerFaction = FactionTeam.Neutral;
        NetworkObject attackerObject = Runner != null ? Runner.FindObject(attackerObjectId) : null;
        if (attackerObject == null
            || FlatDistance(attackerObject.transform.position, transform.position) > MaximumValidatedAttackDistance)
        {
            return false;
        }

        FusionDuckPlayer player = attackerObject.GetComponent<FusionDuckPlayer>();
        if (player != null)
        {
            DuckMover mover = attackerObject.GetComponent<DuckMover>();
            if (mover == null || !mover.HasWeapon)
            {
                return false;
            }

            attackerFaction = player.FactionIndex;
            return FactionTeam.IsAssigned(attackerFaction);
        }

        Unit1BotDuck cpu = attackerObject.GetComponent<Unit1BotDuck>();
        if (cpu != null && cpu.HasWand)
        {
            attackerFaction = cpu.FactionIndex;
            return FactionTeam.IsAssigned(attackerFaction);
        }

        EnemyDuckAI convertedShadow = attackerObject.GetComponent<EnemyDuckAI>();
        if (convertedShadow != null && convertedShadow.HasWand && convertedShadow.IsConverted)
        {
            attackerFaction = convertedShadow.FactionIndex;
            return FactionTeam.IsAssigned(attackerFaction);
        }

        return false;
    }

    /// <summary>
    /// Attempts to protect the closest intact friendly crystal. Returning true
    /// means the caster was close enough; the owning network authority performs
    /// the final distance and faction validation before changing state.
    /// </summary>
    public static bool TryProtectNearestFriendlyCrystal(
        Vector3 casterPosition,
        int casterFaction,
        float shieldDuration,
        int repairAmount,
        out FactionCrystal protectedCrystal)
    {
        protectedCrystal = null;
        if (!FactionTeam.IsAssigned(casterFaction))
        {
            return false;
        }

        FactionCrystal[] crystals = FindObjectsByType<FactionCrystal>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        float closestDistance = float.PositiveInfinity;
        for (int index = 0; index < crystals.Length; index++)
        {
            FactionCrystal candidate = crystals[index];
            if (candidate == null
                || candidate.IsDestroyed
                || !FactionTeam.AreAllies(casterFaction, candidate.FactionIndex))
            {
                continue;
            }

            float distance = FlatDistance(casterPosition, candidate.transform.position);
            if (distance > candidate.guardianProtectionRange || distance >= closestDistance)
            {
                continue;
            }

            protectedCrystal = candidate;
            closestDistance = distance;
        }

        return protectedCrystal != null
            && protectedCrystal.RequestGuardianProtection(casterFaction, casterPosition, shieldDuration, repairAmount);
    }

    public static FactionCrystal FindForFaction(int factionIndex)
    {
        FactionCrystal[] crystals = FindObjectsByType<FactionCrystal>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int index = 0; index < crystals.Length; index++)
        {
            if (crystals[index] != null && crystals[index].FactionIndex == factionIndex)
            {
                return crystals[index];
            }
        }

        return null;
    }

    private bool RequestGuardianProtection(
        int casterFaction,
        Vector3 casterPosition,
        float shieldDuration,
        int repairAmount)
    {
        if (!CanProtect(casterFaction, casterPosition, shieldDuration))
        {
            return false;
        }

        if (Object != null && !HasStateAuthority)
        {
            RPC_RequestGuardianProtection(casterFaction, casterPosition, shieldDuration, repairAmount);
            return true;
        }

        ApplyGuardianProtection(casterFaction, casterPosition, shieldDuration, repairAmount);
        return true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestGuardianProtection(
        int casterFaction,
        Vector3 casterPosition,
        float shieldDuration,
        int repairAmount)
    {
        ApplyGuardianProtection(casterFaction, casterPosition, shieldDuration, repairAmount);
    }

    private void ApplyGuardianProtection(
        int casterFaction,
        Vector3 casterPosition,
        float shieldDuration,
        int repairAmount)
    {
        if (!CanProtect(casterFaction, casterPosition, shieldDuration))
        {
            return;
        }

        ShieldUntil = Time.time + shieldDuration;
        CurrentHealth = Mathf.Min(maximumHealth, CurrentHealth + Mathf.Max(0, repairAmount));
        RPC_NotifyCrystalState(FactionIndex, CurrentHealth, maximumHealth, false, true);
    }

    private bool CanProtect(int casterFaction, Vector3 casterPosition, float shieldDuration)
    {
        return !IsDestroyed
            && shieldDuration > 0f
            && FactionTeam.AreAllies(casterFaction, FactionIndex)
            && FlatDistance(casterPosition, transform.position) <= guardianProtectionRange;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyCrystalState(
        int factionIndex,
        int currentHealth,
        int healthMaximum,
        bool destroyed,
        bool protectedNow)
    {
        impactPulseUntil = Time.time + (destroyed ? 0.55f : 0.2f);
        FactionCrystalAlertHud.GetOrCreate().Show(
            factionIndex,
            currentHealth,
            healthMaximum,
            destroyed,
            protectedNow);
    }

    private void CreateVisuals()
    {
        if (crystalVisualRoot != null)
        {
            return;
        }

        targetCollider = GetComponent<Collider>();
        if (targetCollider != null)
        {
            // Crystal damage is range-validated by TakeDamage. The collider
            // exists for targeting, so it must not become an invisible wall
            // that causes route-planning ducks to oscillate around a base.
            targetCollider.isTrigger = true;
        }

        GameObject baseRoot = new("Crystal Base");
        baseRoot.transform.SetParent(transform, false);
        baseRoot.transform.localPosition = new Vector3(0f, 0.15f, 0f);
        GameObject baseDisc = CreatePrimitive(baseRoot.transform, "Base Disc", PrimitiveType.Cylinder, Vector3.zero, new Vector3(1.22f, 0.14f, 1.22f));
        GameObject baseRing = CreatePrimitive(baseRoot.transform, "Base Ring", PrimitiveType.Cylinder, new Vector3(0f, 0.18f, 0f), new Vector3(0.94f, 0.045f, 0.94f));
        baseRenderers = new[] { baseDisc.GetComponent<Renderer>(), baseRing.GetComponent<Renderer>() };

        crystalVisualRoot = new GameObject("Faction Crystal Visual");
        crystalVisualRoot.transform.SetParent(transform, false);
        crystalVisualRoot.transform.localPosition = new Vector3(0f, 1.15f, 0f);
        visualRestScale = crystalVisualRoot.transform.localScale;
        GameObject core = CreatePrimitive(crystalVisualRoot.transform, "Crystal Core", PrimitiveType.Cube, Vector3.zero, new Vector3(0.76f, 1.35f, 0.76f));
        core.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        GameObject shardLeft = CreatePrimitive(crystalVisualRoot.transform, "Crystal Shard Left", PrimitiveType.Cube, new Vector3(-0.5f, 0.05f, 0.03f), new Vector3(0.34f, 0.82f, 0.34f));
        shardLeft.transform.localRotation = Quaternion.Euler(0f, 22f, -24f);
        GameObject shardRight = CreatePrimitive(crystalVisualRoot.transform, "Crystal Shard Right", PrimitiveType.Cube, new Vector3(0.48f, -0.08f, -0.06f), new Vector3(0.31f, 0.7f, 0.31f));
        shardRight.transform.localRotation = Quaternion.Euler(0f, -26f, 27f);
        crystalRenderers = new[] { core.GetComponent<Renderer>(), shardLeft.GetComponent<Renderer>(), shardRight.GetComponent<Renderer>() };

        shieldVisualRoot = new GameObject("Crystal Guardian Shield");
        shieldVisualRoot.transform.SetParent(transform, false);
        shieldVisualRoot.transform.localPosition = new Vector3(0f, 0.37f, 0f);
        GameObject shieldRingLow = CreatePrimitive(shieldVisualRoot.transform, "Shield Ring Low", PrimitiveType.Cylinder, Vector3.zero, new Vector3(1.5f, 0.018f, 1.5f));
        GameObject shieldRingHigh = CreatePrimitive(shieldVisualRoot.transform, "Shield Ring High", PrimitiveType.Cylinder, new Vector3(0f, 1.75f, 0f), new Vector3(1.18f, 0.018f, 1.18f));
        shieldRenderers = new[] { shieldRingLow.GetComponent<Renderer>(), shieldRingHigh.GetComponent<Renderer>() };
        for (int index = 0; index < shieldRenderers.Length; index++)
        {
            ConfigureMaterial(shieldRenderers[index], FactionTeam.GetColor(FactionIndex), 1.7f);
        }
        shieldVisualRoot.SetActive(false);

        // This is a local-only waypoint. Every client builds it, but only the
        // client whose player belongs to this crystal's faction enables it.
        friendlyBeaconRoot = new GameObject("Friendly Crystal Beacon");
        friendlyBeaconRoot.transform.SetParent(transform, false);
        friendlyBeaconRoot.transform.localPosition = new Vector3(0f, 3.35f, 0f);
        GameObject beaconBeam = CreatePrimitive(
            friendlyBeaconRoot.transform,
            "Beacon Beam",
            PrimitiveType.Cylinder,
            new Vector3(0f, 5.5f, 0f),
            new Vector3(0.11f, 5.5f, 0.11f));
        GameObject beaconCap = CreatePrimitive(
            friendlyBeaconRoot.transform,
            "Beacon Cap",
            PrimitiveType.Sphere,
            new Vector3(0f, 11f, 0f),
            Vector3.one * 0.32f);
        friendlyBeaconRenderers = new[]
        {
            beaconBeam.GetComponent<Renderer>(),
            beaconCap.GetComponent<Renderer>()
        };

        GameObject beaconLightObject = new("Beacon Glow", typeof(Light));
        beaconLightObject.transform.SetParent(friendlyBeaconRoot.transform, false);
        beaconLightObject.transform.localPosition = new Vector3(0f, 10.8f, 0f);
        friendlyBeaconLight = beaconLightObject.GetComponent<Light>();
        friendlyBeaconLight.type = LightType.Point;
        friendlyBeaconLight.range = 8f;
        friendlyBeaconLight.intensity = 2.5f;
        friendlyBeaconRoot.SetActive(false);

        GameObject lightObject = new("Crystal Glow", typeof(Light));
        lightObject.transform.SetParent(transform, false);
        lightObject.transform.localPosition = new Vector3(0f, 1.4f, 0f);
        crystalLight = lightObject.GetComponent<Light>();
        crystalLight.type = LightType.Point;
        crystalLight.range = 5f;
        crystalLight.intensity = 1.4f;

        GameObject labelObject = new("Crystal Status", typeof(TextMeshPro));
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition = new Vector3(0f, 3.1f, 0f);
        labelObject.transform.localScale = Vector3.one * 0.2f;
        statusLabel = labelObject.GetComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null)
        {
            statusLabel.font = TMP_Settings.defaultFontAsset;
            statusLabel.fontSharedMaterial = TMP_Settings.defaultFontAsset.material;
        }
        statusLabel.alignment = TextAlignmentOptions.Center;
        statusLabel.fontSize = 5f;
        statusLabel.outlineColor = new Color(0.02f, 0.03f, 0.08f, 1f);
        statusLabel.outlineWidth = 0.24f;
        statusLabel.textWrappingMode = TextWrappingModes.NoWrap;
        statusLabel.rectTransform.sizeDelta = new Vector2(20f, 4f);
    }

    private void RefreshPresentation(bool force)
    {
        if (crystalVisualRoot == null)
        {
            CreateVisuals();
        }

        bool shielded = HasShield;
        if (!force
            && displayedFaction == FactionIndex
            && displayedHealth == CurrentHealth
            && displayedShield == shielded
            && displayedDestroyed == IsDestroyed)
        {
            UpdateAnimatedPresentation();
            return;
        }

        displayedFaction = FactionIndex;
        displayedHealth = CurrentHealth;
        displayedShield = shielded;
        displayedDestroyed = IsDestroyed;
        Color factionColor = FactionTeam.GetColor(FactionIndex);
        Color crystalColor = IsDestroyed ? new Color(0.25f, 0.27f, 0.32f, 1f) : factionColor;
        for (int index = 0; index < crystalRenderers.Length; index++)
        {
            ConfigureMaterial(crystalRenderers[index], crystalColor, IsDestroyed ? 0f : 1.35f);
        }
        for (int index = 0; index < baseRenderers.Length; index++)
        {
            ConfigureMaterial(baseRenderers[index], IsDestroyed ? new Color(0.16f, 0.17f, 0.21f) : factionColor * 0.55f, 0.45f);
        }
        for (int index = 0; index < shieldRenderers.Length; index++)
        {
            ConfigureMaterial(shieldRenderers[index], factionColor, IsDestroyed ? 0f : 1.8f);
        }

        if (crystalVisualRoot != null)
        {
            crystalVisualRoot.SetActive(!IsDestroyed);
        }
        if (shieldVisualRoot != null)
        {
            shieldVisualRoot.SetActive(shielded);
        }
        if (targetCollider != null)
        {
            targetCollider.enabled = !IsDestroyed;
        }
        if (crystalLight != null)
        {
            crystalLight.color = crystalColor;
            crystalLight.intensity = IsDestroyed ? 0f : shielded ? 2f : 1.4f;
        }
        if (statusLabel != null)
        {
            string factionName = FactionTeam.GetName(FactionIndex);
            statusLabel.color = IsDestroyed ? new Color(0.68f, 0.7f, 0.76f) : factionColor;
            statusLabel.text = IsDestroyed
                ? factionName + "方水晶\n已毀壞"
                : factionName + "方水晶　" + CurrentHealth + " / " + maximumHealth
                  + (shielded ? "\n護盾 " + ShieldRemaining.ToString("0.0") + " 秒" : string.Empty);
        }

        UpdateAnimatedPresentation();
    }

    private void Update()
    {
        UpdateAnimatedPresentation();
        UpdateFriendlyCrystalBeacon();
        if (statusLabel == null)
        {
            return;
        }

        Camera camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        Vector3 direction = statusLabel.transform.position - camera.transform.position;
        if (direction.sqrMagnitude > 0.001f)
        {
            statusLabel.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }

    private void UpdateAnimatedPresentation()
    {
        if (crystalVisualRoot != null)
        {
            float pulse = Time.time < impactPulseUntil
                ? 1f + Mathf.Sin(Time.time * 42f) * 0.18f
                : 1f;
            crystalVisualRoot.transform.localScale = visualRestScale * pulse;
            crystalVisualRoot.transform.Rotate(Vector3.up, Time.deltaTime * 20f, Space.Self);
        }
        if (shieldVisualRoot != null && shieldVisualRoot.activeSelf)
        {
            shieldVisualRoot.transform.Rotate(Vector3.up, Time.deltaTime * 72f, Space.Self);
        }
        if (friendlyBeaconLight != null && friendlyBeaconRoot != null && friendlyBeaconRoot.activeSelf)
        {
            friendlyBeaconLight.intensity = 2.5f + Mathf.Sin(Time.time * 3.2f) * 0.45f;
        }
    }

    private void UpdateFriendlyCrystalBeacon()
    {
        if (friendlyBeaconRoot == null)
        {
            return;
        }

        int localFaction = GetLocalPlayerFaction();
        bool shouldShow = !IsDestroyed && FactionTeam.AreAllies(localFaction, FactionIndex);
        if (friendlyBeaconRoot.activeSelf != shouldShow)
        {
            friendlyBeaconRoot.SetActive(shouldShow);
        }

        if (!shouldShow)
        {
            return;
        }

        if (displayedBeaconFaction == FactionIndex)
        {
            return;
        }

        displayedBeaconFaction = FactionIndex;
        Color factionColor = FactionTeam.GetColor(FactionIndex);
        for (int index = 0; index < friendlyBeaconRenderers.Length; index++)
        {
            ConfigureMaterial(friendlyBeaconRenderers[index], factionColor, 4.5f);
        }
        if (friendlyBeaconLight != null)
        {
            friendlyBeaconLight.color = factionColor;
        }
    }

    private static int GetLocalPlayerFaction()
    {
        FusionDuckPlayer[] players = FindObjectsByType<FusionDuckPlayer>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int index = 0; index < players.Length; index++)
        {
            FusionDuckPlayer player = players[index];
            if (player != null && player.HasStateAuthority)
            {
                return player.FactionIndex;
            }
        }

        return FactionTeam.Neutral;
    }

    private static GameObject CreatePrimitive(
        Transform parent,
        string objectName,
        PrimitiveType primitiveType,
        Vector3 localPosition,
        Vector3 localScale)
    {
        GameObject primitive = GameObject.CreatePrimitive(primitiveType);
        primitive.name = objectName;
        primitive.transform.SetParent(parent, false);
        primitive.transform.localPosition = localPosition;
        primitive.transform.localScale = localScale;
        Collider collider = primitive.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
        }
        return primitive;
    }

    private static void ConfigureMaterial(Renderer renderer, Color color, float emissionIntensity)
    {
        if (renderer == null)
        {
            return;
        }

        Material material = renderer.sharedMaterial;
        if (material == null || material.name == "Default-Material")
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = "Runtime Faction Crystal Material" };
            renderer.sharedMaterial = material;
        }
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emissionIntensity);
        }
    }

    private static float FlatDistance(Vector3 first, Vector3 second)
    {
        Vector3 difference = first - second;
        difference.y = 0f;
        return difference.magnitude;
    }
}
