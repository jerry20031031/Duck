using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The shared navigation "brain" for UNIT1's non-player ducks.
///
/// It samples the current map once, marks every duck-sized clear cell, and
/// uses A* to turn an intention (patrol, wand, enemy, or retreat) into a
/// sequence of safe waypoints. Dynamic actors are deliberately ignored while
/// scanning: they move too often to be part of the map, and DuckKinematicMotor
/// remains responsible for close-range collision avoidance.
/// </summary>
public sealed class Unit1RouteScanner : MonoBehaviour
{
    private const string PlannerObjectName = "UNIT1 Route Scanner";
    private const string GeneratedMapName = "Generated Map";
    private const float DefaultCellSize = 1.1f;
    private const float ReplanDistance = 0.8f;
    private const float ReplanInterval = 0.35f;
    private const int MaximumCellCount = 14400;
    private const int NearestCellSearchRadius = 16;

    private static Unit1RouteScanner instance;

    private Scene scannedScene;
    private bool isScanned;
    // A stripped test scene may intentionally contain no static geometry.
    // In that case there is nothing to plan around; returning an empty path
    // must not make every AI stand still.
    private bool isOpenSpaceFallback;
    private float cellSize;
    private float minX;
    private float minZ;
    private int width;
    private int height;
    private bool[] walkable = System.Array.Empty<bool>();
    private float mapY;
    private float duckRadius = 0.54f;
    private float capsuleBottomOffset = 0.9f;
    private float capsuleTopOffset = 2.8f;
    private bool calibratedEnvelope;
    private float groundY;
    private ushort[] knownEdges = System.Array.Empty<ushort>();
    private ushort[] clearEdges = System.Array.Empty<ushort>();
    private Collider[] overlapResults = new Collider[64];
    private readonly RaycastHit[] segmentHits = new RaycastHit[64];

    // Reused by A*. Only one state-authority duck executes at a time on a
    // client, and the completed route is copied to that duck before the next
    // query, so these buffers never become shared mutable path state.
    private float[] costFromStart = System.Array.Empty<float>();
    private int[] cameFrom = System.Array.Empty<int>();
    private bool[] closed = System.Array.Empty<bool>();
    private readonly List<OpenNode> openNodes = new();
    private readonly List<int> reconstructedNodes = new();
    private readonly List<Vector3> roamingRoute = new();
    private readonly RaycastHit[] roamingGroundHits = new RaycastHit[16];
    private bool[] spawnConnectedCells = System.Array.Empty<bool>();
    private readonly List<Vector3> spawnReferences = new();

    private readonly struct OpenNode
    {
        public readonly int Index;
        public readonly float Priority;

        public OpenNode(int index, float priority)
        {
            Index = index;
            Priority = priority;
        }
    }

    /// <summary>Starts the whole-map scan early, before either AI needs a target.</summary>
    public static void PrimeMapScan(Vector3 duckPosition, Collider bodyCollider = null)
    {
        Unit1RouteScanner scanner = GetOrCreate();
        Physics.SyncTransforms();
        scanner.ConfigureEnvelope(duckPosition, bodyCollider);
        scanner.EnsureScanned(duckPosition.y);
    }

