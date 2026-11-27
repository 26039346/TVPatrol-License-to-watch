using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// handle timer, quota and win or loss
public class ShiftManager : MonoBehaviour
{
    public enum ShiftState { Running, Won, Lost }

    public static ShiftManager Instance { get; private set; }

    // 2 minutes 30 seconds
    [SerializeField] float shiftLengthSeconds = 150f;
    [SerializeField] int unlicensedQuota = 4;

    public event Action<ShiftState> ShiftEnded;

    public ShiftState State { get; private set; } = ShiftState.Running;
    public bool IsRunning => State == ShiftState.Running;
    public float TimeRemaining { get; private set; }
    public int Quota => unlicensedQuota;

    void Awake()
    {
        Instance = this;
        TimeRemaining = shiftLengthSeconds;
        Time.timeScale = 1f;
    }

    void Start()
    {
        if (InspectionManager.Instance != null)
            InspectionManager.Instance.HouseInspected += OnHouseInspected;
    }

    void OnDestroy()
    {
        if (InspectionManager.Instance != null)
            InspectionManager.Instance.HouseInspected -= OnHouseInspected;
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (IsRunning)
        {
            TimeRemaining = Mathf.Max(0f, TimeRemaining - Time.deltaTime);
            if (TimeRemaining <= 0f) EndShift(ShiftState.Lost);
        }
        else if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    void OnHouseInspected(HouseInteractable house, bool unlicensed)
    {
        if (IsRunning && InspectionManager.Instance.UnlicensedFound >= unlicensedQuota)
            EndShift(ShiftState.Won);
    }

    // finish shift with given result
    public void EndShift(ShiftState result)
    {
        if (!IsRunning || result == ShiftState.Running) return;
        State = result;
        // pause player and AI on result screen
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ShiftEnded?.Invoke(result);
    }
}
