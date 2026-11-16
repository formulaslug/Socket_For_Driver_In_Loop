using System.Collections.Generic;
using UnityEngine;

// model from julian baldwin in the slack
//
// Put the model at Assets/Resources/CarModel/FS-4.fbx. DILBootstrap then adds this script to
// the car by itself when you press Play (and you get the simple box car if the file isn't there).
//
// When you press Play it:
//   1. loads the model and puts it under the car (nothing is saved into the scene)
//   2. turns it so the nose points forward and the tires sit on the track. It works this out
//      from the parts themselves (the tires and the parts named Front_wing / Back_Wing), so it
//      still comes out right if Unity's importer turned the model on its side
//   3. finds the 4 wheels (each tire plus the rim inside it): all 4 roll with the speed and
//      the front 2 turn with the steering, like CarVisual's box wheels
//   4. moves the driver camera to where the driver's eyes are in this car
public class CarModel : MonoBehaviour
{
    public const string DefaultResource = "CarModel/FS-4";

    [Tooltip("A model inside Assets/Resources, path without the extension. Ignored if Model Prefab is set.")]
    public string resourcePath = DefaultResource;
    [Tooltip("Optional: drag a model here instead of using the Resources folder.")]
    public GameObject modelPrefab;

    [Header("Placement")]
    [Tooltip("Turn and place the model automatically. Turn off to use it exactly as imported.")]
    public bool autoPlace = true;
    [Tooltip("Share of the car's weight on the front tires (2025 spec sheet: 46.3%). The point the " +
             "physics server moves is the center of mass, so this sets how far forward the model sits.")]
    [Range(30f, 70f)] public float frontWeightPercent = 46.3f;
    [Tooltip("Lifts the model. The track's asphalt is drawn 3 cm above zero, so the tires sit on it.")]
    public float rideHeight = 0.03f;
    [Tooltip("Extra turn if the nose still points the wrong way (try 180).")]
    public float extraYawDegrees = 0f;

    [Header("Wheels")]
    public bool animateWheels = true;

    [Header("Driver camera")]
    [Tooltip("Where the driver's eyes are in this car, from the car's center point on the ground: " +
             "x = right, y = up, z = forward. A real driver's eyes are about 0.82 m up, but the front " +
             "roll hoop then fills the middle of the view, so this sits a little higher.")]
    public Vector3 driverEye = new Vector3(0f, 0.9f, 0f);
    public bool setDriverCamera = true;

    public Transform Model { get; private set; }
    public int WheelCount => wheels.Count;

    class Wheel
    {
        public string label;            // FL, FR, RL, RR
        public Transform steer, roll;   // steer turns around up, roll spins around the axle
        public Quaternion steerBase;    // steer's rotation when pointing straight ahead
        public bool front;
        public float radius, rollDegrees;
    }

    readonly List<Wheel> wheels = new List<Wheel>();
    DILCarClient car;

    // Used by DILBootstrap to decide between this and the box car.
    public static bool IsAvailable(string path = DefaultResource) => Resources.Load<GameObject>(path) != null;

    void Start()
    {
        car = GetComponent<DILCarClient>();
        GameObject prefab = modelPrefab != null ? modelPrefab : Resources.Load<GameObject>(resourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"[Car] No model at Assets/Resources/{resourcePath} (.fbx). Using the simple box car instead.");
            if (GetComponent<CarVisual>() == null) gameObject.AddComponent<CarVisual>();
            enabled = false;
            return;
        }

