using UnityEngine;

// the driver's dashboard, along the bottom of the screen:
//
//   [ steering wheel ]  [brake]  ( speed arc + big number )  [throttle]  [ status / camera / reset ]
//
// plus a controls card in the top-left (H shows or hides it) and a "waiting for the server" card
// when socket1.py isn't running. lap times are LapScreen's job.
//
// drawn with Unity's built-in OnGUI using the graphics and styles in HudSkin, so it needs no
// packages and nothing set up in the scene. DILBootstrap adds it when you press Play.
public class Dashboard : MonoBehaviour
{
    [Tooltip("Leave empty to find it automatically.")]
    public DILCarClient car;
    [Tooltip("Optional. Only used to show which camera is active.")]
    public CameraSwitcher cameras;

    [Tooltip("How far the steering wheel turns at full lock. FS-4's VCU uses 75.82 deg (STEERING_MAX_ANGLE).")]
    public float steeringWheelLockDeg = 75.82f;
    public bool showMph = true;
    [Tooltip("Top of the speed arc, in the units shown (mph or km/h). The number keeps counting past it.")]
    public float arcMaxMph = 80f, arcMaxKmh = 130f;

    public KeyCode helpKey = KeyCode.H;
    [Tooltip("The controls card hides itself after this many seconds (H brings it back).")]
    public float helpAutoHideSeconds = 25f;

    const float MpsToMph = 2.23694f, MpsToKmh = 3.6f;
    const int Segments = 40;
    const float ArcDegrees = 240f;

    TrackLoader track;
    CarSound sound;
    bool helpVisible = true, helpTouched;
    float shownSpeed;            // smoothed a little, so the number doesn't flicker

    void Start()
    {
        if (car == null) car = FindFirstObjectByType<DILCarClient>();
        if (cameras == null) cameras = FindFirstObjectByType<CameraSwitcher>();
        track = FindFirstObjectByType<TrackLoader>();
        sound = FindFirstObjectByType<CarSound>();
    }

    void Update()
    {
        if (Input.GetKeyDown(helpKey)) { helpVisible = !helpVisible; helpTouched = true; }
        if (!helpTouched && Time.timeSinceLevelLoad > helpAutoHideSeconds) helpVisible = false;
        if (car != null)
            shownSpeed = Mathf.Lerp(shownSpeed, Mathf.Max(0f, car.SpeedMps), 1f - Mathf.Exp(-12f * Time.deltaTime));
    }

    void OnGUI()
    {
        if (car == null) return;
        GUI.depth = 0;                      // in front of the lap screen
        HudSkin.Begin();

        DrawCluster();
        DrawHelp();
        if (!car.IsConnected) DrawWaiting();
    }

    void DrawCluster()
    {
        const float w = 900f, h = 230f;
        float ox = HudSkin.W / 2f - w / 2f, oy = HudSkin.H - h - 22f;
        HudSkin.DrawPanel(ox, oy, w, h);
        HudSkin.Tint(new Rect(ox + 26f, oy, w - 52f, 2f), HudSkin.Pixel, new Color(HudSkin.Gold.r, HudSkin.Gold.g, HudSkin.Gold.b, 0.75f));

        float gx = ox + 450f, gy = oy + 128f;     // center of the speed arc
        DrawSteeringWheel(ox + 125f, oy + 96f);
        DrawSpeed(gx, gy);

        // brake on the left of the arc, throttle on the right, like the pedals.
        float throttle = car.ThrottleInput;
        HudSkin.VerticalBar(gx - 161f, oy + 36f, 22f, 140f, car.BrakeInput, HudSkin.Red);
        HudSkin.VerticalBar(gx + 139f, oy + 36f, 22f, 140f, Mathf.Abs(throttle), throttle >= 0f ? HudSkin.Green : HudSkin.Blue);
        var small = HudSkin.Text(13, true, TextAnchor.MiddleCenter);
        HudSkin.Label(gx - 180f, oy + 184f, 60f, 20f, "BRAKE", small, HudSkin.TextDim);
        HudSkin.Label(gx + 120f, oy + 184f, 60f, 20f, throttle >= 0f ? "THROTTLE" : "REVERSE", small, HudSkin.TextDim);

        DrawStatus(ox + 640f, oy);
    }

