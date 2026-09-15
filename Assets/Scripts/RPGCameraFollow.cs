using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class RPGCameraFollow : MonoBehaviour
{
    private const int MaxCollisionHits = 32;

    [Header("Target")]
    [Tooltip("The characters this camera follows.")]
    [SerializeField] private Transform[] targets;
    [Tooltip("If no target is assigned, automatically follow every DuckMover in the scene.")]
    [SerializeField] private bool autoFindDuckMovers = true;
    [Tooltip("Where the camera looks, relative to the followed character.")]
    [SerializeField] private Vector3 focusOffset = new Vector3(0f, 1.35f, 0f);

    [Header("View Feel")]
    [Tooltip("Default distance behind the character.")]
    [Range(3f, 10f)]
    [SerializeField] private float followDistance = 5.5f;
    [Tooltip("Default camera height. Lower feels more natural; higher feels more tactical.")]
    [Range(2f, 6f)]
    [SerializeField] private float followHeight = 3.2f;
    [Tooltip("Starting orbit angle around the target.")]
    [Range(-180f, 180f)]
    [SerializeField] private float yawAngle = 0f;
    [Tooltip("Extra space when following a group.")]
    [Range(0f, 3f)]
    [SerializeField] private float groupPadding = 1.2f;
    [Tooltip("Maximum group zoom-out distance.")]
    [Range(0f, 5f)]
    [SerializeField] private float maxGroupZoomOut = 2.5f;
    [Tooltip("Higher is softer. Around 0.2 feels natural for character following.")]
    [Range(0.05f, 0.6f)]
    [SerializeField] private float positionSmoothTime = 0.22f;
    [Tooltip("Higher snaps the camera direction faster.")]
    [Range(3f, 15f)]
    [SerializeField] private float rotationSharpness = 7f;
    [Tooltip("A wider field of view reduces the zoomed-in, dizzy feeling.")]
    [Range(40f, 65f)]
    [SerializeField] private float fieldOfView = 52f;

    [Header("Player Controls")]
    [SerializeField] private bool cameraControlsEnabled = true;
    [SerializeField] private bool rotateWithRightMouse = true;
    [Tooltip("Off by default because E is used for pickup.")]
    [SerializeField] private bool rotateWithKeyboard = false;
    [SerializeField] private bool zoomWithMouseWheel = true;
    [Range(0.02f, 0.2f)]
    [SerializeField] private float mouseYawSensitivity = 0.08f;
    [Range(0.005f, 0.05f)]
    [SerializeField] private float mouseHeightSensitivity = 0.018f;
    [Range(30f, 180f)]
    [SerializeField] private float keyboardYawSpeed = 75f;
    [Range(0.5f, 4f)]
    [SerializeField] private float zoomSpeed = 1.25f;
    [Range(2f, 8f)]
    [SerializeField] private float minFollowDistance = 4f;
    [Range(5f, 14f)]
    [SerializeField] private float maxFollowDistance = 8.5f;
    [Range(1.5f, 5f)]
    [SerializeField] private float minFollowHeight = 2.2f;
    [Range(3f, 8f)]
    [SerializeField] private float maxFollowHeight = 4.8f;

    [Header("Visibility")]
    [Tooltip("Fade objects between the camera and the character.")]
    [SerializeField] private bool fadeBlockingObjects = true;
    [SerializeField] private LayerMask blockingObjectLayers = ~0;
    [Range(0.1f, 0.8f)]
    [SerializeField] private float visibilityCheckRadius = 0.3f;
    [Range(0.15f, 0.75f)]
    [SerializeField] private float blockingObjectAlpha = 0.4f;
    [Range(2f, 12f)]
    [SerializeField] private float blockingObjectFadeSpeed = 8f;

    private Camera attachedCamera;
    private Vector3 currentVelocity;
    private readonly RaycastHit[] visibilityHits = new RaycastHit[MaxCollisionHits];
    private readonly Dictionary<Renderer, FadedRenderer> fadedRenderers = new Dictionary<Renderer, FadedRenderer>();
    private readonly HashSet<Renderer> blockedRenderersThisFrame = new HashSet<Renderer>();
    private readonly List<Renderer> renderersToRestore = new List<Renderer>();
    private float heightDistanceRatio = 0.5f;

    private void Awake()
    {
        attachedCamera = GetComponent<Camera>();
        heightDistanceRatio = followDistance > 0.001f ? followHeight / followDistance : heightDistanceRatio;
        ApplyCameraSettings();
        RefreshTargetsIfNeeded();
        SnapToRPGView();
    }

    private void LateUpdate()
    {
        RefreshTargetsIfNeeded();
        UpdateCameraControls();

        if (!TryGetFocusPoint(out Vector3 focusPoint, out float groupRadius))
        {
            RestoreFadedRenderers();
            return;
        }

        float extraDistance = Mathf.Min(groupRadius + groupPadding, maxGroupZoomOut);
        Vector3 desiredPosition = GetCameraPosition(focusPoint, extraDistance);
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref currentVelocity,
            positionSmoothTime);

        Quaternion desiredRotation = Quaternion.LookRotation(focusPoint - transform.position, Vector3.up);
        float rotationT = 1f - Mathf.Exp(-rotationSharpness * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationT);

        UpdateBlockingObjectFade(focusPoint);
        ApplyCameraSettings();
    }

    private void OnDisable()
    {
        RestoreFadedRenderers();
    }

    private void UpdateCameraControls()
    {
        if (!cameraControlsEnabled)
        {
            return;
        }

        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;

        if (rotateWithRightMouse && mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 mouseDelta = mouse.delta.ReadValue();
            yawAngle += mouseDelta.x * mouseYawSensitivity;
            followHeight = Mathf.Clamp(followHeight - mouseDelta.y * mouseHeightSensitivity, minFollowHeight, maxFollowHeight);
            heightDistanceRatio = followHeight / Mathf.Max(followDistance, 0.001f);
        }

        if (rotateWithKeyboard && keyboard != null)
        {
            float yawInput = 0f;
            if (keyboard.qKey.isPressed)
            {
                yawInput -= 1f;
            }

            if (keyboard.eKey.isPressed)
            {
                yawInput += 1f;
            }

            yawAngle += yawInput * keyboardYawSpeed * Time.deltaTime;
        }

        if (zoomWithMouseWheel && mouse != null)
        {
            float scrollSteps = mouse.scroll.ReadValue().y / 120f;
            if (Mathf.Abs(scrollSteps) > 0.001f)
            {
                followDistance = Mathf.Clamp(followDistance - scrollSteps * zoomSpeed, minFollowDistance, maxFollowDistance);
                followHeight = Mathf.Clamp(followDistance * heightDistanceRatio, minFollowHeight, maxFollowHeight);
            }
        }
    }

    private void OnValidate()
    {
        followDistance = Mathf.Max(1f, followDistance);
        followHeight = Mathf.Max(1f, followHeight);
        groupPadding = Mathf.Max(0f, groupPadding);
        maxGroupZoomOut = Mathf.Max(0f, maxGroupZoomOut);
        positionSmoothTime = Mathf.Max(0.01f, positionSmoothTime);
        rotationSharpness = Mathf.Max(1f, rotationSharpness);
        fieldOfView = Mathf.Clamp(fieldOfView, 30f, 70f);
        mouseYawSensitivity = Mathf.Max(0.01f, mouseYawSensitivity);
        mouseHeightSensitivity = Mathf.Max(0.001f, mouseHeightSensitivity);
        keyboardYawSpeed = Mathf.Max(1f, keyboardYawSpeed);
        zoomSpeed = Mathf.Max(0.1f, zoomSpeed);
        minFollowDistance = Mathf.Max(1f, minFollowDistance);
        maxFollowDistance = Mathf.Max(minFollowDistance, maxFollowDistance);
        minFollowHeight = Mathf.Max(0.5f, minFollowHeight);
        maxFollowHeight = Mathf.Max(minFollowHeight, maxFollowHeight);
        followDistance = Mathf.Clamp(followDistance, minFollowDistance, maxFollowDistance);
        followHeight = Mathf.Clamp(followHeight, minFollowHeight, maxFollowHeight);
        visibilityCheckRadius = Mathf.Max(0.05f, visibilityCheckRadius);
        blockingObjectAlpha = Mathf.Clamp(blockingObjectAlpha, 0.05f, 1f);
        blockingObjectFadeSpeed = Mathf.Max(0.1f, blockingObjectFadeSpeed);
    }

    private void ApplyCameraSettings()
    {
        if (attachedCamera != null)
        {
            attachedCamera.fieldOfView = fieldOfView;
        }
    }

    private void RefreshTargetsIfNeeded()
    {
        if (!autoFindDuckMovers || HasAnyTarget())
        {
            return;
        }

        DuckMover[] movers = FindObjectsByType<DuckMover>(FindObjectsSortMode.None);
        targets = new Transform[movers.Length];

        for (int i = 0; i < movers.Length; i++)
        {
            targets[i] = movers[i].transform;
        }
    }

    private bool HasAnyTarget()
    {
        if (targets == null)
        {
            return false;
        }

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetFocusPoint(out Vector3 focusPoint, out float groupRadius)
    {
        focusPoint = Vector3.zero;
        groupRadius = 0f;

        if (targets == null || targets.Length == 0)
        {
            return false;
        }

        int targetCount = 0;
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null)
            {
                continue;
            }

            focusPoint += targets[i].position;
            targetCount++;
        }

        if (targetCount == 0)
        {
            return false;
        }

        focusPoint = focusPoint / targetCount + focusOffset;

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null)
            {
                continue;
            }

            float distance = Vector3.Distance(focusPoint, targets[i].position + focusOffset);
            groupRadius = Mathf.Max(groupRadius, distance);
        }

        return true;
    }

    private Vector3 GetCameraPosition(Vector3 focusPoint, float extraDistance)
    {
        Vector3 horizontalOffset = Quaternion.Euler(0f, yawAngle, 0f) * Vector3.back;
        return focusPoint + horizontalOffset * (followDistance + extraDistance) + Vector3.up * followHeight;
    }

    private void UpdateBlockingObjectFade(Vector3 focusPoint)
    {
        blockedRenderersThisFrame.Clear();

        if (!fadeBlockingObjects)
        {
            RestoreFadedRenderers();
            return;
        }

        HideBlockingObjectsBetween(transform.position, focusPoint);

        if (targets == null)
        {
            return;
        }

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null)
            {
                HideBlockingObjectsBetween(transform.position, targets[i].position + focusOffset);
            }
        }

        UpdateFadedRenderers();
    }

    private void HideBlockingObjectsBetween(Vector3 cameraPosition, Vector3 targetPosition)
    {
        Vector3 cameraToTarget = targetPosition - cameraPosition;
        float targetDistance = cameraToTarget.magnitude;
        if (targetDistance <= 0.001f)
        {
            return;
        }

        Vector3 direction = cameraToTarget / targetDistance;
        int hitCount = Physics.SphereCastNonAlloc(
            cameraPosition,
            visibilityCheckRadius,
            direction,
            visibilityHits,
            targetDistance,
            blockingObjectLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            if (visibilityHits[i].collider == null || IsTargetCollider(visibilityHits[i].collider.transform))
            {
                continue;
            }

            MarkRenderersOn(visibilityHits[i].collider.transform);
        }
    }

    private void MarkRenderersOn(Transform hitTransform)
    {
        Renderer[] renderers = hitTransform.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            MarkRendererBlocked(renderers[i]);
        }

        Renderer parentRenderer = hitTransform.GetComponentInParent<Renderer>();
        MarkRendererBlocked(parentRenderer);
    }

    private void MarkRendererBlocked(Renderer rendererToFade)
    {
        if (rendererToFade == null || !rendererToFade.enabled)
        {
            return;
        }

        blockedRenderersThisFrame.Add(rendererToFade);
        if (!fadedRenderers.ContainsKey(rendererToFade))
        {
            fadedRenderers.Add(rendererToFade, new FadedRenderer(rendererToFade));
        }
    }

    private void UpdateFadedRenderers()
    {
        renderersToRestore.Clear();

        foreach (KeyValuePair<Renderer, FadedRenderer> pair in fadedRenderers)
        {
            Renderer fadedRenderer = pair.Key;
            FadedRenderer state = pair.Value;

            if (fadedRenderer == null)
            {
                renderersToRestore.Add(fadedRenderer);
                continue;
            }

            float targetAlpha = blockedRenderersThisFrame.Contains(fadedRenderer) ? blockingObjectAlpha : 1f;
            state.CurrentAlpha = Mathf.MoveTowards(
                state.CurrentAlpha,
                targetAlpha,
                blockingObjectFadeSpeed * Time.deltaTime);
            state.ApplyAlpha(state.CurrentAlpha);

            if (!blockedRenderersThisFrame.Contains(fadedRenderer) && Mathf.Approximately(state.CurrentAlpha, 1f))
            {
                state.Restore();
                renderersToRestore.Add(fadedRenderer);
            }
        }

        for (int i = 0; i < renderersToRestore.Count; i++)
        {
            fadedRenderers.Remove(renderersToRestore[i]);
        }
    }

    private void RestoreFadedRenderers()
    {
        foreach (KeyValuePair<Renderer, FadedRenderer> pair in fadedRenderers)
        {
            pair.Value.Restore();
        }

        fadedRenderers.Clear();
        blockedRenderersThisFrame.Clear();
    }

    private bool IsTargetCollider(Transform hitTransform)
    {
        if (hitTransform == null || targets == null)
        {
            return false;
        }

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null && hitTransform.IsChildOf(targets[i]))
            {
                return true;
            }
        }

        return false;
    }

    private void SnapToRPGView()
    {
        if (!TryGetFocusPoint(out Vector3 focusPoint, out float groupRadius))
        {
            return;
        }

        float extraDistance = Mathf.Min(groupRadius + groupPadding, maxGroupZoomOut);
        transform.position = GetCameraPosition(focusPoint, extraDistance);
        transform.rotation = Quaternion.LookRotation(focusPoint - transform.position, Vector3.up);
        currentVelocity = Vector3.zero;
    }

    private sealed class FadedRenderer
    {
        private readonly MaterialFadeState[] materials;

        public float CurrentAlpha { get; set; } = 1f;

        public FadedRenderer(Renderer rendererToFade)
        {
            Material[] rendererMaterials = rendererToFade.materials;
            materials = new MaterialFadeState[rendererMaterials.Length];

            for (int i = 0; i < rendererMaterials.Length; i++)
            {
                materials[i] = new MaterialFadeState(rendererMaterials[i]);
                materials[i].ConfigureForTransparency();
            }
        }

        public void ApplyAlpha(float alpha)
        {
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i].ApplyAlpha(alpha);
            }
        }

        public void Restore()
        {
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i].Restore();
            }
        }
    }

    private sealed class MaterialFadeState
    {
        private readonly Material material;
        private readonly string colorPropertyName;
        private readonly Color originalColor;
        private readonly bool hasSurface;
        private readonly bool hasBlend;
        private readonly bool hasSrcBlend;
        private readonly bool hasDstBlend;
        private readonly bool hasZWrite;
        private readonly float originalSurface;
        private readonly float originalBlend;
        private readonly float originalSrcBlend;
        private readonly float originalDstBlend;
        private readonly float originalZWrite;
        private readonly int originalRenderQueue;
        private readonly bool hadTransparentKeyword;
        private readonly bool hadAlphaTestKeyword;
        private readonly bool hadAlphaPremultiplyKeyword;

        public MaterialFadeState(Material material)
        {
            this.material = material;
            colorPropertyName = material == null ? string.Empty : GetColorPropertyName(material);
            originalColor = string.IsNullOrEmpty(colorPropertyName) ? Color.white : material.GetColor(colorPropertyName);
            hasSurface = material != null && material.HasProperty("_Surface");
            hasBlend = material != null && material.HasProperty("_Blend");
            hasSrcBlend = material != null && material.HasProperty("_SrcBlend");
            hasDstBlend = material != null && material.HasProperty("_DstBlend");
            hasZWrite = material != null && material.HasProperty("_ZWrite");
            originalSurface = hasSurface ? material.GetFloat("_Surface") : 0f;
            originalBlend = hasBlend ? material.GetFloat("_Blend") : 0f;
            originalSrcBlend = hasSrcBlend ? material.GetFloat("_SrcBlend") : 0f;
            originalDstBlend = hasDstBlend ? material.GetFloat("_DstBlend") : 0f;
            originalZWrite = hasZWrite ? material.GetFloat("_ZWrite") : 0f;
            originalRenderQueue = material == null ? -1 : material.renderQueue;
            hadTransparentKeyword = material != null && material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT");
            hadAlphaTestKeyword = material != null && material.IsKeywordEnabled("_ALPHATEST_ON");
            hadAlphaPremultiplyKeyword = material != null && material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON");
        }

        public void ConfigureForTransparency()
        {
            if (material == null)
            {
                return;
            }

            if (hasSurface)
            {
                material.SetFloat("_Surface", 1f);
            }

            if (hasBlend)
            {
                material.SetFloat("_Blend", 0f);
            }

            if (hasSrcBlend)
            {
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            }

            if (hasDstBlend)
            {
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }

            if (hasZWrite)
            {
                material.SetFloat("_ZWrite", 0f);
            }

            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        public void ApplyAlpha(float alpha)
        {
            if (material == null || string.IsNullOrEmpty(colorPropertyName))
            {
                return;
            }

            Color fadedColor = originalColor;
            fadedColor.a = originalColor.a * alpha;
            material.SetColor(colorPropertyName, fadedColor);
        }

        public void Restore()
        {
            if (material == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(colorPropertyName))
            {
                material.SetColor(colorPropertyName, originalColor);
            }

            if (hasSurface)
            {
                material.SetFloat("_Surface", originalSurface);
            }

            if (hasBlend)
            {
                material.SetFloat("_Blend", originalBlend);
            }

            if (hasSrcBlend)
            {
                material.SetFloat("_SrcBlend", originalSrcBlend);
            }

            if (hasDstBlend)
            {
                material.SetFloat("_DstBlend", originalDstBlend);
            }

            if (hasZWrite)
            {
                material.SetFloat("_ZWrite", originalZWrite);
            }

            material.renderQueue = originalRenderQueue;
            SetKeyword("_SURFACE_TYPE_TRANSPARENT", hadTransparentKeyword);
            SetKeyword("_ALPHATEST_ON", hadAlphaTestKeyword);
            SetKeyword("_ALPHAPREMULTIPLY_ON", hadAlphaPremultiplyKeyword);
        }

        private void SetKeyword(string keyword, bool enabled)
        {
            if (enabled)
            {
                material.EnableKeyword(keyword);
            }
            else
            {
                material.DisableKeyword(keyword);
            }
        }

        private static string GetColorPropertyName(Material material)
        {
            if (material.HasProperty("_BaseColor"))
            {
                return "_BaseColor";
            }

            if (material.HasProperty("_Color"))
            {
                return "_Color";
            }

            return string.Empty;
        }
    }
}
