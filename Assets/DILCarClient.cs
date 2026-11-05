using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

// talks to the Python physics server (socket1.py) and moves this GameObject to wherever
// the server says the car is. works with the original socket1.py, no server changes needed.
//
// this is a new file next to the team's Client_Socket.cs (CarPhysicsIPC), which stays untouched.
// it does the same job plus the fixes: D turns right, reset, spawn point, real-time pacing.
// when you press Play, DILBootstrap switches the original component off for that session and
// adds this one to the same object. nothing is saved into the scene.
//
// the loop (on a background thread, so the game never freezes waiting on Python):
//   1. send the driver's inputs   {"throttle":..,"steer":..,"frontBrakes":..,"backBrakes":..}
//   2. wait for the reply         {"x":..,"y":..,"z":..,"yaw":..,"speed":..}
//   3. store it, and Update() copies it onto the car model every frame
// every message is a 4-byte length (big-endian) followed by the JSON text, same as the server.
public class DILCarClient : MonoBehaviour
{
    [Header("Server")]
    public string serverIP = "127.0.0.1";
    public int serverPort = 9001;

    [Header("Where the car starts")]
    [Tooltip("Optional. The server always starts the car at its own (0,0) facing forward. " +
             "Put an empty GameObject on the start line, drag it here, and the car starts there " +
             "(its blue Z arrow = the direction the car faces). Left empty = world origin.")]
    public Transform spawnPoint;

    [Header("Controls")]
    public KeyCode resetKey = KeyCode.R;
    [Tooltip("Must match MAX_STEER_DEGREES in socket1.py: the angle of the front tires at full steer.")]
    public float maxRoadWheelAngleDeg = 25f;

    [Header("Connection")]
    [Tooltip("Keep trying to reach socket1.py every few seconds, so it's fine to start it after pressing Play.")]
    public bool autoReconnect = true;
    public float retrySeconds = 2f;

    [Header("Debug")]
    [Tooltip("Prints every message. Leave off when driving: hundreds of logs a second slow Unity down.")]
    public bool logEveryMessage = false;

    // read by Dashboard and CameraSwitcher.
    public bool IsConnected => _connected;
    public bool HadConnection { get; private set; }  // connected at least once since Play
    public float ThrottleInput { get; private set; } // -1..1 (W = 1, S = -1)
    public float BrakeInput { get; private set; }    // 0..1, the harder of the two brakes
    public float SpeedMps { get; private set; }     // meters per second, from the server
    public float SteerInput { get; private set; }   // -1 = full left, 0 = straight, 1 = full right
    public float RoadWheelAngleDeg => SteerInput * maxRoadWheelAngleDeg;

    // runs after every reset (R key, Reset button, or a new track). TrackLoader listens to it
    // to clear the timer and stand the cones back up.
    public event Action OnReset;

    // the server moves the car 1/300 s per message (stepsPerSecond in params.json5).
    const double StepSeconds = 1.0 / 300.0;

    struct VehicleState { public float x, y, z, yaw, speed; }
    struct Controls { public float throttle, steer, frontBrakes, backBrakes; }

    // shared between the main thread and the network thread, so always touched inside a lock.
    VehicleState _state;
    Controls _controls;
    readonly object _lock = new object();

    volatile bool _hasState;
    volatile bool _connected;
    volatile bool _running;
    bool _started;                       // has Start() run yet?
    bool _warnedNoServer;                // only say "couldn't reach the server" once per outage
    float _nextRetry;

    TcpClient _client;
    NetworkStream _stream;
    Thread _ioThread;

    void Start()
    {
        _started = true;
        PlaceAtSpawn();
        Connect();
    }

    void OnDestroy()
    {
        Disconnect();
    }

