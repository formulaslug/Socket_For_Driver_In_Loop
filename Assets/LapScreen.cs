using System.Collections.Generic;
using UnityEngine;

// lap times along the top of the screen, for all three events:
//
//   autocross (laps):  live lap time, last lap, best lap, cones this lap, a list of recent laps,
//                      and a pop-up after every lap with how it compares to your best
//   accel:             live time, then a results card with cone penalties added
//   skidpad:           which lap you're on (warm-up or timed), right and left times, results card
//
// bests are kept for the whole Play session, through resets and track changes.
//
// it reads TrackLoader's timing (TrackTiming) and only displays it, so the scoring rules stay in
// one place. it turns off TrackLoader's own plain timer box while it's running.
public class LapScreen : MonoBehaviour
{
    [Tooltip("Leave empty to find it automatically.")]
    public TrackLoader track;
    public float lapPopupSeconds = 3.5f;
    public int lapListRows = 6;

    // best result per track name for this Play session (penalties included).
    static readonly Dictionary<string, float> sessionBest = new Dictionary<string, float>();

    TrackTiming timing;          // the one we're watching; TrackLoader makes a new one per track
    int lapsSeen;
    float popupAt = -100f;
    string popupText = "";
    Color popupColor = Color.white;
    float resultAt = -1f;        // when the accel/skidpad result appeared, -1 = no result
    bool resultIsNewBest;
    float bestBeforeResult = -1f;

    void Start()
    {
        if (track == null) track = FindFirstObjectByType<TrackLoader>();
    }

    void Update()
    {
        if (track == null) return;
        track.showTimer = false;                   // this screen replaces the plain timer box
        TrackTiming t = track.Timing;
        if (t == null || track.Current == null) return;

        // new track, or the run was reset (R): start watching from scratch.
        if (t != timing || t.lapTimes.Count < lapsSeen)
        {
            timing = t;
            lapsSeen = 0;
            resultAt = -1f;
        }

        // laps that finished since last frame.
        while (lapsSeen < t.lapTimes.Count)
        {
            float lap = t.lapTimes[lapsSeen] + t.lapConeCounts[lapsSeen] * t.ConePenalty;
            float best = Best();
            if (best < 0f) { popupText = $"LAP {lapsSeen + 1}   {HudSkin.FormatTime(lap)}"; popupColor = HudSkin.TextMain; }
            else if (lap < best) { popupText = $"LAP {lapsSeen + 1}   {HudSkin.FormatTime(lap)}   NEW BEST  -{best - lap:0.00}"; popupColor = HudSkin.Green; }
            else { popupText = $"LAP {lapsSeen + 1}   {HudSkin.FormatTime(lap)}   +{lap - best:0.00}"; popupColor = HudSkin.Red; }
            if (t.lapConeCounts[lapsSeen] > 0)
                popupText += $"   ({t.lapConeCounts[lapsSeen]} cone{(t.lapConeCounts[lapsSeen] == 1 ? "" : "s")})";
            popupAt = Time.time;
            SetBest(lap);
            lapsSeen++;
        }

        // accel or skidpad finished.
        if (t.Result >= 0f && resultAt < 0f)
        {
            resultAt = Time.time;
            bestBeforeResult = Best();
            resultIsNewBest = bestBeforeResult < 0f || t.Result < bestBeforeResult;
            SetBest(t.Result);
        }
        if (t.Result < 0f) resultAt = -1f;
    }

    float Best() => sessionBest.TryGetValue(track.Current.name, out float b) ? b : -1f;

    void SetBest(float value)
    {
        float b = Best();
        if (b < 0f || value < b) sessionBest[track.Current.name] = value;
    }

    void OnGUI()
    {
        if (track == null || track.Timing == null || track.Current == null) return;
        GUI.depth = 1;                             // behind the dashboard's pop-ups
        HudSkin.Begin();
        TrackTiming t = track.Timing;
        float now = Time.time;

        DrawMain(t, now);
        if (t.Mode == "laps" && t.lapTimes.Count > 0) DrawLapList(t);
        DrawPopup(now);
        if (resultAt >= 0f) DrawResult(t, now);
    }

