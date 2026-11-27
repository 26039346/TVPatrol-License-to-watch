using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 1. build HUD when game starts
// 2. show start screen, timer, quota, health, noise and alerts
// 3. show inspection prompts, progress and result screen
public class HUDController : MonoBehaviour
{
    [SerializeField] PlayerInteractor playerInteractor;
    [UnityEngine.Serialization.FormerlySerializedAs("messageDuration")]
    [SerializeField] float messageDurationSeconds = 3f;
    [SerializeField] bool showStartScreen = true;

    static readonly Color PanelColour = new Color(0f, 0f, 0f, 0.55f);
    static readonly Color AccentColour = new Color(1f, 0.82f, 0.25f);
    static readonly Color DangerColour = new Color(1f, 0.3f, 0.25f);
    static readonly Color CalmColour = new Color(0.55f, 0.85f, 0.55f);

    PlayerHealth playerHealth;
    PlayerNoiseEmitter noiseEmitter;
    IThreat[] threats;
    Font font;

    GameObject startScreen, endScreen;
    Text timerText, quotaText, healthText, noiseText, alertText, promptText, messageText;
    Text endTitle, endSummary;
    Image noiseFill, inspectionProgressFill;
    GameObject inspectionProgressBar;
    float messageHideTime;
    bool hasStarted;
    // show start screen once, skip on restart
    static bool startScreenSeen;
    bool isEndScreenShown;

    void Start()
    {
        if (playerInteractor == null) playerInteractor = FindFirstObjectByType<PlayerInteractor>();
        if (playerInteractor != null)
        {
            playerHealth = playerInteractor.GetComponent<PlayerHealth>();
            noiseEmitter = playerInteractor.GetComponent<PlayerNoiseEmitter>();
        }
        threats = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).OfType<IThreat>().ToArray();

        if (InspectionManager.Instance != null) InspectionManager.Instance.HouseInspected += OnHouseInspected;
        if (playerHealth != null) playerHealth.Damaged += OnDamaged;
        if (playerInteractor != null) playerInteractor.InspectionCancelled += OnInspectionCancelled;

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildCanvas();

