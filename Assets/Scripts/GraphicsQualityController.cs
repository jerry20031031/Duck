using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Applies the performance preset to scene-specific camera options that are not
/// stored by Unity's per-quality-level settings.
/// </summary>
public static class GraphicsQualityController
{
    private const int PerformanceQualityLevel = 0;
    private const int PerformanceTargetFrameRate = 30;
    private const int HighQualityTargetFrameRate = 60;
    private const float PerformanceFarClipDistance = 250f;

    private static readonly Dictionary<int, CameraDefaults> DefaultCameraSettings = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneCallback()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public static void Apply(int qualityLevel)
    {
        bool isPerformanceQuality = qualityLevel <= PerformanceQualityLevel;
        Application.targetFrameRate = isPerformanceQuality
            ? PerformanceTargetFrameRate
            : HighQualityTargetFrameRate;

        foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            ApplyToCamera(camera, isPerformanceQuality);
        }
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        DefaultCameraSettings.Clear();
        Apply(QualitySettings.GetQualityLevel());
    }

    private static void ApplyToCamera(Camera camera, bool isPerformanceQuality)
    {
        int cameraId = camera.GetInstanceID();
        if (!DefaultCameraSettings.TryGetValue(cameraId, out CameraDefaults defaults))
        {
            UniversalAdditionalCameraData additionalData = camera.GetComponent<UniversalAdditionalCameraData>();
            defaults = new CameraDefaults(
                camera.farClipPlane,
                camera.allowHDR,
                camera.allowMSAA,
                additionalData != null && additionalData.renderShadows,
                additionalData != null && additionalData.renderPostProcessing);
            DefaultCameraSettings.Add(cameraId, defaults);
        }

        UniversalAdditionalCameraData urpCameraData = camera.GetComponent<UniversalAdditionalCameraData>();
        if (isPerformanceQuality)
        {
            camera.farClipPlane = Mathf.Min(defaults.FarClipPlane, PerformanceFarClipDistance);
            camera.allowHDR = false;
            camera.allowMSAA = false;

            if (urpCameraData != null)
            {
                urpCameraData.renderShadows = false;
                urpCameraData.renderPostProcessing = false;
            }

            return;
        }

        camera.farClipPlane = defaults.FarClipPlane;
        camera.allowHDR = defaults.AllowHdr;
        camera.allowMSAA = defaults.AllowMsaa;

        if (urpCameraData != null)
        {
            urpCameraData.renderShadows = defaults.RenderShadows;
            urpCameraData.renderPostProcessing = defaults.RenderPostProcessing;
        }
    }

    private readonly struct CameraDefaults
    {
        public readonly float FarClipPlane;
        public readonly bool AllowHdr;
        public readonly bool AllowMsaa;
        public readonly bool RenderShadows;
        public readonly bool RenderPostProcessing;

        public CameraDefaults(
            float farClipPlane,
            bool allowHdr,
            bool allowMsaa,
            bool renderShadows,
            bool renderPostProcessing)
        {
            FarClipPlane = farClipPlane;
            AllowHdr = allowHdr;
            AllowMsaa = allowMsaa;
            RenderShadows = renderShadows;
            RenderPostProcessing = renderPostProcessing;
        }
    }
}
