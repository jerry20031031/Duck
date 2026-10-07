using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 本機呈現，死亡狀態／落點由 Fusion 玩家或 CPU 同步。保留網路物件，
/// 只隱藏身體與碰撞；獨立墓碑不會跟著 NetworkTransform 插值漂移。
/// </summary>
[DisallowMultipleComponent]
public sealed class DuckGraveMarker : MonoBehaviour
{
    private readonly List<RendererState> renderers = new();
    private readonly List<ColliderState> colliders = new();
    private readonly List<AnimatorState> animators = new();
    private readonly List<LabelState> labels = new();
    private GameObject grave;
    private Mesh stoneMesh;
    private Material stoneMaterial;
    private Material inscriptionMaterial;
    private bool hidden;
    private DuckWandAttack attack;
    private bool attackWasEnabled;

    private readonly struct RendererState
    {
        public readonly Renderer Item;
        public readonly bool Enabled;
        public RendererState(Renderer item) { Item = item; Enabled = item.enabled; }
    }
    private readonly struct ColliderState
    {
        public readonly Collider Item;
        public readonly bool Enabled;
        public ColliderState(Collider item) { Item = item; Enabled = item.enabled; }
    }
    private readonly struct AnimatorState
    {
        public readonly Animator Item;
        public readonly bool Enabled;
        public AnimatorState(Animator item) { Item = item; Enabled = item.enabled; }
    }
    private readonly struct LabelState
    {
        public readonly GameObject Item;
        public readonly bool Active;
        public LabelState(GameObject item) { Item = item; Active = item.activeSelf; }
    }

    public Transform GraveTransform => grave != null ? grave.transform : null;
    public bool IsShowingGrave => hidden && grave != null;

    public static DuckGraveMarker GetOrAdd(GameObject duck)
    {
        DuckGraveMarker marker = duck.GetComponent<DuckGraveMarker>();
        return marker != null ? marker : duck.AddComponent<DuckGraveMarker>();
    }

