using System;
using System.Collections.Generic;
using UnityEngine;

// Loads a track file and makes it drivable: draws the grass, asphalt and paint, stands up the
// cones, puts the car on the start line, and runs the timer.
//
//   Keys 1, 2, 3 = Blue Max, Accel, Skidpad (the order of trackFiles below)
//   R            = reset (handled by DILCarClient; the timer and cones reset with it)
//
// Track files live in Assets/Resources/Tracks/ so Unity can find them by name.
// Track coordinates are world coordinates, so this object always sits at the origin.
public class TrackLoader : MonoBehaviour
{
    [Tooltip("File names in Assets/Resources/Tracks, without .json. Keys 1, 2, 3... load them in this order.")]
    public string[] trackFiles = { "blue_max", "accel", "skidpad" };
    public int startTrack = 0;

    [Tooltip("Leave empty to find it automatically.")]
    public DILCarClient car;

    [Tooltip("Grass, asphalt and paint sit 1 cm apart in the files. Stretching that a bit stops them flickering from far away.")]
    public float layerHeightScale = 3f;
    public bool showTimer = true;

    public TrackFile Current { get; private set; }
    public TrackTiming Timing { get; private set; }

    // What a cone looks like standing up and knocked over, for the tip-over animation.
    class ConeView
    {
        public Transform transform;
        public Vector3 homePosition, downPosition;
        public Quaternion homeRotation, downRotation;
        public float knockedAt;
    }

    const float TipOverSeconds = 0.3f;

    Transform built;                     // everything drawn for the current track lives under this
    readonly List<ConeView> coneViews = new List<ConeView>();
    readonly List<ConeView> tipping = new List<ConeView>();
    Vector2 previousPosition;
    bool havePrevious;
    GUIStyle hudStyle;

    static Mesh coneMesh;

    // ======================= Unity events =======================

    void Start()
    {
        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        transform.localScale = Vector3.one;

        if (car == null) car = FindFirstObjectByType<DILCarClient>();
        if (car != null) car.OnReset += HandleCarReset;
        Load(startTrack);
    }

    void OnDestroy()
    {
        if (car != null) car.OnReset -= HandleCarReset;
    }

