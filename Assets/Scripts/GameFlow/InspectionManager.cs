using System;
using UnityEngine;

// record inspections and update quota
public class InspectionManager : MonoBehaviour
{
    public static InspectionManager Instance { get; private set; }

    // fire after valid inspection with house and unlicensed flag
    public event Action<HouseInteractable, bool> HouseInspected;

    public int UnlicensedFound { get; private set; }
    public int LicensedConfirmed { get; private set; }
    public int TotalInspections => UnlicensedFound + LicensedConfirmed;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[TVPatrol] Duplicate InspectionManager destroyed.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // false if house inspected or game finished
    public bool TryInspect(HouseInteractable house)
    {
        if (house == null || house.IsInspected) return false;
        if (ShiftManager.Instance != null && !ShiftManager.Instance.IsRunning) return false;

        house.MarkInspected();
        bool unlicensed = !house.HasLicence;
        if (unlicensed) UnlicensedFound++;
        else LicensedConfirmed++;

        HouseInspected?.Invoke(house, unlicensed);
        return true;
    }
}
