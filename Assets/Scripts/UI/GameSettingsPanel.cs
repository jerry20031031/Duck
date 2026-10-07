using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>The same settings and controls layout for MainMenu, BigHall and UNIT1.</summary>
public sealed class GameSettingsPanel
{
    private readonly SettingsUiTheme theme;
    private readonly GameObject settingsCard;
    private readonly GameObject controlsCard;
    private readonly Button returnButton;
    private readonly Button controlsBackButton;
    private readonly Slider masterVolume;
    private readonly Slider musicVolume;
    private readonly Slider sfxVolume;
    private readonly Slider sensitivity;
    private readonly TMP_Text masterVolumeValue;
    private readonly TMP_Text musicVolumeValue;
    private readonly TMP_Text sfxVolumeValue;
    private readonly TMP_Text sensitivityValue;
    private readonly TMP_Text fullscreenValue;
    private readonly TMP_Text qualityValue;
    private readonly UnityAction onClose;

    public GameObject Root { get; }
    public bool IsVisible => Root != null && Root.activeSelf;
    public bool IsShowingControls => controlsCard != null && controlsCard.activeSelf;

    public GameSettingsPanel(Transform parent, string returnLabel, UnityAction close)
    {
        theme = SettingsUiTheme.Shared;
        onClose = close;
        Root = CreateUiObject("Game Settings Screen", parent);
        Stretch(Root.GetComponent<RectTransform>());
        Root.AddComponent<Image>().color = SettingsUiTheme.DimmerColor;

        settingsCard = CreateCard("Settings Card");
        Transform content = CreateContent(settingsCard.transform);
        CreateText(content, "Title", "遊戲設定", new Vector2(0f, 285f), new Vector2(820f, 65f), 46f, true);
        CreateText(content, "Subtitle", "Esc 返回上一頁　｜　設定會立即套用", new Vector2(0f, 238f), new Vector2(880f, 36f), 21f);

        masterVolume = CreateSliderRow(content, "主音量", 165f, 0f, 1f, GameSettings.SetMasterVolume, false, out masterVolumeValue);
        musicVolume = CreateSliderRow(content, "音樂音量", 90f, 0f, 1f, GameSettings.SetMusicVolume, false, out musicVolumeValue);
        sfxVolume = CreateSliderRow(content, "音效音量", 15f, 0f, 1f, GameSettings.SetSfxVolume, false, out sfxVolumeValue);

        CreateText(content, "Fullscreen Label", "全螢幕", new Vector2(-390f, -60f), new Vector2(250f, 50f), 29f);
        Button fullscreenButton = CreateButton(content, "Fullscreen Option", "", new Vector2(145f, -60f), new Vector2(350f, 80f));
        fullscreenValue = fullscreenButton.GetComponentInChildren<TMP_Text>();
        fullscreenButton.onClick.AddListener(() =>
        {
            GameSettings.SetFullscreen(!GameSettings.Fullscreen);
            RefreshValues();
        });

        CreateText(content, "Quality Label", "畫質", new Vector2(-390f, -135f), new Vector2(250f, 50f), 29f);
        Button qualityButton = CreateButton(content, "Quality Option", "", new Vector2(145f, -135f), new Vector2(350f, 80f));
        qualityValue = qualityButton.GetComponentInChildren<TMP_Text>();
        qualityButton.onClick.AddListener(() =>
        {
            int count = QualitySettings.names.Length;
            if (count > 0) GameSettings.SetQualityLevel((GameSettings.QualityLevel + 1) % count);
            RefreshValues();
        });

        sensitivity = CreateSliderRow(content, "滑鼠靈敏度", -210f, 0.25f, 2f, GameSettings.SetMouseSensitivity, true, out sensitivityValue);
        Button helpButton = CreateButton(content, "Controls Help Button", "查看操作說明",
            new Vector2(-240f, -290f), new Vector2(400f, 96f));
        helpButton.onClick.AddListener(ShowControls);
        returnButton = CreateButton(content, "Return Button", returnLabel,
            new Vector2(230f, -290f), new Vector2(400f, 96f));
        returnButton.onClick.AddListener(onClose);
        RestrictNavigation(settingsCard);

        controlsCard = CreateCard("Controls Card");
        Transform controlsContent = CreateContent(controlsCard.transform);
        CreateText(controlsContent, "Title", "操作說明", new Vector2(0f, 285f), new Vector2(820f, 65f), 46f, true);
        const string instructions =
            "W A S D／方向鍵　移動\n" +
            "Shift　奔跑\n" +
            "空白鍵　跳躍\n" +
            "E　拾取武器\n" +
            "滑鼠左鍵　手杖攻擊\n" +
            "按住滑鼠右鍵並拖曳　旋轉鏡頭\n" +
            "滑鼠滾輪　拉近／拉遠鏡頭\n" +
            "R　施放符文技能（可用時）\n" +
            "Esc　開啟設定／返回上一頁";
        TMP_Text instructionsText = CreateText(controlsContent, "Instructions", instructions,
            new Vector2(0f, 8f), new Vector2(900f, 460f), 28f);
        instructionsText.alignment = TextAlignmentOptions.MidlineLeft;
        instructionsText.lineSpacing = 10f;
        controlsBackButton = CreateButton(controlsContent, "Close Controls Button", "返回設定",
            new Vector2(0f, -290f), new Vector2(400f, 96f));
        controlsBackButton.onClick.AddListener(ShowSettings);
        RestrictNavigation(controlsCard);

        controlsCard.SetActive(false);
        RefreshValues();
        Root.SetActive(false);
    }

