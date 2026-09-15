using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuckGame.Vfx
{
    [ExecuteAlways]
    public sealed class RuneCircleVfx : MonoBehaviour
    {
        private const string GeneratedRootName = "Generated Rune Circle";

        [Header("Shape")]
        [SerializeField] private float radius = 1.75f;
        [SerializeField] private float lineHeight = 0.04f;
        [SerializeField] private int circleSegments = 128;

        [Header("Timing")]
        [SerializeField] private bool playOnEnable = true;
        [SerializeField] private bool destroyWhenFinished = true;
        [SerializeField] private float duration = 2.4f;
        [SerializeField] private float fadeInTime = 0.18f;
        [SerializeField] private float fadeOutTime = 0.35f;

        [Header("Motion")]
        [SerializeField] private float outerRingSpeed = 28f;
        [SerializeField] private float innerRingSpeed = -46f;
        [SerializeField] private float runeRingSpeed = 15f;
        [SerializeField] private float pulseStrength = 0.22f;
        [SerializeField] private float pulseSpeed = 6f;

        [Header("Color")]
        [SerializeField] private Color primaryColor = new Color(0.25f, 0.92f, 1f, 1f);
        [SerializeField] private Color secondaryColor = new Color(1f, 0.42f, 0.95f, 1f);
        [SerializeField] private Color accentColor = new Color(1f, 0.95f, 0.52f, 1f);

        private readonly List<LineState> lines = new List<LineState>();
        private Transform generatedRoot;
        private Transform outerRingRoot;
        private Transform innerRingRoot;
        private Transform runeRingRoot;
        private ParticleSystem sparkleParticles;
        private ParticleSystem burstParticles;
        private float age;
        private bool isPlaying;

        public float Radius => radius;

        private void OnEnable()
        {
            if (generatedRoot == null)
            {
                Rebuild();
            }

            if (playOnEnable)
            {
                Play();
            }
        }

        private void Update()
        {
            if (!isPlaying && Application.isPlaying)
            {
                return;
            }

            float deltaTime = Application.isPlaying ? Time.deltaTime : 0f;
            age += deltaTime;

            float alpha = GetLifetimeAlpha();
            float pulse = 1f + Mathf.Sin(age * pulseSpeed) * pulseStrength * alpha;
            ApplyLineVisuals(alpha, pulse);
            AnimateRings(deltaTime);

            if (Application.isPlaying && duration > 0f && age >= duration)
            {
                isPlaying = false;

                if (destroyWhenFinished)
                {
                    Destroy(gameObject);
                }
            }
        }

        private void OnValidate()
        {
            radius = Mathf.Max(0.25f, radius);
            lineHeight = Mathf.Max(0.005f, lineHeight);
            circleSegments = Mathf.Clamp(circleSegments, 24, 256);
            duration = Mathf.Max(0.05f, duration);
            fadeInTime = Mathf.Max(0.01f, fadeInTime);
            fadeOutTime = Mathf.Max(0.01f, fadeOutTime);
            pulseStrength = Mathf.Max(0f, pulseStrength);
            pulseSpeed = Mathf.Max(0f, pulseSpeed);

            if (isActiveAndEnabled)
            {
                Rebuild();
            }
        }

        public void Configure(float newRadius, float newDuration, Color newPrimaryColor, Color newSecondaryColor)
        {
            radius = Mathf.Max(0.25f, newRadius);
            duration = Mathf.Max(0.05f, newDuration);
            primaryColor = newPrimaryColor;
            secondaryColor = newSecondaryColor;
            Rebuild();
        }

        public void Play()
        {
            age = 0f;
            isPlaying = true;

            if (sparkleParticles != null)
            {
                sparkleParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                sparkleParticles.Clear();
                sparkleParticles.Play();
            }

            if (burstParticles != null)
            {
                burstParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                burstParticles.Clear();
                burstParticles.Play();
            }
        }

        [ContextMenu("Rebuild Rune Circle")]
        public void Rebuild()
        {
            ClearGeneratedRoot();
            lines.Clear();

            generatedRoot = new GameObject(GeneratedRootName).transform;
            generatedRoot.SetParent(transform, false);

            outerRingRoot = CreateGroup("Outer Rings");
            innerRingRoot = CreateGroup("Inner Rings");
            runeRingRoot = CreateGroup("Runes");

            BuildCircles();
            BuildRunes();
            BuildParticles();
            ApplyLineVisuals(0f, 1f);
        }

        private void BuildCircles()
        {
            CreateCircle(outerRingRoot, "Outer Circle", radius, 0.055f, primaryColor);
            CreateCircle(outerRingRoot, "Outer Thin Circle", radius * 0.88f, 0.018f, secondaryColor);
            CreateCircle(innerRingRoot, "Middle Circle", radius * 0.62f, 0.035f, primaryColor);
            CreateCircle(innerRingRoot, "Core Circle", radius * 0.28f, 0.026f, accentColor);

            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f;
                CreateRadialLine(innerRingRoot, $"Radial Line {i + 1}", angle, radius * 0.36f, radius * 0.78f, 0.022f, secondaryColor);
            }
        }

        private void BuildRunes()
        {
            int runeCount = 12;
            for (int i = 0; i < runeCount; i++)
            {
                float angle = i * (360f / runeCount);
                Color runeColor = i % 3 == 0 ? accentColor : primaryColor;

                CreateTickRune(runeRingRoot, i, angle, radius * 0.98f, 0.12f, runeColor);
                CreateChevronRune(runeRingRoot, i, angle + 15f, radius * 0.73f, 0.09f, secondaryColor);
            }
        }

        private void BuildParticles()
        {
            sparkleParticles = CreateParticleSystem("Rune Sparkles", primaryColor, true);
            burstParticles = CreateParticleSystem("Opening Burst", accentColor, false);
        }

        private ParticleSystem CreateParticleSystem(string objectName, Color color, bool loop)
        {
            GameObject particleObject = new GameObject(objectName);
            particleObject.transform.SetParent(generatedRoot, false);
            particleObject.transform.localPosition = Vector3.up * lineHeight;

            ParticleSystem particles = particleObject.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = loop;
            main.duration = loop ? Mathf.Max(duration, 1f) : 0.35f;
            main.startLifetime = loop ? new ParticleSystem.MinMaxCurve(0.45f, 0.9f) : new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startSpeed = loop ? new ParticleSystem.MinMaxCurve(0.08f, 0.18f) : new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
            main.startSize = loop ? new ParticleSystem.MinMaxCurve(0.035f, 0.075f) : new ParticleSystem.MinMaxCurve(0.045f, 0.11f);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = loop ? 80 : 48;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = loop ? 28f : 0f;

            if (!loop)
            {
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 42) });
            }

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = loop ? radius * 0.85f : radius * 0.32f;
            shape.arc = 360f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            ParticleSystemRenderer particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            particleRenderer.material = CreateGlowMaterial(color);
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;

            return particles;
        }

        private void CreateCircle(Transform parent, string objectName, float circleRadius, float width, Color color)
        {
            LineRenderer line = CreateLine(parent, objectName, width, color, true);
            line.positionCount = circleSegments;

            for (int i = 0; i < circleSegments; i++)
            {
                float angle = i / (float)circleSegments * Mathf.PI * 2f;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * circleRadius, lineHeight, Mathf.Sin(angle) * circleRadius));
            }
        }

        private void CreateRadialLine(Transform parent, string objectName, float angleDegrees, float innerRadius, float outerRadius, float width, Color color)
        {
            LineRenderer line = CreateLine(parent, objectName, width, color, false);
            line.positionCount = 2;
            line.SetPosition(0, GetPoint(angleDegrees, innerRadius));
            line.SetPosition(1, GetPoint(angleDegrees, outerRadius));
        }

        private void CreateTickRune(Transform parent, int index, float angleDegrees, float runeRadius, float length, Color color)
        {
            float tangentOffset = length * 10f;
            LineRenderer line = CreateLine(parent, $"Tick Rune {index + 1}", 0.03f, color, false);
            line.positionCount = 3;
            line.SetPosition(0, GetPoint(angleDegrees - tangentOffset, runeRadius));
            line.SetPosition(1, GetPoint(angleDegrees, runeRadius - length));
            line.SetPosition(2, GetPoint(angleDegrees + tangentOffset, runeRadius));
        }

        private void CreateChevronRune(Transform parent, int index, float angleDegrees, float runeRadius, float size, Color color)
        {
            LineRenderer line = CreateLine(parent, $"Chevron Rune {index + 1}", 0.024f, color, false);
            line.positionCount = 4;
            line.SetPosition(0, GetPoint(angleDegrees - 5f, runeRadius + size));
            line.SetPosition(1, GetPoint(angleDegrees, runeRadius));
            line.SetPosition(2, GetPoint(angleDegrees + 5f, runeRadius + size));
            line.SetPosition(3, GetPoint(angleDegrees, runeRadius + size * 1.75f));
        }

        private LineRenderer CreateLine(Transform parent, string objectName, float width, Color color, bool loop)
        {
            GameObject lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(parent, false);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = loop;
            line.numCornerVertices = 4;
            line.numCapVertices = 4;
            line.textureMode = LineTextureMode.Tile;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.material = CreateGlowMaterial(color);

            LineState lineState = new LineState(line, color, width);
            lines.Add(lineState);
            return line;
        }

        private Material CreateGlowMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader)
            {
                name = "Rune Circle Glow",
                hideFlags = HideFlags.DontSave
            };

            SetMaterialColor(material, color);

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat("_DstBlend", (float)BlendMode.One);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0f);
            }

            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            else if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private void ApplyLineVisuals(float alpha, float pulse)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                LineState lineState = lines[i];
                if (lineState.Renderer == null)
                {
                    continue;
                }

                Color lineColor = lineState.BaseColor;
                lineColor.a *= alpha;
                lineState.Renderer.startColor = lineColor;
                lineState.Renderer.endColor = lineColor;
                lineState.Renderer.startWidth = lineState.Width * pulse;
                lineState.Renderer.endWidth = lineState.Width * pulse;
                SetMaterialColor(lineState.Renderer.sharedMaterial, lineColor);
            }

            if (generatedRoot != null)
            {
                float scale = Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(age / fadeInTime));
                generatedRoot.localScale = Vector3.one * scale;
            }
        }

        private void AnimateRings(float deltaTime)
        {
            RotateGroup(outerRingRoot, outerRingSpeed * deltaTime);
            RotateGroup(innerRingRoot, innerRingSpeed * deltaTime);
            RotateGroup(runeRingRoot, runeRingSpeed * deltaTime);
        }

        private void RotateGroup(Transform group, float degrees)
        {
            if (group == null || Mathf.Approximately(degrees, 0f))
            {
                return;
            }

            group.Rotate(Vector3.up, degrees, Space.Self);
        }

        private float GetLifetimeAlpha()
        {
            float alphaIn = Mathf.Clamp01(age / fadeInTime);
            if (duration <= 0f)
            {
                return alphaIn;
            }

            float timeLeft = duration - age;
            float alphaOut = Mathf.Clamp01(timeLeft / fadeOutTime);
            return Mathf.Min(alphaIn, alphaOut);
        }

        private Vector3 GetPoint(float angleDegrees, float pointRadius)
        {
            float angle = angleDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * pointRadius, lineHeight, Mathf.Sin(angle) * pointRadius);
        }

        private Transform CreateGroup(string objectName)
        {
            GameObject groupObject = new GameObject(objectName);
            groupObject.transform.SetParent(generatedRoot, false);
            return groupObject.transform;
        }

        private void ClearGeneratedRoot()
        {
            Transform existingRoot = transform.Find(GeneratedRootName);
            if (existingRoot == null)
            {
                return;
            }

            StopParticlesUnder(existingRoot);

            if (Application.isPlaying)
            {
                Destroy(existingRoot.gameObject);
            }
            else
            {
                DestroyImmediate(existingRoot.gameObject);
            }
        }

        private static void StopParticlesUnder(Transform root)
        {
            ParticleSystem[] particleSystems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                particleSystems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private readonly struct LineState
        {
            public LineState(LineRenderer renderer, Color baseColor, float width)
            {
                Renderer = renderer;
                BaseColor = baseColor;
                Width = width;
            }

            public LineRenderer Renderer { get; }
            public Color BaseColor { get; }
            public float Width { get; }
        }
    }
}
