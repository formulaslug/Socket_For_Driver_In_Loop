using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// One track (accel, skidpad, Blue Max...) read from a "fs-dil-track-v1" JSON file.
//
// All points stay in the SERVER's frame: Vector2(x, z) in meters, the same numbers that are
// in the file and that socket1.py uses. TrackLoader converts to Unity only when it draws,
// with ToUnity(): server (x, z) -> Unity (z, height, x).
//
// Why a hand-written JSON reader at the bottom? Unity's built-in JsonUtility can't read
// arrays of arrays, and the edge lines are lists of [x, z] pairs. This keeps the track files
// exactly as they are and needs no extra package.
public class TrackFile
{
    public class Surface
    {
        public string material;          // grass, asphalt, paint_white
        public List<Vector2> left = new List<Vector2>();
        public List<Vector2> right = new List<Vector2>();
        public bool closed;              // true = the last point joins back to the first (a loop)
        public float height;             // meters above the ground, so layers don't flicker
    }

    public class Cone
    {
        public Vector2 pos;
        public string kind;              // blue (left edge), yellow (right edge), orange, big_orange
    }

    public class Gate
    {
        public string name;
        public Vector2 a, b;             // the two ends of the timing line
        public Vector2 forward;          // only crossings going this way count
    }

    public string name = "";
    public string description = "";
    public float conePenaltySeconds;
    public Vector2 spawnPos;
    public float spawnYaw;               // radians, server convention
    public string timingMode = "";       // "laps", "start_finish" or "skidpad"
    public string lapGate = "", startGate = "", finishGate = "";
    public List<Surface> surfaces = new List<Surface>();
    public List<Cone> cones = new List<Cone>();
    public List<Gate> gates = new List<Gate>();

    // Server (x, z) + height -> Unity position. The ONE place this swap lives for the track.
    public static Vector3 ToUnity(Vector2 serverXZ, float height) =>
        new Vector3(serverXZ.y, height, serverXZ.x);

    // ======================= Reading the file =======================

    public static TrackFile Parse(string json)
    {
        var root = (Dictionary<string, object>)MiniJson.Parse(json);
        var t = new TrackFile();

        t.name = Str(root, "name");
        t.description = Str(root, "description");
        t.conePenaltySeconds = Num(root, "cone_penalty_s");

        var spawn = Obj(root, "spawn");
        t.spawnPos = new Vector2(Num(spawn, "x"), Num(spawn, "z"));
        t.spawnYaw = Num(spawn, "yaw");

        var timing = Obj(root, "timing");
        t.timingMode = Str(timing, "mode");
        t.lapGate = Str(timing, "gate");
        t.startGate = Str(timing, "start");
        t.finishGate = Str(timing, "finish");

        foreach (Dictionary<string, object> s in List(root, "surfaces"))
        {
            var surface = new Surface
            {
                material = Str(s, "material"),
                closed = s.TryGetValue("closed", out object c) && c is bool b && b,
                height = Num(s, "height"),
            };
            foreach (object p in List(s, "left")) surface.left.Add(Point(p));
            foreach (object p in List(s, "right")) surface.right.Add(Point(p));
            t.surfaces.Add(surface);
        }

        foreach (Dictionary<string, object> c in List(root, "cones"))
            t.cones.Add(new Cone { pos = new Vector2(Num(c, "x"), Num(c, "z")), kind = Str(c, "kind") });

        foreach (Dictionary<string, object> g in List(root, "gates"))
            t.gates.Add(new Gate
            {
                name = Str(g, "name"),
                a = Point(g["a"]),
                b = Point(g["b"]),
                forward = Point(g["forward"]),
            });

        return t;
    }

    // Small helpers so the code above reads like the file does.
    static Dictionary<string, object> Obj(Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out object v) && v is Dictionary<string, object> o ? o : new Dictionary<string, object>();

    static List<object> List(Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out object v) && v is List<object> l ? l : new List<object>();

    static string Str(Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out object v) && v is string s ? s : "";

    static float Num(Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out object v) && v is double n ? (float)n : 0f;

    static Vector2 Point(object value)        // [x, z] -> Vector2(x, z)
    {
        var xz = value as List<object>;
        return xz != null && xz.Count >= 2 ? new Vector2((float)(double)xz[0], (float)(double)xz[1]) : new Vector2(0f, 0f);
    }

    // ======================= A tiny JSON reader =======================
    // Turns JSON text into: Dictionary<string, object> for {...}, List<object> for [...],
    // double for numbers, string, bool, or null. Reads one character at a time, left to right.
    static class MiniJson
    {
        public static object Parse(string text)
        {
            int i = 0;
            object value = ReadValue(text, ref i);
            SkipSpace(text, ref i);
            if (i != text.Length) throw new FormatException($"Unexpected text at character {i}");
            return value;
        }

        static object ReadValue(string s, ref int i)
        {
            SkipSpace(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON ended early");
            char c = s[i];
            if (c == '{') return ReadObject(s, ref i);
            if (c == '[') return ReadArray(s, ref i);
            if (c == '"') return ReadString(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            return ReadNumber(s, ref i);
        }

        static Dictionary<string, object> ReadObject(string s, ref int i)
        {
            var obj = new Dictionary<string, object>();
            i++;                                            // skip {
            SkipSpace(s, ref i);
            if (s[i] == '}') { i++; return obj; }
            while (true)
            {
                SkipSpace(s, ref i);
                string key = ReadString(s, ref i);
                SkipSpace(s, ref i);
                Expect(s, ref i, ':');
                obj[key] = ReadValue(s, ref i);
                SkipSpace(s, ref i);
                if (s[i] == ',') { i++; continue; }
                Expect(s, ref i, '}');
                return obj;
            }
        }

        static List<object> ReadArray(string s, ref int i)
        {
            var list = new List<object>();
            i++;                                            // skip [
            SkipSpace(s, ref i);
            if (s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ReadValue(s, ref i));
                SkipSpace(s, ref i);
                if (s[i] == ',') { i++; continue; }
                Expect(s, ref i, ']');
                return list;
            }
        }

        static string ReadString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                if (s[i] == '\\')                           // escapes like \n, \", °
                {
                    i++;
                    char e = s[i];
                    if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                    else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'r' ? '\r' : e == 'b' ? '\b' : e == 'f' ? '\f' : e);
                }
                else sb.Append(s[i]);
                i++;
            }
            i++;                                            // skip the closing "
            return sb.ToString();
        }

        static double ReadNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == start) throw new FormatException($"Unexpected character '{s[i]}' at {i}");
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static void Expect(string s, ref int i, char c)
        {
            if (i >= s.Length || s[i] != c) throw new FormatException($"Expected '{c}' at character {i}");
            i++;
        }
    }
}