    void Update()
    {
        // reset = hang up and call again. the server gives every new connection a brand-new
        // car parked at its (0,0), which we draw at spawnPoint. works with any server version.
        if (Input.GetKeyDown(resetKey))
            ResetCar();

        float steer = Input.GetAxis("Horizontal");          // A/D or arrows
        var c = new Controls
        {
            throttle    = Input.GetAxis("Vertical"),       // W/S or arrows
            steer       = steer,
            frontBrakes = Input.GetKey(KeyCode.Space) ? 1f : 0f,
            backBrakes  = Input.GetKey(KeyCode.X) ? 1f : 0f,
        };
        SteerInput = steer;
        ThrottleInput = c.throttle;
        BrakeInput = Mathf.Max(c.frontBrakes, c.backBrakes);
        lock (_lock) { _controls = c; }

        // server not there (not started yet, or it stopped)? quietly try again every few seconds.
        if (!_connected && autoReconnect && Time.unscaledTime >= _nextRetry)
        {
            _nextRetry = Time.unscaledTime + retrySeconds;
            Disconnect();
            PlaceAtSpawn();
            if (Connect(quiet: true)) OnReset?.Invoke();
        }

        if (!_connected || !_hasState)
            return;

        VehicleState s;
        lock (_lock) { s = _state; }
        SpeedMps = s.speed;

        // server coordinates -> Unity coordinates.
        // the server's car starts facing +x, and a RIGHT turn swings it toward +z.
        // in Unity, seen from above, +Z is forward and +X is to the right.
        // so server x becomes Unity Z, and server z becomes Unity X.
        // (copying (x, y, z) straight across makes a mirror image: that's why D used to
        // turn the car left, and why the car could slide sideways after turning.)
        // height is pinned to 0: the track is flat, and the original socket1.py fills "y" with
        // Sims-Data's own sideways position, which would lift the car into the air in turns.
        Vector3 local = new Vector3(s.z, 0f, s.x);
        // Unity's yaw turns +Z toward +X, the same way the server's yaw turns +x toward +z.
        Quaternion localRotation = Quaternion.Euler(0f, s.yaw * Mathf.Rad2Deg, 0f);

        Vector3 origin = spawnPoint ? spawnPoint.position : Vector3.zero;
        Quaternion facing = spawnPoint ? Quaternion.Euler(0f, spawnPoint.eulerAngles.y, 0f) : Quaternion.identity;
        transform.position = origin + facing * local;
        transform.rotation = facing * localRotation;
    }

    // called by the R key and by the Dashboard's Reset button.
    public void ResetCar()
    {
        Disconnect();
        PlaceAtSpawn();
        Connect();
        OnReset?.Invoke();
    }

    // used by TrackLoader: move the start line. if we're already driving, reset onto it now;
    // if Start() hasn't run yet, Start() will put the car there.
    public void SetSpawnPoint(Transform newSpawn)
    {
        spawnPoint = newSpawn;
        if (_started) ResetCar();
    }

    // returns true if connected. quiet = don't repeat the error (used by the automatic retries).
    bool Connect(bool quiet = false)
    {
        if (_client != null || _ioThread != null)
            Disconnect();                 // never run two connections at once
        _nextRetry = Time.unscaledTime + retrySeconds;
        try
        {
            _client = new TcpClient { NoDelay = true };   // NoDelay: send small messages right away
            // give up after 1 s instead of freezing the game if the address is wrong.
            IAsyncResult attempt = _client.BeginConnect(serverIP, serverPort, null, null);
            if (!attempt.AsyncWaitHandle.WaitOne(1000))
                throw new SocketException((int)SocketError.TimedOut);
            _client.EndConnect(attempt);
            _stream = _client.GetStream();
        }
        catch (SocketException)
        {
            _client?.Close();
            _client = null;
            _connected = false;
            if (!quiet || !_warnedNoServer)
                Debug.LogWarning($"[IPC] Couldn't reach the physics server at {serverIP}:{serverPort}. Start socket1.py" +
                                 (autoReconnect ? "; the game connects by itself once it's running." : ", then press R."));
            _warnedNoServer = true;
            return false;
        }

        _warnedNoServer = false;
        _hasState = false;
        _running = true;
        _connected = true;
        HadConnection = true;
        _ioThread = new Thread(IOLoop) { IsBackground = true };
        _ioThread.Start();
        Debug.Log("[IPC] Connected to the physics server");
        return true;
    }

    void Disconnect()
    {
        _running = false;
        _connected = false;
        _stream?.Close();      // this also wakes the network thread if it's waiting on a reply
        _client?.Close();
        _ioThread?.Join(500);
        _stream = null;
        _client = null;
        _ioThread = null;
        SpeedMps = 0f;
    }