    public void ShowSettings()
    {
        RefreshValues();
        Root.SetActive(true);
        settingsCard.SetActive(true);
        controlsCard.SetActive(false);
        EventSystem.current?.SetSelectedGameObject(returnButton.gameObject);
    }

    public void ShowControls()
    {
        Root.SetActive(true);
        settingsCard.SetActive(false);
        controlsCard.SetActive(true);
        EventSystem.current?.SetSelectedGameObject(controlsBackButton.gameObject);
    }

    public void Hide() => Root.SetActive(false);

    public void HandleBack()
    {
        if (IsShowingControls) ShowSettings();
        else onClose?.Invoke();
    }

    public void RefreshValues()
    {
        masterVolume.SetValueWithoutNotify(GameSettings.MasterVolume);
        musicVolume.SetValueWithoutNotify(GameSettings.MusicVolume);
        sfxVolume.SetValueWithoutNotify(GameSettings.SfxVolume);
        sensitivity.SetValueWithoutNotify(GameSettings.MouseSensitivity);
        masterVolumeValue.text = FormatValue(GameSettings.MasterVolume, false);
        musicVolumeValue.text = FormatValue(GameSettings.MusicVolume, false);
        sfxVolumeValue.text = FormatValue(GameSettings.SfxVolume, false);
        sensitivityValue.text = FormatValue(GameSettings.MouseSensitivity, true);
        fullscreenValue.text = GameSettings.Fullscreen ? "開啟" : "關閉";
        qualityValue.text = GetQualityDisplayName();
    }