    void DrawMain(TrackTiming t, float now)
    {
        const float w = 600f, h = 150f;
        float x = HudSkin.W / 2f - w / 2f, y = 22f;
        HudSkin.DrawPanel(x, y, w, h);

        var header = HudSkin.Text(17, true);
        var headerRight = HudSkin.Text(17, true, TextAnchor.MiddleRight);
        var big = HudSkin.Text(60, true, TextAnchor.MiddleCenter);
        var prompt = HudSkin.Text(26, true, TextAnchor.MiddleCenter);
        HudSkin.Label(x + 24f, y + 12f, 360f, 26f, track.Current.name.ToUpperInvariant(), header, HudSkin.Gold);

        string dim = "<color=#" + HudSkin.Hex(HudSkin.TextDim) + ">";
        string col1 = "", col2 = "", col3 = "", right = "";
        float pen = t.ConePenalty;

        if (t.Mode == "laps")
        {
            right = t.RunStart >= 0f ? $"LAP {t.lapTimes.Count + 1}" : "AUTOCROSS";
            if (t.RunStart >= 0f)
                HudSkin.Label(x, y + 38f, w, 68f, HudSkin.FormatTime(now - t.RunStart), big, HudSkin.TextMain);
            else
                HudSkin.Label(x, y + 38f, w, 68f, "Cross the start line", prompt, HudSkin.TextDim);

            if (t.lapTimes.Count > 0)
            {
                int last = t.lapTimes.Count - 1;
                int cones = t.lapConeCounts[last];
                col1 = $"{dim}LAST</color>  {HudSkin.FormatTime(t.lapTimes[last] + cones * pen)}";
            }
            else col1 = $"{dim}LAST</color>  --";
            float best = Best();
            col2 = $"{dim}BEST</color>  {(best >= 0f ? HudSkin.FormatTime(best) : "--")}";
            col3 = $"{dim}CONES</color>  {t.ConesThisLap}" + (t.ConesThisLap > 0 ? $" {dim}(+{t.ConesThisLap * pen:0.#} s)</color>" : "");
        }
        else if (t.Mode == "start_finish")
        {
            right = "ACCEL";
            if (t.Result >= 0f)
                HudSkin.Label(x, y + 38f, w, 68f, HudSkin.FormatTime(t.Result, 3), big, HudSkin.Gold);
            else if (t.RunStart >= 0f)
                HudSkin.Label(x, y + 38f, w, 68f, HudSkin.FormatTime(now - t.RunStart), big, HudSkin.TextMain);
            else
                HudSkin.Label(x, y + 38f, w, 68f, "Floor it! The timer starts at the line", prompt, HudSkin.TextDim);

            int cones = t.Result >= 0f ? t.ResultCones : t.ConesThisLap;   // after the finish, the ones that counted
            col1 = $"{dim}CONES</color>  {cones}" + (cones > 0 ? $" {dim}(+{cones * pen:0.#} s)</color>" : "");
            col2 = $"{dim}PENALTY</color>  {pen:0.###} s {dim}each</color>";
            float best = Best();
            col3 = $"{dim}BEST</color>  {(best >= 0f ? HudSkin.FormatTime(best, 3) : "--")}";
        }
        else if (t.Mode == "skidpad")
        {
            int step = t.SkidpadStep;
            string[] steps = { "DRIVE IN", "RIGHT  WARM-UP", "RIGHT  TIMED", "LEFT  WARM-UP", "LEFT  TIMED", "DONE" };
            right = steps[step];
            if (t.Result >= 0f)
                HudSkin.Label(x, y + 38f, w, 68f, HudSkin.FormatTime(t.Result, 3), big, HudSkin.Gold);
            else if (step == 0)
                HudSkin.Label(x, y + 38f, w, 68f, "Drive in through the gate", prompt, HudSkin.TextDim);
            else
            {
                bool timed = step == 2 || step == 4;
                HudSkin.Label(x, y + 38f, w, 68f, HudSkin.FormatTime(now - t.LastCrossing), big, timed ? HudSkin.TextMain : HudSkin.TextDim);
            }
            col1 = $"{dim}RIGHT</color>  {(t.SkidRight >= 0f ? HudSkin.FormatTime(t.SkidRight, 3) : "--")}";
            col2 = $"{dim}LEFT</color>  {(t.SkidLeft >= 0f ? HudSkin.FormatTime(t.SkidLeft, 3) : "--")}";
            int hits = t.Result >= 0f ? t.ResultCones : t.HitTotal;
            col3 = $"{dim}CONES</color>  {hits}" + (hits > 0 ? $" {dim}(+{hits * pen:0.###} s)</color>" : "");
        }

        HudSkin.Label(x + 300f, y + 12f, 276f, 26f, right, headerRight, HudSkin.TextMain);
        var row = HudSkin.Text(18, true, TextAnchor.MiddleCenter);
        HudSkin.Tint(new Rect(x + 24f, y + 108f, w - 48f, 1f), HudSkin.Pixel, new Color(1f, 1f, 1f, 0.10f));
        HudSkin.Label(x, y + 114f, 200f, 28f, col1, row, HudSkin.TextMain);
        HudSkin.Label(x + 200f, y + 114f, 200f, 28f, col2, row, HudSkin.TextMain);
        HudSkin.Label(x + 400f, y + 114f, 200f, 28f, col3, row, HudSkin.TextMain);
    }

