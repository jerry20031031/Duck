using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class MainMenuController : MonoBehaviour
{
    private const string StartButtonName = "Start Game Button";
    private const string SettingsButtonName = "Settings Button";
    private const string QuitButtonName = "Quit Game Button";

    [SerializeField] private string gameSceneName = "BigHall";
    [SerializeField] private AudioClip backgroundMusic;
    [SerializeField, Range(0f, 1f)] private float backgroundMusicVolume = 0.45f;

    private GameObject settingsPanel;
    private GameObject controlsPanel;
    private GameObject[] mainMenuObjects;
    private Button woodButtonTemplate;
    private TMP_Text textTemplate;
    private TMP_Text fullscreenValueText;
    private TMP_Text qualityValueText;
    private bool settingsVisible;
    private bool controlsVisible;

    private static readonly Color PanelColor = new(0.94f, 0.84f, 0.65f, 0.98f);
    private static readonly Color LabelColor = new(0.16f, 0.10f, 0.06f, 1f);
    private static readonly Color SliderBackgroundColor = new(0.28f, 0.18f, 0.10f, 0.72f);
    private static readonly Color SliderFillColor = new(0.89f, 0.55f, 0.17f, 1f);
    private static readonly Color SliderHandleColor = new(1f, 0.91f, 0.68f, 1f);

    private void Awake()
    {
        GameSettings.ApplyAll();
        PlayBackgroundMusic();
        CacheMainMenuObjects();
        WireButton(StartButtonName, StartGame);
        WireButton(SettingsButtonName, OpenSettings);
        WireButton(QuitButtonName, QuitGame);
        BuildSettingsInterface();
    }

    private void PlayBackgroundMusic()
    {
        if (backgroundMusic == null)
        {
            Debug.LogWarning("Main menu background music is not assigned.");
            return;
        }

        AudioSource source = GetComponent<AudioSource>();
        if (source == null)
        {
            source = gameObject.AddComponent<AudioSource>();
        }

        source.clip = backgroundMusic;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = backgroundMusicVolume;

        AudioChannelVolume channelVolume = GetComponent<AudioChannelVolume>();
        if (channelVolume == null)
        {
            channelVolume = gameObject.AddComponent<AudioChannelVolume>();
        }

        channelVolume.SetChannel(GameAudioChannel.Music);
        channelVolume.SetSourceVolume(backgroundMusicVolume);
        source.Play();
    }

    private void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            return;
        }

        if (controlsVisible)
        {
            CloseControls();
        }
        else if (settingsVisible)
        {
            CloseSettings();
        }
    }

    public void StartGame()
    {
        GameSettings.Save();
        SceneManager.LoadScene(gameSceneName);
    }

    public void OpenSettings()
    {
        if (settingsPanel == null)
        {
            return;
        }

        SetMainMenuVisible(false);
        settingsVisible = true;
        settingsPanel.SetActive(true);
        SelectFirstButton(settingsPanel);
    }

    public void CloseSettings()
    {
        GameSettings.Save();
        settingsVisible = false;
        settingsPanel.SetActive(false);
        SetMainMenuVisible(true);

        GameObject settingsButton = GameObject.Find(SettingsButtonName);
        EventSystem.current?.SetSelectedGameObject(settingsButton);
    }

    public void OpenControls()
    {
        settingsVisible = false;
        controlsVisible = true;
        settingsPanel.SetActive(false);
        controlsPanel.SetActive(true);
        SelectFirstButton(controlsPanel);
    }

    public void CloseControls()
    {
        controlsVisible = false;
        settingsVisible = true;
        controlsPanel.SetActive(false);
        settingsPanel.SetActive(true);
        SelectFirstButton(settingsPanel);
    }

    public void QuitGame()
    {
        GameSettings.Save();
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void CacheMainMenuObjects()
    {
        GameObject settingsObject = GameObject.Find(SettingsButtonName);
        woodButtonTemplate = settingsObject != null ? settingsObject.GetComponent<Button>() : null;
        textTemplate = settingsObject != null ? settingsObject.GetComponentInChildren<TMP_Text>(true) : null;

        mainMenuObjects = new[]
        {
            GameObject.Find("Title"),
            GameObject.Find(StartButtonName),
            settingsObject,
            GameObject.Find(QuitButtonName)
        };
    }

    private void BuildSettingsInterface()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null || woodButtonTemplate == null || textTemplate == null)
        {
            Debug.LogError("The main menu needs a Canvas, wood button template, and TMP text template.");
            return;
        }

        settingsPanel = CreateScreen("Settings Screen", canvas.transform);
        CreatePanelBackdrop(settingsPanel.transform, new Vector2(1040f, 970f));
        CreateText(settingsPanel.transform, "遊戲設定", new Vector2(0f, 400f), new Vector2(720f, 80f), 46f);

        CreateSliderRow(settingsPanel.transform, "主音量", 285f, 0f, 1f, GameSettings.MasterVolume,
            value => GameSettings.SetMasterVolume(value), false);
        CreateSliderRow(settingsPanel.transform, "音樂音量", 185f, 0f, 1f, GameSettings.MusicVolume,
            value => GameSettings.SetMusicVolume(value), false);
        CreateSliderRow(settingsPanel.transform, "音效音量", 85f, 0f, 1f, GameSettings.SfxVolume,
            value => GameSettings.SetSfxVolume(value), false);

        CreateText(settingsPanel.transform, "全螢幕", new Vector2(-310f, -25f), new Vector2(260f, 60f), 30f);
        Button fullscreenButton = CreateWoodButton(settingsPanel.transform, "Fullscreen Option", string.Empty,
            new Vector2(210f, -25f), new Vector2(330f, 66f), ToggleFullscreen);
        fullscreenValueText = fullscreenButton.GetComponentInChildren<TMP_Text>();
        UpdateFullscreenText();

        CreateText(settingsPanel.transform, "畫質", new Vector2(-310f, -125f), new Vector2(260f, 60f), 30f);
        Button qualityButton = CreateWoodButton(settingsPanel.transform, "Quality Option", string.Empty,
            new Vector2(210f, -125f), new Vector2(330f, 66f), CycleQuality);
        qualityValueText = qualityButton.GetComponentInChildren<TMP_Text>();
        UpdateQualityText();

        CreateSliderRow(settingsPanel.transform, "滑鼠靈敏度", -225f, 0.25f, 2f, GameSettings.MouseSensitivity,
            value => GameSettings.SetMouseSensitivity(value), true);

        CreateWoodButton(settingsPanel.transform, "Controls Help Button", "查看操作說明",
            new Vector2(-225f, -365f), new Vector2(400f, 72f), OpenControls);
        CreateWoodButton(settingsPanel.transform, "Back To Main Menu Button", "返回主選單",
            new Vector2(225f, -365f), new Vector2(400f, 72f), CloseSettings);
        settingsPanel.SetActive(false);

        controlsPanel = CreateScreen("Controls Screen", canvas.transform);
        CreatePanelBackdrop(controlsPanel.transform, new Vector2(940f, 800f));
        CreateText(controlsPanel.transform, "操作說明", new Vector2(0f, 300f), new Vector2(720f, 80f), 46f);

        const string instructions =
            "W A S D／方向鍵　移動\n" +
            "Shift　奔跑\n" +
            "空白鍵　跳躍\n" +
            "E　拾取武器\n" +
            "按住滑鼠右鍵並拖曳　旋轉鏡頭\n" +
            "滑鼠滾輪　拉近／拉遠鏡頭\n" +
            "R　施放符文技能（可用時）\n" +
            "Esc　返回上一頁";
        TMP_Text helpText = CreateText(
            controlsPanel.transform,
            instructions,
            new Vector2(0f, 20f),
            new Vector2(760f, 450f),
            30f);
        helpText.alignment = TextAlignmentOptions.MidlineLeft;
        helpText.lineSpacing = 18f;

        CreateWoodButton(controlsPanel.transform, "Close Controls Button", "返回設定",
            new Vector2(0f, -305f), new Vector2(400f, 72f), CloseControls);
        controlsPanel.SetActive(false);
    }

    private GameObject CreateScreen(string screenName, Transform parent)
    {
        GameObject screen = CreateUiObject(screenName, parent);
        RectTransform rect = screen.GetComponent<RectTransform>();
        Stretch(rect);

        RawImage dimmer = screen.AddComponent<RawImage>();
        dimmer.texture = Texture2D.whiteTexture;
        dimmer.color = new Color(0.03f, 0.02f, 0.01f, 0.68f);
        dimmer.raycastTarget = true;
        return screen;
    }

    private void CreatePanelBackdrop(Transform parent, Vector2 size)
    {
        GameObject panel = CreateUiObject("Parchment Panel", parent);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;

        RawImage image = panel.AddComponent<RawImage>();
        image.texture = Texture2D.whiteTexture;
        image.color = PanelColor;
        image.raycastTarget = false;
    }

    private void CreateSliderRow(
        Transform parent,
        string label,
        float y,
        float minimum,
        float maximum,
        float current,
        UnityEngine.Events.UnityAction<float> onChanged,
        bool multiplier)
    {
        CreateText(parent, label, new Vector2(-310f, y), new Vector2(300f, 60f), 30f);

        GameObject sliderObject = CreateUiObject(label + " Slider", parent);
        RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
        sliderRect.anchorMin = sliderRect.anchorMax = sliderRect.pivot = new Vector2(0.5f, 0.5f);
        sliderRect.anchoredPosition = new Vector2(165f, y);
        sliderRect.sizeDelta = new Vector2(420f, 48f);

        Image background = CreateImage("Background", sliderObject.transform, SliderBackgroundColor);
        Stretch(background.rectTransform, new Vector2(0f, 17f), new Vector2(0f, -17f));

        GameObject fillAreaObject = CreateUiObject("Fill Area", sliderObject.transform);
        RectTransform fillArea = fillAreaObject.GetComponent<RectTransform>();
        Stretch(fillArea, new Vector2(12f, 17f), new Vector2(-12f, -17f));
        Image fill = CreateImage("Fill", fillArea, SliderFillColor);
        Stretch(fill.rectTransform);

        GameObject handleAreaObject = CreateUiObject("Handle Slide Area", sliderObject.transform);
        RectTransform handleArea = handleAreaObject.GetComponent<RectTransform>();
        Stretch(handleArea, new Vector2(14f, 0f), new Vector2(-14f, 0f));
        Image handle = CreateImage("Handle", handleArea, SliderHandleColor);
        RectTransform handleRect = handle.rectTransform;
        handleRect.anchorMin = handleRect.anchorMax = new Vector2(0f, 0.5f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.sizeDelta = new Vector2(30f, 48f);

        Slider slider = sliderObject.AddComponent<Slider>();
        slider.minValue = minimum;
        slider.maxValue = maximum;
        slider.wholeNumbers = false;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handleRect;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;

        TMP_Text valueText = CreateText(parent, string.Empty, new Vector2(425f, y), new Vector2(135f, 55f), 25f);
        slider.SetValueWithoutNotify(current);
        UpdateSliderValueText(valueText, current, multiplier);
        slider.onValueChanged.AddListener(value =>
        {
            onChanged.Invoke(value);
            UpdateSliderValueText(valueText, value, multiplier);
        });
    }

    private Button CreateWoodButton(
        Transform parent,
        string objectName,
        string label,
        Vector2 position,
        Vector2 size,
        UnityEngine.Events.UnityAction action)
    {
        Button button = Instantiate(woodButtonTemplate, parent);
        button.name = objectName;
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
        text.text = label;
        text.fontSize = 28f;
        text.alignment = TextAlignmentOptions.Center;
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = Vector2.zero;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
        return button;
    }

    private TMP_Text CreateText(
        Transform parent,
        string value,
        Vector2 position,
        Vector2 size,
        float fontSize)
    {
        TMP_Text text = Instantiate(textTemplate, parent);
        text.name = string.IsNullOrEmpty(value) ? "Value" : value + " Text";
        text.text = value;
        text.fontSize = fontSize;
        text.color = LabelColor;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;

        RectTransform rect = text.rectTransform;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return text;
    }

    private static Image CreateImage(string objectName, Transform parent, Color color)
    {
        GameObject imageObject = CreateUiObject(objectName, parent);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static GameObject CreateUiObject(string objectName, Transform parent)
    {
        GameObject uiObject = new(objectName, typeof(RectTransform));
        uiObject.layer = 5;
        uiObject.transform.SetParent(parent, false);
        return uiObject;
    }

    private static void Stretch(RectTransform rect, Vector2 offsetMin = default, Vector2 offsetMax = default)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static void UpdateSliderValueText(TMP_Text valueText, float value, bool multiplier)
    {
        valueText.text = multiplier
            ? $"{value:0.00}x"
            : $"{Mathf.RoundToInt(value * 100f)}%";
    }

    private void ToggleFullscreen()
    {
        GameSettings.SetFullscreen(!GameSettings.Fullscreen);
        UpdateFullscreenText();
    }

    private void UpdateFullscreenText()
    {
        if (fullscreenValueText != null)
        {
            fullscreenValueText.text = GameSettings.Fullscreen ? "開啟" : "關閉";
        }
    }

    private void CycleQuality()
    {
        int qualityCount = QualitySettings.names.Length;
        if (qualityCount == 0)
        {
            return;
        }

        GameSettings.SetQualityLevel((GameSettings.QualityLevel + 1) % qualityCount);
        UpdateQualityText();
    }

    private void UpdateQualityText()
    {
        if (qualityValueText == null)
        {
            return;
        }

        string[] names = QualitySettings.names;
        qualityValueText.text = names.Length == 0
            ? "無可用選項"
            : GetQualityDisplayName(GameSettings.QualityLevel, names.Length);
    }

    private static string GetQualityDisplayName(int level, int count)
    {
        if (count <= 1)
        {
            return "預設";
        }

        float quality = level / (float)(count - 1);
        if (quality <= 0.2f)
        {
            return "最低";
        }

        if (quality <= 0.4f)
        {
            return "低";
        }

        if (quality <= 0.65f)
        {
            return "中";
        }

        if (quality <= 0.85f)
        {
            return "高";
        }

        return "最高";
    }

    private void SetMainMenuVisible(bool visible)
    {
        foreach (GameObject menuObject in mainMenuObjects)
        {
            if (menuObject != null)
            {
                menuObject.SetActive(visible);
            }
        }
    }

    private static void SelectFirstButton(GameObject panel)
    {
        Button firstButton = panel != null ? panel.GetComponentInChildren<Button>(true) : null;
        EventSystem.current?.SetSelectedGameObject(firstButton != null ? firstButton.gameObject : null);
    }

    private static void WireButton(string buttonName, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = GameObject.Find(buttonName);
        if (buttonObject == null)
        {
            Debug.LogWarning("Missing main menu button: " + buttonName);
            return;
        }

        Button button = buttonObject.GetComponent<Button>();
        if (button == null)
        {
            Debug.LogWarning(buttonName + " needs a Button component.");
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }
}
