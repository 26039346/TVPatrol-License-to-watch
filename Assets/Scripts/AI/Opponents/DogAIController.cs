using UnityEngine;

// 1. dog patrols, detects, chases, attacks, searches then returns
// 2. Perception handles senses, SteeringAgent handles movement along A* paths
// 3. bark when player spotted so resident can hear
[RequireComponent(typeof(SteeringAgent), typeof(Perception))]
public class DogAIController : MonoBehaviour, IThreat
{
    public enum DogState { Patrol, Detect, Chase, Attack, Search, Return }

    [SerializeField] Transform[] patrolPoints;
    [SerializeField] Transform player;

    [Header("Alarm")]
    // alarm hearing radius
    [SerializeField] float alarmRadius = 25f;

    [Header("Movement")]
    [SerializeField] float patrolSpeed = 2.5f;
    // faster than walking, slower than sprinting
    [SerializeField] float chaseSpeed = 5.5f;
    // max chase distance from yard
    [SerializeField] float leashRadius = 22f;
    // search area around the last known position
    [SerializeField] float sniffRadius = 3f;

    [Header("Timings")]
    [UnityEngine.Serialization.FormerlySerializedAs("detectDuration")]
    [SerializeField] float detectionDelaySeconds = 0.6f;
    [UnityEngine.Serialization.FormerlySerializedAs("loseSightTime")]
    [SerializeField] float lostSightTimeoutSeconds = 2f;
    [UnityEngine.Serialization.FormerlySerializedAs("searchDuration")]
    [SerializeField] float searchDurationSeconds = 5f;
    [UnityEngine.Serialization.FormerlySerializedAs("patrolWait")]
    [SerializeField] float patrolWaitSeconds = 1f;

    [Header("Attack")]
    [SerializeField] float attackRange = 1.6f;
    [UnityEngine.Serialization.FormerlySerializedAs("attackCooldown")]
    [SerializeField] float attackCooldownSeconds = 1.5f;
    [SerializeField] int attackDamage = 1;

    [Header("Feedback")]
    [SerializeField] Renderer bodyRenderer;
    [SerializeField] AudioSource barkSource;

    public DogState State { get; private set; }

    // IThreat: lets the inspector's target selector avoid the dog without referencing this class.
    public Vector3 Position => transform.position;
    public bool IsHostile => State == DogState.Detect || State == DogState.Chase || State == DogState.Attack;
    public float DangerRadius => perception != null ? perception.ViewDistance : 9f;

    SteeringAgent steering;
    Perception perception;
    PlayerHealth playerHealth;
    Vector3 homePosition;
    // search position from last sight or sound
    Vector3 searchCentre;
    int patrolPointIndex;
    float stateElapsedSeconds;
    float nextAttackTime;
    MaterialPropertyBlock colourBlock;

    // Sensory memory lives in Perception: where the inspector was last sensed, and how long ago.
    Vector3 LastKnownPlayerPosition => perception.LastKnownPlayerPosition;

    void Start()
    {
        // Dogs saved in the scene before steering existed won't have the component yet.
        steering = GetComponent<SteeringAgent>();
        if (steering == null) steering = gameObject.AddComponent<SteeringAgent>();
        perception = GetComponent<Perception>();
        perception.SoundHeard += OnHeard;
        homePosition = transform.position;
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
        if (player != null) playerHealth = player.GetComponent<PlayerHealth>();
        colourBlock = new MaterialPropertyBlock();
        if (barkSource != null && barkSource.clip == null) barkSource.clip = BarkClip.Get();
        EnterState(DogState.Patrol);
    }

    void Update()
    {
        // check sight and refresh memory
        bool canSee = perception.CanSee(player);
        stateElapsedSeconds += Time.deltaTime;

        switch (State)
        {
            case DogState.Patrol: UpdatePatrol(canSee); break;
            case DogState.Detect: UpdateDetect(canSee); break;
            case DogState.Chase: UpdateChase(); break;
            case DogState.Attack: UpdateAttack(); break;
            case DogState.Search: UpdateSearch(canSee); break;
            case DogState.Return: UpdateReturn(canSee); break;
        }
    }


    void UpdatePatrol(bool canSee)
    {
        if (canSee) { EnterState(DogState.Detect); return; }
        if (patrolPoints == null || patrolPoints.Length == 0) return;

        // Wait briefly at each point, then move to the next one.
        if (steering.HasArrived && stateElapsedSeconds >= patrolWaitSeconds)
        {
            patrolPointIndex = (patrolPointIndex + 1) % patrolPoints.Length;
            stateElapsedSeconds = 0f;
        }
        steering.MoveTo(patrolPoints[patrolPointIndex].position, patrolSpeed);
    }

    void UpdateDetect(bool canSee)
    {
        // Short alert pause (stop, face player, bark) so the player gets a warning before the chase.
        steering.FaceTowards(LastKnownPlayerPosition);
        if (stateElapsedSeconds < detectionDelaySeconds) return;
        EnterState(canSee ? DogState.Chase : DogState.Search);
    }

