using System;
using System.Threading;

// makes the car's sound from scratch, sample by sample (no audio files).
//
// what you hear, and what sets it:
//   gear whine   the 11-tooth pinion meshing: (motor revs per second) x 11. this is most of what
//                an electric FSAE car sounds like. louder on throttle.
//   motor hum    a lower tone at (motor revs per second) x motorOrder, with a couple of overtones
//   wind / road  noise that gets louder and brighter with speed
//   tire squeal  a wavering tone when the car is cornering hard (sideways g above ~1.1)
//   cone hit     a short thunk
// motor speed comes from the car's speed: wheel revs x the gear ratio (40/11 on FS-4).
public class CarSynth
{
    // set these from the game (any thread). changes are smoothed, so no clicks.
    public float speed;          // m/s
    public float throttle;       // -1..1
    public float brake;          // 0..1
    public float lateralG;       // sideways acceleration in g (sign doesn't matter)
    public float volume = 0.8f;  // 0..1, 0 = muted

    // drivetrain.
    public float gearRatio = 40f / 11f;
    public float tireRadius = 0.2032f;     // 16 in tire
    public int pinionTeeth = 11;
    public float motorOrder = 6f;

    public readonly int sampleRate;

    // state, only touched on the audio thread
    double phGear, phMotor, phSqueal, phWobble, phThunk;
    float sSpeed, sLoad, sSqueal, sVolume;
    float lp1, lp2, sq1, sq2;
    float thunkEnv;
    uint rng = 2463534242u;
    int pendingThunks;

    public CarSynth(int sampleRate) { this.sampleRate = Math.Max(8000, sampleRate); }

    // call when a cone gets hit (any thread).
    public void Thunk() => Interlocked.Increment(ref pendingThunks);

    // fills an interleaved buffer (what Unity's OnAudioFilterRead hands over). overwrites it.
    public void Render(float[] data, int channels)
    {
        if (channels < 1) return;
        int frames = data.Length / channels;
        float dt = 1f / sampleRate;
        float kFast = 1f - (float)Math.Exp(-dt / 0.05f);   // follow inputs over ~50 ms
        float kSlow = 1f - (float)Math.Exp(-dt / 0.15f);
        float thunkDecay = (float)Math.Exp(-dt / 0.085f);
        const double TwoPi = 2.0 * Math.PI;

        for (int f = 0; f < frames; f++)
        {
            // follow the inputs smoothly.
            float load = Clamp01(Math.Max(throttle, 0f) + 0.45f * brake + 0.5f * Math.Max(-throttle, 0f));
            sSpeed += (Math.Max(0f, speed) - sSpeed) * kFast;
            sLoad += (load - sLoad) * kFast;
            float squealTarget = Clamp01((Math.Abs(lateralG) - 1.1f) / 0.7f) * Clamp01(sSpeed / 8f);
            sSqueal += (squealTarget - sSqueal) * kSlow;
            sVolume += (Clamp01(volume) - sVolume) * kSlow;

            if (Volatile.Read(ref pendingThunks) > 0)
            {
                Interlocked.Decrement(ref pendingThunks);
                thunkEnv = 1f;
                phThunk = 0;
            }

            // drivetrain tones.
            double motorHz = sSpeed / (TwoPi * tireRadius) * gearRatio;
            phGear = Wrap(phGear + motorHz * pinionTeeth * dt);
            phMotor = Wrap(phMotor + motorHz * motorOrder * dt);
            float fromStop = Clamp01(sSpeed / 4f);            // fades in as the car starts rolling
            float gear = (float)(Math.Sin(TwoPi * phGear) + 0.35 * Math.Sin(2 * TwoPi * phGear) + 0.12 * Math.Sin(3 * TwoPi * phGear));
            gear *= 0.12f * fromStop * (0.35f + 0.65f * sLoad) * (0.6f + 0.4f * Clamp01(sSpeed / 25f));
            float motor = (float)(Math.Sin(TwoPi * phMotor) + 0.45 * Math.Sin(2 * TwoPi * phMotor) + 0.2 * Math.Sin(3 * TwoPi * phMotor));
            motor *= 0.07f * fromStop * (0.25f + 0.75f * sLoad);

            // wind and road: noise through two low-pass filters that open up with speed.
            float white = Noise();
            float cutoff = 220f + 45f * sSpeed;
            float a = 1f - (float)Math.Exp(-TwoPi * cutoff * dt);
            lp1 += (white - lp1) * a;
            lp2 += (lp1 - lp2) * a;
            float wind = lp2 * 1.6f * Math.Min(0.6f, (float)Math.Pow(sSpeed / 30f, 1.5));

            // tire squeal: a wavering ~1 kHz tone mixed with band-limited noise. LOL thank you jared
            float squeal = 0f;
            if (sSqueal > 0.001f)
            {
                phWobble = Wrap(phWobble + 5.5 * dt);
                double sqHz = 980.0 + 70.0 * Math.Sin(TwoPi * phWobble) + 60.0 * lp1;
                phSqueal = Wrap(phSqueal + sqHz * dt);
                float bandA = 1f - (float)Math.Exp(-TwoPi * 1400f * dt);
                sq1 += (white - sq1) * bandA;
                sq2 += (sq1 - sq2) * bandA;
                squeal = ((float)Math.Sin(TwoPi * phSqueal) * 0.7f + (sq1 - sq2) * 1.5f) * 0.2f * sSqueal;
            }

            // cone thunk: a low knock and a puff of noise, dying away in ~0.2 s.
            float thunk = 0f;
            if (thunkEnv > 0.0005f)
            {
                phThunk = Wrap(phThunk + 150.0 * dt);
                thunk = thunkEnv * ((float)Math.Sin(TwoPi * phThunk) * 0.6f + lp1 * 2.5f) * 0.32f;
                thunkEnv *= thunkDecay;
            }

            // mix, keep it from clipping, set the volume.
            float x = (gear + motor + wind + squeal + thunk) * sVolume;
            x = SoftClip(x);
            for (int c = 0; c < channels; c++)
                data[f * channels + c] = x;
        }
    }

    // white noise in -1..1 (xorshift: fast and the same every run).
    float Noise()
    {
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        return (rng / 4294967295f) * 2f - 1f;
    }

    // gentle limiter: almost straight for normal levels, rounds off loud peaks instead of clipping.
    static float SoftClip(float x)
    {
        if (x > 3f) return 1f;
        if (x < -3f) return -1f;
        return x * (27f + x * x) / (27f + 9f * x * x);
    }

    static double Wrap(double p) => p - Math.Floor(p);
    static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
}
