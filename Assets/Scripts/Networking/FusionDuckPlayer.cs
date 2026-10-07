using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
[RequireComponent(typeof(DuckMover))]
public sealed class FusionDuckPlayer : NetworkBehaviour
{
    private const string SelectedCharacterIndexKey = "SelectedCharacterIndex";
    private const string SelectedSkinColorIndexKey = "SelectedSkinColorIndex";
    private const string PlayerNameKey = "PlayerName";
    private const string DefaultPlayerName = "小鴨";
    private const float PlayerColliderRadius = 0.58f;
    private const float PlayerColliderHeight = 3.077536f;
    private const float PlayerColliderCenterY = 1.785727f;
    // Only scale the art: collision, movement speed, and wand reach stay
    // consistent with the existing level layout.
    private const float MatchVisualScale = 0.88f;
    public const int MaximumHealth = 3;
    private const float DamageImmunityDuration = 0.8f;

    private static readonly Color[] SkinColors =
    {
        Color.white,
        new Color(1f, 0.28f, 0.28f),
        new Color(0.25f, 0.55f, 1f),
        new Color(0.25f, 1f, 0.45f),
        new Color(1f, 0.35f, 0.78f),
        new Color(0.65f, 0.32f, 1f)
    };

    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");

    [SerializeField] private GameObject[] characterVisuals = System.Array.Empty<GameObject>();
    [SerializeField] private int[] bodyMaterialIndices = System.Array.Empty<int>();

    [Networked] public int CharacterIndex { get; set; }
    [Networked] public float AnimationSpeed { get; set; }
    [Networked] public NetworkBool AnimationGrounded { get; set; }
    [Networked] public int AnimationStateHash { get; set; }
    /// <summary>0 until UNIT1's roulette assigns the player's match faction.</summary>
    [Networked] public int FactionIndex { get; set; }
    [Networked] public int SkinColorIndex { get; set; }
    /// <summary>Set by each player while waiting in BigHall.</summary>
    [Networked] public NetworkBool IsReady { get; set; }
    /// <summary>Used by the first-level enemy AI to identify wand carriers.</summary>
    [Networked] public NetworkBool HasWand { get; set; }
    [Networked] public int CurrentHealth { get; private set; }
    [Networked] public NetworkBool IsEliminated { get; private set; }
    [Networked] public Vector3 GravePosition { get; private set; }
    [Networked] public float GraveYaw { get; private set; }
    /// <summary>Real-time expiry for the temporary shield supplied by a Guardian Wand.</summary>
    [Networked] public float GuardianShieldUntil { get; private set; }
    [Networked] public NetworkString<_32> PlayerName { get; set; }

    private int appliedCharacterIndex = -1;
    private int appliedSkinColorIndex = -1;
    private int appliedAnimationStateHash;
    private Animator activeAnimator;
    private DuckMover mover;
    private MaterialPropertyBlock colorProperties;
    private DuckPlayerNameplate nameplate;
    private string appliedPlayerName = string.Empty;
    private bool controlAuthorityConfigured;
    private bool controlAuthorityInitialized;
    private bool stateAuthorityRequestIssued;
    // The persistent transfer component has already verified that this is the
    // Runner.LocalPlayer object.  In Shared mode InputAuthority can be None,
    // so this explicit mapping is what lets us request StateAuthority after a
    // scene switch.
    private bool isLocalSceneTransferPlayer;
    private bool hasReportedControlState;
    private bool reportedStateAuthority;
    private bool reportedInputAuthority;
    private bool reportedSceneTransferLocal;
    private bool reportedBodyKinematic;
    private bool reportedMoverEnabled;
    private float damageImmuneUntil;
    private bool matchVisualScaleApplied;

