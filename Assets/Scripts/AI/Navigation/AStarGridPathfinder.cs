using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// build 8-direction A* grid from grass bounds, check obstacles with physics
public class AStarGridPathfinder : MonoBehaviour, IPathfinder
{
    [SerializeField, Min(0.6f)] float cellSize = 1.5f;
    [SerializeField, Range(0.15f, 0.6f)] float agentRadius = 0.32f;
    [SerializeField] bool useDangerCosts = false;
    [SerializeField, Min(0f)] float dangerPenalty = 4f;
    [SerializeField] bool showGridGizmos = false;

    bool[,] walkable;
    Vector3 firstCell;
    int width, height;
    readonly Collider[] overlapBuffer = new Collider[32];
    readonly List<IThreat> threats = new List<IThreat>();

    public int LastExpandedNodes { get; private set; }
    public int LastWaypointCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegisterSceneCallback()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid() || Object.FindFirstObjectByType<AStarGridPathfinder>() != null)
            return;

        var go = new GameObject("Person B - AStar Grid");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<AStarGridPathfinder>();
    }

    void Start()
    {
        if (BuildGrid())
        {
            Pathfinding.Current = this;
            Debug.Log($"[Person B/A*] Ready: {width} x {height} cells, size {cellSize}m.");
        }
    }

    void OnDestroy()
    {
        if (ReferenceEquals(Pathfinding.Current, this))
            Pathfinding.Current = null;
    }

    bool BuildGrid()
    {
        GameObject ground = GameObject.Find("Neighbourhood_Greybox/Ground/Grass");
        if (ground == null) ground = GameObject.Find("Grass");
        var groundCollider = ground != null ? ground.GetComponent<BoxCollider>() : null;
        if (groundCollider == null)
        {
            Debug.LogWarning("[Person B/A*] Greybox Grass collider missing; keeping NavMesh fallback.");
            return false;
        }

        Bounds b = groundCollider.bounds;
        width = Mathf.FloorToInt(b.size.x / cellSize);
        height = Mathf.FloorToInt(b.size.z / cellSize);
        if (width < 2 || height < 2) return false;

        firstCell = new Vector3(b.min.x + cellSize * 0.5f, b.max.y, b.min.z + cellSize * 0.5f);
        walkable = new bool[width, height];

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            Vector3 p = CellPosition(x, y);
            // Probe above floor level so the ground itself is not treated as a wall.
            Vector3 centre = p + Vector3.up * 0.95f;
            Vector3 halfSize = new Vector3(agentRadius, 0.7f, agentRadius);
            int count = Physics.OverlapBoxNonAlloc(centre, halfSize, overlapBuffer,
                                                 Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            bool blocked = false;
            for (int i = 0; i < count; i++)
            {
                if (IsObstacle(overlapBuffer[i])) { blocked = true; break; }
            }
            walkable[x, y] = !blocked;
        }
        return true;
    }

    static bool IsObstacle(Collider c)
    {
        if (c == null || c.isTrigger) return false;
        // Ignore moving actors, which must not become permanent walls in the grid.
        if (c.GetComponentInParent<NavMeshAgent>() != null) return false;
        if (c.GetComponentInParent<PlayerController>() != null) return false;
        if (c.attachedRigidbody != null) return false;
        return true;
    }

    Vector3 CellPosition(int x, int y) =>
        firstCell + new Vector3(x * cellSize, 0f, y * cellSize);

    Vector2Int CellOf(Vector3 p) => new Vector2Int(
        Mathf.RoundToInt((p.x - firstCell.x) / cellSize),
        Mathf.RoundToInt((p.z - firstCell.z) / cellSize));

    bool IsFree(int x, int y) =>
        x >= 0 && y >= 0 && x < width && y < height && walkable[x, y];

    bool NearestFree(Vector3 p, out Vector2Int best)
    {
        Vector2Int centre = CellOf(p);
        float bestDist = float.MaxValue;
        best = new Vector2Int(-1, -1);
        // Searching a local area also handles destinations just inside an obstacle.
        for (int dx = -4; dx <= 4; dx++)
        for (int dy = -4; dy <= 4; dy++)
        {
            int x = centre.x + dx, y = centre.y + dy;
            if (!IsFree(x, y)) continue;
            float d = (CellPosition(x, y) - p).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = new Vector2Int(x, y); }
        }
        return best.x >= 0;
    }

    static float Octile(Vector2Int a, Vector2Int b)
    {
        int dx = Mathf.Abs(a.x - b.x);
        int dy = Mathf.Abs(a.y - b.y);
        return Mathf.Max(dx, dy) + (1.41421356f - 1f) * Mathf.Min(dx, dy);
    }

    public bool TryFindPath(Vector3 start, Vector3 goal, List<Vector3> path)
    {
        path.Clear();
        LastExpandedNodes = 0;
        LastWaypointCount = 0;
        if (walkable == null || !NearestFree(start, out Vector2Int first) ||
            !NearestFree(goal, out Vector2Int last)) return false;
        // Do not claim to reach a destination deep inside a building.
        if (Vector3.Distance(CellPosition(last.x, last.y), goal) > cellSize * 1.6f)
            return false;

        float[,] g = new float[width, height];
        bool[,] closed = new bool[width, height];
        bool[,] inOpen = new bool[width, height];
        Vector2Int[,] parent = new Vector2Int[width, height];
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            g[x, y] = float.PositiveInfinity;

        var open = new List<Vector2Int> { first };
        g[first.x, first.y] = 0f;
        inOpen[first.x, first.y] = true;
        if (useDangerCosts) FindThreats();
        bool found = false;

        while (open.Count > 0)
        {
            int bestIndex = 0;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < open.Count; i++)
            {
                Vector2Int n = open[i];
                float f = g[n.x, n.y] + Octile(n, last);
                if (f < bestScore) { bestScore = f; bestIndex = i; }
            }

            Vector2Int current = open[bestIndex];
            open.RemoveAt(bestIndex);
            inOpen[current.x, current.y] = false;
            closed[current.x, current.y] = true;
            LastExpandedNodes++;

            if (current == last) { found = true; break; }

            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = current.x + dx, ny = current.y + dy;
                if (!IsFree(nx, ny) || closed[nx, ny]) continue;
                // Never walk diagonally through the corner of a building.
                if (dx != 0 && dy != 0 &&
                    (!IsFree(current.x + dx, current.y) ||
                     !IsFree(current.x, current.y + dy))) continue;

                float step = (dx == 0 || dy == 0) ? 1f : 1.41421356f;
                float newG = g[current.x, current.y] + step + DangerCost(nx, ny);
                if (newG >= g[nx, ny]) continue;
                g[nx, ny] = newG;
                parent[nx, ny] = current;
                if (!inOpen[nx, ny])
                {
                    open.Add(new Vector2Int(nx, ny));
                    inOpen[nx, ny] = true;
                }
            }
        }
        if (!found) return false;

        var raw = new List<Vector3>();
        Vector2Int at = last;
        raw.Add(CellPosition(at.x, at.y));
        while (at != first)
        {
            at = parent[at.x, at.y];
            raw.Add(CellPosition(at.x, at.y));
        }
        raw.Reverse();
        raw[0] = start;
        // Preserve the exact goal only if it was genuinely walkable.
        if (IsFree(CellOf(goal).x, CellOf(goal).y)) raw[raw.Count - 1] = goal;

        // Check the actual reconstructed route, not only grid-cell centres.
        // If a narrow wall crosses a grid edge, this prevents walking through it.
        for (int i = 1; i < raw.Count; i++)
            if (!ClearSegment(raw[i - 1], raw[i])) return false;

        // Remove intermediate points when a straight segment is clear.
        int index = 0;
        path.Add(raw[0]);
        while (index < raw.Count - 1)
        {
            int next = raw.Count - 1;
            while (next > index + 1 && !ClearSegment(raw[index], raw[next])) next--;
            path.Add(raw[next]);
            index = next;
        }
        LastWaypointCount = path.Count;
        return true;
    }

    bool ClearSegment(Vector3 start, Vector3 end)
    {
        // A sphere cast gives a little clearance around the walking character.
        Vector3 a = start + Vector3.up * 0.95f;
        Vector3 b = end + Vector3.up * 0.95f;
        Vector3 v = b - a;
        float length = v.magnitude;
        if (length < 0.01f) return true;
        RaycastHit[] hits = Physics.SphereCastAll(a, agentRadius, v / length,
                                                 length, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
            if (IsObstacle(hit.collider)) return false;
        return true;
    }

    void FindThreats()
    {
        threats.Clear();
        foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (behaviour is IThreat t && t.IsHostile) threats.Add(t);
    }

    float DangerCost(int x, int y)
    {
        if (!useDangerCosts) return 0f;
        Vector3 p = CellPosition(x, y);
        float extra = 0f;
        foreach (IThreat threat in threats)
        {
            float d = Vector3.Distance(p, threat.Position);
            if (d < threat.DangerRadius && threat.DangerRadius > 0f)
                extra += dangerPenalty * (1f - d / threat.DangerRadius);
        }
        return extra;
    }

    void OnDrawGizmosSelected()
    {
        if (!showGridGizmos || walkable == null) return;
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            Gizmos.color = walkable[x, y] ? new Color(0.2f, 0.8f, 0.2f, 0.3f) :
                                          new Color(1f, 0.2f, 0.2f, 0.6f);
            Gizmos.DrawCube(CellPosition(x, y) + Vector3.up * 0.03f,
                            new Vector3(cellSize * 0.85f, 0.02f, cellSize * 0.85f));
        }
    }
}
