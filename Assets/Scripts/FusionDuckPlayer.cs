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
    [Networked] public int SkinColorIndex { get; set; }
    [Networked] public NetworkString<_32> PlayerName { get; set; }

    private int appliedCharacterIndex = -1;
    private int appliedSkinColorIndex = -1;
    private int appliedAnimationStateHash;
    private Animator activeAnimator;
    private DuckMover mover;
    private MaterialPropertyBlock colorProperties;
    private DuckPlayerNameplate nameplate;
    private string appliedPlayerName = string.Empty;

    public static int SkinColorCount => SkinColors.Length;

    public static Color GetSkinColor(int index)
    {
        return SkinColors[Mathf.Clamp(index, 0, SkinColors.Length - 1)];
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
        if (HasStateAuthority)
        {
            CharacterIndex = ClampCharacterIndex(PlayerPrefs.GetInt(SelectedCharacterIndexKey, 0));
            SkinColorIndex = ClampSkinColorIndex(PlayerPrefs.GetInt(SelectedSkinColorIndexKey, 0));
            PlayerName = SanitizePlayerName(PlayerPrefs.GetString(PlayerNameKey, DefaultPlayerName));
            CaptureAnimationState();
        }

        ApplyCharacterVisual(CharacterIndex);
        ApplySkinColor(SkinColorIndex);
        ApplyPlayerName(PlayerName.ToString());
        ConfigureAuthority();
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
        if (!HasStateAuthority)
        {
            ApplyRemoteAnimation();
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority)
        {
            CaptureAnimationState();
        }
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

    private void ConfigureAuthority()
    {
        bool isLocalPlayer = HasStateAuthority;
        mover ??= GetComponent<DuckMover>();
        if (mover != null)
        {
            mover.enabled = isLocalPlayer;
        }

        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = !isLocalPlayer;
            body.useGravity = isLocalPlayer;
        }

        if (!isLocalPlayer || Camera.main == null)
        {
            return;
        }

        RPGCameraFollow cameraFollow = Camera.main.GetComponent<RPGCameraFollow>();
        cameraFollow?.SetTarget(transform);
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
