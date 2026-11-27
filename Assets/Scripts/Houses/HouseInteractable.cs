using System.Collections.Generic;
using UnityEngine;

// 1. store house licence and inspection state
// 2. hide licence from player until inspected
public class HouseInteractable : MonoBehaviour
{
    static readonly List<HouseInteractable> all = new List<HouseInteractable>();
    public static IReadOnlyList<HouseInteractable> All => all;

    [SerializeField] string houseId = "H0";
    [SerializeField] string displayName = "House";
    [SerializeField] bool hasLicence;
    [SerializeField] bool tvIsPlaying;
    [SerializeField] Transform inspectionPoint;
    [SerializeField] AudioSource tvAudioSource;

    public string HouseId => houseId;
    public string DisplayName => displayName;
    public bool HasLicence => hasLicence;
    public bool TvIsPlaying => tvIsPlaying;
    public AudioSource TvAudioSource => tvAudioSource;
    public bool IsInspected { get; private set; }

    public Vector3 InspectionPosition => inspectionPoint != null ? inspectionPoint.position : transform.position;

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    // 1. set licence and TV state before Start
    // 2. TVAudioController reads TV state in Start
    public void Configure(bool licensed, bool tvPlaying)
    {
        hasLicence = licensed;
        tvIsPlaying = tvPlaying;
    }

    // mark inspected, only called by InspectionManager
    public void MarkInspected() => IsInspected = true;

    void OnDrawGizmos()
    {
        // 1. green for licensed, red for unlicensed
        // 2. filled sphere if TV playing
        Gizmos.color = hasLicence ? Color.green : Color.red;
        Vector3 p = InspectionPosition + Vector3.up * 0.2f;
        if (tvIsPlaying) Gizmos.DrawSphere(p, 0.4f);
        else Gizmos.DrawWireSphere(p, 0.4f);
    }
}