    public static Button CreateShortcutButton(Transform parent, UnityAction open)
    {
        Button button = CreateThemedButton(SettingsUiTheme.Shared, parent, "Settings Shortcut Hint", "Esc　設定",
            Vector2.zero, new Vector2(300f, 78f));
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-28f, -20f);
        button.GetComponentInChildren<TMP_Text>().fontSize = 22f;
        // The hint is clickable, but keyboard focus stays inside the open page.
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(open);
        return button;
    }

    private GameObject CreateCard(string name)
    {
        GameObject card = CreateUiObject(name, Root.transform);
        SetCenteredRect(card.GetComponent<RectTransform>(), new Vector2(1560f, 990f), Vector2.zero);
        RawImage frame = card.AddComponent<RawImage>();
        frame.texture = theme != null ? theme.PanelTexture : Texture2D.whiteTexture;
        frame.color = theme != null ? Color.white : new Color(0.94f, 0.84f, 0.65f, 1f);
        frame.raycastTarget = false;
        return card;
    }

    private static Transform CreateContent(Transform card)
    {
        GameObject content = CreateUiObject("Content", card);
        // Keep the upper-right crown decoration clear of labels and controls.
        SetCenteredRect(content.GetComponent<RectTransform>(), new Vector2(1120f, 670f), new Vector2(-70f, 0f));
        return content.transform;
    }

    private Slider CreateSliderRow(Transform parent, string label, float y, float minimum, float maximum,
        UnityAction<float> onChanged, bool multiplier, out TMP_Text valueText)
    {
        CreateText(parent, label + " Label", label, new Vector2(-390f, y), new Vector2(290f, 50f), 29f);
        GameObject sliderObject = CreateUiObject(label + " Slider", parent);
        SetCenteredRect(sliderObject.GetComponent<RectTransform>(), new Vector2(430f, 44f), new Vector2(110f, y));

        Image background = CreateImage("Background", sliderObject.transform, SettingsUiTheme.SliderBackgroundColor);
        Stretch(background.rectTransform, 0f, 0f, 15f, 15f);
        GameObject fillArea = CreateUiObject("Fill Area", sliderObject.transform);
        Stretch(fillArea.GetComponent<RectTransform>(), 12f, 12f, 15f, 15f);
        Image fill = CreateImage("Fill", fillArea.transform, SettingsUiTheme.SliderFillColor);
        Stretch(fill.rectTransform);
        GameObject handleArea = CreateUiObject("Handle Slide Area", sliderObject.transform);
        Stretch(handleArea.GetComponent<RectTransform>(), 15f, 15f);
        Image handle = CreateImage("Handle", handleArea.transform, SettingsUiTheme.SliderHandleColor);
        SetCenteredRect(handle.rectTransform, new Vector2(30f, 44f), Vector2.zero);
        Outline border = handle.gameObject.AddComponent<Outline>();
        border.effectColor = new Color(0.43f, 0.25f, 0.11f, 0.95f);
        border.effectDistance = new Vector2(2f, -2f);

        Slider slider = sliderObject.AddComponent<Slider>();
        slider.minValue = minimum;
        slider.maxValue = maximum;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        SettingsUiTheme.ApplyButtonColors(slider);

        TMP_Text display = CreateText(parent, label + " Value", "", new Vector2(405f, y), new Vector2(140f, 50f), 25f);
        valueText = display;
        slider.onValueChanged.AddListener(value =>
        {
            onChanged(value);
            display.text = FormatValue(value, multiplier);
        });
        return slider;
    }

    private Button CreateButton(Transform parent, string name, string label, Vector2 position, Vector2 size)
        => CreateThemedButton(theme, parent, name, label, position, size);

    private static Button CreateThemedButton(SettingsUiTheme style, Transform parent, string name, string label,
        Vector2 position, Vector2 size)
    {
        GameObject buttonObject = CreateUiObject(name, parent);
        SetCenteredRect(buttonObject.GetComponent<RectTransform>(), size, position);
        RawImage image = buttonObject.AddComponent<RawImage>();
        image.texture = style != null ? style.ButtonTexture : Texture2D.whiteTexture;
        image.color = style != null ? Color.white : SettingsUiTheme.SliderHandleColor;
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        SettingsUiTheme.ApplyButtonColors(button);
        CreateThemedText(style, buttonObject.transform, "Label", label, Vector2.zero,
            new Vector2(size.x * 0.80f, size.y * 0.55f), 27f, true);
        return button;
    }

    private TMP_Text CreateText(Transform parent, string name, string value, Vector2 position, Vector2 dimensions,
        float fontSize, bool bold = false)
        => CreateThemedText(theme, parent, name, value, position, dimensions, fontSize, bold);

    private static TMP_Text CreateThemedText(SettingsUiTheme style, Transform parent, string name, string value,
        Vector2 position, Vector2 dimensions, float fontSize, bool bold)
    {
        GameObject textObject = CreateUiObject(name, parent);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = style != null ? style.Font : TMP_Settings.defaultFontAsset;
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        text.color = bold ? SettingsUiTheme.TitleColor : SettingsUiTheme.LabelColor;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        SetCenteredRect(text.rectTransform, dimensions, position);
        return text;
    }

    private static string FormatValue(float value, bool multiplier)
        => multiplier ? value.ToString("0.00") + "x" : Mathf.RoundToInt(value * 100f) + "%";

    private static string GetQualityDisplayName()
    {
        int count = QualitySettings.names.Length;
        if (count == 0) return "無可用選項";
        if (count == 1) return "預設";
        float quality = GameSettings.QualityLevel / (float)(count - 1);
        if (quality <= 0.2f) return "最低";
        if (quality <= 0.4f) return "低";
        if (quality <= 0.65f) return "中";
        if (quality <= 0.85f) return "高";
        return "最高";
    }

    private static void RestrictNavigation(GameObject card)
    {
        Selectable[] items = card.GetComponentsInChildren<Selectable>();
        for (int i = 0; i < items.Length; i++)
        {
            items[i].navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = items[(i + items.Length - 1) % items.Length],
                selectOnDown = items[(i + 1) % items.Length]
            };
        }
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        Image image = CreateUiObject(name, parent).AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject uiObject = new(name, typeof(RectTransform));
        uiObject.layer = 5;
        uiObject.transform.SetParent(parent, false);
        return uiObject;
    }

    private static void SetCenteredRect(RectTransform rect, Vector2 size, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float bottom = 0f, float top = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }
}
