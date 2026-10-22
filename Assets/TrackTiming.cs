using System;
using System.Collections.Generic;
using UnityEngine;

// Scores a run: cone hits, timing gates, and lap / accel / skidpad times.
//
// Plain C# with no Unity objects in it, so it can be tested outside Unity. It is the same
// logic as TrackRun in tracks/drive_test.py (the Python autopilot test), line for line, so
// the autopilot's scores predict what the game shows.
//
// Everything is in the server's frame: positions are Vector2(x, z) in meters.
public class TrackTiming
{
    // Car footprint for cone hits: an FS car is about 2.9 m long and 1.45 m wide.
    public float carHalfLength = 1.45f;
    public float carHalfWidth = 0.72f;
    public const float ConeRadius = 0.15f;

    readonly TrackFile track;
    readonly bool[] coneHit;

    // laps mode
    public readonly List<float> lapTimes = new List<float>();       // raw lap times
    public readonly List<int> lapConeCounts = new List<int>();      // cones hit during each lap
    // skidpad mode: times the start/stop gate was crossed
    readonly List<float> skidCrossings = new List<float>();

    float lapStart = -1f;            // -1 = not started yet
    int lapCones;                    // cones hit since the current lap (or run) started
    public int HitTotal { get; private set; }
    public float Result { get; private set; } = -1f;   // accel / skidpad final time, -1 = not done
    public float SkidRight { get; private set; } = -1f;
    public float SkidLeft { get; private set; } = -1f;

    public TrackTiming(TrackFile track)
    {
        this.track = track;
        coneHit = new bool[track.cones.Count];
    }

    public bool IsConeHit(int index) => coneHit[index];

    // Start the run over: cones stand back up, all times cleared.
    public void Reset()
    {
        Array.Clear(coneHit, 0, coneHit.Length);
        lapTimes.Clear();
        lapConeCounts.Clear();
        skidCrossings.Clear();
        lapStart = -1f;
        lapCones = 0;
        HitTotal = 0;
        Result = -1f;
        SkidRight = SkidLeft = -1f;
    }

    // Call once per frame with where the car was and where it is now.
    // heading = unit vector the car points along. Returns the cones hit THIS call.
    public List<int> Update(float time, Vector2 from, Vector2 to, Vector2 heading)
    {
        var newlyHit = new List<int>();

        // Cone hits: is the cone inside the car's rectangle (grown by the cone's radius)?
        // "along" = how far ahead of the car's center, "side" = how far to the right.
        Vector2 right = new Vector2(-heading.y, heading.x);
        for (int i = 0; i < coneHit.Length; i++)
        {
            if (coneHit[i]) continue;
            Vector2 rel = new Vector2(track.cones[i].pos.x - to.x, track.cones[i].pos.y - to.y);
            float along = Dot(rel, heading);
            float side = Dot(rel, right);
            if (Math.Abs(along) <= carHalfLength + ConeRadius && Math.Abs(side) <= carHalfWidth + ConeRadius)
            {
                coneHit[i] = true;
                HitTotal++;
                lapCones++;
                newlyHit.Add(i);
            }
        }

        // Gates: only crossings in the gate's forward direction count.
        Vector2 move = new Vector2(to.x - from.x, to.y - from.y);
        foreach (TrackFile.Gate gate in track.gates)
        {
            if (SegmentsCross(from, to, gate.a, gate.b) && Dot(move, gate.forward) > 0f)
                OnGate(gate.name, time);
        }
        return newlyHit;
    }

    void OnGate(string name, float time)
    {
        string mode = track.timingMode;
        if (mode == "laps" && name == track.lapGate)
        {
            if (lapStart >= 0f)                      // the very first crossing just starts lap 1
            {
                lapTimes.Add(time - lapStart);
                lapConeCounts.Add(lapCones);
            }
            lapStart = time;
            lapCones = 0;
        }
        else if (mode == "start_finish")
        {
            if (name == track.startGate && lapStart < 0f)
            {
                lapStart = time;
                lapCones = 0;
            }
            else if (name == track.finishGate && lapStart >= 0f && Result < 0f)
            {
                Result = (time - lapStart) + lapCones * track.conePenaltySeconds;
            }
        }
        else if (mode == "skidpad" && name == track.lapGate)
        {
            // crossing 0 = drive in, 1 = after the right warm-up lap, 2 = after the timed right lap,
            // 3 = after the left warm-up lap, 4 = after the timed left lap
            skidCrossings.Add(time);
            if (skidCrossings.Count == 5)
            {
                SkidRight = skidCrossings[2] - skidCrossings[1];
                SkidLeft = skidCrossings[4] - skidCrossings[3];
                Result = (SkidRight + SkidLeft) / 2f + HitTotal * track.conePenaltySeconds;
            }
        }
    }

    // What the on-screen timer shows. now = current time on the same clock as Update's.
    public string StatusText(float now)
    {
        string s = "";
        float penalty = track.conePenaltySeconds;
        string mode = track.timingMode;

        if (mode == "laps")
        {
            s += lapStart >= 0f
                ? $"Lap {lapTimes.Count + 1}: {now - lapStart:0.00} s   cones this lap: {lapCones}\n"
                : "Cross the start/finish line to start lap 1\n";
            if (lapTimes.Count > 0)
            {
                int last = lapTimes.Count - 1;
                float best = float.MaxValue;
                for (int i = 0; i < lapTimes.Count; i++)
                    best = Math.Min(best, lapTimes[i] + lapConeCounts[i] * penalty);
                s += $"Last lap: {lapTimes[last]:0.00} s (+{lapConeCounts[last] * penalty:0.0} s cones)   Best: {best:0.00} s\n";
            }
        }
        else if (mode == "start_finish")
        {
            if (Result >= 0f) s += $"Time: {Result:0.000} s (cone penalties included)\n";
            else if (lapStart >= 0f) s += $"Time: {now - lapStart:0.00} s\n";
            else s += "Go! The timer starts at the start line\n";
        }
        else if (mode == "skidpad")
        {
            string[] steps =
            {
                "Drive in through the entry gate", "Right circle: warm-up lap", "Right circle: TIMED lap",
                "Left circle: warm-up lap", "Left circle: TIMED lap", "Done! Exit straight ahead",
            };
            s += steps[Math.Min(skidCrossings.Count, 5)] + "\n";
            if (Result >= 0f)
                s += $"Right {SkidRight:0.000} s   Left {SkidLeft:0.000} s   Result {Result:0.000} s (cone penalties included)\n";
        }

        s += $"Cones hit: {HitTotal} (+{penalty:0.###} s each)";
        return s;
    }

    static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;

    // Did the move p0 -> p1 cross the gate segment a -> b? Both segments have to straddle each other.
    static bool SegmentsCross(Vector2 p0, Vector2 p1, Vector2 a, Vector2 b)
    {
        return Side(a, b, p0) * Side(a, b, p1) < 0f && Side(p0, p1, a) * Side(p0, p1, b) < 0f;
    }

    // Which side of the line o -> u the point v is on: positive one side, negative the other.
    static float Side(Vector2 o, Vector2 u, Vector2 v) => (u.x - o.x) * (v.y - o.y) - (u.y - o.y) * (v.x - o.x);
}
