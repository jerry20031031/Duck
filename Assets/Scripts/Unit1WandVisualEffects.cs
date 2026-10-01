using UnityEngine;

/// <summary>
/// Runtime-only spell visuals for UNIT1. They deliberately use simple Unity
/// primitives so every multiplayer client can show the same readable effect
/// without needing a particle prefab or extra imported assets.
/// </summary>
public static class Unit1WandVisualEffects
{
    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorProperty = Shader.PropertyToID("_EmissionColor");
    private static Material effectMaterial;

    public static void PlayCast(Vector3 origin, Vector3 end, Unit1WandEffectProfile profile)
    {
        float projectileSize = profile.Ability == Unit1WandAbility.Heavy ? 0.28f : 0.17f;
        float travelTime = profile.Ability == Unit1WandAbility.Heavy ? 0.26f : 0.16f;
        CreatePulse(origin, profile.EffectColor, projectileSize * 0.75f, projectileSize * 1.7f, 0.16f);
        Unit1MagicProjectileVfx.Create(origin, end, profile.EffectColor, profile.Ability, projectileSize, travelTime);
    }

    public static void CreateImpact(Vector3 position, Color color, Unit1WandAbility ability)
    {
        float scale = ability == Unit1WandAbility.Heavy ? 1.15f : 0.72f;
        CreatePulse(position, color, 0.12f, scale, ability == Unit1WandAbility.Heavy ? 0.42f : 0.28f);
        CreateRing(position + Vector3.up * 0.06f, color, 0.18f, scale * 1.55f, ability == Unit1WandAbility.Heavy ? 0.52f : 0.32f, 0.09f);

        if (ability == Unit1WandAbility.Heavy)
        {
            Color pale = Color.Lerp(color, Color.white, 0.48f);
            CreateRing(position + Vector3.up * 0.12f, pale, 0.1f, scale * 2.35f, 0.7f, 0.055f);
            CreateBurst(position + Vector3.up * 0.18f, color, 10, 1.3f, 0.36f);
        }
        else
        {
            CreateBurst(position + Vector3.up * 0.16f, color, 6, 0.7f, 0.22f);
        }
    }

    public static void CreateGuardianAura(Transform owner, Color color, float duration)
    {
        if (owner == null || duration <= 0f)
        {
            return;
        }

        GameObject aura = new GameObject("Guardian Shield Aura");
        aura.transform.SetParent(owner, false);
        aura.transform.localPosition = Vector3.zero;
        aura.AddComponent<Unit1ShieldAuraVfx>().Configure(color, duration);
    }

    internal static Material EffectMaterial
    {
        get
        {
            if (effectMaterial != null)
            {
                return effectMaterial;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            effectMaterial = new Material(shader)
            {
                name = "UNIT1 Spell Effect Material",
                hideFlags = HideFlags.DontSave
            };
            if (effectMaterial.HasProperty(BaseColorProperty)) effectMaterial.SetColor(BaseColorProperty, Color.white);
            if (effectMaterial.HasProperty(ColorProperty)) effectMaterial.SetColor(ColorProperty, Color.white);
            return effectMaterial;
        }
    }

    internal static void ApplyGlow(Renderer renderer, Color color)
    {
        if (renderer == null)
        {
            return;
        }

        MaterialPropertyBlock properties = new MaterialPropertyBlock();
        properties.SetColor(BaseColorProperty, color);
        properties.SetColor(ColorProperty, color);
        properties.SetColor(EmissionColorProperty, color * 2.2f);
        renderer.SetPropertyBlock(properties);
        renderer.sharedMaterial = EffectMaterial;
    }

    private static void CreatePulse(Vector3 position, Color color, float startScale, float endScale, float duration)
    {
        GameObject pulse = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        pulse.name = "Spell Glow";
        pulse.transform.position = position;
        Collider collider = pulse.GetComponent<Collider>();
        if (collider != null) Object.Destroy(collider);
        ApplyGlow(pulse.GetComponent<Renderer>(), color);
        pulse.AddComponent<Unit1PulseVfx>().Configure(startScale, endScale, duration);
    }

    private static void CreateRing(Vector3 position, Color color, float startRadius, float endRadius, float duration, float width)
    {
        GameObject ring = new GameObject("Spell Impact Ring");
        ring.transform.position = position;
        ring.AddComponent<Unit1RingVfx>().Configure(color, startRadius, endRadius, duration, width);
    }

    private static void CreateBurst(Vector3 position, Color color, int count, float radius, float duration)
    {
        GameObject burst = new GameObject("Spell Impact Burst");
        burst.transform.position = position;
        burst.AddComponent<Unit1BurstVfx>().Configure(color, count, radius, duration);
    }
}

public sealed class Unit1MagicProjectileVfx : MonoBehaviour
{
    private Vector3 origin;
    private Vector3 destination;
    private Color color;
    private Unit1WandAbility ability;
    private float size;
    private float duration;
    private float elapsed;

