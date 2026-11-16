using UnityEngine;

// plays the car's sound (made by CarSynth, no audio files): gear whine and motor hum that rise
// with speed and get louder on throttle, wind and road noise, tire squeal in hard corners, and
// a thunk when you hit a cone. M mutes it.
//
// DILBootstrap puts this on the car. the sound is "2D" (not placed in the world), so it's the
// same volume in every camera view.
[RequireComponent(typeof(AudioSource))]
public class CarSound : MonoBehaviour
{
    [Range(0f, 1f)] public float volume = 0.8f;
    public KeyCode muteKey = KeyCode.M;

    [Header("Drivetrain (sets the pitch)")]
    [Tooltip("FS-4's VCU assumes 40/11.")]
    public float gearRatio = 40f / 11f;
    public float tireRadius = 0.2032f;
    public int pinionTeeth = 11;
    [Tooltip("Pitch of the motor hum, in multiples of the motor's revs per second.")]
    public float motorOrder = 6f;

    public bool Muted { get; private set; }

    DILCarClient car;
    TrackLoader track;
    CarSynth synth;
    float lastYaw, yawRate;      // degrees, degrees per second (smoothed)
    int lastConeHits;
    TrackTiming lastTiming;

    void Awake()
    {
        int rate = AudioSettings.outputSampleRate;
        synth = new CarSynth(rate);

        // the sound is written straight into the audio stream in OnAudioFilterRead. an AudioSource
        // only runs that while it's playing, so give it a silent looping clip to play.
        AudioSource source = GetComponent<AudioSource>();
        AudioClip silence = AudioClip.Create("DIL car sound", rate, 1, rate, false);
        silence.SetData(new float[rate], 0);
        source.clip = silence;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;    // 2D
        source.dopplerLevel = 0f;
        source.volume = 1f;
        source.Play();
    }

    void Start()
    {
        car = GetComponent<DILCarClient>();
        if (car == null) car = FindFirstObjectByType<DILCarClient>();
        track = FindFirstObjectByType<TrackLoader>();
        if (car != null) lastYaw = car.transform.eulerAngles.y;
    }

    void Update()
    {
        if (Input.GetKeyDown(muteKey)) Muted = !Muted;
        if (car == null) return;

        bool live = car.IsConnected;
        float speed = live ? Mathf.Max(0f, car.SpeedMps) : 0f;

        // sideways g from how fast the car is turning: a = v x yaw rate.
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        float yaw = car.transform.eulerAngles.y;
        float rate = Mathf.DeltaAngle(lastYaw, yaw) / dt;
        lastYaw = yaw;
        if (Mathf.Abs(rate) > 720f) rate = 0f;         // a reset teleport, not a turn
        yawRate = Mathf.Lerp(yawRate, rate, 1f - Mathf.Exp(-8f * dt));

        synth.gearRatio = gearRatio;
        synth.tireRadius = tireRadius;
        synth.pinionTeeth = pinionTeeth;
        synth.motorOrder = motorOrder;
        synth.speed = speed;
        synth.throttle = live ? car.ThrottleInput : 0f;
        synth.brake = live ? car.BrakeInput : 0f;
        synth.lateralG = speed * yawRate * Mathf.Deg2Rad / 9.81f;
        synth.volume = Muted ? 0f : volume;

        // a thunk for every cone knocked over since last frame.
        TrackTiming timing = track != null ? track.Timing : null;
        if (timing != null)
        {
            if (timing != lastTiming || timing.HitTotal < lastConeHits) lastConeHits = timing.HitTotal;   // new track or reset
            for (int i = lastConeHits; i < timing.HitTotal; i++) synth.Thunk();
            lastConeHits = timing.HitTotal;
            lastTiming = timing;
        }
    }

    // Unity calls this on the audio thread with the buffer to fill.
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (synth != null) synth.Render(data, channels);
    }
}
