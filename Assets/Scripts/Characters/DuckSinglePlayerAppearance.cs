using UnityEngine;

[DisallowMultipleComponent]
public sealed class DuckSinglePlayerAppearance : MonoBehaviour
{
    private const string SelectedSkinColorIndexKey = "SelectedSkinColorIndex";

    [SerializeField] private int bodyMaterialIndex = 1;

    private int appliedColorIndex = -1;
    private MaterialPropertyBlock colorProperties;

    public void Configure(string playerName, int materialIndex)
    {
        bodyMaterialIndex = Mathf.Max(0, materialIndex);
        DuckPlayerNameplate nameplate = GetComponent<DuckPlayerNameplate>();
        nameplate ??= gameObject.AddComponent<DuckPlayerNameplate>();
        nameplate.SetDisplayName(playerName);
        SetSkinColor(PlayerPrefs.GetInt(SelectedSkinColorIndexKey, 0));
    }

    public void SetSkinColor(int colorIndex)
    {
        int selectedColor = Mathf.Clamp(colorIndex, 0, FusionDuckPlayer.SkinColorCount - 1);
        PlayerPrefs.SetInt(SelectedSkinColorIndexKey, selectedColor);
        PlayerPrefs.Save();
        ApplySkinColor(selectedColor);
    }

    private void ApplySkinColor(int colorIndex)
    {
        if (appliedColorIndex == colorIndex)
        {
            return;
        }

        SkinnedMeshRenderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = renderers[i];
            if (bodyMaterialIndex < 0 || bodyMaterialIndex >= renderer.sharedMaterials.Length)
            {
                continue;
            }

            renderer.SetPropertyBlock(null, bodyMaterialIndex);
            if (colorIndex == 0)
            {
                continue;
            }

            colorProperties ??= new MaterialPropertyBlock();
            colorProperties.Clear();
            Color color = FusionDuckPlayer.GetSkinColor(colorIndex);
            colorProperties.SetColor(Shader.PropertyToID("_BaseColor"), color);
            colorProperties.SetColor(Shader.PropertyToID("_Color"), color);
            renderer.SetPropertyBlock(colorProperties, bodyMaterialIndex);
        }

        appliedColorIndex = colorIndex;
    }
}