    void DrawSteeringWheel(float cx, float cy)
    {
        // turning right (positive steer) spins the wheel clockwise, like a real one.
        float wheelDeg = car.SteerInput * steeringWheelLockDeg;
        const float size = 168f;
        HudSkin.Rotated(wheelDeg, cx, cy, () =>
            HudSkin.Tint(new Rect(cx - size / 2f, cy - size / 2f, size, size), HudSkin.Wheel, Color.white));
        HudSkin.Label(cx - 100f, cy + 86f, 200f, 28f, $"{wheelDeg:+0;-0;0}°",
                      HudSkin.Text(24, true, TextAnchor.MiddleCenter), HudSkin.TextMain);
        HudSkin.Label(cx - 100f, cy + 112f, 200f, 18f, $"tires {car.RoadWheelAngleDeg:+0.0;-0.0;0.0}°",
                      HudSkin.Text(14, false, TextAnchor.MiddleCenter), HudSkin.TextDim);
    }

    void DrawSpeed(float gx, float gy)
    {
        float speed = showMph ? shownSpeed * MpsToMph : shownSpeed * MpsToKmh;
        float max = showMph ? arcMaxMph : arcMaxKmh;
        float lit = Mathf.Clamp01(speed / max) * Segments;   // how many segments are on (last one partly)

        // a soft glow behind the number that gets stronger with speed
        float glow = 0.08f + 0.18f * Mathf.Clamp01(speed / max);
        HudSkin.Tint(new Rect(gx - 120f, gy - 120f, 240f, 240f), HudSkin.Glow,
                     new Color(HudSkin.Gold.r, HudSkin.Gold.g, HudSkin.Gold.b, glow));

        const float outer = 112f, inner = 86f, segWidth = 8f;
        for (int i = 0; i < Segments; i++)
        {
            float angle = -ArcDegrees / 2f + (i + 0.5f) * ArcDegrees / Segments;
            float on = Mathf.Clamp01(lit - i);
            Color c = Color.Lerp(HudSkin.Track, SegmentColor(i / (float)(Segments - 1)), on);
            HudSkin.Rotated(angle, gx, gy, () =>
                HudSkin.Tint(new Rect(gx - segWidth / 2f, gy - outer, segWidth, outer - inner), HudSkin.Segment, c));
        }

        HudSkin.Label(gx - 120f, gy - 62f, 240f, 96f, $"{speed:0}", HudSkin.Text(84, true, TextAnchor.MiddleCenter), HudSkin.TextMain);
        HudSkin.Label(gx - 60f, gy + 30f, 120f, 22f, showMph ? "MPH" : "KM/H", HudSkin.Text(18, true, TextAnchor.MiddleCenter), HudSkin.TextDim);
        string other = showMph ? $"{shownSpeed * MpsToKmh:0} km/h" : $"{shownSpeed * MpsToMph:0} mph";
        HudSkin.Label(gx - 80f, gy + 52f, 160f, 20f, other, HudSkin.Text(15, false, TextAnchor.MiddleCenter), HudSkin.TextDim);

        // the arc's two ends, just inside it
        var tick = HudSkin.Text(13, false, TextAnchor.MiddleCenter);
        HudSkin.Label(gx - 72f, gy + 30f, 40f, 20f, "0", tick, HudSkin.TextDim);
        HudSkin.Label(gx + 32f, gy + 30f, 40f, 20f, $"{max:0}", tick, HudSkin.TextDim);
    }

    // gold most of the way, then orange, then red at the top.
    static Color SegmentColor(float t)
    {
        if (t < 0.72f) return HudSkin.Gold;
        if (t < 0.88f) return Color.Lerp(HudSkin.Gold, HudSkin.Orange, (t - 0.72f) / 0.16f);
        return Color.Lerp(HudSkin.Orange, HudSkin.Red, (t - 0.88f) / 0.12f);
    }