    void Update()
    {
        for (int i = 0; i < trackFiles.Length && i < 9; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                Load(i);

        AnimateTippingCones();
        if (car == null || Timing == null) return;

        // Where the car is, back in the server's frame: Unity (X, Z) = server (z, x).
        Vector3 p = car.transform.position;
        Vector3 f = car.transform.forward;
        Vector2 position = new Vector2(p.z, p.x);
        Vector2 heading = new Vector2(f.z, f.x);

        // A jump of more than 5 m in one frame is a reset, not driving, so it can't cross a gate.
        float dx = position.x - previousPosition.x, dz = position.y - previousPosition.y;
        if (havePrevious && dx * dx + dz * dz < 25f)
        {
            foreach (int cone in Timing.Update(Time.time, previousPosition, position, heading))
                KnockOver(cone, heading);
        }
        previousPosition = position;
        havePrevious = true;
    }

    // ======================= Loading a track =======================

    public void Load(int index)
    {
        if (index < 0 || index >= trackFiles.Length) return;

        TextAsset file = Resources.Load<TextAsset>("Tracks/" + trackFiles[index]);
        if (file == null)
        {
            Debug.LogError($"[Track] Couldn't find Assets/Resources/Tracks/{trackFiles[index]}.json");
            return;
        }
        TrackFile track;
        try { track = TrackFile.Parse(file.text); }
        catch (Exception e)
        {
            Debug.LogError($"[Track] {trackFiles[index]}.json couldn't be read: {e.Message}");
            return;
        }

        // Throw away the old track and start fresh.
        if (built != null) Destroy(built.gameObject);
        built = new GameObject("Track: " + track.name).transform;
        built.SetParent(transform, false);
        coneViews.Clear();
        tipping.Clear();
        Current = track;
        Timing = new TrackTiming(track);
        havePrevious = false;

        foreach (TrackFile.Surface surface in track.surfaces)
            BuildSurface(surface);
        BuildCones(track);

        // The start line: an empty object facing down the track. The car spawns on it.
        Transform spawn = new GameObject("Spawn").transform;
        spawn.SetParent(built, false);
        spawn.position = TrackFile.ToUnity(track.spawnPos, 0f);
        spawn.rotation = Quaternion.Euler(0f, track.spawnYaw * Mathf.Rad2Deg, 0f);
        if (car != null) car.SetSpawnPoint(spawn);

        Debug.Log($"[Track] Loaded {track.name}: {track.cones.Count} cones, {track.gates.Count} timing gate(s)");
    }

    // A flat strip between a left and a right edge line: one quad (2 triangles) per step.
    void BuildSurface(TrackFile.Surface s)
    {
        int n = Math.Min(s.left.Count, s.right.Count);
        if (n < 2) return;
        float height = s.height * layerHeightScale;

        var vertices = new Vector3[n * 2];
        var normals = new Vector3[n * 2];
        for (int i = 0; i < n; i++)
        {
            vertices[2 * i] = TrackFile.ToUnity(s.left[i], height);
            vertices[2 * i + 1] = TrackFile.ToUnity(s.right[i], height);
            normals[2 * i] = normals[2 * i + 1] = Vector3.up;
        }

        int quads = s.closed ? n : n - 1;
        var triangles = new int[quads * 12];
        for (int q = 0; q < quads; q++)
        {
            int a = 2 * q, b = a + 1, c = 2 * ((q + 1) % n), d = c + 1;
            // Both windings, so the strip shows from above whichever way the file drew it.
            int[] both = { a, c, b, b, c, d, a, b, c, b, d, c };
            Array.Copy(both, 0, triangles, q * 12, 12);
        }

        var mesh = new Mesh { name = s.material };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        var go = new GameObject(s.material);
        go.transform.SetParent(built, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = SimpleMaterials.Get(ColorFor(s.material));
    }

    void BuildCones(TrackFile track)
    {
        if (coneMesh == null) coneMesh = MakeConeMesh(16);
        float ground = 0.01f * layerHeightScale;          // cones stand on the asphalt layer

        foreach (TrackFile.Cone cone in track.cones)
        {
            bool big = cone.kind == "big_orange";
            var go = new GameObject("Cone " + cone.kind);
            go.transform.SetParent(built, false);
            go.transform.position = TrackFile.ToUnity(cone.pos, ground);
            // FSAE cones: 30 cm wide x 45 cm tall, the big timing cones 40 x 70
            go.transform.localScale = big ? new Vector3(0.4f, 0.7f, 0.4f) : new Vector3(0.3f, 0.45f, 0.3f);
            go.AddComponent<MeshFilter>().sharedMesh = coneMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = SimpleMaterials.Get(ColorFor(cone.kind));

            coneViews.Add(new ConeView
            {
                transform = go.transform,
                homePosition = go.transform.position,
                homeRotation = go.transform.rotation,
                knockedAt = -1f,
            });
        }
    }

    // ======================= Cones getting hit =======================

    void KnockOver(int index, Vector2 heading)
    {
        ConeView cone = coneViews[index];
        Vector3 forward = new Vector3(heading.y, 0f, heading.x);   // car's heading, in Unity
        // Lie it down pointing the way the car was going, shoved 0.8 m ahead.
        cone.downRotation = Quaternion.FromToRotation(Vector3.up, forward);
        cone.downPosition = cone.homePosition + forward * 0.8f + Vector3.up * 0.15f;
        cone.knockedAt = Time.time;
        tipping.Add(cone);
    }

    void AnimateTippingCones()
    {
        for (int i = tipping.Count - 1; i >= 0; i--)
        {
            ConeView cone = tipping[i];
            float t = Mathf.Clamp01((Time.time - cone.knockedAt) / TipOverSeconds);
            float eased = 1f - (1f - t) * (1f - t);                 // fast at first, settling at the end
            cone.transform.position = Vector3.Lerp(cone.homePosition, cone.downPosition, eased);
            cone.transform.rotation = Quaternion.Slerp(cone.homeRotation, cone.downRotation, eased);
            if (t >= 1f) tipping.RemoveAt(i);
        }
    }

    // R was pressed (or the track changed): clear the timer and stand every cone back up.
    void HandleCarReset()
    {
        if (Timing != null) Timing.Reset();
        tipping.Clear();
        foreach (ConeView cone in coneViews)
        {
            cone.transform.position = cone.homePosition;
            cone.transform.rotation = cone.homeRotation;
            cone.knockedAt = -1f;
        }
        havePrevious = false;
    }

    // ======================= Timer on screen =======================

    void OnGUI()
    {
        if (!showTimer || Timing == null || Current == null) return;
        if (hudStyle == null) hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 22 };

        Matrix4x4 originalMatrix = GUI.matrix;
        float k = Screen.height / 1080f;                       // laid out for 1080p, scaled to fit
        GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
        float screenW = Screen.width / k;

        Rect box = new Rect(screenW - 650, 100, 620, 170);
        GUI.Box(box, GUIContent.none);
        int keys = Math.Min(trackFiles.Length, 9);
        string text = $"{Current.name}     (keys 1-{keys} change track)\n" + Timing.StatusText(Time.time);
        GUI.Label(new Rect(box.x + 16, box.y + 10, box.width - 32, box.height - 20), text, hudStyle);

        GUI.matrix = originalMatrix;
    }

    // ======================= Looks =======================

    static Color ColorFor(string name)
    {
        switch (name)
        {
            case "grass": return new Color(0.10f, 0.25f, 0.06f);
            case "asphalt": return new Color(0.12f, 0.12f, 0.13f);
            case "paint_white": return new Color(0.85f, 0.85f, 0.85f);
            case "blue": return new Color(0.02f, 0.10f, 0.80f);
            case "yellow": return new Color(1.00f, 0.70f, 0.00f);
            default: return new Color(1.00f, 0.25f, 0.00f);   // orange, big_orange
        }
    }

    // A cone 1 m wide and 1 m tall with its base at y = 0. Each cone object scales it to size.
    // Every side triangle gets its own corners so it's lit as a flat face.
    static Mesh MakeConeMesh(int segments)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        float r = 0.5f;
        for (int i = 0; i < segments; i++)
        {
            float a0 = 2f * Mathf.PI * i / segments, a1 = 2f * Mathf.PI * (i + 1) / segments;
            float mid = (a0 + a1) / 2f;
            Vector3 normal = new Vector3(Mathf.Cos(mid), r, Mathf.Sin(mid)).normalized;   // tilted out and up
            int start = vertices.Count;
            vertices.Add(new Vector3(r * Mathf.Cos(a0), 0f, r * Mathf.Sin(a0)));
            vertices.Add(new Vector3(r * Mathf.Cos(a1), 0f, r * Mathf.Sin(a1)));
            vertices.Add(new Vector3(0f, 1f, 0f));
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            // both windings, so it shows from outside whichever way round they are
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 1 });
        }
        var mesh = new Mesh { name = "Cone" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
