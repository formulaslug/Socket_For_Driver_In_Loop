using UnityEngine;

// On-screen dashboard: speedometer, steering wheel + angle, and a Reset button.
//
// Uses Unity's built-in immediate-mode GUI (OnGUI), so there's nothing to set up:
// add this to any GameObject (the car is fine) and press Play. Everything is drawn in
// code every frame, which is slower than a Canvas UI but plenty for a few labels.
public class Dashboard : MonoBehaviour
{
    [Tooltip("Leave empty to find it automatically.")]
    public DILCarClient car;
    [Tooltip("Optional. Only used to show which camera is active.")]
    public CameraSwitcher cameras;

    [Tooltip("How far the steering wheel turns at full lock. FS-4's VCU uses 75.82 deg (STEERING_MAX_ANGLE).")]
    public float steeringWheelLockDeg = 75.82f;
    public bool showMph = true;

    const float MpsToMph = 2.23694f;
    const float MpsToKmh = 3.6f;

    Texture2D _wheelTexture;
    GUIStyle _bigNumber, _label, _warning;
    TrackLoader track;

    void Start()
    {
        if (car == null) car = FindFirstObjectByType<DILCarClient>();
        if (cameras == null) cameras = FindFirstObjectByType<CameraSwitcher>();
        track = FindFirstObjectByType<TrackLoader>();
        _wheelTexture = MakeSteeringWheelTexture(256);
    }

    void OnGUI()
    {
        if (car == null) return;
        MakeStylesOnce();

        // Everything is laid out for a 1080p screen, then scaled to whatever the real size is.
        Matrix4x4 originalMatrix = GUI.matrix;
        float k = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
        float screenW = Screen.width / k;
        float screenH = 1080f;

        // ---- Panel in the bottom-left corner ----
        Rect panel = new Rect(30, screenH - 260, 520, 230);
        GUI.Box(panel, GUIContent.none);

        // ---- Speedometer ----
        float speed = Mathf.Max(0f, car.SpeedMps);
        float mainSpeed = showMph ? speed * MpsToMph : speed * MpsToKmh;
        string mainUnit = showMph ? "mph" : "km/h";
        string otherSpeed = showMph ? $"{speed * MpsToKmh:0} km/h" : $"{speed * MpsToMph:0} mph";
        GUI.Label(new Rect(panel.x + 20, panel.y + 15, 260, 110), $"{mainSpeed:0}", _bigNumber);
        GUI.Label(new Rect(panel.x + 25, panel.y + 125, 260, 30), $"{mainUnit}    ({otherSpeed})", _label);

        // ---- Steering wheel ----
        // Turning right (positive steer) spins the wheel clockwise, like a real one.
        float wheelDeg = car.SteerInput * steeringWheelLockDeg;
        Rect wheelRect = new Rect(panel.x + 300, panel.y + 15, 150, 150);
        Matrix4x4 beforeRotate = GUI.matrix;
        GUIUtility.RotateAroundPivot(wheelDeg, wheelRect.center * k);   // pivot is in real screen pixels
        GUI.DrawTexture(wheelRect, _wheelTexture);
        GUI.matrix = beforeRotate;
        GUI.Label(new Rect(panel.x + 280, panel.y + 168, 230, 30),
                  $"wheel {wheelDeg:+0;-0;0}°   tires {car.RoadWheelAngleDeg:+0.0;-0.0;0.0}°", _label);

        // ---- Reset button (same as pressing R) ----
        if (GUI.Button(new Rect(panel.x + 20, panel.y + 170, 150, 40), $"Reset ({car.resetKey})"))
            car.ResetCar();

        // ---- Status line across the top ----
        string help = "W/S throttle   A/D steer   Space front brake   X rear brake   R reset";
        if (cameras != null)
            help += $"   {cameras.switchKey}: camera ({cameras.CurrentName})";
        if (track != null)
            help += "   1/2/3: track";
        GUI.Label(new Rect(30, 20, screenW - 60, 30), help, _label);
        if (!car.IsConnected)
            GUI.Label(new Rect(30, 55, screenW - 60, 30),
                      $"Not connected to socket1.py. Start it, then press {car.resetKey}.", _warning);

        GUI.matrix = originalMatrix;   // put the scaling back so other scripts' GUI isn't affected
    }

    // GUIStyles can only be built inside OnGUI (they copy GUI.skin), so do it on the first call.
    void MakeStylesOnce()
    {
        if (_bigNumber != null) return;
        _bigNumber = new GUIStyle(GUI.skin.label) { fontSize = 96, fontStyle = FontStyle.Bold };
        _label = new GUIStyle(GUI.skin.label) { fontSize = 22 };
        _warning = new GUIStyle(_label);
        _warning.normal.textColor = new Color(1f, 0.45f, 0.35f);
    }

    // Draws a simple steering wheel into a texture once: a rim, a horizontal bar, a hub,
    // and an orange mark at 12 o'clock so you can see how far it has turned.
    static Texture2D MakeSteeringWheelTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        Color32 clear = new Color32(0, 0, 0, 0);
        Color32 grey = new Color32(220, 220, 220, 255);
        Color32 orange = new Color32(255, 140, 0, 255);

        float c = (size - 1) / 2f;     // center
        float R = size / 2f;           // outer radius
        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                float dx = px - c, dy = py - c;              // dy > 0 is the top of the texture
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                Color32 color = clear;
                bool rim = r > 0.80f * R && r < 0.98f * R;
                bool bar = Mathf.Abs(dy) < 0.07f * R && r < 0.82f * R;
                bool hub = r < 0.22f * R;
                bool topMark = rim && dy > 0 && Mathf.Abs(dx) < 0.09f * R;
                if (rim || bar || hub) color = grey;
                if (topMark) color = orange;
                pixels[py * size + px] = color;
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }
}
