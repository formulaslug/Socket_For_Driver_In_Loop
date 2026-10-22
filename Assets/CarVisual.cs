using UnityEngine;

// A simple FSAE-style car built from Unity's basic shapes, with wheels that roll with the
// speed and front wheels that turn with the steering. Put it on the object that has
// DILCarClient (DILBootstrap adds it for you if the car has nothing to show).
//
// The car's origin sits on the ground at its center. +Z is forward, +X is right.
// Sizes come from the FS cars: 16 in tires, about 1.59 m wheelbase, 2.9 m long.
public class CarVisual : MonoBehaviour
{
    [Header("Size (meters)")]
    [Tooltip("16 in tire = 0.2032 m radius")]
    public float wheelRadius = 0.2032f;
    [Tooltip("Center of the car to the front axle (2025 spec sheet: 0.8535 m)")]
    public float frontAxle = 0.85f;
    [Tooltip("Center of the car to the rear axle (2025 spec sheet: 0.7365 m)")]
    public float rearAxle = 0.74f;
    [Tooltip("Center of the car to the middle of each wheel, sideways")]
    public float halfTrack = 0.62f;

    [Header("Colors")]
    public Color bodyColor = new Color(0.05f, 0.18f, 0.55f);     // UCSC blue
    public Color accentColor = new Color(0.99f, 0.76f, 0.05f);   // UCSC gold
    public Color carbonColor = new Color(0.15f, 0.15f, 0.17f);
    public Color tireColor = new Color(0.07f, 0.07f, 0.07f);

    DILCarClient car;
    Transform frontLeft, frontRight;          // these turn for steering
    readonly Transform[] rolling = new Transform[4];
    float rollDegrees;

    void Start()
    {
        car = GetComponent<DILCarClient>();
        Build();
    }

    void Update()
    {
        if (car == null) return;

        // Rolling without slipping: distance = angle x radius, so the wheel turns
        // (speed / radius) radians every second. Times 57.3 for degrees.
        rollDegrees = (rollDegrees + car.SpeedMps / wheelRadius * Mathf.Rad2Deg * Time.deltaTime) % 360f;
        Quaternion roll = Quaternion.Euler(rollDegrees, 0f, 0f);    // spin around the axle (X)
        foreach (Transform wheel in rolling)
            if (wheel != null) wheel.localRotation = roll;

        // Steering: front wheels turn around the vertical axis. Positive = right, like the server.
        Quaternion steer = Quaternion.Euler(0f, car.RoadWheelAngleDeg, 0f);
        if (frontLeft != null) frontLeft.localRotation = steer;
        if (frontRight != null) frontRight.localRotation = steer;
    }

    void Build()
    {
        Transform model = new GameObject("Car Model").transform;
        model.SetParent(transform, false);

        // Body: a center tub, a nose, two sidepods and a gold stripe.
        Box("Tub", model, new Vector3(0f, 0.32f, 0f), new Vector3(0.55f, 0.32f, 2.0f), bodyColor);
        Box("Nose", model, new Vector3(0f, 0.27f, 1.3f), new Vector3(0.36f, 0.22f, 0.65f), bodyColor);
        Box("Sidepod L", model, new Vector3(-0.45f, 0.27f, -0.15f), new Vector3(0.32f, 0.26f, 0.9f), bodyColor);
        Box("Sidepod R", model, new Vector3(0.45f, 0.27f, -0.15f), new Vector3(0.32f, 0.26f, 0.9f), bodyColor);
        Box("Stripe", model, new Vector3(0f, 0.485f, 0.35f), new Vector3(0.12f, 0.01f, 1.3f), accentColor);

        // Wings, roll hoop and the driver's helmet.
        Box("Front wing", model, new Vector3(0f, 0.09f, 1.62f), new Vector3(1.3f, 0.04f, 0.3f), carbonColor);
        Box("Rear wing", model, new Vector3(0f, 0.98f, -1.28f), new Vector3(1.0f, 0.05f, 0.32f), carbonColor);
        Box("Wing post L", model, new Vector3(-0.3f, 0.66f, -1.22f), new Vector3(0.04f, 0.6f, 0.04f), carbonColor);
        Box("Wing post R", model, new Vector3(0.3f, 0.66f, -1.22f), new Vector3(0.04f, 0.6f, 0.04f), carbonColor);
        Box("Roll hoop", model, new Vector3(0f, 0.82f, -0.42f), new Vector3(0.5f, 0.06f, 0.06f), carbonColor);
        SimpleMaterials.Part(PrimitiveType.Sphere, "Helmet", model,
            new Vector3(0f, 0.66f, -0.2f), new Vector3(0.26f, 0.26f, 0.26f), accentColor);

        // Wheels: the front pair can steer, all four roll.
        frontLeft = Wheel(model, "Wheel FL", new Vector3(-halfTrack, wheelRadius, frontAxle), 0);
        frontRight = Wheel(model, "Wheel FR", new Vector3(halfTrack, wheelRadius, frontAxle), 1);
        Wheel(model, "Wheel RL", new Vector3(-halfTrack, wheelRadius, -rearAxle), 2);
        Wheel(model, "Wheel RR", new Vector3(halfTrack, wheelRadius, -rearAxle), 3);
    }

    void Box(string name, Transform parent, Vector3 position, Vector3 size, Color color)
    {
        SimpleMaterials.Part(PrimitiveType.Cube, name, parent, position, size, color);
    }

    // Each wheel is three levels, so steering and rolling don't fight:
    //   pivot (turns left/right) -> roller (spins around the axle) -> tire + a spoke you can see spin
    Transform Wheel(Transform parent, string name, Vector3 position, int index)
    {
        Transform pivot = new GameObject(name).transform;
        pivot.SetParent(parent, false);
        pivot.localPosition = position;

        Transform roller = new GameObject("Roller").transform;
        roller.SetParent(pivot, false);
        rolling[index] = roller;

        float diameter = 2f * wheelRadius;
        // Unity's cylinder is 2 m tall, so a Y scale of 0.1 makes a 0.2 m wide tire.
        GameObject tire = SimpleMaterials.Part(PrimitiveType.Cylinder, "Tire", roller,
            Vector3.zero, new Vector3(diameter, 0.1f, diameter), tireColor);
        tire.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);   // lay it on its side: axle along X

        // A gold bar through the hub, a bit wider than the tire, so you can see it roll.
        SimpleMaterials.Part(PrimitiveType.Cube, "Spoke", roller,
            Vector3.zero, new Vector3(0.21f, diameter * 0.75f, 0.06f), accentColor);
        return pivot;
    }
}
