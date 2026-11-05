using UnityEngine;

// the cameras. press C to go through them:
//   chase:    behind and above the car. it swings out a little in corners so you see the car's
//             side, and pulls back and widens a touch at speed
//   driver:   in the cockpit (CarModel sets the eye point for the FS-4), glances into turns
//   overhead: high above the car, track ahead at the top of the screen, handy for learning a course
//
// put this on the Main Camera (DILBootstrap does). it finds the car by itself.
// all offsets are in meters from the car's center point on the ground: x = right, y = up, z = forward.
[RequireComponent(typeof(Camera))]
public class CameraSwitcher : MonoBehaviour
{
    public enum View { Chase, Driver, Overhead }

    [Tooltip("Leave empty to use the object that has DILCarClient on it.")]
    public Transform car;
    public KeyCode switchKey = KeyCode.C;
    public View startView = View.Chase;
    [Tooltip("Off = C only switches between chase and driver (the two the issue asked for).")]
    public bool includeOverhead = true;

    [Header("Driver view")]
    [Tooltip("Where the driver's eyes are. CarModel sets this for the FS-4 model.")]
    public Vector3 driverEyeOffset = new Vector3(0f, 0.8f, 0.05f);
    public float driverFieldOfView = 75f;
    [Tooltip("How far the view turns toward where you're steering, at full lock.")]
    public float lookIntoTurnDegrees = 9f;
    [Tooltip("Tilts the view down a little so you see more of the track right in front of the nose.")]
    public float driverPitchDegrees = 3f;

    [Header("Chase view")]
    public Vector3 chaseOffset = new Vector3(0f, 1.5f, -4.0f);
    [Tooltip("The camera aims this high above the ground...")]
    public float chaseAimHeight = 0.55f;
    [Tooltip("...and this far ahead of the car, so the car sits a bit low in the picture.")]
    public float chaseLookAhead = 2.5f;
    public float chaseFieldOfView = 60f;
    [Tooltip("Higher = the camera turns with the car sooner. Lower = it swings out more in corners.")]
    public float chaseTurnStiffness = 4f;
    [Tooltip("At top speed the camera sits this much further back, and the view is this much wider.")]
    public float speedPullBack = 0.8f, speedExtraFov = 6f;
    public float topSpeedMps = 30f;

    [Header("Overhead view")]
    public float overheadHeight = 26f;
    public float overheadFieldOfView = 55f;

    [Header("Shake")]
    [Tooltip("A small buzz that grows with speed, more in the cockpit. Set to 0 to turn it off.")]
    [Range(0f, 2f)] public float shake = 1f;

    public View Current { get; private set; }
    public bool IsDriverView => Current == View.Driver;
    public string CurrentName => Current.ToString().ToLowerInvariant();
    public string CurrentLabel => Current.ToString();

    Camera _camera;
    DILCarClient _client;
    float _chaseYaw;           // the chase camera's own heading, which lags the car's
    float _lookYaw;            // driver view glance, smoothed
    Vector3 _lastCarPosition;

    void Start()
    {
        _camera = GetComponent<Camera>();
        _client = FindFirstObjectByType<DILCarClient>();
        if (car == null && _client != null) car = _client.transform;
        Current = startView;
        Snap();
    }

    void Update()
    {
        if (Input.GetKeyDown(switchKey))
        {
            Current = Next(Current);
            Snap();                                    // don't swoop from one view to the next
        }
    }

    View Next(View v)
    {
        if (v == View.Chase) return View.Driver;
        if (v == View.Driver) return includeOverhead ? View.Overhead : View.Chase;
        return View.Chase;
    }

    // LateUpdate runs after every Update, so the car has already moved this frame.
    void LateUpdate()
    {
        if (car == null) return;
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        float speed = _client != null ? Mathf.Max(0f, _client.SpeedMps) : 0f;
        float steer = _client != null ? _client.SteerInput : 0f;
        float fast = Mathf.Clamp01(speed / topSpeedMps);
        float carYaw = car.eulerAngles.y;

        // after a reset or a new track the car jumps far away: snap instead of flying there.
        if ((car.position - _lastCarPosition).sqrMagnitude > 25f) Snap();
        _lastCarPosition = car.position;

        if (Current == View.Driver)
        {
            _lookYaw = Mathf.Lerp(_lookYaw, steer * lookIntoTurnDegrees, 1f - Mathf.Exp(-6f * dt));
            transform.position = car.position + car.rotation * driverEyeOffset + ShakeOffset(1f, fast);
            transform.rotation = car.rotation * Quaternion.Euler(driverPitchDegrees, _lookYaw, ShakeRoll(fast));
            _camera.fieldOfView = driverFieldOfView;
            _camera.nearClipPlane = 0.05f;             // so the cockpit right in front of you isn't cut off
        }
        else if (Current == View.Chase)
        {
            // the camera's heading follows the car's with a little lag.
            _chaseYaw = Mathf.LerpAngle(_chaseYaw, carYaw, 1f - Mathf.Exp(-chaseTurnStiffness * dt));
            Quaternion heading = Quaternion.Euler(0f, _chaseYaw, 0f);
            Vector3 offset = chaseOffset + new Vector3(0f, 0.25f * speedPullBack * fast, -speedPullBack * fast);
            transform.position = car.position + heading * offset + ShakeOffset(0.4f, fast);
            Vector3 aim = car.position + Vector3.up * chaseAimHeight + heading * Vector3.forward * chaseLookAhead;
            transform.rotation = Quaternion.LookRotation(aim - transform.position, Vector3.up);
            _camera.fieldOfView = chaseFieldOfView + speedExtraFov * fast;
            _camera.nearClipPlane = 0.1f;
        }
        else
        {
            // straight down, with the car's heading pointing up the screen.
            _chaseYaw = Mathf.LerpAngle(_chaseYaw, carYaw, 1f - Mathf.Exp(-2f * dt));
            Quaternion heading = Quaternion.Euler(0f, _chaseYaw, 0f);
            transform.position = car.position + Vector3.up * overheadHeight + heading * Vector3.forward * 4f;
            transform.rotation = Quaternion.Euler(90f, _chaseYaw, 0f);
            _camera.fieldOfView = overheadFieldOfView;
            _camera.nearClipPlane = 0.3f;
        }
    }

    void Snap()
    {
        if (car == null) return;
        _chaseYaw = car.eulerAngles.y;
        _lookYaw = 0f;
        _lastCarPosition = car.position;
    }

    // a small random wobble (Perlin noise, so it's smooth). strength grows with speed.
    Vector3 ShakeOffset(float amount, float fast)
    {
        if (shake <= 0f || fast <= 0f) return Vector3.zero;
        float t = Time.time * 13f;
        float a = shake * amount * (0.002f + 0.009f * fast * fast);
        return new Vector3((Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 2f * a,
                           (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f * a, 0f);
    }

    float ShakeRoll(float fast)
    {
        if (shake <= 0f) return 0f;
        return shake * (Mathf.PerlinNoise(Time.time * 9f, 5.1f) - 0.5f) * 0.6f * fast;
    }
}
