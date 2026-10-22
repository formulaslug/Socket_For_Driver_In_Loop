using UnityEngine;

// Two views of the car. Press C to switch:
//   Driver: in the cockpit, looking where the car points (what the driver sees)
//   Chase:  above and behind the car, following smoothly
//
// Put this on the Main Camera. It finds the car by itself, or drag the car into "car".
// All offsets are in meters, measured from the car's position (which sits on the ground).
[RequireComponent(typeof(Camera))]
public class CameraSwitcher : MonoBehaviour
{
    [Tooltip("Leave empty to use the object that has DILCarClient on it.")]
    public Transform car;
    public KeyCode switchKey = KeyCode.C;
    public bool startInDriverView = false;

    [Header("Driver view")]
    [Tooltip("Where the driver's eyes are: x = right, y = up, z = forward.")]
    public Vector3 driverEyeOffset = new Vector3(0f, 0.8f, 0.05f);
    public float driverFieldOfView = 75f;

    [Header("Chase view")]
    [Tooltip("Where the camera floats: y = up, negative z = behind the car.")]
    public Vector3 chaseOffset = new Vector3(0f, 2.5f, -6f);
    [Tooltip("The camera aims this high above the car's base.")]
    public float chaseAimHeight = 0.8f;
    [Tooltip("Higher = sticks closer to the car, lower = floatier.")]
    public float chaseStiffness = 6f;
    public float chaseFieldOfView = 60f;

    public bool IsDriverView { get; private set; }
    public string CurrentName => IsDriverView ? "driver" : "chase";

    Camera _camera;

    void Start()
    {
        _camera = GetComponent<Camera>();
        if (car == null)
        {
            DILCarClient ipc = FindFirstObjectByType<DILCarClient>();
            if (ipc != null) car = ipc.transform;
        }
        IsDriverView = startInDriverView;
        SnapToChasePosition();
    }

    void Update()
    {
        if (Input.GetKeyDown(switchKey))
        {
            IsDriverView = !IsDriverView;
            if (!IsDriverView) SnapToChasePosition();   // don't swoop in from the cockpit
        }
    }

    // LateUpdate runs after every Update, so the car has already moved this frame.
    // Moving the camera here stops it from lagging one frame behind and jittering.
    void LateUpdate()
    {
        if (car == null) return;

        if (IsDriverView)
        {
            // Glued to the car: no smoothing, or the view would wobble against the cockpit.
            // car.rotation * offset turns the offset with the car, so "forward" stays forward.
            transform.position = car.position + car.rotation * driverEyeOffset;
            transform.rotation = car.rotation;
            _camera.fieldOfView = driverFieldOfView;
        }
        else
        {
            Vector3 target = car.position + car.rotation * chaseOffset;
            // Move part of the way toward the target each frame. The Exp makes it behave
            // the same at 30 fps or 144 fps.
            float t = 1f - Mathf.Exp(-chaseStiffness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, target, t);
            // After a reset or a new track the car jumps far away: snap instead of flying there.
            if ((target - transform.position).sqrMagnitude > 30f * 30f)
                transform.position = target;
            transform.LookAt(car.position + Vector3.up * chaseAimHeight);
            _camera.fieldOfView = chaseFieldOfView;
        }
    }

    void SnapToChasePosition()
    {
        if (car == null) return;
        transform.position = car.position + car.rotation * chaseOffset;
        transform.LookAt(car.position + Vector3.up * chaseAimHeight);
    }
}
