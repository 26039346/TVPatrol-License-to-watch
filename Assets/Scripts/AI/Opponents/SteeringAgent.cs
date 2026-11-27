using System.Collections.Generic;
using UnityEngine;

// 1. move using steering forces, limit force and speed each frame
// 2. follow A* points and slow down at the end
// 3. move directly with wall avoidance if no pathfinder
// 4. sphere cast stops movement through walls
public class SteeringAgent : MonoBehaviour
{
    [Header("Limits")]
    // max acceleration, m/s²
    [SerializeField] float maxForce = 25f;
    // turn speed in degrees per second
    [SerializeField] float turnSpeed = 540f;
    // collision radius
    [SerializeField] float radius = 0.35f;
    // wall check height
    [SerializeField] float bodyHeight = 0.45f;

    [Header("Arrive")]
    // slow down within this distance
    [SerializeField] float slowingRadius = 2.5f;
    [SerializeField] float stopDistance = 0.3f;

    [Header("Path following (A*)")]
    // switch waypoint within this distance
    [SerializeField] float waypointRadius = 0.8f;
    // seconds between path updates
    [UnityEngine.Serialization.FormerlySerializedAs("repathInterval")]
    [SerializeField] float pathUpdateIntervalSeconds = 0.4f;
    // update path early if goal moves this far
    [UnityEngine.Serialization.FormerlySerializedAs("repathGoalMove")]
    [SerializeField] float goalMoveThreshold = 1f;

    [Header("Pursue")]
    // max prediction time in seconds
    [UnityEngine.Serialization.FormerlySerializedAs("maxPrediction")]
    [SerializeField] float maxPredictionSeconds = 1.2f;

    [Header("Wander")]
    // wander circle distance ahead
    [SerializeField] float wanderDistance = 1.5f;
    [SerializeField] float wanderRadius = 1f;
    // wander target drift speed
    [SerializeField] float wanderJitter = 4f;

    [Header("Wall avoidance")]
    [UnityEngine.Serialization.FormerlySerializedAs("feelerLength")]
    [SerializeField] float wallCheckDistance = 1.6f;
    [UnityEngine.Serialization.FormerlySerializedAs("feelerAngle")]
    [SerializeField] float wallCheckAngle = 35f;
    [SerializeField] float avoidanceWeight = 3f;

    [Header("Debug")]
    [SerializeField] bool drawGizmos = true;

    enum Mode { Idle, MoveTo, Pursue, Sniff }

    Mode mode = Mode.Idle;
    Vector3 velocity;
    float maxSpeed = 3f;
    float groundHeight;

    // MoveTo / Pursue
    Vector3 goal;
    readonly List<Vector3> path = new List<Vector3>();
    int waypointIndex;
    bool hasPath;
    float nextPathUpdateTime;
    Vector3 lastPathGoal;
    IPathfinder currentPathfinder;

    // Pursue
    Transform chaseTarget;
    Vector3 lastTargetPosition;
    Vector3 targetVelocity;

    // Sniff (wander tethered to a point)
    Vector3 sniffCentre;
    float sniffRadius;
    Vector3 wanderTarget = Vector3.forward;

    readonly RaycastHit[] hits = new RaycastHit[8];

    public Vector3 Velocity => velocity;
    public float Speed => velocity.magnitude;
    // true if destination reached or idle
    public bool HasArrived => mode == Mode.Idle || (mode == Mode.MoveTo && FlatDistance(transform.position, goal) <= stopDistance + 0.2f);
    // true if following A* path, false if moving directly
    public bool UsingAStar => hasPath;

    void Awake()
    {
        groundHeight = transform.position.y;
        // Disable NavMeshAgent to prevent competing movement updates.
        var legacy = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (legacy != null) legacy.enabled = false;
    }


    // follow A* path to target and slow down on arrival
    public void MoveTo(Vector3 destination, float speed)
    {
        maxSpeed = speed;
        if (mode != Mode.MoveTo || FlatDistance(destination, goal) > 0.1f)
        {
            mode = Mode.MoveTo;
            goal = destination;
            // update path now
            nextPathUpdateTime = 0f;
        }
    }

    // chase predicted target position
    public void Pursue(Transform target, float speed)
    {
        maxSpeed = speed;
        if (mode != Mode.Pursue || chaseTarget != target)
        {
            mode = Mode.Pursue;
            chaseTarget = target;
            lastTargetPosition = target.position;
            targetVelocity = Vector3.zero;
            nextPathUpdateTime = 0f;
        }
    }

    // wander around point within radius
    public void Sniff(Vector3 centre, float radius, float speed)
    {
        maxSpeed = speed;
        mode = Mode.Sniff;
        sniffCentre = centre;
        sniffRadius = radius;
        hasPath = false;
    }