        if (showStartScreen && !startScreenSeen)
        {
            // pause shift until player starts
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            BeginShift();
        }
    }

    void OnDestroy()
    {
        if (InspectionManager.Instance != null) InspectionManager.Instance.HouseInspected -= OnHouseInspected;
        if (playerHealth != null) playerHealth.Damaged -= OnDamaged;
        if (playerInteractor != null) playerInteractor.InspectionCancelled -= OnInspectionCancelled;
    }

    void Update()
    {
        var shift = ShiftManager.Instance;
        var inspections = InspectionManager.Instance;
        if (shift == null || inspections == null) return;

        if (!hasStarted)
        {
            bool pressed = (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame))
                        || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                        || (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
            if (pressed) BeginShift();
            return;
        }

        // Status panel.
        int seconds = Mathf.CeilToInt(shift.TimeRemaining);
        timerText.text = $"{seconds / 60}:{seconds % 60:00}";
        timerText.color = seconds <= 60 ? DangerColour : Color.white;
        quotaText.text = $"Unlicensed found   {inspections.UnlicensedFound} / {shift.Quota}";
        if (playerHealth != null)
        {
            healthText.text = $"Health   {playerHealth.Current} / {playerHealth.MaxHealth}";
            healthText.color = playerHealth.Current <= 1 ? DangerColour : Color.white;
        }

        UpdateNoiseMeter();

        // Alert banner: any opponent actively hostile.
        bool spotted = threats.Any(t => t is Object o && o != null && t.IsHostile);
        alertText.gameObject.SetActive(spotted && shift.IsRunning);

        // Prompt and messages.
        var target = playerInteractor != null ? playerInteractor.CurrentTarget : null;
        promptText.gameObject.SetActive(shift.IsRunning && target != null);
        bool checking = playerInteractor != null && playerInteractor.IsInspecting;
        if (target != null)
            promptText.text = checking
                ? $"Checking licence...  {Mathf.FloorToInt(playerInteractor.InspectProgress * 100f)}%"
                : $"[Hold E]  Inspect {target.DisplayName}";
        inspectionProgressBar.SetActive(shift.IsRunning && checking);
        if (checking) inspectionProgressFill.fillAmount = playerInteractor.InspectProgress;
        messageText.gameObject.SetActive(Time.unscaledTime < messageHideTime && shift.IsRunning);

        if (!shift.IsRunning && !isEndScreenShown) ShowEndScreen(shift, inspections);
    }

    void UpdateNoiseMeter()
    {
        if (noiseEmitter == null) { noiseText.transform.parent.gameObject.SetActive(false); return; }
        float r = noiseEmitter.CurrentNoiseRadius;
        // scale noise bar to 12 m sprint radius
        noiseFill.fillAmount = Mathf.MoveTowards(noiseFill.fillAmount, Mathf.Clamp01(r / 12f), Time.deltaTime * 4f);
        if (r <= 0f) { noiseText.text = "Noise: silent"; noiseFill.color = CalmColour; }
        else if (r < 6f) { noiseText.text = "Noise: quiet"; noiseFill.color = AccentColour; }
        else { noiseText.text = "Noise: LOUD"; noiseFill.color = DangerColour; }
    }

    void BeginShift()
    {
        hasStarted = true;
        startScreenSeen = true;
        startScreen.SetActive(false);
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        ShowMessage("Listen for TVs... but only an inspection proves who has no licence.", 7f);
    }

    void ShowEndScreen(ShiftManager shift, InspectionManager inspections)
    {
        isEndScreenShown = true;
        endScreen.SetActive(true);
        bool won = shift.State == ShiftManager.ShiftState.Won;
        endTitle.text = won ? "SHIFT COMPLETE"
            : playerHealth != null && playerHealth.Current == 0 ? "CHASED OFF THE STREET"
            : "OUT OF TIME";
        endTitle.color = won ? CalmColour : DangerColour;
        int seconds = Mathf.CeilToInt(shift.TimeRemaining);
        endSummary.text =
            $"Unlicensed found: {inspections.UnlicensedFound} / {shift.Quota}\n" +
            $"Licences confirmed: {inspections.LicensedConfirmed}\n" +
            $"Time left: {seconds / 60}:{seconds % 60:00}\n\n" +
            "Press R to restart";
    }

    void OnDamaged(int remaining, string source) =>
        ShowMessage(source == "Dog" ? "Bitten by the dog!" : "Hit by " + source + "!", 2f, DangerColour);

    void OnInspectionCancelled(string reason) => ShowMessage(reason, 1.5f, DangerColour);

    void OnHouseInspected(HouseInteractable house, bool unlicensed) =>
        ShowMessage(unlicensed
                ? house.DisplayName + ": no licence recorded"
                : house.DisplayName + ": licence confirmed",
            messageDurationSeconds, unlicensed ? AccentColour : CalmColour);

    void ShowMessage(string text, float duration, Color? colour = null)
    {
        messageText.text = text;
        messageText.color = colour ?? Color.white;
        messageHideTime = Time.unscaledTime + duration;
    }


    void BuildCanvas()
    {
        var canvasGo = new GameObject("HUD Canvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var root = canvasGo.transform;

        // Top-left status panel.
        var status = Panel(root, "Status", new Vector2(0, 1), new Vector2(24, -24), new Vector2(420, 150));
        timerText = Label(status, "Timer", "2:30", 44, TextAnchor.UpperLeft, new Vector2(20, -12), new Vector2(380, 54));
        quotaText = Label(status, "Quota", "", 24, TextAnchor.UpperLeft, new Vector2(20, -70), new Vector2(380, 32));
        healthText = Label(status, "Health", "", 24, TextAnchor.UpperLeft, new Vector2(20, -104), new Vector2(380, 32));

        // Top-right noise meter.
        var noise = Panel(root, "Noise", new Vector2(1, 1), new Vector2(-24, -24), new Vector2(300, 80));
        noiseText = Label(noise, "NoiseLabel", "Noise: silent", 22, TextAnchor.UpperLeft, new Vector2(18, -10), new Vector2(264, 28));
        var barBack = Rect(noise, "BarBack", new Vector2(0, 1), new Vector2(18, -46), new Vector2(264, 16));
        barBack.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);
        var fill = Rect(barBack, "Fill", new Vector2(0, 1), Vector2.zero, new Vector2(264, 16));
        noiseFill = fill.gameObject.AddComponent<Image>();
        noiseFill.sprite = WhiteSprite();
        noiseFill.type = Image.Type.Filled;
        noiseFill.fillMethod = Image.FillMethod.Horizontal;
        noiseFill.fillAmount = 0f;

        // Alert banner under the noise meter.
        alertText = Label(root, "Alert", "! SPOTTED !", 34, TextAnchor.MiddleCenter, new Vector2(-24, -118), new Vector2(300, 50), new Vector2(1, 1));
        alertText.color = DangerColour;
        alertText.gameObject.SetActive(false);

        // Centre prompt and message.
        promptText = Label(root, "Prompt", "", 34, TextAnchor.MiddleCenter, new Vector2(0, -160), new Vector2(1200, 50), new Vector2(0.5f, 0.5f));
        promptText.color = AccentColour;

        // Licence-check progress bar, just under the prompt.
        var inspectBack = Rect(root, "InspectBar", new Vector2(0.5f, 0.5f), new Vector2(0, -200), new Vector2(420, 18));
        inspectBack.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
        var inspectFillRect = Rect(inspectBack, "Fill", new Vector2(0, 1), Vector2.zero, new Vector2(420, 18));
        inspectFillRect.pivot = new Vector2(0, 1);
        inspectionProgressFill = inspectFillRect.gameObject.AddComponent<Image>();
        inspectionProgressFill.sprite = WhiteSprite();
        inspectionProgressFill.type = Image.Type.Filled;
        inspectionProgressFill.fillMethod = Image.FillMethod.Horizontal;
        inspectionProgressFill.color = AccentColour;
        inspectionProgressBar = inspectBack.gameObject;
        inspectionProgressBar.SetActive(false);
        messageText = Label(root, "Message", "", 28, TextAnchor.MiddleCenter, new Vector2(0, 160), new Vector2(1400, 50), new Vector2(0.5f, 0f));

        startScreen = BuildStartScreen(root);
        endScreen = BuildEndScreen(root);
        endScreen.SetActive(false);
        startScreen.SetActive(showStartScreen && !startScreenSeen);
    }

    GameObject BuildStartScreen(Transform root)
    {
        var screen = FullScreen(root, "StartScreen", new Color(0.05f, 0.06f, 0.1f, 0.92f));
        var title = Label(screen, "Title", "TV PATROL", 96, TextAnchor.MiddleCenter, new Vector2(0, 300), new Vector2(1400, 120), new Vector2(0.5f, 0.5f));
        title.color = AccentColour;
        Label(screen, "Subtitle", "Licence to Watch", 36, TextAnchor.MiddleCenter, new Vector2(0, 220), new Vector2(1400, 50), new Vector2(0.5f, 0.5f));
        Label(screen, "Goal",
            "You are a TV licence inspector. Find 4 unlicensed households before the shift ends.\n" +
            "TV sound means someone is watching, not that they lack a licence. Inspect to be sure.\n" +
            "Watch out for the dog at No. 3 and the resident at No. 4. Sprinting is fast but loud.",
            26, TextAnchor.MiddleCenter, new Vector2(0, 90), new Vector2(1500, 140), new Vector2(0.5f, 0.5f));
        Label(screen, "Controls",
            "WASD  Move          Mouse  Look          Shift  Sprint (loud)\n" +
            "Hold E  Inspect house         Esc  Free cursor          R  Restart after shift",
            26, TextAnchor.MiddleCenter, new Vector2(0, -60), new Vector2(1500, 90), new Vector2(0.5f, 0.5f)).color = new Color(0.8f, 0.85f, 0.95f);
        var start = Label(screen, "Start", "Press ENTER or click to start your shift", 34, TextAnchor.MiddleCenter, new Vector2(0, -220), new Vector2(1400, 60), new Vector2(0.5f, 0.5f));
        start.color = AccentColour;
        return screen.gameObject;
    }

    GameObject BuildEndScreen(Transform root)
    {
        var screen = FullScreen(root, "EndScreen", new Color(0f, 0f, 0f, 0.75f));
        endTitle = Label(screen, "Title", "", 80, TextAnchor.MiddleCenter, new Vector2(0, 140), new Vector2(1600, 110), new Vector2(0.5f, 0.5f));
        endSummary = Label(screen, "Summary", "", 32, TextAnchor.UpperCenter, new Vector2(0, 40), new Vector2(1200, 300), new Vector2(0.5f, 0.5f));
        return screen.gameObject;
    }


    RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        return rt;
    }

    RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var rt = Rect(parent, name, anchor, position, size);
        rt.gameObject.AddComponent<Image>().color = PanelColour;
        return rt;
    }

    RectTransform FullScreen(Transform parent, string name, Color colour)
    {
        var rt = Rect(parent, name, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.gameObject.AddComponent<Image>().color = colour;
        return rt;
    }

    Text Label(Transform parent, string name, string text, int size, TextAnchor alignment, Vector2 position, Vector2 boxSize, Vector2? anchor = null)
    {
        var rt = Rect(parent, name, anchor ?? new Vector2(0, 1), position, boxSize);
        var label = rt.gameObject.AddComponent<Text>();
        label.font = font;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = FontStyle.Bold;
        label.alignment = alignment;
        label.color = Color.white;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        var shadow = rt.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(2f, -2f);
        return label;
    }

    // reset static state on play if domain reload disabled
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => startScreenSeen = false;

    static Sprite whiteSprite;
    static Sprite WhiteSprite()
    {
        if (whiteSprite == null)
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new UnityEngine.Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        return whiteSprite;
    }
}
