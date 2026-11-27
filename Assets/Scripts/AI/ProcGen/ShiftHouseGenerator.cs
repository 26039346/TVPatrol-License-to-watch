using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 1. set house licences and TVs before Start
// 2. same seed gives same shift
[DefaultExecutionOrder(-100)]
public class ShiftHouseGenerator : MonoBehaviour
{
    [SerializeField] bool useFixedSeed = false;
    [SerializeField] int fixedSeed = 12345;
    [SerializeField, Range(0f, 1f)] float chanceTvOn = 0.6f;

    public int CurrentSeed { get; private set; }
    public int UnlicensedHouses { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegisterSceneCallback()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid() || UnityEngine.Object.FindFirstObjectByType<ShiftHouseGenerator>() != null)
            return;

        var go = new GameObject("Person B - Procedural Shift");
        SceneManager.MoveGameObjectToScene(go, scene);
        // Adding the component runs Awake before any House TVAudioController.Start().
        go.AddComponent<ShiftHouseGenerator>();
    }

    void Awake()
    {
        Generate();
    }

    public void Generate()
    {
        // Find scene objects directly: their OnEnable() may not have run yet
        // when a manually placed generator executes early in Awake().
        var houses = new List<HouseInteractable>();
        foreach (HouseInteractable house in FindObjectsByType<HouseInteractable>(FindObjectsSortMode.None))
            if (house != null && house.gameObject.scene == gameObject.scene) houses.Add(house);
        houses.Sort((a, b) => string.CompareOrdinal(a.HouseId, b.HouseId));

        if (houses.Count == 0)
        {
            Debug.LogWarning("[Person B/ProcGen] No houses were found.");
            return;
        }

        CurrentSeed = useFixedSeed ? fixedSeed : Environment.TickCount;
        var random = new System.Random(CurrentSeed);

        // Shuffle a COPY of the list, rather than changing the house registry.
        for (int i = houses.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (houses[i], houses[j]) = (houses[j], houses[i]);
        }

        ShiftManager shift = FindFirstObjectByType<ShiftManager>();
        int quota = shift != null ? shift.Quota : 4;
        // Reserve one licensed house if possible, and always satisfy the quota.
        int minimumUnlicensed = Mathf.Min(quota, houses.Count);
        int maximumUnlicensed = houses.Count > minimumUnlicensed ? houses.Count - 1 : houses.Count;
        if (maximumUnlicensed < minimumUnlicensed) maximumUnlicensed = minimumUnlicensed;
        UnlicensedHouses = random.Next(minimumUnlicensed, maximumUnlicensed + 1);

        bool[] licence = new bool[houses.Count];
        bool[] tvOn = new bool[houses.Count];
        for (int i = 0; i < houses.Count; i++)
        {
            licence[i] = i >= UnlicensedHouses;
            tvOn[i] = random.NextDouble() < chanceTvOn;
        }

        // Guaranteed clues: a silent offender and a licensed TV playing.
        if (UnlicensedHouses > 0) tvOn[0] = false;
        if (UnlicensedHouses < houses.Count) tvOn[UnlicensedHouses] = true;

        for (int i = 0; i < houses.Count; i++)
            houses[i].Configure(licence[i], tvOn[i]);

        Debug.Log($"[Person B/ProcGen] Seed={CurrentSeed}, houses={houses.Count}, " +
                  $"unlicensed={UnlicensedHouses}, quota={quota}. " +
                  "Use a fixed seed in the Inspector to repeat this shift.");
        if (UnlicensedHouses < quota || UnlicensedHouses == houses.Count)
            Debug.LogWarning("[Person B/ProcGen] Not enough houses for every requested rule.");
    }
}