    void DrawStatus(float x, float oy)
    {
        bool live = car.IsConnected;
        float pulse = live ? 1f : 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));
        Color dot = live ? HudSkin.Green : HudSkin.Red;
        HudSkin.Tint(new Rect(x, oy + 31f, 13f, 13f), HudSkin.Disc, new Color(dot.r, dot.g, dot.b, pulse));
        HudSkin.Label(x + 22f, oy + 24f, 200f, 26f, live ? "LIVE" : "NO SERVER", HudSkin.Text(17, true), live ? HudSkin.TextMain : HudSkin.Red);

        var caption = HudSkin.Text(13, true);
        var value = HudSkin.Text(21, true);
        string dim = "<color=#" + HudSkin.Hex(HudSkin.TextDim) + ">";
        if (cameras != null)
        {
            HudSkin.Label(x, oy + 60f, 220f, 18f, "CAMERA", caption, HudSkin.TextDim);
            HudSkin.Label(x, oy + 78f, 230f, 28f, $"{cameras.CurrentLabel}   {dim}{cameras.switchKey}</color>", value, HudSkin.TextMain);
        }
        if (sound != null)
        {
            HudSkin.Label(x, oy + 110f, 220f, 18f, "SOUND", caption, HudSkin.TextDim);
            HudSkin.Label(x, oy + 128f, 230f, 28f, $"{(sound.Muted ? "Muted" : "On")}   {dim}{sound.muteKey}</color>", value, HudSkin.TextMain);
        }
        if (HudSkin.Button(x, oy + 166f, 230f, 44f, $"RESET     {car.resetKey}"))
            car.ResetCar();
    }

    void DrawHelp()
    {
        var keyStyle = HudSkin.Text(16, true);
        if (!helpVisible)
        {
            HudSkin.DrawPanel(22f, 22f, 190f, 40f, 0.85f);
            HudSkin.Label(40f, 22f, 170f, 40f, $"{helpKey}   <color=#{HudSkin.Hex(HudSkin.TextMain)}>show controls</color>", keyStyle, HudSkin.Gold);
            return;
        }

        var rows = new System.Collections.Generic.List<string[]>
        {
            new[] { "W / S", "throttle (S backs up)" },
            new[] { "A / D", "steer" },
            new[] { "Space", "front brake" },
            new[] { "X", "rear brake" },
            new[] { car.resetKey.ToString(), "reset to the start" },
        };
        if (cameras != null) rows.Add(new[] { cameras.switchKey.ToString(), "change camera" });
        if (track != null) rows.Add(new[] { "1  2  3", "Blue Max, Accel, Skidpad" });
        if (sound != null) rows.Add(new[] { sound.muteKey.ToString(), "sound on / off" });
        rows.Add(new[] { helpKey.ToString(), "hide this" });

        float h = 52f + rows.Count * 27f;
        HudSkin.DrawPanel(22f, 22f, 330f, h, 0.92f);
        HudSkin.Label(42f, 34f, 280f, 22f, "CONTROLS", HudSkin.Text(13, true), HudSkin.TextDim);
        var action = HudSkin.Text(16, false);
        for (int i = 0; i < rows.Count; i++)
        {
            float y = 60f + i * 27f;
            HudSkin.Label(42f, y, 90f, 24f, rows[i][0], keyStyle, HudSkin.Gold);
            HudSkin.Label(132f, y, 210f, 24f, rows[i][1], action, HudSkin.TextMain);
        }
    }

    void DrawWaiting()
    {
        const float w = 720f, h = 170f;
        float x = HudSkin.W / 2f - w / 2f, y = 250f;          // above the car, below the lap panel
        HudSkin.DrawPanel(x, y, w, h);

        // spinner: 12 dashes, the bright one goes round
        float cx = x + 72f, cy = y + h / 2f;
        int head = (int)(Time.unscaledTime * 12f) % 12;
        for (int i = 0; i < 12; i++)
        {
            int behind = (head - i + 12) % 12;
            float a = 1f - behind / 12f;
            Color c = new Color(HudSkin.Gold.r, HudSkin.Gold.g, HudSkin.Gold.b, 0.15f + 0.85f * a * a);
            HudSkin.Rotated(i * 30f, cx, cy, () =>
                HudSkin.Tint(new Rect(cx - 4f, cy - 30f, 8f, 16f), HudSkin.Segment, c));
        }

        string title = car.HadConnection ? "Lost the physics server" : "Waiting for the physics server";
        HudSkin.Label(x + 130f, y + 30f, w - 150f, 36f, title, HudSkin.Text(28, true), HudSkin.TextMain);
        HudSkin.Label(x + 130f, y + 78f, w - 150f, 26f,
                      $"Start it in a terminal:   <color=#{HudSkin.Hex(HudSkin.Gold)}>python3 socket1.py</color>",
                      HudSkin.Text(18, false), HudSkin.TextDim);
        HudSkin.Label(x + 130f, y + 108f, w - 150f, 26f,
                      car.autoReconnect ? "The sim connects by itself as soon as it's running."
                                        : $"Then press {car.resetKey}.",
                      HudSkin.Text(18, false), HudSkin.TextDim);
    }
}
