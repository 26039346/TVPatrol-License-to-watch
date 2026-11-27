using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class PersonBValidationMenu
{
    [MenuItem("TVPatrol/Person B/Check AI Systems (Play Mode)")]
    public static void RunChecks()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Person B] Enter Play Mode first, then run the checks.");
            return;
        }

        var resident = Object.FindFirstObjectByType<ResidentAIController>();
        if (resident == null)
            Debug.LogWarning("[B1] Resident missing. Use TVPatrol > Create Resident in a test scene.");
        else
            Debug.Log($"[B1] Resident active. Mood={resident.CurrentMood}; " +
                      $"branch={resident.ActiveBranch}; suspicion={resident.Suspicion:F2}. " +
                      "Walk near the resident to test warning, chase and memory.");

        if (Pathfinding.Current == null)
            Debug.LogWarning("[B2] Custom pathfinder not registered. Check greybox Grass collider.");
        else
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (resident != null && player != null)
            {
                var path = new List<Vector3>();
                bool ok = Pathfinding.Current.TryFindPath(player.transform.position,
                                                          resident.transform.position, path);
                Debug.Log($"[B2] Path available={ok}; waypoints={path.Count}.");
            }
        }

        var generator = Object.FindFirstObjectByType<ShiftHouseGenerator>();
        int unlicensed = 0, licensedTv = 0, silentUnlicensed = 0;
        foreach (HouseInteractable house in HouseInteractable.All)
        {
            if (!house.HasLicence) unlicensed++;
            if (house.HasLicence && house.TvIsPlaying) licensedTv++;
            if (!house.HasLicence && !house.TvIsPlaying) silentUnlicensed++;
        }
        ShiftManager shift = Object.FindFirstObjectByType<ShiftManager>();
        int quota = shift != null ? shift.Quota : 4;
        Debug.Log($"[B3] Seed={(generator != null ? generator.CurrentSeed.ToString() : "N/A")}; " +
                  $"unlicensed={unlicensed} (quota={quota}), licensedTV={licensedTv}, " +
                  $"silentOffenders={silentUnlicensed}; rulesPass=" +
                  (unlicensed >= quota && licensedTv > 0 && silentUnlicensed > 0));
    }
}