    public static void Create(Vector3 origin, Vector3 destination, Color color, Unit1WandAbility ability, float size, float duration)
    {
        GameObject projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        projectile.name = ability == Unit1WandAbility.Heavy ? "Heavy Spell Orb" : "Swift Spell Orb";
        Collider collider = projectile.GetComponent<Collider>();
        if (collider != null) Object.Destroy(collider);
        Unit1WandVisualEffects.ApplyGlow(projectile.GetComponent<Renderer>(), color);

        TrailRenderer trail = projectile.AddComponent<TrailRenderer>();
        trail.time = ability == Unit1WandAbility.Heavy ? 0.3f : 0.18f;
        trail.minVertexDistance = 0.03f;
        trail.startWidth = size * 0.9f;
        trail.endWidth = 0.015f;
        trail.startColor = color;
        trail.endColor = new Color(color.r, color.g, color.b, 0f);
        trail.sharedMaterial = Unit1WandVisualEffects.EffectMaterial;

        projectile.AddComponent<Unit1MagicProjectileVfx>().Configure(origin, destination, color, ability, size, duration);
    }

    private void Configure(Vector3 newOrigin, Vector3 newDestination, Color newColor, Unit1WandAbility newAbility, float newSize, float newDuration)
    {
        origin = newOrigin;
        destination = newDestination;
        color = newColor;
        ability = newAbility;
        size = newSize;
        duration = Mathf.Max(0.05f, newDuration);
        transform.position = origin;
        transform.localScale = Vector3.one * size;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(elapsed / duration);
        Vector3 travel = Vector3.Lerp(origin, destination, Mathf.SmoothStep(0f, 1f, progress));
        float arc = Mathf.Sin(progress * Mathf.PI) * (ability == Unit1WandAbility.Heavy ? 0.52f : 0.26f);
        transform.position = travel + Vector3.up * arc;
        transform.localScale = Vector3.one * (size * (1f + Mathf.Sin(progress * Mathf.PI) * 0.42f));

        if (progress < 1f)
        {
            return;
        }

        Unit1WandVisualEffects.CreateImpact(destination, color, ability);
        Destroy(gameObject, 0.32f);
        enabled = false;
    }
}

public sealed class Unit1PulseVfx : MonoBehaviour
{
    private float startScale;
    private float endScale;
    private float duration;
    private float elapsed;

    public void Configure(float newStartScale, float newEndScale, float newDuration)
    {
        startScale = newStartScale;
        endScale = newEndScale;
        duration = Mathf.Max(0.05f, newDuration);
        transform.localScale = Vector3.one * startScale;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(elapsed / duration);
        transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, progress);
        if (progress >= 1f)
        {
            Destroy(gameObject);
        }
    }
}

public sealed class Unit1RingVfx : MonoBehaviour
{
    private LineRenderer line;
    private Color color;
    private float startRadius;
    private float endRadius;
    private float duration;
    private float elapsed;

    public void Configure(Color newColor, float newStartRadius, float newEndRadius, float newDuration, float width)
    {
        color = newColor;
        startRadius = newStartRadius;
        endRadius = newEndRadius;
        duration = Mathf.Max(0.05f, newDuration);
        line = gameObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 32;
        line.startWidth = width;
        line.endWidth = width * 0.25f;
        line.numCapVertices = 3;
        line.sharedMaterial = Unit1WandVisualEffects.EffectMaterial;
        SetRadius(startRadius, 1f);
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(elapsed / duration);
        SetRadius(Mathf.Lerp(startRadius, endRadius, progress), 1f - progress);
        if (progress >= 1f)
        {
            Destroy(gameObject);
        }
    }