    // slow down and stop
    public void Stop()
    {
        mode = Mode.Idle;
        hasPath = false;
    }

    // turn towards point without moving
    public void FaceTowards(Vector3 point)
    {
        Vector3 direction = point - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), turnSpeed * Time.deltaTime);
    }


    void Update()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f) return;

        Vector3 force = mode switch
        {
            Mode.MoveTo => FollowRoute(goal, true),
            Mode.Pursue => PursueForce(deltaTime),
            Mode.Sniff => SniffForce(deltaTime),
            // brake while idle
            _ => -velocity / Mathf.Max(deltaTime, 0.05f),
        };
        if (mode != Mode.Idle) force += WallAvoidance() * avoidanceWeight;
        force = Flat(Vector3.ClampMagnitude(force, maxForce));

        velocity = Vector3.ClampMagnitude(velocity + force * deltaTime, mode == Mode.Idle ? velocity.magnitude : maxSpeed);
        if (mode == Mode.Idle && velocity.sqrMagnitude < 0.01f) velocity = Vector3.zero;

        Move(velocity * deltaTime);

        // Face the direction of travel.
        if (velocity.sqrMagnitude > 0.05f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(velocity), turnSpeed * deltaTime);
    }


    // move straight towards target at full speed
    Vector3 Seek(Vector3 target)
    {
        Vector3 desired = Flat(target - transform.position).normalized * maxSpeed;
        return desired - velocity;
    }

    // slow down inside slowingRadius
    Vector3 Arrive(Vector3 target)
    {
        Vector3 toTarget = Flat(target - transform.position);
        float distance = toTarget.magnitude;
        // brake hard on the spot
        if (distance <= stopDistance) return -velocity * 4f;
        float speed = maxSpeed * Mathf.Clamp01(distance / slowingRadius);
        Vector3 desired = toTarget / distance * speed;
        return desired - velocity;
    }

    // predict target position from velocity, limit prediction to maxPredictionSeconds
    Vector3 PursueForce(float deltaTime)
    {
        if (chaseTarget == null) { Stop(); return Vector3.zero; }

        // Estimate the target's velocity from its movement (smoothed so jitter doesn't swing the prediction).
        Vector3 measured = Flat(chaseTarget.position - lastTargetPosition) / deltaTime;
        targetVelocity = Vector3.Lerp(targetVelocity, measured, 1f - Mathf.Exp(-8f * deltaTime));
        lastTargetPosition = chaseTarget.position;

        float distance = FlatDistance(transform.position, chaseTarget.position);
        float prediction = Mathf.Min(distance / Mathf.Max(maxSpeed, 0.1f), maxPredictionSeconds);
        Vector3 predicted = chaseTarget.position + targetVelocity * prediction;

        // Close and in plain view: pure pursuit. Otherwise route round obstacles with A*.
        if (distance < 4f && ClearLine(transform.position, predicted))
        {
            hasPath = false;
            return Seek(predicted);
        }
        return FollowRoute(predicted, false);
    }

    // 1. move random target around circle ahead
    // 2. head back to centre if outside search area
    Vector3 SniffForce(float deltaTime)
    {
        wanderTarget += new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * wanderJitter * deltaTime;
        wanderTarget = wanderTarget.normalized * wanderRadius;
        Vector3 forward = velocity.sqrMagnitude > 0.01f ? velocity.normalized : transform.forward;
        Vector3 circleCentre = transform.position + forward * wanderDistance;
        Vector3 wander = Seek(circleCentre + wanderTarget);

        float outside = FlatDistance(transform.position, sniffCentre) - sniffRadius;
        Vector3 tether = outside > 0f ? Seek(sniffCentre) * Mathf.Clamp01(outside) * 2f : Vector3.zero;
        return wander + tether;
    }

    // 1. get A* route and follow each point
    // 2. update route if goal moves, slow down at end if needed
    Vector3 FollowRoute(Vector3 target, bool brakeAtEnd)
    {
        IPathfinder pathfinder = Pathfinding.Current;
        bool goalMoved = FlatDistance(target, lastPathGoal) > goalMoveThreshold;
        if (Time.time >= nextPathUpdateTime || goalMoved || !ReferenceEquals(pathfinder, currentPathfinder))
        {
            currentPathfinder = pathfinder;
            lastPathGoal = target;
            nextPathUpdateTime = Time.time + pathUpdateIntervalSeconds;
            hasPath = pathfinder != null && pathfinder.TryFindPath(transform.position, target, path) && path.Count > 0;
            // path[0] is our own position
            waypointIndex = path.Count > 1 ? 1 : 0;
        }

        if (!hasPath)
            // seek directly if no pathfinder
            return brakeAtEnd ? Arrive(target) : Seek(target);

        while (waypointIndex < path.Count - 1 && FlatDistance(transform.position, path[waypointIndex]) <= waypointRadius)
            waypointIndex++;

        bool last = waypointIndex >= path.Count - 1;
        Vector3 waypoint = last ? target : path[waypointIndex];
        return last && brakeAtEnd ? Arrive(waypoint) : Seek(waypoint);
    }

    // 1. check ahead, left and right for walls
    // 2. push away more when closer to wall
    Vector3 WallAvoidance()
    {
        Vector3 forward = velocity.sqrMagnitude > 0.01f ? velocity.normalized : transform.forward;
        float length = wallCheckDistance + Speed * 0.3f;
        Vector3 origin = Eye();
        Vector3 force = Vector3.zero;

        for (int i = -1; i <= 1; i++)
        {
            Vector3 direction = Quaternion.Euler(0f, i * wallCheckAngle, 0f) * forward;
            float feeler = i == 0 ? length : length * 0.7f;
            if (NearestWall(origin, direction, feeler, 0f, out RaycastHit hit))
            {
                float penetration = feeler - hit.distance;
                force += Flat(hit.normal) * penetration * maxSpeed;
            }
        }
        return force;
    }


    // move by delta and slide along walls
    void Move(Vector3 delta)
    {
        for (int attempt = 0; attempt < 2 && delta.sqrMagnitude > 1e-6f; attempt++)
        {
            float distance = delta.magnitude;
            if (!NearestWall(Eye(), delta / distance, distance + 0.02f, radius, out RaycastHit hit)) break;
            // Keep the part of the motion that runs along the wall, and remove the part going into it.
            Vector3 normal = Flat(hit.normal).normalized;
            float allowed = Mathf.Max(0f, hit.distance - 0.02f);
            transform.position += delta.normalized * allowed;
            delta = Vector3.ProjectOnPlane(delta.normalized * (distance - allowed), normal);
            velocity = Vector3.ProjectOnPlane(velocity, normal);
        }
        Vector3 p = transform.position + delta;
        // stay at ground height
        p.y = groundHeight;
        transform.position = p;
    }

    bool ClearLine(Vector3 from, Vector3 to)
    {
        Vector3 d = Flat(to - from);
        float distance = d.magnitude;
        return distance < 0.01f || !NearestWall(Eye(), d / distance, distance, radius * 0.8f, out _);
    }

    // ray or sphere cast, ignore ground, triggers, characters and self
    bool NearestWall(Vector3 origin, Vector3 direction, float distance, float castRadius, out RaycastHit nearest)
    {
        int count = castRadius > 0f
            ? Physics.SphereCastNonAlloc(origin, castRadius, direction, hits, distance, ~0, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(origin, direction, hits, distance, ~0, QueryTriggerInteraction.Ignore);

        nearest = default;
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider c = hits[i].collider;
            if (c.transform.IsChildOf(transform)) continue;
            if (c is CharacterController || c.attachedRigidbody != null || c.CompareTag("Player")) continue;
            // skip hits starting inside collider
            if (hits[i].distance <= 0f) continue;
            // floor, not a wall
            if (hits[i].normal.y > 0.7f) continue;
            if (hits[i].distance < best) { best = hits[i].distance; nearest = hits[i]; }
        }
        return best < float.MaxValue;
    }

    Vector3 Eye() => new Vector3(transform.position.x, groundHeight + bodyHeight, transform.position.z);
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static float FlatDistance(Vector3 a, Vector3 b) => Flat(a - b).magnitude;

    void OnDrawGizmos()
    {
        if (!drawGizmos || !Application.isPlaying) return;
        // A* route (green), velocity (blue), feelers (yellow).
        if (hasPath)
        {
            Gizmos.color = Color.green;
            for (int i = 1; i < path.Count; i++) Gizmos.DrawLine(path[i - 1] + Vector3.up * 0.1f, path[i] + Vector3.up * 0.1f);
        }
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(Eye(), Eye() + velocity);
        Gizmos.color = Color.yellow;
        Vector3 forward = velocity.sqrMagnitude > 0.01f ? velocity.normalized : transform.forward;
        for (int i = -1; i <= 1; i++)
            Gizmos.DrawRay(Eye(), Quaternion.Euler(0f, i * wallCheckAngle, 0f) * forward * (i == 0 ? wallCheckDistance : wallCheckDistance * 0.7f));
        if (mode == Mode.Sniff)
        {
            Gizmos.color = new Color(0.6f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(sniffCentre, sniffRadius);
        }
    }
}