        // Measure everything with the car parked at the origin facing +Z, so world space is car space.
        Vector3 savedPosition = transform.position;
        Quaternion savedRotation = transform.rotation;
        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        // A holder that cancels out any scale on the car object, so the model stays life size.
        Transform holder = new GameObject("3D Model").transform;
        holder.SetParent(transform, false);
        Vector3 s = transform.lossyScale;
        holder.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);

        GameObject instance = Instantiate(prefab, holder);
        instance.name = prefab.name;
        Model = instance.transform;
        foreach (Collider c in instance.GetComponentsInChildren<Collider>())
            Destroy(c);                                 // the server does the physics, not Unity

        var parts = new List<Renderer>(instance.GetComponentsInChildren<Renderer>());
        HideLeftoverFloor(parts);
        if (parts.Count > 0)
        {
            float length = Largest(Union(parts).size);
            if (length < 1f || length > 10f)
                Debug.LogWarning($"[Car] The model is {length:0.##} m long, but the car is about 2.9 m. Select the " +
                                 "model file, and in the Inspector's Model tab change Scale Factor (0.01 if it was in cm, " +
                                 "0.001 if in mm), then Apply.");
        }
        var tires = parts.FindAll(IsTire);

        if (autoPlace) Place(parts, tires);
        if (animateWheels) FindWheels(parts, tires);

        transform.SetPositionAndRotation(savedPosition, savedRotation);

        if (setDriverCamera)
        {
            CameraSwitcher cameras = FindFirstObjectByType<CameraSwitcher>();
            if (cameras != null) cameras.driverEyeOffset = driverEye;
        }
        Debug.Log($"[Car] Loaded the {prefab.name} model: {parts.Count} parts, {wheels.Count} animated wheels.");
    }

    void Update()
    {
        if (car == null) return;
        foreach (Wheel w in wheels)
        {
            // Rolling without slipping: the wheel turns (speed / radius) radians every second.
            w.rollDegrees = (w.rollDegrees + car.SpeedMps / w.radius * Mathf.Rad2Deg * Time.deltaTime) % 360f;
            w.roll.localRotation = Quaternion.Euler(w.rollDegrees, 0f, 0f);
            if (w.front)
                w.steer.localRotation = w.steerBase * Quaternion.Euler(0f, car.RoadWheelAngleDeg, 0f);
        }
    }

    

    void Place(List<Renderer> parts, List<Renderer> tires)
    {
        if (parts.Count == 0) return;

        // Importers only ever turn a model by quarter turns, so each direction is one of the axes.
        // Forward = the car's longest side. Sideways = the thin side of a tire (the axle).
        // Up = the remaining one, pointing from the tires toward the body.
        Bounds all = Union(parts);
        int forwardAxis = Longest(all.size);
        int sideAxis = tires.Count > 0 ? Shortest(tires[0].bounds.size) : Shortest(all.size);
        if (sideAxis == forwardAxis) sideAxis = Shortest(all.size);
        int upAxis = 3 - forwardAxis - sideAxis;
        if (upAxis < 0 || upAxis > 2 || upAxis == forwardAxis || upAxis == sideAxis)
        {
            Debug.LogWarning("[Car] Couldn't work out which way the model faces. Using it as imported.");
            return;
        }

        Vector3 forward = Axis(forwardAxis), up = Axis(upAxis);
        Vector3 bottom = tires.Count > 0 ? Union(tires).center : all.min;
        if (Vector3.Dot(all.center - bottom, up) < 0f) up = -up;

        // Front vs back: from the rear wing to the front wing.
        Renderer frontWing = FindPart(parts, "wing", "front", "nose");
        Renderer rearWing = FindPart(parts, "wing", "back", "rear");
        if (frontWing != null && rearWing != null)
        {
            if (Vector3.Dot(frontWing.bounds.center - rearWing.bounds.center, forward) < 0f)
                forward = -forward;
        }
        else
        {
            Debug.LogWarning("[Car] No parts named Front_wing / Back_Wing, so the model's front can't be told " +
                             "from its back. If it drives backwards, set Extra Yaw Degrees to 180.");
        }

        // Turn the whole model (around the car's origin) so forward -> +Z and up -> +Y.
        Quaternion turn = Quaternion.AngleAxis(extraYawDegrees, Vector3.up) *
                          Quaternion.Inverse(Quaternion.LookRotation(forward, up));
        Model.localPosition = turn * Model.localPosition;
        Model.localRotation = turn * Model.localRotation;

    
        all = Union(parts);
        float centerX = all.center.x, groundY = all.min.y, massZ = all.center.z;
        if (tires.Count == 4)
        {
            var z = new List<float>();
            float sumX = 0f;
            groundY = float.MaxValue;
            foreach (Renderer t in tires)
            {
                z.Add(t.bounds.center.z);
                sumX += t.bounds.center.x;
                groundY = Mathf.Min(groundY, t.bounds.min.y);
            }
            z.Sort();
            float rearAxle = (z[0] + z[1]) / 2f, frontAxle = (z[2] + z[3]) / 2f;
            massZ = rearAxle + frontWeightPercent / 100f * (frontAxle - rearAxle);
            centerX = sumX / 4f;
        }
        Vector3 shift = new Vector3(-centerX, rideHeight - groundY, -massZ);
        Model.position += shift;   // world = car space here
    }


    void FindWheels(List<Renderer> parts, List<Renderer> tires)
    {
        if (tires.Count == 0)
        {
            Debug.LogWarning("[Car] No tires found (parts named Wheel_ or Tire, or with a tire/rubber material), so the wheels won't turn.");
            return;
        }

        foreach (Renderer tire in tires)
        {
            Bounds b = tire.bounds;
            Vector3 center = b.center;

            // Everything else small that's centered on this tire (the rim, the wheel nut) turns with it.
            var members = new List<Transform> { tire.transform };
            foreach (Renderer r in parts)
            {
                if (tires.Contains(r) || members.Contains(r.transform)) continue;
                bool centered = (r.bounds.center - center).magnitude < 0.1f;
                bool fits = r.bounds.size.magnitude <= b.size.magnitude * 1.1f;
                if (centered && fits) members.Add(r.transform);
            }

            var w = new Wheel
            {
                front = center.z > 0f,
                radius = Mathf.Max(0.05f, b.size.y / 2f),
            };
            w.label = (w.front ? "F" : "R") + (center.x < 0f ? "L" : "R");

            // steer sits at the wheel's center, lined up with the car. roll is inside it.
            w.steer = new GameObject("Wheel " + w.label).transform;
            w.steer.SetPositionAndRotation(center, Quaternion.identity);
            w.steer.SetParent(Model, true);
            w.steerBase = w.steer.localRotation;
            w.roll = new GameObject("Wheel " + w.label + " spin").transform;
            w.roll.SetParent(w.steer, false);

            foreach (Transform m in members)
            {
                bool insideAnother = false;
                foreach (Transform other in members)
                    if (other != m && m.IsChildOf(other)) insideAnother = true;
                if (!insideAnother) m.SetParent(w.roll, true);
            }
            wheels.Add(w);
        }
        wheels.Sort((first, second) => string.CompareOrdinal(first.label, second.label));
    }


    // A floor or backdrop left over from a render scene is many times bigger than the car's parts.
    // Hide it (the track is the floor here). Compared with the typical part, so it still works if
    // the whole model came in at the wrong scale.
    static void HideLeftoverFloor(List<Renderer> parts)
    {
        if (parts.Count < 3) return;
        var sizes = new List<float>();
        foreach (Renderer r in parts) sizes.Add(Largest(r.bounds.size));
        sizes.Sort();
        float typical = sizes[sizes.Count / 2];
        for (int i = parts.Count - 1; i >= 0; i--)
        {
            if (Largest(parts[i].bounds.size) > 15f * typical)
            {
                parts[i].enabled = false;
                parts.RemoveAt(i);
            }
        }
    }

    static float Largest(Vector3 v) => Mathf.Max(v.x, Mathf.Max(v.y, v.z));

    static bool IsTire(Renderer r)
    {
        Vector3 size = r.bounds.size;
        if (Mathf.Max(size.x, size.y, size.z) > 0.7f) return false;    // bigger than a wheel
        string name = r.gameObject.name.ToLowerInvariant();
        if (name.StartsWith("wheel_") || name.Contains("tire") || name.Contains("tyre")) return true;
        foreach (Material m in r.sharedMaterials)
        {
            if (m == null) continue;
            string mat = m.name.ToLowerInvariant();
            if (mat.Contains("tire") || mat.Contains("tyre")) return true;
        }
        return false;
    }

    // First part whose name contains `must` and any one of `either`.
    static Renderer FindPart(List<Renderer> parts, string must, params string[] either)
    {
        foreach (Renderer r in parts)
        {
            string name = r.gameObject.name.ToLowerInvariant();
            if (!name.Contains(must)) continue;
            foreach (string e in either)
                if (name.Contains(e)) return r;
        }
        return null;
    }

    static Bounds Union(List<Renderer> rs)
    {
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Count; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
    static int Longest(Vector3 v) => v.x >= v.y && v.x >= v.z ? 0 : v.y >= v.z ? 1 : 2;
    static int Shortest(Vector3 v) => v.x <= v.y && v.x <= v.z ? 0 : v.y <= v.z ? 1 : 2;
}

 // actual hell trying to do this oh my 