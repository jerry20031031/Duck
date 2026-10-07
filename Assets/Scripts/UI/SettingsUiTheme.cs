using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared settings artwork and colors, using the existing lobby UI assets.</summary>
[CreateAssetMenu(menuName = "Duck/UI/Settings Theme")]
public sealed class SettingsUiTheme : ScriptableObject
{
    [SerializeField] private Texture2D panelTexture;
    [SerializeField] private Texture2D buttonTexture;
    [SerializeField] private TMP_FontAsset font;

    private static SettingsUiTheme cachedTheme;
    public static SettingsUiTheme Shared => cachedTheme != null
        ? cachedTheme
        : cachedTheme = Resources.Load<SettingsUiTheme>("SettingsUiTheme");

    public Texture2D PanelTexture => panelTexture;
    public Texture2D ButtonTexture => buttonTexture;
    public TMP_FontAsset Font => font != null ? font : TMP_Settings.defaultFontAsset;

    public static readonly Color DimmerColor = new(0.03f, 0.02f, 0.01f, 0.68f);
    public static readonly Color TitleColor = new(0.29f, 0.15f, 0.065f, 1f);
    public static readonly Color LabelColor = new(0.36f, 0.21f, 0.10f, 1f);
    public static readonly Color SliderBackgroundColor = new(0.28f, 0.18f, 0.10f, 0.72f);
    public static readonly Color SliderFillColor = new(0.89f, 0.55f, 0.17f, 1f);
    public static readonly Color SliderHandleColor = new(1f, 0.91f, 0.68f, 1f);

    public static void ApplyButtonColors(Selectable selectable)
    {
        ColorBlock colors = selectable.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.87f, 0.62f, 1f);
        colors.pressedColor = new Color(0.86f, 0.66f, 0.4f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.55f, 0.48f, 0.4f, 0.72f);
        selectable.colors = colors;
    }
}
