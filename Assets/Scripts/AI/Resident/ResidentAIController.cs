using System.Collections.Generic;
using TVPatrol.AI.BehaviourTree;
using UnityEngine;
using UnityEngine.AI;

// 1. resident behaviour tree, suspicion rises near house and drops when player leaves
// 2. priority: confront, warn, watch, go home, wander garden
// 3. chase after warning when suspicion full, stop when suspicion drops to half
// 4. reset warning when suspicion reaches 0
// 5. sight raises suspicion faster, footsteps and alarms also raise it
// 6. warn or turn hostile to alert dog
[RequireComponent(typeof(NavMeshAgent), typeof(Perception))]
public class ResidentAIController : MonoBehaviour, IThreat
{
    public enum Mood { Unaware, Suspicious, Warning, Hostile, CalmingDown }

    [SerializeField] Transform player;
    [SerializeField] Transform[] gardenPoints;

    [Header("Suspicion")]
    // distance from home that raises suspicion
    [SerializeField] float awarenessRadius = 9f;
    // suspicion gained per second at radius edge
    [SerializeField] float suspicionRise = 0.25f;
    // suspicion multiplier near door
    [SerializeField] float closeMultiplier = 2f;
    // suspicion lost per second while player away
    [SerializeField] float suspicionDecay = 0.2f;
    [SerializeField, Range(0f, 1f)] float warnThreshold = 0.5f;
    // suspicion multiplier if nearby but unseen
    [SerializeField, Range(0f, 1f)] float unseenRateScale = 0.4f;
    // suspicion per footstep on property
    [SerializeField] float footstepSuspicion = 0.05f;
    // suspicion from nearby dog alarm
    [SerializeField] float alarmSuspicion = 0.3f;
    [SerializeField] float alarmRadius = 25f;

    [Header("Confront")]
    [SerializeField] float warningDuration = 1.5f;
    // max chase distance from home
    [SerializeField] float leashRadius = 16f;
    [SerializeField] float walkSpeed = 1.8f;
    // faster than walking, slower than sprinting
    [SerializeField] float chaseSpeed = 4.6f;
    [SerializeField] float attackRange = 1.5f;
    [SerializeField] float attackCooldown = 2f;
    [SerializeField] int attackDamage = 1;

    [Header("Memory (Person B extension)")]
    [SerializeField] float memorySeconds = 7f;
    [SerializeField] float investigateStoppingDistance = 1.1f;

    [Header("Custom navigation (Person B extension)")]
    [SerializeField] float pathRefreshSeconds = 1f;

    [Header("Feedback")]
    [SerializeField] Renderer bodyRenderer;
    [SerializeField] AudioSource voiceSource;

    public Mood CurrentMood { get; private set; }
    public float Suspicion { get; private set; }
    public string ActiveBranch { get; private set; }

    // IThreat
    public Vector3 Position => transform.position;
    public bool IsHostile => CurrentMood == Mood.Hostile || CurrentMood == Mood.Warning;
    public float DangerRadius => awarenessRadius;