    private void SetRadius(float radius, float alpha)
    {
        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i / (float)line.positionCount * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
        }

        line.startColor = new Color(color.r, color.g, color.b, alpha);
        line.endColor = new Color(color.r, color.g, color.b, alpha * 0.15f);
    }
}

public sealed class Unit1BurstVfx : MonoBehaviour
{
    private LineRenderer[] rays;
    private Color color;
    private float radius;
    private float duration;
    private float elapsed;

    public void Configure(Color newColor, int count, float newRadius, float newDuration)
    {
        color = newColor;
        radius = newRadius;
        duration = Mathf.Max(0.05f, newDuration);
        rays = new LineRenderer[count];
        for (int i = 0; i < count; i++)
        {
            GameObject ray = new GameObject("Spell Spark");
            ray.transform.SetParent(transform, false);
            LineRenderer line = ray.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.startWidth = 0.055f;
            line.endWidth = 0.01f;
            line.sharedMaterial = Unit1WandVisualEffects.EffectMaterial;
            float angle = i / (float)count * Mathf.PI * 2f;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0.12f + (i % 2) * 0.11f, Mathf.Sin(angle)).normalized;
            line.SetPosition(0, direction * 0.08f);
            line.SetPosition(1, direction * radius);
            rays[i] = line;
        }
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(elapsed / duration);
        float alpha = 1f - progress;
        for (int i = 0; i < rays.Length; i++)
        {
            if (rays[i] == null) continue;
            rays[i].startColor = new Color(color.r, color.g, color.b, alpha);
            rays[i].endColor = new Color(color.r, color.g, color.b, 0f);
        }

        if (progress >= 1f)
        {
            Destroy(gameObject);
        }
    }
}

public sealed class Unit1ShieldAuraVfx : MonoBehaviour
{
    private LineRenderer[] rings;
    private Light glow;
    private Color color;
    private float duration;
    private float elapsed;

    public void Configure(Color newColor, float newDuration)
    {
        color = newColor;
        duration = Mathf.Max(0.1f, newDuration);
        rings = new[]
        {
            CreateRing(false, 1.02f, 0.24f),
            CreateRing(true, 0.9f, 0.13f),
            CreateRing(true, 0.9f, 0.07f)
        };
        rings[2].transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

        glow = gameObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = color;
        glow.range = 3.2f;
        glow.intensity = 3.4f;
        glow.shadows = LightShadows.None;
    }

    private LineRenderer CreateRing(bool vertical, float radius, float width)
    {
        GameObject ring = new GameObject("Shield Ring");
        ring.transform.SetParent(transform, false);
        LineRenderer line = ring.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 36;
        line.startWidth = width;
        line.endWidth = width * 0.5f;
        line.sharedMaterial = Unit1WandVisualEffects.EffectMaterial;
        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i / (float)line.positionCount * Mathf.PI * 2f;
            line.SetPosition(i, vertical
                ? new Vector3(Mathf.Cos(angle) * radius, 1.2f + Mathf.Sin(angle) * radius, 0f)
                : new Vector3(Mathf.Cos(angle) * radius, 0.16f, Mathf.Sin(angle) * radius));
        }
        return line;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float remaining = Mathf.Clamp01(1f - elapsed / duration);
        float pulse = 0.92f + Mathf.Sin(Time.time * 8f) * 0.08f;
        transform.localScale = Vector3.one * pulse;
        for (int i = 0; i < rings.Length; i++)
        {
            rings[i].startColor = new Color(color.r, color.g, color.b, remaining * 0.9f);
            rings[i].endColor = new Color(color.r, color.g, color.b, remaining * 0.2f);
        }

        if (glow != null)
        {
            glow.intensity = 3.4f * remaining * pulse;
        }

        if (elapsed >= duration)
        {
            Destroy(gameObject);
        }
    }
}
