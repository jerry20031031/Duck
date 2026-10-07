using System;
using System.Collections.Generic;
using System.Reflection;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Editor-only, opt-in. No saved scene changes, forced AI goals or disabled combat.
// Unity -batchmode -nographics -quit -executeMethod Unit1AINavigationRegression.Run
public static class Unit1AINavigationRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static Scene testScene;
    private static GameObject map;
    private static int assertions;

    [MenuItem("Tools/UNIT1 Tests/AI Random Flat Spawn Regression")]
    public static void CheckUnit1RandomSpawns()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Run in edit mode after compilation.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty || SceneManager.GetSceneAt(i).name == "UNIT1")
                throw new InvalidOperationException("Run with saved scenes and UNIT1 not already open.");
        Scene previous = SceneManager.GetActiveScene();
        UnityEngine.Random.State randomState = UnityEngine.Random.state;
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/UNIT1.unity", OpenSceneMode.Additive);
        assertions = 0;
        try
        {
            SceneManager.SetActiveScene(scene);
            Unit1GameDirector director = Object.FindFirstObjectByType<Unit1GameDirector>();
            var references = (List<Vector3>)Invoke(director, "GetNpcSpawnReferences");
            Bounds playableArea = new(references[0], Vector3.zero);
            foreach (Vector3 reference in references) playableArea.Encapsulate(reference);
            playableArea.Expand(new Vector3(24f, 0f, 24f));
            var distinct = new HashSet<Vector2>();
            for (int round = 0; round < 6; round++)
            {
                UnityEngine.Random.InitState(3301 + round * 7919);
                var occupied = (List<Vector3>)Invoke(director, "GetOccupiedNpcSpawnPositions");
                for (int i = 0; i < 9; i++)
                {
                    string name = i < 6 ? "GreyShadowDuck" : "Unit1CpuDuck";
                    CapsuleCollider shape = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + name + ".prefab").GetComponent<CapsuleCollider>();
                    Check(Unit1RouteScanner.TryFindSpawnPoint(shape, references, occupied, 8f, out Vector3 point), "actual map random spawn " + name);
                    VerifySpawnBody(shape, point, scene);
                    Check(point.x >= playableArea.min.x && point.x <= playableArea.max.x
                        && point.z >= playableArea.min.z && point.z <= playableArea.max.z, "spawn stays near authored play area");
                    foreach (Vector3 other in occupied) Check(FlatDistance(point, other) >= 8f, "actual spawn spacing");
                    occupied.Add(point); distinct.Add(new Vector2(point.x, point.z));
                    Debug.Log("[SPAWN-CHECK] round=" + round + " type=" + name + " p=" + point.ToString("F2"));
                }
            }
            Check(distinct.Count >= 18, "multiple rounds produce varied locations rather than six fixed markers");
            Debug.Log("[SPAWN-CHECK] PASS samples=54 distinct=" + distinct.Count + " assertions=" + assertions);
        }
        finally
        {
            UnityEngine.Random.state = randomState;
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    public static void VerifyLiveSpawnLayout(Unit1BotDuck[] bots, EnemyDuckAI[] shadows)
    {
        var occupied = new List<Vector3>();
        foreach (Unit1BotDuck bot in bots) VerifyLiveSpawn(bot, occupied);
        foreach (EnemyDuckAI grey in shadows) VerifyLiveSpawn(grey, occupied);
        Debug.Log("[SPAWN-CHECK] live " + (bots.Length + shadows.Length) + " NPCs flat ground / body clearance / 8m spacing PASS.");
    }

    private static void VerifyLiveSpawn(Component actor, List<Vector3> occupied)
    {
        VerifySpawnBody(actor.GetComponent<CapsuleCollider>(), actor.transform.position, actor.gameObject.scene);
        foreach (Vector3 other in occupied) Check(FlatDistance(actor.transform.position, other) >= 7.99f, "live spawn spacing");
        occupied.Add(actor.transform.position);
    }

    private static void VerifySpawnBody(CapsuleCollider shape, Vector3 position, Scene scene)
    {
        Vector3 scale = shape.transform.lossyScale;
        float radius = shape.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float height = Mathf.Max(shape.height * Mathf.Abs(scale.y), radius * 2f);
        Vector3 centre = position + Vector3.Scale(shape.center, scale);
        Vector3 bottom = centre - Vector3.up * (height * 0.5f - radius);
        Vector3 top = centre + Vector3.up * (height * 0.5f - radius);
        Physics.SyncTransforms();
        foreach (Collider hit in Physics.OverlapCapsule(bottom, top, radius, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            Check(hit.attachedRigidbody != null || hit.GetComponentInParent<NetworkObject>() != null || hit.gameObject.scene != scene,
                "spawn body does not overlap static geometry " + hit.name);
        float feetY = centre.y - height * 0.5f;
        float low = float.PositiveInfinity, high = float.NegativeInfinity;
        for (int z = -1; z <= 1; z++)
        {
            for (int x = -1; x <= 1; x++)
            {
                Vector3 offset = new Vector3(x, 0f, z).normalized * (radius + 0.135f);
                RaycastHit surface = default;
                foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(position.x, feetY + 0.5f, position.z) + offset,
                    Vector3.down, 1f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
                    if (hit.collider.attachedRigidbody == null && hit.collider.GetComponentInParent<NetworkObject>() == null
                        && hit.collider.gameObject.scene == scene && (surface.collider == null || hit.point.y > surface.point.y)) surface = hit;
                Check(surface.collider != null && surface.normal.y >= Mathf.Cos(12f * Mathf.Deg2Rad), "flat support across spawn footprint");
                Check(feetY - surface.point.y >= 0.01f && feetY - surface.point.y <= 0.17f, "spawn feet close to supported ground");
                low = Mathf.Min(low, surface.point.y); high = Mathf.Max(high, surface.point.y);
            }
        }
        Check(high - low <= 0.1201f, "spawn footprint height spread");
    }

    // Explicit read-only diagnosis against the saved map. The temporary
    // additive scene and prefab instances are discarded, never saved.
    [MenuItem("Tools/UNIT1 Tests/Wand Reachability Diagnostic")]
    public static void DiagnoseUnit1Wands()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Run in edit mode after compilation.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty || SceneManager.GetSceneAt(i).name == "UNIT1")
                throw new InvalidOperationException("Run with saved scenes and UNIT1 not already open.");
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/UNIT1.unity", OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            Vector3[] starts = { new(1.51f, -0.49f, 22.12f), new(11.97f, -0.49f, 88.5f),
                new(-34.6f, -0.49f, 15.29f), new(41.3f, -0.49f, 83.1f) };
            foreach (string prefab in new[] { "Unit1CpuDuck", "GreyShadowDuck" })
            {
                GameObject actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + prefab + ".prefab"), starts[0], Quaternion.identity);
                SceneManager.MoveGameObjectToScene(actor, scene);
                object ai = prefab == "Unit1CpuDuck" ? (object)actor.GetComponent<Unit1BotDuck>() : actor.GetComponent<EnemyDuckAI>();
                Invoke(ai, "ApplyMatchVisualScale");
                Physics.SyncTransforms();
                Unit1RouteScanner.PrimeMapScan(actor.transform.position, actor.GetComponent<Collider>());
            }
            Unit1RouteScanner scanner = Object.FindFirstObjectByType<Unit1RouteScanner>();
            Debug.Log("[WAND-DIAG] envelope radius=" + Read(scanner, "duckRadius") + " ground=" + Read(scanner, "mapY")
                + " bottom=" + Read(scanner, "capsuleBottomOffset") + " top=" + Read(scanner, "capsuleTopOffset"));
            foreach (Unit1WandPickup wand in Object.FindObjectsByType<Unit1WandPickup>(FindObjectsSortMode.None))
            {
                foreach (Vector3 start in starts)
                {
                    var route = new List<Vector3>();
                    Invoke(scanner, "FindPath", start, wand.transform.position, route, 0.78f);
                    int tight = route.Count;
                    Invoke(scanner, "FindPath", start, wand.transform.position, route, wand.NavigationArrivalDistance);
                    Check(route.Count > 0, "reachable wand interaction area " + wand.name + " from " + start);
                    // Edit-mode diagnostics must not read Fusion ownership:
                    // the scene NetworkObjects have not had Spawned() yet.
                    Vector3 pickupOffset = wand.transform.position - route[route.Count - 1];
                    Check(FlatDistance(wand.transform.position, route[route.Count - 1]) <= (float)Read(wand, "pickupDistance")
                        && Mathf.Abs(pickupOffset.y) <= (float)Read(wand, "pickupVerticalDistance"),
                        "reachable endpoint inside wand pickup range " + wand.name);
                    Debug.Log("[WAND-DIAG] " + wand.name + " from=" + start.ToString("F2")
                        + " tightRoute=" + tight + " interactionRoute=" + route.Count
                        + " startClear=" + Invoke(scanner, "IsCellClear", start)
                        + " goalClear=" + Invoke(scanner, "IsCellClear", wand.transform.position)
                        + " end=" + (route.Count > 0 ? route[route.Count - 1].ToString("F2") : "none"));
                }
                float mapY = (float)Read(scanner, "mapY"), radius = (float)Read(scanner, "duckRadius");
                Vector3 bottom = wand.transform.position; bottom.y = mapY + (float)Read(scanner, "capsuleBottomOffset");
                Vector3 top = bottom; top.y = mapY + (float)Read(scanner, "capsuleTopOffset");
                foreach (Collider hit in Physics.OverlapCapsule(bottom, top, radius, Physics.AllLayers, QueryTriggerInteraction.Ignore))
                    if (hit.attachedRigidbody == null && hit.GetComponentInParent<NetworkObject>() == null)
                        Debug.Log("[WAND-DIAG] goal overlap " + wand.name + " with=" + hit.name + " bounds=" + hit.bounds);
            }
            Debug.Log("[WAND-DIAG] all wand interaction routes and pickup endpoints PASS.");
        }
        finally
        {
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    public static void Run()
    {
        assertions = 0;
        Scene previous = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(previous.path))
        {
            if (previous.isDirty) throw new InvalidOperationException("Unsaved scene: close it before testing.");
            previous = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
        }
        if (GameObject.Find("Generated Map") != null) throw new InvalidOperationException("Run without a loaded Generated Map.");
        testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(testScene);
        try
        {
            Memory(); Commitment(); Motor(); Routes(); Spawns();
            Debug.Log("[AI-REGRESSION] PASS assertions=" + assertions);
        }
        finally
        {
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(testScene, true);
        }
    }

    private static void Memory()
    {
        var memory = new Unit1NavigationMemory();
        memory.Remember(Vector3.zero, 0); memory.Remember(Vector3.right * 5, 0);
        Check(memory.IsUnreachable(Vector3.up * 10, 1), "failure ignores height");
        Check(memory.IsUnreachable(Vector3.right * 5, 1), "second failure does not erase first");
        Check(!memory.IsUnreachable(Vector3.right * 2, 1), "valid nearby goal not excluded");
        Check(!memory.IsUnreachable(Vector3.zero, 5), "failure expires");
        for (int i = 0; i < 9; i++) memory.Remember(Vector3.right * i * 5, 10);
        Check(!memory.IsUnreachable(Vector3.zero, 11), "capacity eight evicts oldest");
        Check(memory.IsUnreachable(Vector3.right * 40, 11), "newest failure retained");
    }

    private static void Motor()
    {
        ResetWorld();
        Rigidbody duck = Duck(new Vector3(0, 0.05f, -5)), neighbour = Duck(new Vector3(0, 0.05f, -3.5f));
        Vector3 before = duck.position;
        Check(!DuckKinematicMotor.TryGetBlockingHit(duck, Vector3.forward, 2, true, out _), "AI recovery ignores other ducks");
        Check(DuckKinematicMotor.TryGetBlockingHit(duck, Vector3.forward, 2, false, out _), "player still collides with ducks");
        Check(duck.position == before, "recovery query is read-only");
        Object.DestroyImmediate(neighbour.gameObject);
        GameObject transient = Box("Network pickup", new Vector3(0, 1, -3.5f), Vector3.one, false);
        transient.AddComponent<NetworkObject>(); Physics.SyncTransforms();
        Check(!DuckKinematicMotor.TryGetBlockingHit(duck, Vector3.forward, 2, true, out _), "network object without Rigidbody ignored");
        Object.DestroyImmediate(transient);
        GameObject slope = Box("Sloped ground", new Vector3(0, -0.35f, -4), new Vector3(2, 0.1f, 1), true);
        slope.transform.rotation = Quaternion.Euler(-45, 0, 0);
        GameObject wall = Box("Wall", new Vector3(0, 1.5f, -2), new Vector3(4, 3, 0.2f), false);
        Physics.SyncTransforms();
        Check(DuckKinematicMotor.TryGetBlockingHit(duck, Vector3.forward, 5, true, out RaycastHit hit)
            && hit.collider.gameObject == wall, "ground cannot mask farther wall");
        Check(DuckKinematicMotor.Move(duck, Vector3.forward * 5, out hit, true), "motor reports wall blockage");
        Check(duck.position.z < wall.GetComponent<Collider>().bounds.min.z - 0.49f, "capsule stops before wall");
    }

    private static void Commitment()
    {
        ResetWorld();
        var owner = new GameObject("Target commitment CPU");
        Unit1BotDuck bot = owner.AddComponent<Unit1BotDuck>();
        var current = new GameObject("Current wand");
        var candidate = new GameObject("Candidate wand");
        current.transform.position = Vector3.forward * 20f;
        candidate.transform.position = Vector3.forward * 17f;
        typeof(Unit1BotDuck).GetField("stateEnterTime", Private).SetValue(bot, Time.time - 10f);
        Check(!(bool)Invoke(bot, "ShouldSwitchTarget", candidate.transform, current.transform), "small distance advantage keeps committed goal");
        candidate.transform.position = Vector3.forward * 5f;
        Check((bool)Invoke(bot, "ShouldSwitchTarget", candidate.transform, current.transform), "clearly better goal can replace old goal");
        typeof(Unit1BotDuck).GetField("stateEnterTime", Private).SetValue(bot, Time.time);
        Check(!(bool)Invoke(bot, "ShouldSwitchTarget", candidate.transform, current.transform), "new action respects minimum commitment");
        Debug.Log("[AI-REGRESSION] target commitment PASS");
    }

    private static void Routes()
    {
        ResetWorld(); Rigidbody duck = Duck(new Vector3(-6, 0.05f, -6)); Prime(duck);
        Follow(duck, new Vector3(6, 0, 6), 0.2f, "open floor");
        ResetWorld(); duck = Duck(new Vector3(-6, 0.05f, -6));
        Box("Non-Static wall", new Vector3(0, 1.5f, 0), new Vector3(5, 3, 0.3f), false); Prime(duck);
        Follow(duck, new Vector3(6, 0, 6), 0.2f, "Static floor plus Non-Static wall");

        // Both endpoints are clear; only a continuous edge cast detects this wall.
        ResetWorld(); duck = Duck(new Vector3(-6, 0.05f, -6)); Prime(duck);
        Unit1RouteScanner scanner = Object.FindFirstObjectByType<Unit1RouteScanner>();
        float size = (float)Read(scanner, "cellSize");
        Vector3 a = new((float)Read(scanner, "minX") + 15.5f * size, 0.05f, (float)Read(scanner, "minZ") + 15.5f * size);
        Vector3 b = a + Vector3.right * size;
        Box("Between centres", (a + b) * 0.5f + Vector3.up * 1.45f, new Vector3(0.005f, 3, 0.3f), false);
        duck.position = a; Rescan(scanner, duck);
        Check(!(bool)Invoke(scanner, "IsLineWalkable", a, b), "continuous edge detects thin wall");
        Follow(duck, b, 0.15f, "thin wall");

        ResetWorld(); duck = Duck(new Vector3(-6, 0.05f, -6), 0.2f); Prime(duck);
        scanner = Object.FindFirstObjectByType<Unit1RouteScanner>(); size = (float)Read(scanner, "cellSize");
        a = new Vector3((float)Read(scanner, "minX") + 15.5f * size, 0.05f, (float)Read(scanner, "minZ") + 15.5f * size);
        Box("Blocked centre", a + Vector3.up, new Vector3(0.1f, 2, 0.1f), false);
        duck.position = a + Vector3.right * 0.48f; Rescan(scanner, duck);
        int cell = (int)Invoke(scanner, "PositionToIndex", duck.position);
        Check(!((bool[])Read(scanner, "walkable"))[cell], "quantized start cell blocked");
        Check((bool)Invoke(scanner, "IsCellClear", duck.position), "real starting capsule clear");
        Follow(duck, duck.position + Vector3.forward * 5, 0.2f, "off-centre start");

        ResetWorld(); duck = Duck(new Vector3(-8, 0.05f, -3));
        Box("Fixed target", new Vector3(0, 1.5f, 0), new Vector3(3, 3, 3), false); Prime(duck);
        Follow(duck, Vector3.zero, 2.6f, "tactical arrival outside target collider");

        // Reproduces the UNIT1 guardian wand beside a low fence: a duck's
        // capsule cannot occupy the wand centre, but pickup range is reachable.
        ResetWorld(); duck = Duck(new Vector3(-6, 0.05f, -6));
        Box("Pickup fence", new Vector3(0, 0.45f, -0.3f), new Vector3(4, 0.9f, 0.2f), false);
        var wandObject = new GameObject("Fence-side wand", typeof(NetworkObject), typeof(Unit1WandPickup));
        wandObject.transform.position = new Vector3(0, 1.1f, 0);
        Unit1WandPickup wand = wandObject.GetComponent<Unit1WandPickup>();
        Prime(duck);
        Check(wand.NavigationArrivalDistance > 0.78f && wand.NavigationArrivalDistance < 1.5f,
            "AI pickup approach derives from validated radius with margin");
        Follow(duck, wand.transform.position, wand.NavigationArrivalDistance, "pickup fence interaction range");
        var arrivalRoute = new List<Vector3>(); int arrivalIndex = 0; Vector3 arrivalPlan = Vector3.zero; float arrivalTime = 0;
        Check(Unit1RouteScanner.TryGetNextWaypoint(duck.position, wand.transform.position, wand.NavigationArrivalDistance,
            arrivalRoute, ref arrivalIndex, ref arrivalPlan, ref arrivalTime, out _), "already in pickup range remains reachable without entering centre");

        ResetWorld(); duck = Duck(new Vector3(-6, 0.05f, -6)); Prime(duck);
        var route = new List<Vector3>(); int index = 0; Vector3 planned = Vector3.zero; float nextPlan = 0;
        Unit1RouteScanner.TryGetNextWaypoint(duck.position, Vector3.zero, 0.2f, route, ref index, ref planned, ref nextPlan, out _);
        Vector3 newGoal = new(5, 0, -6); nextPlan = float.PositiveInfinity;
        Check(Unit1RouteScanner.TryGetNextWaypoint(duck.position, newGoal, 0.2f, route, ref index, ref planned, ref nextPlan, out _)
            && planned == newGoal, "new objective bypasses old cooldown");
        ResetWorld(false); duck = Duck(Vector3.zero); Prime(duck);
        Follow(duck, new Vector3(8, 0, 0), 0.2f, "stripped scene");
    }

    private static void Follow(Rigidbody duck, Vector3 goal, float arrival, string label)
    {
        var route = new List<Vector3>(); int index = 0, ticks = 0; Vector3 planned = Vector3.zero; float nextPlan = 0;
        while (FlatDistance(duck.position, goal) > arrival && ticks++ < 2000)
        {
            Check(Unit1RouteScanner.TryGetNextWaypoint(duck.position, goal, arrival, route,
                ref index, ref planned, ref nextPlan, out Vector3 waypoint), label + ": route exists");
            Vector3 direction = waypoint - duck.position; direction.y = 0;
            Check(!DuckKinematicMotor.Move(duck, direction.normalized * Mathf.Min(0.0575f, direction.magnitude), out _, true),
                label + ": real capsule passes");
        }
        Check(FlatDistance(duck.position, goal) <= arrival, label + ": arrives without freeze");
        Debug.Log("[AI-REGRESSION] " + label + " ticks=" + ticks);
    }

    private static void Spawns()
    {
        UnityEngine.Random.State randomState = UnityEngine.Random.state;
        try
        {
            ResetWorld();
            Rigidbody duck = Duck(new Vector3(-8f, 0.05f, -8f));
            CapsuleCollider shape = duck.GetComponent<CapsuleCollider>();
            Box("Spawn obstacle", new Vector3(0f, 1.5f, 0f), new Vector3(5f, 3f, 5f), false);
            GameObject slope = Box("Spawn slope", new Vector3(12f, 0.7f, 0f), new Vector3(4f, 0.2f, 4f), false);
            slope.transform.rotation = Quaternion.Euler(0f, 0f, 25f);
            var references = new List<Vector3> { duck.position, new Vector3(20f, 0f, 20f) };
            var occupied = new List<Vector3> { duck.position };
            UnityEngine.Random.InitState(7523);
            for (int i = 0; i < 16; i++)
            {
                Check(Unit1RouteScanner.TryFindSpawnPoint(shape, references, occupied, 5f, out Vector3 point), "random supported spawn exists");
                VerifySpawnBody(shape, point, testScene);
                foreach (Vector3 other in occupied) Check(FlatDistance(point, other) >= 5f, "random spawn reserves earlier selections");
                occupied.Add(point);
            }
            Unit1RouteScanner scanner = Object.FindFirstObjectByType<Unit1RouteScanner>();
            Check(!(bool)Invoke(scanner, "TryGetFlatSpawnSurface", new Vector3(12f, 0f, 0f), 0f), "steep slope rejected");
            Check(!(bool)Invoke(scanner, "TryGetFlatSpawnSurface", new Vector3(29.8f, 0f, 0f), 0f), "unsupported footprint at floor edge rejected");
            Check(!Unit1RouteScanner.TryFindSpawnPoint(shape, references, new List<Vector3> { Vector3.zero }, 10000f, out _), "crowded map has no unchecked fallback");
            ResetWorld(false);
            Box("Connected ground", new Vector3(-8f, -0.5f, 0f), new Vector3(8f, 1f, 8f), false);
            Box("Isolated flat island", new Vector3(2f, -0.5f, 0f), new Vector3(4f, 1f, 4f), false);
            duck = Duck(new Vector3(-8f, 0.05f, 0f));
            shape = duck.GetComponent<CapsuleCollider>();
            references = new List<Vector3> { duck.position };
            for (int i = 0; i < 8; i++)
            {
                Check(Unit1RouteScanner.TryFindSpawnPoint(shape, references, null, 5f, out Vector3 point), "connected platform spawn exists");
                Check(point.x < 0f, "isolated flat island is not a spawn area");
            }
            ResetWorld(false); duck = Duck(Vector3.zero);
            Check(!Unit1RouteScanner.TryFindSpawnPoint(duck.GetComponent<CapsuleCollider>(), new List<Vector3> { Vector3.zero }, null, 5f, out _), "no ground means no floating NPC spawn");
            Debug.Log("[AI-REGRESSION] random flat spawns PASS (slope / edge / obstacle / spacing / isolation / no-ground).");
        }
        finally { UnityEngine.Random.state = randomState; }
    }

    private static void ResetWorld(bool floor = true)
    {
        foreach (GameObject root in testScene.GetRootGameObjects()) Object.DestroyImmediate(root);
        map = new GameObject("Generated Map");
        if (floor) Box("Static floor", new Vector3(0, -0.5f, 0), new Vector3(60, 1, 60), true);
        Physics.SyncTransforms();
    }
    private static GameObject Box(string name, Vector3 position, Vector3 size, bool isStatic)
    {
        var obj = new GameObject(name); obj.transform.SetParent(map.transform); obj.transform.position = position;
        obj.isStatic = isStatic; obj.AddComponent<BoxCollider>().size = size; return obj;
    }
    private static Rigidbody Duck(Vector3 position, float radius = 0.5f)
    {
        var obj = new GameObject("Regression duck"); obj.transform.position = position;
        var collider = obj.AddComponent<CapsuleCollider>(); collider.radius = radius; collider.height = 2; collider.center = Vector3.up;
        Rigidbody body = obj.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
        Physics.SyncTransforms(); return body;
    }
    private static void Prime(Rigidbody duck) => Unit1RouteScanner.PrimeMapScan(duck.position, duck.GetComponent<Collider>());
    private static void Rescan(Unit1RouteScanner scanner, Rigidbody duck)
    {
        typeof(Unit1RouteScanner).GetField("isScanned", Private).SetValue(scanner, false); Prime(duck);
    }
    private static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
    private static object Read(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
    private static object Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static void Check(bool condition, string message)
    {
        assertions++; if (!condition) throw new InvalidOperationException("[AI-REGRESSION] FAIL " + message);
    }
}