    void PlaceAtSpawn()
    {
        transform.position = spawnPoint ? spawnPoint.position : Vector3.zero;
        transform.rotation = spawnPoint ? Quaternion.Euler(0f, spawnPoint.eulerAngles.y, 0f) : Quaternion.identity;
    }

    void IOLoop()
    {
        // keep the sim locked to real time: one message = 1/300 s of driving, so we send
        // exactly as many messages as real seconds have passed x 300. if we fall behind
        // (a slow frame, or a sleep that ran long), we send a few back to back to catch up.
        var clock = Stopwatch.StartNew();
        double simTime = 0.0;

        try
        {
            while (_running)
            {
                double realTime = clock.Elapsed.TotalSeconds;
                if (simTime > realTime)
                {
                    Thread.Sleep(1);             // ahead of real time, wait a moment
                    continue;
                }
                if (realTime - simTime > 0.25)
                    simTime = realTime - 0.25;   // way behind (a hitch): don't fast-forward more than 0.25 s

                Controls c;
                lock (_lock) { c = _controls; }

                string json = "{" +
                    $"\"throttle\":{Num(c.throttle)},\"steer\":{Num(c.steer)}," +
                    $"\"frontBrakes\":{Num(c.frontBrakes)},\"backBrakes\":{Num(c.backBrakes)}," +
                    $"\"dt\":{Num(StepSeconds)}" +
                    "}";
                SendMsg(json);

                string reply = RecvMsg();
                if (reply == null)
                    break;                       // server hung up
                if (logEveryMessage)
                    Debug.Log($"[IPC] sent {json}  got {reply}");

                VehicleState s = new VehicleState
                {
                    x     = ExtractFloat(reply, "x"),
                    y     = ExtractFloat(reply, "y"),
                    z     = ExtractFloat(reply, "z"),
                    yaw   = ExtractFloat(reply, "yaw"),
                    speed = ExtractFloat(reply, "speed"),
                };
                lock (_lock) { _state = s; }
                _hasState = true;
                simTime += StepSeconds;
            }
        }
        catch (Exception e)
        {
            if (_running)                        // only complain if we didn't hang up on purpose
                Debug.LogWarning($"[IPC] Lost the physics server: {e.Message}");
        }
        _connected = false;
    }

    // numbers always written with a dot (0.5), even on computers set to a language that uses
    // a comma (0,5), because JSON only understands the dot.
    static string Num(double v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    void SendMsg(string text)
    {
        byte[] payload = Encoding.UTF8.GetBytes(text);
        int len = payload.Length;
        byte[] header =
        {
            (byte)((len >> 24) & 0xFF), (byte)((len >> 16) & 0xFF),
            (byte)((len >> 8) & 0xFF),  (byte)(len & 0xFF),
        };
        _stream.Write(header, 0, 4);
        _stream.Write(payload, 0, len);
    }

    string RecvMsg()
    {
        byte[] header = RecvExactly(4);
        if (header == null) return null;
        int len = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
        byte[] payload = RecvExactly(len);
        return payload == null ? null : Encoding.UTF8.GetString(payload);
    }

    // TCP can hand us a message in pieces, so keep reading until we have all n bytes.
    byte[] RecvExactly(int n)
    {
        byte[] buf = new byte[n];
        int total = 0;
        while (total < n)
        {
            int read = _stream.Read(buf, total, n - total);
            if (read == 0) return null;          // connection closed
            total += read;
        }
        return buf;
    }

    // tiny JSON reader: finds "key": and parses the number after it.
    // fine for the flat messages the server sends (no nested objects).
    static float ExtractFloat(string json, string key)
    {
        string search = $"\"{key}\":";
        int idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return 0f;
        int start = idx + search.Length;
        while (start < json.Length && json[start] == ' ') start++;
        int end = start;
        while (end < json.Length && (char.IsDigit(json[end]) || "+-.eE".IndexOf(json[end]) >= 0))
            end++;
        return double.TryParse(json.Substring(start, end - start),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double val) ? (float)val : 0f;
    }
}
