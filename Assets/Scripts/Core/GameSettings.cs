using System;
using UnityEngine;

/// <summary>
/// Stores player-facing options and applies them across scenes.
/// </summary>
public static class GameSettings
{
    private const string MasterVolumeKey = "Settings.MasterVolume";
    private const string MusicVolumeKey = "Settings.MusicVolume";
    private const string SfxVolumeKey = "Settings.SfxVolume";
    private const string FullscreenKey = "Settings.Fullscreen";
    private const string QualityKey = "Settings.Quality";
    private const string MouseSensitivityKey = "Settings.MouseSensitivity";

    private const float DefaultVolume = 0.8f;
    private const float DefaultSensitivity = 1f;
    private const int PerformanceQualityLevel = 0;
    private const int MinimumHighQualityGraphicsMemoryMb = 4096;
    private const int MinimumHighQualitySystemMemoryMb = 8192;
    private const int MinimumHighQualityProcessorCount = 4;

    public static event Action AudioVolumesChanged;

    public static float MasterVolume => PlayerPrefs.GetFloat(MasterVolumeKey, DefaultVolume);
    public static float MusicVolume => PlayerPrefs.GetFloat(MusicVolumeKey, DefaultVolume);
    public static float SfxVolume => PlayerPrefs.GetFloat(SfxVolumeKey, DefaultVolume);
    public static bool Fullscreen => PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) == 1;
    public static int QualityLevel => Mathf.Clamp(
        PlayerPrefs.GetInt(QualityKey, QualitySettings.GetQualityLevel()),
        0,
        Mathf.Max(0, QualitySettings.names.Length - 1));
    public static float MouseSensitivity => Mathf.Clamp(
        PlayerPrefs.GetFloat(MouseSensitivityKey, DefaultSensitivity),
        0.25f,
        2f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyOnLaunch()
    {
        SelectInitialQualityLevel();
        ApplyAll();
    }

    public static void ApplyAll()
    {
        AudioListener.volume = MasterVolume;
        Screen.fullScreen = Fullscreen;

        if (QualitySettings.names.Length > 0)
        {
            QualitySettings.SetQualityLevel(QualityLevel, true);
        }

        GraphicsQualityController.Apply(QualityLevel);

        AudioVolumesChanged?.Invoke();
    }

    public static void SetMasterVolume(float value)
    {
        value = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(MasterVolumeKey, value);
        AudioListener.volume = value;
    }

    public static void SetMusicVolume(float value)
    {
        PlayerPrefs.SetFloat(MusicVolumeKey, Mathf.Clamp01(value));
        AudioVolumesChanged?.Invoke();
    }

    public static void SetSfxVolume(float value)
    {
        PlayerPrefs.SetFloat(SfxVolumeKey, Mathf.Clamp01(value));
        AudioVolumesChanged?.Invoke();
    }

    public static void SetFullscreen(bool value)
    {
        PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0);
        Screen.fullScreen = value;
    }

    public static void SetQualityLevel(int value)
    {
        if (QualitySettings.names.Length == 0)
        {
            return;
        }

        int qualityLevel = Mathf.Clamp(value, 0, QualitySettings.names.Length - 1);
        PlayerPrefs.SetInt(QualityKey, qualityLevel);
        QualitySettings.SetQualityLevel(qualityLevel, true);
        GraphicsQualityController.Apply(qualityLevel);
    }

    private static void SelectInitialQualityLevel()
    {
        if (PlayerPrefs.HasKey(QualityKey) || QualitySettings.names.Length == 0)
        {
            return;
        }

        int qualityLevel = GetRecommendedQualityLevel();
        PlayerPrefs.SetInt(QualityKey, qualityLevel);
        Debug.Log(
            $"Auto-selected {QualitySettings.names[qualityLevel]} quality " +
            $"(GPU memory: {SystemInfo.graphicsMemorySize} MB, " +
            $"system memory: {SystemInfo.systemMemorySize} MB, " +
            $"CPU cores: {SystemInfo.processorCount}).");
    }

    private static int GetRecommendedQualityLevel()
    {
        if (QualitySettings.names.Length <= 1)
        {
            return PerformanceQualityLevel;
        }

        bool supportsHighQuality =
            SystemInfo.graphicsMemorySize >= MinimumHighQualityGraphicsMemoryMb &&
            SystemInfo.systemMemorySize >= MinimumHighQualitySystemMemoryMb &&
            SystemInfo.processorCount >= MinimumHighQualityProcessorCount;

        return supportsHighQuality
            ? QualitySettings.names.Length - 1
            : PerformanceQualityLevel;
    }

    public static void SetMouseSensitivity(float value)
    {
        PlayerPrefs.SetFloat(MouseSensitivityKey, Mathf.Clamp(value, 0.25f, 2f));
    }

    public static void Save()
    {
        PlayerPrefs.Save();
    }
}