    public static int SkinColorCount => SkinColors.Length;
    public string FactionDisplayName => GetFactionName(FactionIndex);
    public bool IsAlive => !IsEliminated;
    // Shared mode can have no InputAuthority. Camera ownership uses the
    // runner's actual player mapping, never the StateAuthority of an AI/peer.
    public bool IsLocalPlayerObject => Object != null && Object.IsValid && Runner != null
        && Runner.TryGetPlayerObject(Runner.LocalPlayer, out NetworkObject local) && local == Object;
    public bool HasGuardianShield => !IsEliminated && Time.time < GuardianShieldUntil;
    public float GuardianShieldRemaining => HasGuardianShield ? GuardianShieldUntil - Time.time : 0f;

    public static Color GetSkinColor(int index)
    {
        return SkinColors[Mathf.Clamp(index, 0, SkinColors.Length - 1)];
    }

    /// <summary>
    /// Makes the next local shared-room player use the prefab's original
    /// colour instead of the colour selected in a previous room.
    /// </summary>
    public static void ResetLocalSkinColorSelection()
    {
        PlayerPrefs.SetInt(SelectedSkinColorIndexKey, 0);
        PlayerPrefs.Save();
    }

    public static Color GetFactionColor(int factionIndex)
    {
        return FactionTeam.GetColor(factionIndex);
    }

    public static string GetFactionName(int factionIndex)
    {
        return FactionTeam.GetName(factionIndex);
    }

    public void ConfigureVisuals(GameObject[] visuals, int[] materialIndices)
    {
        characterVisuals = visuals ?? System.Array.Empty<GameObject>();
        bodyMaterialIndices = materialIndices ?? System.Array.Empty<int>();
    }

    public override void Spawned()
    {
        ConfigurePlayerCollider();
        mover = GetComponent<DuckMover>();
        ApplyMatchVisualScale();
        if (HasStateAuthority)
        {
            CharacterIndex = ClampCharacterIndex(PlayerPrefs.GetInt(SelectedCharacterIndexKey, 0));
            // A first-level player is spawned as a new network object, so use
            // the local colour selected on the BigHall colour pad.
            SkinColorIndex = ClampSkinColorIndex(PlayerPrefs.GetInt(SelectedSkinColorIndexKey, 0));
            FactionIndex = 0;
            IsReady = false;
            HasWand = false;
            CurrentHealth = MaximumHealth;
            IsEliminated = false;
            GravePosition = transform.position;
            GraveYaw = transform.eulerAngles.y;
            GuardianShieldUntil = 0f;
            PlayerName = SanitizePlayerName(PlayerPrefs.GetString(PlayerNameKey, DefaultPlayerName));
            CaptureAnimationState();
        }

        ApplyCharacterVisual(CharacterIndex);
        ApplySkinColor(SkinColorIndex);
        ApplyPlayerName(PlayerName.ToString());
        EnsureControlAuthority();
        ApplyDeathPresentation();
    }