    private void ConfigureEnvelope(Vector3 position, Collider collider)
    {
        if (collider == null) return;
        Bounds bounds = collider.bounds;
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z) + 0.035f;
        float bodyHeight = bounds.size.y + 0.07f;
        float floorY = bounds.min.y;
        int count = Physics.RaycastNonAlloc(position + Vector3.up * 4f, Vector3.down,
            roamingGroundHits, 8f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        float nearestGround = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = roamingGroundHits[i];
            if (IsMapObstacle(hit.collider) && hit.collider.gameObject.scene == GetNavigationScene()
                && hit.normal.y > 0.55f && hit.point.y <= position.y + 1f)
                nearestGround = Mathf.Max(nearestGround, hit.point.y);
        }
        if (!float.IsNegativeInfinity(nearestGround)) floorY = nearestGround;
        ConfigureEnvelope(radius, bodyHeight, floorY);
    }

    private void ConfigureEnvelope(float radius, float bodyHeight, float floorY)
    {
        if (calibratedEnvelope)
        {
            radius = Mathf.Max(radius, duckRadius);
            bodyHeight = Mathf.Max(bodyHeight, capsuleTopOffset + duckRadius - 0.035f);
        }
        bool changed = !calibratedEnvelope || radius > duckRadius + 0.001f
            || bodyHeight > capsuleTopOffset + duckRadius - 0.035f + 0.001f;
        if (!changed) return;
        duckRadius = radius;
        capsuleBottomOffset = radius + 0.035f;
        capsuleTopOffset = Mathf.Max(capsuleBottomOffset, bodyHeight - radius + 0.035f);
        groundY = floorY;
        calibratedEnvelope = true;
        isScanned = false;
    }

    /// <summary>
    /// Returns a route point for this simulation tick. Callers retain their
    /// own route list so a CPU and a Grey Shadow can pursue separate plans.
    /// </summary>
    public static bool TryGetNextWaypoint(
        Vector3 currentPosition,
        Vector3 destination,
        float arrivalDistance,
        List<Vector3> route,
        ref int routeIndex,
        ref Vector3 plannedDestination,
        ref float nextPlanTime,
        out Vector3 waypoint)
    {
        Unit1RouteScanner scanner = GetOrCreate();
        waypoint = destination;
        if (!scanner.EnsureScanned(currentPosition.y))
        {
            return false;
        }

        if (scanner.isOpenSpaceFallback)
        {
            route.Clear();
            routeIndex = 0;
            plannedDestination = destination;
            nextPlanTime = Time.time + ReplanInterval;
            waypoint = destination;
            return true;
        }

        bool destinationChanged = FlatDistance(plannedDestination, destination) > ReplanDistance;
        bool routeFinished = routeIndex >= route.Count;
        bool alreadyAtPlanDestination = FlatDistance(currentPosition, plannedDestination) <= arrivalDistance;
        if ((route.Count == 0 || (routeFinished && !alreadyAtPlanDestination) || destinationChanged)
            && (destinationChanged || route.Count == 0 || Time.time >= nextPlanTime))
        {
            route.Clear();
            routeIndex = 0;
            plannedDestination = destination;
            scanner.FindPath(currentPosition, destination, route, arrivalDistance);
            nextPlanTime = Time.time + ReplanInterval;
        }

        const float waypointArrivalDistance = 0.18f;
        while (routeIndex < route.Count
            && FlatDistance(currentPosition, route[routeIndex]) <= waypointArrivalDistance
            && (routeIndex + 1 < route.Count
                ? scanner.IsLineWalkable(currentPosition, route[routeIndex + 1])
                : FlatDistance(currentPosition, destination) <= arrivalDistance
                    || scanner.IsLineWalkable(currentPosition, destination)))
        {
            routeIndex++;
        }

        if (routeIndex < route.Count)
        {
            waypoint = route[routeIndex];
            waypoint.y = destination.y;
            return true;
        }

        if (FlatDistance(currentPosition, destination) <= arrivalDistance
            && scanner.IsCellClear(currentPosition))
        {
            // A valid interaction point need not reach a blocked target
            // centre. Report arrival, not a failed route requiring recovery.
            waypoint = currentPosition;
            return true;
        }
        // A clear final segment is safe even after all intermediate cells
        // have been consumed. This avoids stopping one grid cell short of a
        // moving wand or duck.
        if (scanner.IsLineWalkable(currentPosition, destination))
        {
            waypoint = destination;
            return true;
        }
        return false;
    }

    /// <summary>Moves a patrol goal out of a prop before the plan is requested.</summary>
    public static Vector3 ClampToWalkablePoint(Vector3 desiredPosition, float duckY)
    {
        Unit1RouteScanner scanner = GetOrCreate();
        if (!scanner.EnsureScanned(duckY))
        {
            return desiredPosition;
        }

        int index = scanner.FindNearestWalkable(desiredPosition);
        if (index < 0)
        {
            return desiredPosition;
        }

        Vector3 safePosition = scanner.CellCenter(index);
        safePosition.y = desiredPosition.y;
        return safePosition;
    }

    /// <summary>
    /// Selects a reachable exploration leg from the duck's current position,
    /// not its spawn position. Repeated legs can cover the map without trapping
    /// an idle duck in its old small patrol circle.
    /// </summary>
    public static bool TryFindRoamingPoint(Vector3 currentPosition, int seed, out Vector3 point)
    {
        Unit1RouteScanner scanner = GetOrCreate();
        point = currentPosition;
        if (!scanner.EnsureScanned(currentPosition.y)) return false;
        for (int option = 0; option < 8; option++)
        {
            float angle = Mathf.Repeat(seed * 79.7f + option * 137.5f, 360f) * Mathf.Deg2Rad;
            float distance = 12f + Mathf.Repeat(seed * 0.73f + option * 0.91f, 1f) * 16f;
            Vector3 desired = currentPosition + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
            if (scanner.isOpenSpaceFallback)
            {
                point = desired;
                return true;
            }
            int cell = scanner.FindNearestWalkable(desired);
            if (cell < 0) continue;
            Vector3 candidate = scanner.CellCenter(cell);
            candidate.y = currentPosition.y;
            if (FlatDistance(currentPosition, candidate) < 6f
                || !scanner.HasRoamingGround(candidate)) continue;
            scanner.FindPath(currentPosition, candidate, scanner.roamingRoute);
            if (scanner.roamingRoute.Count == 0) continue;
            point = candidate;
            return true;
        }
        return false;
    }

    private bool HasRoamingGround(Vector3 point)
    {
        int count = Physics.RaycastNonAlloc(point + Vector3.up * 4f, Vector3.down,
            roamingGroundHits, 8f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = roamingGroundHits[i].collider;
            if (hit != null && hit.GetComponentInParent<NetworkObject>() == null
                && Vector3.Dot(roamingGroundHits[i].normal, Vector3.up) > 0.55f) return true;
        }
        return false;
    }

    /// <summary>
    /// Randomizes authoritative NPC spawns over the existing collision grid,
    /// not just authored markers. Requires flat support under the whole body,
    /// clearance, spacing and a route back to an authored playable area.
    /// No unchecked position is returned if the map has no safe candidates.
    /// </summary>
    public static bool TryFindSpawnPoint(CapsuleCollider shape, IReadOnlyList<Vector3> references,
        IReadOnlyList<Vector3> occupied, float minimumSpacing, out Vector3 position)
    {
        position = default;
        if (shape == null || shape.direction != 1 || references == null || references.Count == 0) return false;
        Unit1RouteScanner scanner = GetOrCreate();
        Physics.SyncTransforms();
        Vector3 scale = shape.transform.lossyScale;
        float radius = shape.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float height = Mathf.Max(shape.height * Mathf.Abs(scale.y), radius * 2f);
        float feetOffset = shape.center.y * scale.y - height * 0.5f;
        Scene scene = GetNavigationScene();
        if (!scanner.calibratedEnvelope)
        {
            bool foundGround = false;
            for (int i = 0; i < references.Count; i++)
            {
                if (!scanner.TryGetSpawnSurface(references[i], scene, out RaycastHit hit)) continue;
                scanner.ConfigureEnvelope(radius + 0.035f, height + 0.07f, hit.point.y);
                foundGround = true;
                break;
            }
            if (!foundGround) return false;
        }
        else scanner.ConfigureEnvelope(radius + 0.035f, height + 0.07f, scanner.groundY);
        if (!scanner.EnsureScanned(references[0].y) || scanner.isOpenSpaceFallback) return false;
        scanner.EnsureSpawnConnections(references);
        // Large foundation colliders can extend far beyond the actual level.
        // Randomize throughout the authored play area, with a modest margin,
        // rather than putting opponents hundreds of metres outside the town.
        Bounds playableArea = new(references[0], Vector3.zero);
        for (int i = 1; i < references.Count; i++) playableArea.Encapsulate(references[i]);
        playableArea.Expand(new Vector3(24f, 0f, 24f));

        // Rejection sampling distributes ordinary spawns across all safe cells.
        // A bounded, randomly-started exhaustive pass is only a crowded-map
        // fallback, so rows of blocked cells cannot bias spawns to their edge.
        const int randomAttempts = 128;
        int first = Random.Range(0, scanner.walkable.Length);
        for (int option = 0; option < scanner.walkable.Length + randomAttempts; option++)
        {
            int cell = option < randomAttempts ? Random.Range(0, scanner.walkable.Length)
                : (first + option - randomAttempts) % scanner.walkable.Length;
            if (!scanner.spawnConnectedCells[cell]) continue;
            Vector3 candidate = scanner.CellCenter(cell);
            if (candidate.x < playableArea.min.x || candidate.x > playableArea.max.x
                || candidate.z < playableArea.min.z || candidate.z > playableArea.max.z) continue;
            if (!HasSpawnSpacing(candidate, occupied, minimumSpacing)
                || !scanner.TryGetFlatSpawnSurface(candidate, out float floorY)
                || !scanner.IsCellClearAtGround(candidate, floorY)) continue;
            candidate.y = floorY + 0.04f - feetOffset;
            position = candidate;
            return true;
        }
        return false;
    }

    private void EnsureSpawnConnections(IReadOnlyList<Vector3> references)
    {
        bool sameReferences = spawnConnectedCells.Length == walkable.Length && spawnReferences.Count == references.Count;
        for (int i = 0; sameReferences && i < references.Count; i++)
            sameReferences = (spawnReferences[i] - references[i]).sqrMagnitude < 0.0001f;
        if (sameReferences) return;
        spawnReferences.Clear();
        for (int i = 0; i < references.Count; i++) spawnReferences.Add(references[i]);
        spawnConnectedCells = new bool[walkable.Length];
        var support = new byte[walkable.Length];
        var pending = new Queue<int>();
        foreach (Vector3 reference in spawnReferences)
        {
            int cell = FindNearestWalkable(reference, true);
            if (cell < 0 || spawnConnectedCells[cell] || !HasSpawnRouteSupport(cell, support)) continue;
            spawnConnectedCells[cell] = true;
            pending.Enqueue(cell);
        }
        // Unlike the navigation grid's obstacle-only cells, this flood also
        // requires actual ground. Empty air must not connect a floating island.
        while (pending.Count > 0)
        {
            int current = pending.Dequeue();
            GetCellCoordinates(current, out int x, out int z);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dz == 0) || !IsWalkable(x + dx, z + dz)) continue;
                    int next = ToIndex(x + dx, z + dz);
                    if (spawnConnectedCells[next] || !HasSpawnRouteSupport(next, support)) continue;
                    if (dx != 0 && dz != 0
                        && (!IsWalkable(x + dx, z) || !IsWalkable(x, z + dz)
                            || !HasSpawnRouteSupport(ToIndex(x + dx, z), support)
                            || !HasSpawnRouteSupport(ToIndex(x, z + dz), support))) continue;
                    if (!IsEdgeWalkable(current, next, dx, dz)) continue;
                    spawnConnectedCells[next] = true;
                    pending.Enqueue(next);
                }
            }
        }
    }

    private bool HasSpawnRouteSupport(int cell, byte[] support)
    {
        if (support[cell] == 0)
            support[cell] = (byte)(TryGetSpawnSurface(CellCenter(cell), scannedScene, out RaycastHit hit)
                && Mathf.Abs(hit.point.y - mapY) <= 0.3f ? 2 : 1);
        return support[cell] == 2;
    }

    private static bool HasSpawnSpacing(Vector3 candidate, IReadOnlyList<Vector3> occupied, float minimumSpacing)
    {
        if (occupied == null) return true;
        for (int i = 0; i < occupied.Count; i++)
            if (FlatDistance(candidate, occupied[i]) < minimumSpacing) return false;
        return true;
    }

    private bool TryGetSpawnSurface(Vector3 reference, Scene scene, out RaycastHit surface)
    {
        surface = default;
        int count = Physics.RaycastNonAlloc(reference + Vector3.up * 1f, Vector3.down,
            roamingGroundHits, 2f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        if (count == roamingGroundHits.Length) return false;
        float highest = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = roamingGroundHits[i];
            if (!IsMapObstacle(hit.collider) || hit.collider.gameObject.scene != scene || hit.point.y <= highest) continue;
            highest = hit.point.y;
            surface = hit;
        }
        return surface.collider != null && surface.normal.y >= Mathf.Cos(12f * Mathf.Deg2Rad);
    }

    private bool TryGetFlatSpawnSurface(Vector3 candidate, out float floorY)
    {
        floorY = float.NegativeInfinity;
        float lowest = float.PositiveInfinity;
        // Centre, sides and diagonals: a centre ray alone permits cliff edges.
        for (int z = -1; z <= 1; z++)
        {
            for (int x = -1; x <= 1; x++)
            {
                Vector3 offset = new Vector3(x, 0f, z).normalized * (duckRadius + 0.1f);
                if (!TryGetSpawnSurface(candidate + offset, scannedScene, out RaycastHit hit)
                    || Mathf.Abs(hit.point.y - mapY) > 0.3f) return false;
                floorY = Mathf.Max(floorY, hit.point.y);
                lowest = Mathf.Min(lowest, hit.point.y);
            }
        }
        return floorY - lowest <= 0.12f;
    }

    /// <summary>Forces a fresh plan after the motor reports a genuine blockage.</summary>
    public static void InvalidateRoute(List<Vector3> route, ref int routeIndex, ref float nextPlanTime)
    {
        route.Clear();
        routeIndex = 0;
        nextPlanTime = 0f;
    }

    /// <summary>
    /// Finds a short, map-clear recovery point on one chosen side of an
    /// obstacle. Keeping the caller's chosen side stable prevents a duck from
    /// bouncing left/right against the same piece of scenery.
    /// </summary>
    public static bool TryFindRecoveryPoint(
        Vector3 currentPosition,
        Vector3 destination,
        int side,
        out Vector3 recoveryPoint)
    {
        Unit1RouteScanner scanner = GetOrCreate();
        recoveryPoint = currentPosition;
        if (!scanner.EnsureScanned(currentPosition.y))
        {
            return false;
        }

        Vector3 forward = destination - currentPosition;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
        {
            return false;
        }

        forward.Normalize();
        Vector3 sideDirection = Vector3.Cross(Vector3.up, forward) * (side >= 0 ? 1f : -1f);
        float bestScore = float.PositiveInfinity;

        // First favour a forward diagonal, then a true sidestep, then a
        // slight backward diagonal. This escapes a close prop without making
        // a U-turn unless the forward route is genuinely closed.
        for (int option = 0; option < 3; option++)
        {
            float forwardWeight = option switch
            {
                0 => 0.65f,
                1 => 0.15f,
                _ => -0.55f
            };
            float distance = 1.65f + option * 0.75f;
            Vector3 candidateDirection = (sideDirection + forward * forwardWeight).normalized;
            int cell = scanner.FindNearestWalkable(currentPosition + candidateDirection * distance);
            if (cell < 0)
            {
                continue;
            }

            Vector3 candidate = scanner.CellCenter(cell);
            candidate.y = currentPosition.y;
            if (FlatDistance(currentPosition, candidate) < 0.45f
                || !scanner.IsLineWalkable(currentPosition, candidate))
            {
                continue;
            }

            float score = FlatDistance(candidate, destination) + option * 0.18f;
            if (score < bestScore)
            {
                bestScore = score;
                recoveryPoint = candidate;
            }
        }
        return bestScore < float.PositiveInfinity;
    }

    private static Unit1RouteScanner GetOrCreate()
    {
        if (instance != null)
        {
            return instance;
        }

        GameObject plannerObject = GameObject.Find(PlannerObjectName);
        if (plannerObject == null)
        {
            plannerObject = new GameObject(PlannerObjectName);
        }

        instance = plannerObject.GetComponent<Unit1RouteScanner>();
        if (instance == null)
        {
            instance = plannerObject.AddComponent<Unit1RouteScanner>();
        }

        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    private bool EnsureScanned(float duckY)
    {
        // The lobby can remain the active scene while UNIT1 is loaded
        // additively. Bind the scan to the generated map's own scene instead
        // of accidentally sampling lobby furniture in that arrangement.
        Scene navigationScene = GetNavigationScene();
        if (isScanned && scannedScene == navigationScene)
        {
            return true;
        }

        return ScanMap(navigationScene, duckY);
    }

    private static Scene GetNavigationScene()
    {
        GameObject mapRoot = GameObject.Find(GeneratedMapName);
        return mapRoot != null ? mapRoot.scene : SceneManager.GetActiveScene();
    }

    private bool ScanMap(Scene scene, float duckY)
    {
        if (!scene.IsValid())
        {
            return false;
        }

        scannedScene = scene;
        if (!TryGetMapBounds(out Bounds mapBounds))
        {
            // There are no static colliders at all.  This is a valid test
            // arena, not a failed route: callers may move directly because
            // no scene geometry exists to collide with.
            scannedScene = scene;
            isScanned = true;
            isOpenSpaceFallback = true;
            Debug.Log("[AI Brain] 沒有地圖碰撞體：使用開放場地移動。", this);
            return true;
        }

        Physics.SyncTransforms();
        isOpenSpaceFallback = false;
        spawnConnectedCells = System.Array.Empty<bool>();
        mapY = calibratedEnvelope ? groundY : duckY;
        cellSize = DefaultCellSize;
        mapBounds.Expand(new Vector3(cellSize * 2f, 0f, cellSize * 2f));
        while (EstimatedCellCount(mapBounds, cellSize) > MaximumCellCount)
        {
            cellSize *= 1.15f;
        }

        minX = mapBounds.min.x;
        minZ = mapBounds.min.z;
        width = Mathf.Max(1, Mathf.CeilToInt(mapBounds.size.x / cellSize));
        height = Mathf.Max(1, Mathf.CeilToInt(mapBounds.size.z / cellSize));
        int cellCount = width * height;
        walkable = new bool[cellCount];
        knownEdges = new ushort[cellCount];
        clearEdges = new ushort[cellCount];

        int clearCellCount = 0;
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = ToIndex(x, z);
                bool clear = IsCellClear(CellCenter(index));
                walkable[index] = clear;
                if (clear)
                {
                    clearCellCount++;
                }
            }
        }

        EnsureSearchCapacity(cellCount);
        scannedScene = scene;
        isScanned = clearCellCount > 0;
        if (isScanned)
        {
            Debug.Log($"[AI Brain] 已掃描 {scene.name}：{clearCellCount}/{cellCount} 個可走路格（{cellSize:0.00}m，固定場景幾何，半徑 {duckRadius:0.00}m）。", this);
        }
        else
        {
            Debug.LogWarning("[AI Brain] 地圖掃描沒有找到可走路格；小鴨暫時使用近距離避障。", this);
        }

        return isScanned;
    }

    private bool TryGetMapBounds(out Bounds mapBounds)
    {
        GameObject mapRoot = GameObject.Find(GeneratedMapName);
        Collider[] colliders = mapRoot != null
            ? mapRoot.GetComponentsInChildren<Collider>(true)
            : Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        // Static is an optimization flag, not a collision rule. Fixed props
        // must be included even when a Static floor exists in the same scene.
        return TryAccumulateMapBounds(colliders, out mapBounds);
    }

    private bool TryAccumulateMapBounds(
        Collider[] colliders,
        out Bounds mapBounds)
    {
        bool hasBounds = false;
        mapBounds = new Bounds();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (!IsMapObstacle(collider) || collider.gameObject.scene != scannedScene)
            {
                continue;
            }

            if (!hasBounds)
            {
                mapBounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                mapBounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds && mapBounds.size.x > 0.5f && mapBounds.size.z > 0.5f;
    }

    private static int EstimatedCellCount(Bounds bounds, float size)
    {
        int estimatedWidth = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / size));
        int estimatedHeight = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / size));
        return estimatedWidth * estimatedHeight;
    }

    private bool IsCellClear(Vector3 cellPosition)
    {
        return IsCellClearAtGround(cellPosition, mapY);
    }

    private bool IsCellClearAtGround(Vector3 cellPosition, float floorY)
    {
        Vector3 bottom = new(cellPosition.x, floorY + capsuleBottomOffset, cellPosition.z);
        Vector3 top = new(cellPosition.x, floorY + capsuleTopOffset, cellPosition.z);
        int count = Physics.OverlapCapsuleNonAlloc(
            bottom,
            top,
            duckRadius,
            overlapResults,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        // A full result buffer means we cannot prove that the cell is clear.
        if (count == overlapResults.Length)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            Collider collider = overlapResults[i];
            if (IsMapObstacle(collider) && collider.gameObject.scene == scannedScene)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsMapObstacle(Collider collider)
    {
        // Rigidbodies belong to ducks, pickups, and transient effects. Those
        // are tactical concerns, not permanent map routes.
        if (collider == null
            || !collider.enabled
            || !collider.gameObject.activeInHierarchy
            || collider.isTrigger
            || collider.attachedRigidbody != null)
        {
            return false;
        }

        return collider.GetComponentInParent<NetworkObject>() == null;
    }

    private void FindPath(Vector3 startPosition, Vector3 goalPosition, List<Vector3> route, float arrivalDistance = 0f)
    {
        route.Clear();
        if (IsLineWalkable(startPosition, goalPosition))
        {
            route.Add(goalPosition);
            return;
        }
        int start = FindNearestWalkable(startPosition, true);
        int goal = FindNearestWalkable(goalPosition);
        if (start < 0 || goal < 0)
        {
            return;
        }

        int cellCount = walkable.Length;
        for (int i = 0; i < cellCount; i++)
        {
            costFromStart[i] = float.PositiveInfinity;
            cameFrom[i] = -1;
            closed[i] = false;
        }

        openNodes.Clear();
        costFromStart[start] = 0f;
        PushOpen(start, EstimateCost(start, goal));

        bool reachedGoal = false;
        int reachedIndex = -1;
        while (openNodes.Count > 0)
        {
            int current = PopOpen();
            if (closed[current])
            {
                continue;
            }

            if ((current == goal && IsLineWalkable(CellCenter(current), goalPosition))
                || FlatDistance(CellCenter(current), goalPosition) <= arrivalDistance)
            {
                reachedGoal = true;
                reachedIndex = current;
                break;
            }

            closed[current] = true;
            GetCellCoordinates(current, out int currentX, out int currentZ);
            for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    if (offsetX == 0 && offsetZ == 0)
                    {
                        continue;
                    }

                    int nextX = currentX + offsetX;
                    int nextZ = currentZ + offsetZ;
                    if (!IsWalkable(nextX, nextZ))
                    {
                        continue;
                    }

                    // Do not let a diagonal squeeze between two blocked
                    // cells; the real capsule would not fit there either.
                    if (offsetX != 0 && offsetZ != 0
                        && (!IsWalkable(currentX + offsetX, currentZ)
                            || !IsWalkable(currentX, currentZ + offsetZ)))
                    {
                        continue;
                    }

                    int next = ToIndex(nextX, nextZ);
                    if (closed[next] || !IsEdgeWalkable(current, next, offsetX, offsetZ))
                    {
                        continue;
                    }

                    float stepCost = offsetX == 0 || offsetZ == 0 ? 1f : 1.41421356f;
                    float candidateCost = costFromStart[current] + stepCost;
                    if (candidateCost >= costFromStart[next])
                    {
                        continue;
                    }

                    cameFrom[next] = current;
                    costFromStart[next] = candidateCost;
                    PushOpen(next, candidateCost + EstimateCost(next, goal));
                }
            }
        }

        if (!reachedGoal)
        {
            return;
        }

        reconstructedNodes.Clear();
        int pathNode = reachedIndex;
        while (pathNode >= 0)
        {
            reconstructedNodes.Add(pathNode);
            if (pathNode == start)
            {
                break;
            }

            pathNode = cameFrom[pathNode];
        }

        if (reconstructedNodes.Count == 0 || reconstructedNodes[reconstructedNodes.Count - 1] != start)
        {
            route.Clear();
            return;
        }

        // Connect the real position to its cell centre first. Simply skipping
        // this node can cut a wall when the duck is off-centre in a coarse cell.
        for (int i = reconstructedNodes.Count - 1; i >= 0; i--)
        {
            route.Add(CellCenter(reconstructedNodes[i]));
        }

        if (route.Count > 0 && IsLineWalkable(route[route.Count - 1], goalPosition))
        {
            route.Add(goalPosition);
        }
    }

    private bool IsLineWalkable(Vector3 from, Vector3 to)
    {
        if (PositionToIndex(from) < 0 || PositionToIndex(to) < 0
            || !IsCellClear(from) || !IsCellClear(to)) return false;
        float distance = FlatDistance(from, to);
        if (distance < 0.001f) return true;
        Vector3 direction = to - from;
        direction.y = 0f;
        int count = Physics.CapsuleCastNonAlloc(
            new Vector3(from.x, mapY + capsuleBottomOffset, from.z),
            new Vector3(from.x, mapY + capsuleTopOffset, from.z), duckRadius,
            direction / distance, segmentHits, distance, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        if (count == segmentHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Collider hit = segmentHits[i].collider;
            if (IsMapObstacle(hit) && hit.gameObject.scene == scannedScene) return false;
        }
        return true;
    }

    private bool IsEdgeWalkable(int from, int to, int dx, int dz)
    {
        int direction = (dz + 1) * 3 + dx + 1;
        ushort bit = (ushort)(1 << direction);
        if ((knownEdges[from] & bit) != 0) return (clearEdges[from] & bit) != 0;
        ushort reverse = (ushort)(1 << (8 - direction));
        bool clear = IsLineWalkable(CellCenter(from), CellCenter(to));
        knownEdges[from] |= bit;
        knownEdges[to] |= reverse;
        if (clear) { clearEdges[from] |= bit; clearEdges[to] |= reverse; }
        return clear;
    }

    private int FindNearestWalkable(Vector3 position, bool requireConnection = false)
    {
        int centreX = Mathf.Clamp(Mathf.FloorToInt((position.x - minX) / cellSize), 0, width - 1);
        int centreZ = Mathf.Clamp(Mathf.FloorToInt((position.z - minZ) / cellSize), 0, height - 1);
        if (IsUsableCell(centreX, centreZ, position, requireConnection))
        {
            return ToIndex(centreX, centreZ);
        }

        for (int radius = 1; radius <= NearestCellSearchRadius; radius++)
        {
            int minSearchX = centreX - radius;
            int maxSearchX = centreX + radius;
            int minSearchZ = centreZ - radius;
            int maxSearchZ = centreZ + radius;
            for (int x = minSearchX; x <= maxSearchX; x++)
            {
                if (IsUsableCell(x, minSearchZ, position, requireConnection))
                {
                    return ToIndex(x, minSearchZ);
                }

                if (IsUsableCell(x, maxSearchZ, position, requireConnection))
                {
                    return ToIndex(x, maxSearchZ);
                }
            }

            for (int z = minSearchZ + 1; z < maxSearchZ; z++)
            {
                if (IsUsableCell(minSearchX, z, position, requireConnection))
                {
                    return ToIndex(minSearchX, z);
                }

                if (IsUsableCell(maxSearchX, z, position, requireConnection))
                {
                    return ToIndex(maxSearchX, z);
                }
            }
        }

        return -1;
    }

    private bool IsUsableCell(int x, int z, Vector3 position, bool requireConnection)
    {
        return IsWalkable(x, z)
            && (!requireConnection || IsLineWalkable(position, CellCenter(ToIndex(x, z))));
    }

    private void EnsureSearchCapacity(int cellCount)
    {
        if (costFromStart.Length == cellCount)
        {
            return;
        }

        costFromStart = new float[cellCount];
        cameFrom = new int[cellCount];
        closed = new bool[cellCount];
    }

    private void PushOpen(int index, float priority)
    {
        OpenNode node = new(index, priority);
        openNodes.Add(node);
        int child = openNodes.Count - 1;
        while (child > 0)
        {
            int parent = (child - 1) / 2;
            if (openNodes[parent].Priority <= node.Priority)
            {
                break;
            }

            openNodes[child] = openNodes[parent];
            child = parent;
        }

        openNodes[child] = node;
    }

    private int PopOpen()
    {
        OpenNode root = openNodes[0];
        OpenNode last = openNodes[openNodes.Count - 1];
        openNodes.RemoveAt(openNodes.Count - 1);
        if (openNodes.Count == 0)
        {
            return root.Index;
        }

        int parent = 0;
        while (true)
        {
            int left = parent * 2 + 1;
            if (left >= openNodes.Count)
            {
                break;
            }

            int right = left + 1;
            int smallest = right < openNodes.Count && openNodes[right].Priority < openNodes[left].Priority
                ? right
                : left;
            if (openNodes[smallest].Priority >= last.Priority)
            {
                break;
            }

            openNodes[parent] = openNodes[smallest];
            parent = smallest;
        }

        openNodes[parent] = last;
        return root.Index;
    }

    private float EstimateCost(int from, int to)
    {
        GetCellCoordinates(from, out int fromX, out int fromZ);
        GetCellCoordinates(to, out int toX, out int toZ);
        int horizontal = Mathf.Abs(fromX - toX);
        int vertical = Mathf.Abs(fromZ - toZ);
        int diagonal = Mathf.Min(horizontal, vertical);
        return diagonal * 1.41421356f + (Mathf.Max(horizontal, vertical) - diagonal);
    }

    private bool IsWalkable(int x, int z)
    {
        return x >= 0 && x < width && z >= 0 && z < height && walkable[ToIndex(x, z)];
    }

    private int PositionToIndex(Vector3 position)
    {
        int x = Mathf.FloorToInt((position.x - minX) / cellSize);
        int z = Mathf.FloorToInt((position.z - minZ) / cellSize);
        return x >= 0 && x < width && z >= 0 && z < height ? ToIndex(x, z) : -1;
    }

    private int ToIndex(int x, int z)
    {
        return z * width + x;
    }

    private void GetCellCoordinates(int index, out int x, out int z)
    {
        x = index % width;
        z = index / width;
    }

    private Vector3 CellCenter(int index)
    {
        GetCellCoordinates(index, out int x, out int z);
        return new Vector3(minX + (x + 0.5f) * cellSize, mapY, minZ + (z + 0.5f) * cellSize);
    }

    private static float FlatDistance(Vector3 first, Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;
        return Vector3.Distance(first, second);
    }
}