    NavMeshAgent agent;
    Perception perception;
    bool alarmRaisedThisConfrontation;
    PlayerHealth playerHealth;
    Node root;
    Vector3 home;
    bool warned;
    float warningStartTime = -1f;
    float nextAttackTime;
    float nextPotterTime;
    int gardenIndex;
    MaterialPropertyBlock colourBlock;
    Vector3 lastSeenPosition;
    float lastSeenTime = float.NegativeInfinity;
    bool hasSeenPlayer;
    readonly List<Vector3> route = new List<Vector3>();
    int routeIndex;
    float nextRouteSearch;
    Vector3 routeGoal;
    IPathfinder routeProvider;
    bool hasRoute;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        perception = GetComponent<Perception>();
        perception.SoundHeard += OnHeard;
        home = transform.position;
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
        if (player != null) playerHealth = player.GetComponent<PlayerHealth>();
        if (voiceSource != null && voiceSource.clip == null) voiceSource.clip = WarningClip.Get();
        colourBlock = new MaterialPropertyBlock();
        root = BuildTree();
    }

    Node BuildTree()
    {
        return new Selector("Root",
            new Sequence("Confront",
                new Condition("Warned and still here", ShouldConfront),
                new Selector("Attack or chase",
                    new Sequence("Attack",
                        new Condition("In attack range and visible", () =>
                            player != null && perception.CanSee(player) && DistanceToPlayer() <= attackRange),
                        new ActionNode("Swing", Attack)),
                    new ActionNode("Chase", Chase))),
            new Sequence("Warn",
                new Condition("Suspicious enough, not warned", () => Suspicion >= warnThreshold && !warned),
                new ActionNode("Cock shotgun", Warn)),
            // Look where the inspector was last seen, without knowing their hidden position.
            new Cooldown("Search cooldown",
                new Sequence("Investigate",
                    new Condition("Lost inspector but remembers", ShouldInvestigate),
                    new ActionNode("Search last position", Investigate)),
                2f, () => Time.time),
            new Sequence("Watch",
                new Condition("Any suspicion", () => Suspicion > 0f),
                new ActionNode("Glare from the door", Watch)),
            new Sequence("Go home",
                new Condition("Away from home", () => Vector3.Distance(transform.position, home) > awarenessRadius),
                new ActionNode("Walk home", GoHome)),
            new ActionNode("Potter", Potter));
    }

    void Update()
    {
        UpdateSuspicion();
        root.Tick();
        UpdateColour();
    }


    // 1. start chase after warning when suspicion full
    // 2. keep chasing until suspicion drops to half to avoid switching constantly
    bool ShouldConfront()
    {
        if (!warned || PlayerDistanceFromHome() > leashRadius) return false;
        if (!perception.CanSee(player) && !HasRecentMemory()) return false;
        return Suspicion >= 1f || (CurrentMood == Mood.Hostile && Suspicion > 0.5f);
    }

    void UpdateSuspicion()
    {
        if (player == null) return;
        float d = PlayerDistanceFromHome();
        // Remember the last place the inspector was actually visible.
        bool canSee = perception.CanSee(player);
        if (canSee && d <= leashRadius)
        {
            lastSeenPosition = player.position;
            lastSeenTime = Time.time;
            hasSeenPlayer = true;
        }
        if (d <= awarenessRadius)
        {
            // 1. suspicion rises faster near house
            // 2. full rate if seen, reduced rate if nearby but unseen
            float closeness = 1f - d / awarenessRadius;
            float sightScale = canSee ? 1f : unseenRateScale;
            Suspicion += suspicionRise * Mathf.Lerp(1f, closeMultiplier, closeness) * sightScale * Time.deltaTime;
        }
        else
        {
            Suspicion -= suspicionDecay * Time.deltaTime;
        }
        Suspicion = Mathf.Clamp01(Suspicion);

        // Fully calmed down: forget the warning so the next visit starts the cycle again.
        if (Suspicion <= 0f)
        {
            warned = false;
            alarmRaisedThisConfrontation = false;
            hasSeenPlayer = false;
        }
    }

    void OnHeard(Stimulus s)
    {
        // Only sounds about someone on or near the property matter to the resident.
        if (Vector3.Distance(home, s.TargetPosition) > awarenessRadius * 1.5f) return;
        float bump = s.Kind == StimulusKind.Alarm ? alarmSuspicion : footstepSuspicion;
        Suspicion = Mathf.Clamp01(Suspicion + bump);
    }

    void RaiseAlarm()
    {
        if (player == null) return;
        StimulusBus.Emit(new Stimulus(StimulusKind.Alarm, transform.position, alarmRadius, player.position, gameObject));
    }

    void OnDestroy()
    {
        if (perception != null) perception.SoundHeard -= OnHeard;
    }


    Status Attack()
    {
        Enter(Mood.Hostile, "Attack");
        agent.isStopped = true;
        route.Clear();
        Face(player.position);
        if (Time.time >= nextAttackTime)
        {
            nextAttackTime = Time.time + attackCooldown;
            if (playerHealth != null) playerHealth.TakeDamage(attackDamage, "the resident");
        }
        return Status.Running;
    }

    Status Chase()
    {
        Enter(Mood.Hostile, "Chase");
        if (!alarmRaisedThisConfrontation)
        {
            alarmRaisedThisConfrontation = true;
            // alert dog
            RaiseAlarm();
        }
        agent.isStopped = false;
        agent.speed = chaseSpeed;
        // When sight is lost, chase only the remembered position, not the real hidden player.
        Vector3 destination = perception.CanSee(player) ? player.position : lastSeenPosition;
        MoveTo(destination);
        return Status.Running;
    }

    Status Warn()
    {
        Enter(Mood.Warning, "Warn");
        agent.isStopped = true;
        Face(player.position);

        if (warningStartTime < 0f)
        {
            warningStartTime = Time.time;
            if (voiceSource != null) voiceSource.PlayOneShot(voiceSource.clip);
            RaiseAlarm();
        }
        if (Time.time - warningStartTime < warningDuration) return Status.Running;

        warned = true;
        warningStartTime = -1f;
        return Status.Success;
    }

    Status Watch()
    {
        // After a chase, suspicion is still high while the inspector runs off: that reads as "calming down".
        Enter(warned ? Mood.CalmingDown : Mood.Suspicious, "Watch");
        agent.isStopped = false;
        agent.speed = walkSpeed;
        MoveTo(home);
        if (Vector3.Distance(transform.position, home) < 1f && player != null && perception.CanSee(player))
            Face(player.position);
        return Status.Running;
    }

    Status GoHome()
    {
        Enter(Mood.CalmingDown, "Go home");
        agent.isStopped = false;
        agent.speed = walkSpeed;
        MoveTo(home);
        return Status.Running;
    }

    Status Potter()
    {
        Enter(Mood.Unaware, "Potter");
        agent.isStopped = false;
        agent.speed = walkSpeed;
        if (gardenPoints == null || gardenPoints.Length == 0) return Status.Success;

        bool arrived = Vector3.Distance(transform.position, gardenPoints[gardenIndex].position)
                       <= agent.stoppingDistance + 0.8f;
        if (arrived && Time.time >= nextPotterTime)
        {
            gardenIndex = (gardenIndex + 1) % gardenPoints.Length;
            // linger at each spot
            nextPotterTime = Time.time + Random.Range(2f, 4f);
        }
        MoveTo(gardenPoints[gardenIndex].position);
        return Status.Running;
    }


    bool HasRecentMemory() => hasSeenPlayer && Time.time - lastSeenTime <= memorySeconds;

    bool ShouldInvestigate()
    {
        return warned && Suspicion > 0f && HasRecentMemory() &&
               !perception.CanSee(player) &&
               Vector3.Distance(home, lastSeenPosition) <= leashRadius;
    }

    Status Investigate()
    {
        Enter(Mood.Suspicious, "Investigate last seen position");
        agent.isStopped = false;
        agent.speed = walkSpeed;
        if (Vector3.Distance(transform.position, lastSeenPosition) <= investigateStoppingDistance)
        {
            // Finishing the search clears memory and starts the decorator's cooldown.
            hasSeenPlayer = false;
            return Status.Success;
        }
        MoveTo(lastSeenPosition);
        return Status.Running;
    }

    void MoveTo(Vector3 destination)
    {
        IPathfinder pathfinder = Pathfinding.Current;
        if (pathfinder == null)
        {
            // Use NavMesh when no custom pathfinder is registered.
            agent.SetDestination(destination);
            return;
        }

        bool providerChanged = !ReferenceEquals(routeProvider, pathfinder);
        bool targetMoved = Vector3.Distance(routeGoal, destination) > 1.5f;
        if (providerChanged || targetMoved || Time.time >= nextRouteSearch)
        {
            routeProvider = pathfinder;
            routeGoal = destination;
            nextRouteSearch = Time.time + Mathf.Max(0.2f, pathRefreshSeconds);
            hasRoute = pathfinder.TryFindPath(transform.position, destination, route);
            routeIndex = 0;
        }

        if (!hasRoute || route.Count == 0)
        {
            // If a destination is outside the grid, use the project's existing NavMesh.
            agent.SetDestination(destination);
            return;
        }

        // Skip waypoints that the resident has already reached.
        while (routeIndex < route.Count - 1 &&
               Vector3.Distance(transform.position, route[routeIndex]) <= 0.8f)
            routeIndex++;
        agent.SetDestination(route[routeIndex]);
    }


    void Enter(Mood mood, string branch)
    {
        CurrentMood = mood;
        ActiveBranch = branch;
        // If the warning was interrupted (e.g. the inspector left), allow it to replay next time.
        if (mood != Mood.Warning) warningStartTime = -1f;
    }

    float DistanceToPlayer() => player == null ? float.MaxValue : Vector3.Distance(transform.position, player.position);
    float PlayerDistanceFromHome() => player == null ? float.MaxValue : Vector3.Distance(home, player.position);

    void Face(Vector3 position)
    {
        Vector3 dir = position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 360f * Time.deltaTime);
    }

    void UpdateColour()
    {
        if (bodyRenderer == null) return;
        Color c = CurrentMood switch
        {
            Mood.Unaware => new Color(0.35f, 0.45f, 0.35f),
            Mood.Suspicious => Color.yellow,
            Mood.Warning => new Color(1f, 0.45f, 0f),
            Mood.Hostile => Color.red,
            // calming down
            _ => new Color(0.3f, 0.6f, 1f),
        };
        bodyRenderer.GetPropertyBlock(colourBlock);
        colourBlock.SetColor("_BaseColor", c);
        colourBlock.SetColor("_Color", c);
        bodyRenderer.SetPropertyBlock(colourBlock);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 h = Application.isPlaying ? home : transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(h, awarenessRadius);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(h, leashRadius);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