    private void ApplyMatchVisualScale()
    {
        if (matchVisualScaleApplied)
        {
            return;
        }

        // There are two selectable model roots, one of which is inactive.
        // Scale both roots once so switching character never changes size.
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

    private void ConfigurePlayerCollider()
    {
        CapsuleCollider capsule = GetComponent<CapsuleCollider>();
        if (capsule == null)
        {
            return;
        }

        capsule.direction = 1;
        capsule.center = new Vector3(0f, PlayerColliderCenterY, 0f);
        capsule.height = PlayerColliderHeight;
        capsule.radius = PlayerColliderRadius;
        capsule.enabled = true;
        capsule.isTrigger = false;
    }

    public override void Render()
    {
        ApplyCharacterVisual(CharacterIndex);
        ApplySkinColor(SkinColorIndex);
        ApplyPlayerName(PlayerName.ToString());
        EnsureControlAuthority();
        ReportControlState("render");
        ApplyDeathPresentation();
        if (!HasStateAuthority && !IsEliminated)
        {
            ApplyRemoteAnimation();
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
        {
            return;
        }

        mover ??= GetComponent<DuckMover>();
        if (IsEliminated || Unit1GameDirector.IsMatchFinished())
        {
            CaptureAnimationState();
            return;
        }

        mover?.SimulateNetworkMovement(Runner.DeltaTime);
        CaptureAnimationState();
    }

    public void SetSkinColor(int colorIndex)
    {
        if (!HasStateAuthority)
        {
            return;
        }

        int selectedColor = ClampSkinColorIndex(colorIndex);
        SkinColorIndex = selectedColor;
        PlayerPrefs.SetInt(SelectedSkinColorIndexKey, selectedColor);
        PlayerPrefs.Save();
        ApplySkinColor(selectedColor);
    }

    /// <summary>Receives the room master's draw without touching lobby skin selection.</summary>
    public void AssignRandomFaction(int factionIndex)
    {
        factionIndex = FactionTeam.Normalize(factionIndex);
        if (HasStateAuthority)
        {
            FactionIndex = factionIndex;
        }

        // This method is invoked only by the director's targeted RPC on the
        // player's own client.  Keep the presentation local, independently
        // from when Fusion confirms the transferred StateAuthority.
        FactionRouletteHud.GetOrCreate().Play(factionIndex);
    }

    public void SetPlayerName(string playerName)
    {
        if (!HasStateAuthority)
        {
            return;
        }

        string sanitizedName = SanitizePlayerName(playerName);
        PlayerName = sanitizedName;
        PlayerPrefs.SetString(PlayerNameKey, sanitizedName);
        PlayerPrefs.Save();
        ApplyPlayerName(sanitizedName);
    }

    public void SetReady(bool ready)
    {
        if (HasStateAuthority)
        {
            IsReady = ready;
        }
    }

    public void SetHasWand(bool value)
    {
        if (HasStateAuthority)
        {
            HasWand = value && !IsEliminated;
        }
    }

    /// <summary>Activates the Guardian Wand's short damage-immunity window.</summary>
    public bool ActivateGuardianShield(float duration)
    {
        if (Object != null && !HasStateAuthority)
        {
            RPC_RequestGuardianShield(duration);
            return false;
        }

        return ApplyGuardianShield(duration);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestGuardianShield(float duration)
    {
        ApplyGuardianShield(duration);
    }

    private bool ApplyGuardianShield(float duration)
    {
        if (IsEliminated || duration <= 0f)
        {
            return false;
        }

        GuardianShieldUntil = Time.time + duration;
        return true;
    }

    /// <summary>Applies an enemy spell hit on this duck's StateAuthority.</summary>
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
        GravePosition = DuckGraveMarker.FindGroundPosition(transform, GetComponent<Collider>());
        GraveYaw = transform.eulerAngles.y;
        HasWand = false;
        mover ??= GetComponent<DuckMover>();
        mover?.ReleaseNetworkWand();
        EnsureControlAuthority();
        ApplyDeathPresentation();
        Debug.Log("[UNIT1] " + PlayerName + " 已陣亡。", this);
    }

    /// <summary>
    /// Called only for Runner.LocalPlayer's mapped object after UNIT1 loads.
    /// This asks Fusion to hand StateAuthority to the local mapped duck after
    /// UNIT1 loads.  Scene placement and camera setup do not wait for that
    /// request, but movement begins only after Fusion confirms the authority.
    /// </summary>
    public void EnableLocalControlFromSceneTransfer()
    {
        isLocalSceneTransferPlayer = true;
        if (!HasStateAuthority && !stateAuthorityRequestIssued)
        {
            Object.RequestStateAuthority();
            stateAuthorityRequestIssued = true;
        }

        controlAuthorityInitialized = false;
        EnsureControlAuthority();
        ReportControlState("UNIT1 scene transfer");
    }

    private void ApplyCharacterVisual(int characterIndex)
    {
        int selectedIndex = ClampCharacterIndex(characterIndex);
        if (appliedCharacterIndex == selectedIndex)
        {
            return;
        }

        for (int i = 0; i < characterVisuals.Length; i++)
        {
            if (characterVisuals[i] != null)
            {
                characterVisuals[i].SetActive(i == selectedIndex);
            }
        }

        appliedCharacterIndex = selectedIndex;
        appliedSkinColorIndex = -1;
        activeAnimator = GetComponentInChildren<Animator>();
        appliedAnimationStateHash = 0;
        mover ??= GetComponent<DuckMover>();
        mover?.RefreshCharacterReferences();

        nameplate?.RefreshPosition();
    }

    private void ApplySkinColor(int colorIndex)
    {
        int selectedColor = ClampSkinColorIndex(colorIndex);
        if (appliedSkinColorIndex == selectedColor || appliedCharacterIndex < 0 || appliedCharacterIndex >= characterVisuals.Length)
        {
            return;
        }

        GameObject activeVisual = characterVisuals[appliedCharacterIndex];
        if (activeVisual == null)
        {
            return;
        }

        SkinnedMeshRenderer[] renderers = activeVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int bodyMaterialIndex = GetBodyMaterialIndex(appliedCharacterIndex);
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = renderers[i];
            if (bodyMaterialIndex < 0 || bodyMaterialIndex >= renderer.sharedMaterials.Length)
            {
                continue;
            }

            renderer.SetPropertyBlock(null, bodyMaterialIndex);
            if (selectedColor == 0)
            {
                continue;
            }

            colorProperties ??= new MaterialPropertyBlock();
            Color skinColor = GetSkinColor(selectedColor);
            colorProperties.Clear();
            colorProperties.SetColor(BaseColorProperty, skinColor);
            colorProperties.SetColor(ColorProperty, skinColor);
            renderer.SetPropertyBlock(colorProperties, bodyMaterialIndex);
        }

        appliedSkinColorIndex = selectedColor;
    }

    private void ApplyPlayerName(string playerName)
    {
        string sanitizedName = SanitizePlayerName(playerName);
        if (appliedPlayerName == sanitizedName)
        {
            return;
        }

        nameplate ??= GetComponent<DuckPlayerNameplate>();
        nameplate ??= gameObject.AddComponent<DuckPlayerNameplate>();
        nameplate.SetDisplayName(sanitizedName);
        appliedPlayerName = sanitizedName;
    }

    private void EnsureControlAuthority()
    {
        // InputAuthority can be None in Shared mode.  The transfer component
        // only identifies which object should request StateAuthority; it must
        // never become a second movement authority itself.
        if (isLocalSceneTransferPlayer && !HasStateAuthority)
        {
            if (!stateAuthorityRequestIssued)
            {
                Object.RequestStateAuthority();
                stateAuthorityRequestIssued = true;
            }
        }
        else if (HasStateAuthority)
        {
            stateAuthorityRequestIssued = false;
        }

        // The owner of the network state is the sole Rigidbody writer.  Letting
        // a locally mapped object move before StateAuthority arrives creates a
        // Unity FixedUpdate/Fusion snapshot tug-of-war after a scene switch.
        bool receivesLocalInput = HasStateAuthority && !IsEliminated;
        if (controlAuthorityInitialized && controlAuthorityConfigured == receivesLocalInput)
        {
            return;
        }

        controlAuthorityInitialized = true;
        controlAuthorityConfigured = receivesLocalInput;
        mover ??= GetComponent<DuckMover>();
        if (mover != null)
        {
            mover.enabled = receivesLocalInput;
        }

        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null)
        {
            // The local state owner uses a manual collision motor in the
            // Fusion tick; replicas are network snapshots. Neither side may
            // run an independent Unity gravity/velocity simulation.
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
            }

            body.isKinematic = true;
            body.useGravity = false;
        }