    void UpdateChase()
    {
        if (DistanceToPlayer() <= attackRange) { EnterState(DogState.Attack); return; }
        // Hearing footsteps keeps the remembered position up to date, but only sight keeps the chase going.
        if (perception.SecondsSinceSeen > lostSightTimeoutSeconds || Vector3.Distance(transform.position, homePosition) > leashRadius)
        {
            EnterState(DogState.Search);
            return;
        }
        // 1. chase predicted position while player visible
        // 2. move to last seen position after losing sight
        if (perception.SecondsSinceSeen < 0.1f) steering.Pursue(player, chaseSpeed);
        else steering.MoveTo(LastKnownPlayerPosition, chaseSpeed);
    }

    void UpdateAttack()
    {
        steering.FaceTowards(player.position);
        if (DistanceToPlayer() > attackRange * 1.2f) { EnterState(DogState.Chase); return; }

        if (Time.time >= nextAttackTime)
        {
            nextAttackTime = Time.time + attackCooldownSeconds;
            Bark();
            if (playerHealth != null) playerHealth.TakeDamage(attackDamage, "Dog");
        }
    }

    void UpdateSearch(bool canSee)
    {
        if (canSee && Vector3.Distance(transform.position, homePosition) <= leashRadius) { EnterState(DogState.Chase); return; }
        if (stateElapsedSeconds >= searchDurationSeconds) { EnterState(DogState.Return); return; }

        // Travel to the search spot along A*, then sniff around it (wander tethered to the spot).
        float speed = isInvestigatingAlarm ? chaseSpeed * 0.8f : patrolSpeed;
        // Hysteresis (2x radius) stops it flicking between travelling and sniffing at the edge.
        if (Vector3.Distance(transform.position, searchCentre) > sniffRadius * 2f) steering.MoveTo(searchCentre, speed);
        else steering.Sniff(searchCentre, sniffRadius, patrolSpeed);
    }

    void UpdateReturn(bool canSee)
    {
        if (canSee && Vector3.Distance(transform.position, homePosition) <= leashRadius * 0.5f) { EnterState(DogState.Detect); return; }
        if (steering.HasArrived) EnterState(DogState.Patrol);
    }

    void EnterState(DogState next)
    {
        State = next;
        stateElapsedSeconds = 0f;

        // Entry actions only. Per-frame movement commands come from the Update* methods.
        switch (next)
        {
            case DogState.Patrol:
                if (patrolPoints == null || patrolPoints.Length == 0) steering.Stop();
                break;
            case DogState.Detect:
            case DogState.Attack:
                steering.Stop();
                if (next == DogState.Detect) { Bark(); RaiseAlarm(); }
                break;
            case DogState.Chase:
                isInvestigatingAlarm = false;
                break;
            case DogState.Search:
                // from Perception's memory
                searchCentre = LastKnownPlayerPosition;
                steering.MoveTo(searchCentre, patrolSpeed);
                break;
            case DogState.Return:
                patrolPointIndex = NearestPatrolIndex();
                steering.MoveTo(patrolPoints != null && patrolPoints.Length > 0 ? patrolPoints[patrolPointIndex].position : homePosition, patrolSpeed);
                break;
        }
        UpdateColour();
    }


    bool isInvestigatingAlarm;

    // check nearby sounds inside leash range if not busy with player
    void OnHeard(Stimulus s)
    {
        if (State == DogState.Detect || State == DogState.Chase || State == DogState.Attack) return;
        if (Vector3.Distance(homePosition, s.TargetPosition) > leashRadius) return;

        isInvestigatingAlarm = s.Kind == StimulusKind.Alarm;
        // restarts the search timer
        EnterState(DogState.Search);
        // search where the sound came from
        searchCentre = s.TargetPosition;
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


    float DistanceToPlayer() => player == null ? float.MaxValue : Vector3.Distance(transform.position, player.position);

    int NearestPatrolIndex()
    {
        int best = 0;
        float bestSqr = float.MaxValue;
        for (int i = 0; patrolPoints != null && i < patrolPoints.Length; i++)
        {
            float sqr = (patrolPoints[i].position - transform.position).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = i; }
        }
        return best;
    }

    void Bark()
    {
        if (barkSource == null) return;
        barkSource.pitch = Random.Range(0.9f, 1.1f);
        barkSource.PlayOneShot(barkSource.clip);
    }

    // change body colour to match current state
    void UpdateColour()
    {
        if (bodyRenderer == null) return;
        Color c = State switch
        {
            DogState.Patrol => new Color(0.55f, 0.40f, 0.25f),
            DogState.Detect => Color.yellow,
            DogState.Chase => new Color(1f, 0.45f, 0f),
            DogState.Attack => Color.red,
            DogState.Search => new Color(0.6f, 0.3f, 0.9f),
            // return state
            _ => new Color(0.3f, 0.6f, 1f),
        };
        bodyRenderer.GetPropertyBlock(colourBlock);
        colourBlock.SetColor("_BaseColor", c);
        colourBlock.SetColor("_Color", c);
        bodyRenderer.SetPropertyBlock(colourBlock);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 p = transform.position;
        // Sight cone and proximity range are drawn by Perception.
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(p, attackRange);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(Application.isPlaying ? homePosition : p, leashRadius);
    }
}
