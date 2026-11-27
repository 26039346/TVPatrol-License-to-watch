using System.Collections.Generic;
using UnityEngine;

// 1. shared pathfinding for AI agents
// 2. Pathfinding.Current is null if none registered
public interface IPathfinder
{
    // fill path with world positions from start to goal, false if unreachable
    bool TryFindPath(Vector3 start, Vector3 goal, List<Vector3> path);
}

public static class Pathfinding
{
    // active pathfinder, null if none registered
    public static IPathfinder Current { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => Current = null;
}