        if (!receivesLocalInput || !IsLocalPlayerObject)
        {
            return;
        }

        RPGCameraFollow cameraFollow = RPGCameraFollow.GetOrActivateMainCamera();
        cameraFollow?.SetTarget(transform);
    }

    private void ApplyDeathPresentation()
    {
        DuckGraveMarker.GetOrAdd(gameObject).Apply(IsEliminated, GravePosition, GraveYaw);
        if (IsLocalPlayerObject)
        {
            Unit1SpectatorController spectator = GetComponent<Unit1SpectatorController>();
            if (spectator == null) spectator = gameObject.AddComponent<Unit1SpectatorController>();
            spectator.Bind(this);
        }
    }

    /// <summary>
    /// Emits a compact state-change-only record for diagnosing scene-transfer
    /// movement.  It deliberately avoids per-frame spam.
    /// </summary>
    private void ReportControlState(string source)
    {
        Rigidbody body = GetComponent<Rigidbody>();
        mover ??= GetComponent<DuckMover>();

        bool bodyKinematic = body != null && body.isKinematic;
        bool moverEnabled = mover != null && mover.enabled;
        if (hasReportedControlState
            && reportedStateAuthority == HasStateAuthority
            && reportedInputAuthority == HasInputAuthority
            && reportedSceneTransferLocal == isLocalSceneTransferPlayer
            && reportedBodyKinematic == bodyKinematic
            && reportedMoverEnabled == moverEnabled)
        {
            return;
        }

        hasReportedControlState = true;
        reportedStateAuthority = HasStateAuthority;
        reportedInputAuthority = HasInputAuthority;
        reportedSceneTransferLocal = isLocalSceneTransferPlayer;
        reportedBodyKinematic = bodyKinematic;
        reportedMoverEnabled = moverEnabled;

        Debug.Log(
            "[UNIT1-DIAG Player] " + source
            + " | object=" + (Object != null ? Object.Id.ToString() : "none")
            + " | stateAuthority=" + HasStateAuthority
            + " | inputAuthority=" + HasInputAuthority
            + " | mappedLocal=" + isLocalSceneTransferPlayer
            + " | moverEnabled=" + moverEnabled
            + " | kinematic=" + bodyKinematic
            + " | gravity=" + (body != null && body.useGravity));
    }

    private void CaptureAnimationState()
    {
        mover ??= GetComponent<DuckMover>();
        if (mover == null)
        {
            return;
        }

        AnimationSpeed = mover.NetworkAnimationSpeed;
        AnimationGrounded = mover.NetworkAnimationGrounded;
        AnimationStateHash = mover.NetworkAnimationStateHash;
    }

    private void ApplyRemoteAnimation()
    {
        if (activeAnimator == null)
        {
            activeAnimator = GetComponentInChildren<Animator>();
        }

        if (activeAnimator == null)
        {
            return;
        }

        activeAnimator.SetFloat("Speed", AnimationSpeed);
        activeAnimator.SetBool("Grounded", AnimationGrounded);

        if (AnimationStateHash != 0 && appliedAnimationStateHash != AnimationStateHash)
        {
            activeAnimator.CrossFadeInFixedTime(AnimationStateHash, 0.08f);
            appliedAnimationStateHash = AnimationStateHash;
        }
    }

    private int ClampCharacterIndex(int characterIndex)
    {
        return characterVisuals.Length == 0 ? 0 : Mathf.Clamp(characterIndex, 0, characterVisuals.Length - 1);
    }

    private static int ClampSkinColorIndex(int colorIndex)
    {
        return Mathf.Clamp(colorIndex, 0, SkinColors.Length - 1);
    }

    private int GetBodyMaterialIndex(int characterIndex)
    {
        return characterIndex >= 0 && characterIndex < bodyMaterialIndices.Length
            ? bodyMaterialIndices[characterIndex]
            : -1;
    }

    private static string SanitizePlayerName(string playerName)
    {
        string value = string.IsNullOrWhiteSpace(playerName) ? DefaultPlayerName : playerName.Trim();
        return value.Length <= 16 ? value : value.Substring(0, 16);
    }
}