    /// <summary>只由死亡角色的 StateAuthority 計算一次，再同步給所有客戶端。</summary>
    public static Vector3 FindGroundPosition(Transform duck, Collider bodyCollider)
    {
        float feetY = bodyCollider != null ? bodyCollider.bounds.min.y : duck.position.y;
        Vector3 origin = new(duck.position.x, feetY + 0.5f, duck.position.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 80f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        Vector3 ground = new(duck.position.x, feetY, duck.position.z);
        foreach (RaycastHit hit in hits)
        {
            Collider collider = hit.collider;
            if (collider == null || collider.transform.IsChildOf(duck) || collider.attachedRigidbody != null
                || collider.GetComponentInParent<Fusion.NetworkObject>() != null || hit.normal.y < 0.55f) continue;
            if (hit.distance < nearest) { nearest = hit.distance; ground.y = hit.point.y; }
        }
        return ground + Vector3.up * 0.015f;
    }

    public void Apply(bool eliminated, Vector3 groundPosition, float yaw)
    {
        if (eliminated && !hidden)
        {
            // 手杖的 visualRoot 會暫時掛在右手，不能一起關掉它的 Renderer；
            // 否則放回地面後手杖會永久看不見。只收集角色自己的呈現。
            Unit1WandPickup[] wands = FindObjectsByType<Unit1WandPickup>(FindObjectsSortMode.None);
            foreach (Renderer item in GetComponentsInChildren<Renderer>(true))
                if (!IsWandVisual(item.transform, wands)) renderers.Add(new RendererState(item));
            foreach (Collider item in GetComponentsInChildren<Collider>(true))
                if (!IsWandVisual(item.transform, wands)) colliders.Add(new ColliderState(item));
            foreach (Animator item in GetComponentsInChildren<Animator>(true))
                if (!IsWandVisual(item.transform, wands)) animators.Add(new AnimatorState(item));
            // TMP can create new fallback-font submeshes after the first
            // snapshot. Hide the whole label so no floating text survives.
            foreach (TMP_Text item in GetComponentsInChildren<TMP_Text>(true))
                if (!IsWandVisual(item.transform, wands)) labels.Add(new LabelState(item.gameObject));
            attack = GetComponent<DuckWandAttack>();
            attackWasEnabled = attack != null && attack.enabled;
            hidden = true;
            CreateGrave(groundPosition, yaw);
        }
        else if (!eliminated && hidden)
        {
            foreach (RendererState state in renderers) if (state.Item != null) state.Item.enabled = state.Enabled;
            foreach (ColliderState state in colliders) if (state.Item != null) state.Item.enabled = state.Enabled;
            foreach (AnimatorState state in animators) if (state.Item != null) state.Item.enabled = state.Enabled;
            foreach (LabelState state in labels) if (state.Item != null) state.Item.SetActive(state.Active);
            if (attack != null) attack.enabled = attackWasEnabled;
            renderers.Clear(); colliders.Clear(); animators.Clear(); labels.Clear();
            hidden = false;
            DisposeGrave();
        }
        if (!hidden) return;
        EnforceHidden();
        if (grave != null) grave.transform.SetPositionAndRotation(groundPosition, Quaternion.Euler(0, yaw, 0));
    }

    private static bool IsWandVisual(Transform item, Unit1WandPickup[] wands)
    {
        foreach (Unit1WandPickup wand in wands)
            if (wand != null && wand.VisualRoot != null
                && (item == wand.VisualRoot || item.IsChildOf(wand.VisualRoot))) return true;
        return false;
    }

    private void LateUpdate() { if (hidden) EnforceHidden(); }

    private void EnforceHidden()
    {
        foreach (LabelState state in labels) if (state.Item != null && state.Item.activeSelf) state.Item.SetActive(false);
        foreach (RendererState state in renderers) if (state.Item != null) state.Item.enabled = false;
        foreach (ColliderState state in colliders) if (state.Item != null) state.Item.enabled = false;
        foreach (AnimatorState state in animators) if (state.Item != null) state.Item.enabled = false;
        if (attack != null) attack.enabled = false;
    }

    private void CreateGrave(Vector3 position, float yaw)
    {
        grave = new GameObject("Duck Grave - " + name);
        SceneManager.MoveGameObjectToScene(grave, gameObject.scene);
        grave.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        stoneMaterial = new Material(shader) { name = "Runtime Grave Stone", color = new Color(0.37f, 0.39f, 0.43f) };
        if (stoneMaterial.HasProperty("_Smoothness")) stoneMaterial.SetFloat("_Smoothness", 0.1f);
        inscriptionMaterial = new Material(shader) { name = "Runtime Grave Inscription", color = new Color(0.10f, 0.11f, 0.13f) };
        MakeBox("Stone base", new Vector3(0, 0.08f, 0), new Vector3(1.05f, 0.16f, 0.48f), stoneMaterial);
        GameObject stone = new("Rounded gravestone", typeof(MeshFilter), typeof(MeshRenderer));
        stone.transform.SetParent(grave.transform, false);
        stone.transform.localPosition = Vector3.up * 0.14f;
        stoneMesh = BuildStoneMesh();
        stone.GetComponent<MeshFilter>().sharedMesh = stoneMesh;
        stone.GetComponent<MeshRenderer>().sharedMaterial = stoneMaterial;
        MakeBox("Cross upright", new Vector3(0, 0.94f, -0.165f), new Vector3(0.06f, 0.38f, 0.018f), inscriptionMaterial);
        MakeBox("Cross arms", new Vector3(0, 1.01f, -0.165f), new Vector3(0.27f, 0.06f, 0.018f), inscriptionMaterial);
        GameObject inscription = new("RIP", typeof(TextMeshPro));
        inscription.transform.SetParent(grave.transform, false);
        inscription.transform.localPosition = new Vector3(0, 0.47f, -0.17f);
        TextMeshPro text = inscription.GetComponent<TextMeshPro>();
        text.font = TMP_Settings.defaultFontAsset;
        text.text = "RIP"; text.fontSize = 4f; text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(0.13f, 0.14f, 0.16f); text.raycastTarget = false;
        text.rectTransform.sizeDelta = new Vector2(0.75f, 0.35f);
        // 墓碑純呈現，不新增任何阻路 Collider。
    }

    private void MakeBox(string part, Vector3 position, Vector3 scale, Material material)
    {
        GameObject box = new(part, typeof(MeshFilter), typeof(MeshRenderer));
        box.transform.SetParent(grave.transform, false);
        box.transform.localPosition = position; box.transform.localScale = scale;
        // 使用 Unity 內建 Cube mesh，不建立／延後刪除碰撞體。
        GameObject template = GameObject.CreatePrimitive(PrimitiveType.Cube);
        template.SetActive(false);
        box.GetComponent<MeshFilter>().sharedMesh = template.GetComponent<MeshFilter>().sharedMesh;
        Collider templateCollider = template.GetComponent<Collider>();
        if (templateCollider != null) templateCollider.enabled = false;
        DisposeObject(template);
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static Mesh BuildStoneMesh()
    {
        var outline = new List<Vector2> { new(-0.45f, 0), new(0.45f, 0) };
        for (int i = 0; i <= 12; i++)
        {
            float angle = i * Mathf.PI / 12f;
            outline.Add(new Vector2(Mathf.Cos(angle) * 0.45f, 0.9f + Mathf.Sin(angle) * 0.45f));
        }
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        // 各面使用獨立頂點，石碑邊緣保持俐落而非被平滑成氣球。
        void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            int start = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        }
        for (int i = 0; i < outline.Count; i++)
        {
            Vector2 a = outline[i], b = outline[(i + 1) % outline.Count];
            Vector3 af = new(a.x, a.y, -0.15f), bf = new(b.x, b.y, -0.15f);
            Vector3 ab = new(a.x, a.y, 0.15f), bb = new(b.x, b.y, 0.15f);
            Triangle(new Vector3(0, 0.65f, -0.15f), bf, af);
            Triangle(new Vector3(0, 0.65f, 0.15f), ab, bb);
            Triangle(af, bf, bb); Triangle(af, bb, ab);
        }
        Mesh mesh = new() { name = "Runtime Rounded Gravestone" };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    private void OnDestroy() => DisposeGrave();
    private void DisposeGrave()
    {
        DisposeObject(grave); DisposeObject(stoneMesh); DisposeObject(stoneMaterial); DisposeObject(inscriptionMaterial);
        grave = null; stoneMesh = null; stoneMaterial = null; inscriptionMaterial = null;
    }
    private static void DisposeObject(Object item)
    {
        if (item == null) return;
        if (Application.isPlaying) Destroy(item); else DestroyImmediate(item);
    }
}
