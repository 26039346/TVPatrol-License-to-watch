using UnityEngine;

// opponent position, hostility and danger radius
public interface IThreat
{
    // current threat position
    Vector3 Position { get; }

    // true if chasing or attacking
    bool IsHostile { get; }

    // danger radius around threat
    float DangerRadius { get; }
}