    // recent laps, newest on top. the fastest one (cones included) is gold.
    void DrawLapList(TrackTiming t)
    {
        int n = Mathf.Min(lapListRows, t.lapTimes.Count);
        const float w = 260f;
        float x = HudSkin.W - w - 22f, y = 22f, h = 50f + n * 28f;
        HudSkin.DrawPanel(x, y, w, h);
        HudSkin.Label(x + 20f, y + 12f, 200f, 22f, "LAPS", HudSkin.Text(13, true), HudSkin.TextDim);

        float pen = t.ConePenalty;
        int bestIndex = 0;
        for (int i = 1; i < t.lapTimes.Count; i++)
            if (t.lapTimes[i] + t.lapConeCounts[i] * pen < t.lapTimes[bestIndex] + t.lapConeCounts[bestIndex] * pen)
                bestIndex = i;

        var num = HudSkin.Text(16, true);
        var time = HudSkin.Text(18, true, TextAnchor.MiddleRight);
        var cones = HudSkin.Text(14, false, TextAnchor.MiddleRight);
        for (int row = 0; row < n; row++)
        {
            int i = t.lapTimes.Count - 1 - row;
            float ry = y + 40f + row * 28f;
            Color c = i == bestIndex ? HudSkin.Gold : HudSkin.TextMain;
            HudSkin.Label(x + 20f, ry, 40f, 26f, (i + 1).ToString(), num, HudSkin.TextDim);
            HudSkin.Label(x + 50f, ry, 110f, 26f, HudSkin.FormatTime(t.lapTimes[i] + t.lapConeCounts[i] * pen), time, c);
            int k = t.lapConeCounts[i];
            HudSkin.Label(x + 150f, ry, 90f, 26f, k > 0 ? $"{k} cone{(k == 1 ? "" : "s")}" : "clean", cones,
                          k > 0 ? HudSkin.Red : HudSkin.TextDim);
        }
    }

    void DrawPopup(float now)
    {
        float age = now - popupAt;
        if (age > lapPopupSeconds) return;
        float alpha = Mathf.Clamp01((lapPopupSeconds - age) / 0.6f) * Mathf.Clamp01(age / 0.15f);
        const float w = 560f;
        float x = HudSkin.W / 2f - w / 2f;
        HudSkin.DrawPanel(x, 184f, w, 46f, alpha);
        HudSkin.Label(x, 184f, w, 46f, popupText, HudSkin.Text(20, true, TextAnchor.MiddleCenter), popupColor, alpha);
    }

    // the card for a finished accel or skidpad run. stays until R.
    void DrawResult(TrackTiming t, float now)
    {
        float alpha = Mathf.Clamp01((now - resultAt) / 0.3f);
        const float w = 620f, h = 250f;
        float x = HudSkin.W / 2f - w / 2f, y = 250f;          // above the car, below the lap panel
        HudSkin.DrawPanel(x, y, w, h, alpha);

        var center = TextAnchor.MiddleCenter;
        HudSkin.Label(x, y + 18f, w, 26f, resultIsNewBest ? "RUN COMPLETE  -  NEW BEST" : "RUN COMPLETE",
                      HudSkin.Text(18, true, center), resultIsNewBest ? HudSkin.Green : HudSkin.Gold, alpha);
        HudSkin.Label(x, y + 48f, w, 84f, HudSkin.FormatTime(t.Result, 3) + " s", HudSkin.Text(72, true, center), HudSkin.TextMain, alpha);

        float pen = t.ConePenalty;
        string detail;
        if (t.Mode == "skidpad")
            detail = $"right {HudSkin.FormatTime(t.SkidRight, 3)}   left {HudSkin.FormatTime(t.SkidLeft, 3)}   average of the two" +
                     (t.ResultCones > 0 ? $"   + {t.ResultCones} x {pen:0.###} s cones" : "");
        else
        {
            int cones = t.ResultCones;      // counted when the car crossed the finish, not cones hit after
            float raw = t.Result - cones * pen;
            detail = cones > 0 ? $"{HudSkin.FormatTime(raw, 3)} s driving   + {cones} cone{(cones == 1 ? "" : "s")} x {pen:0.###} s"
                               : "clean run, no cones";
        }
        HudSkin.Label(x, y + 140f, w, 26f, detail, HudSkin.Text(17, false, center), HudSkin.TextDim, alpha);

        string bestLine = bestBeforeResult >= 0f
            ? (resultIsNewBest ? $"previous best {HudSkin.FormatTime(bestBeforeResult, 3)} s" : $"session best {HudSkin.FormatTime(bestBeforeResult, 3)} s")
            : "first run this session";
        HudSkin.Label(x, y + 170f, w, 24f, bestLine, HudSkin.Text(16, false, center), HudSkin.TextDim, alpha);
        HudSkin.Label(x, y + 206f, w, 26f, "Press R to run again", HudSkin.Text(18, true, center), HudSkin.Gold, alpha);
    }
}
