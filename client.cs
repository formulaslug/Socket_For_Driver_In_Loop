using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class CarPhysicsIPC : MonoBehaviour
{
    [Header("Server")]
    public string serverIP   = "127.0.0.1";
    public int    serverPort = 9001;

    // ── latest state written by background thread, read by Update() ──────
    private struct VehicleState
    {
        public float x, y, z, yaw, speed;
    }

    private VehicleState _state;
    private readonly object _stateLock = new object();
    private bool _hasState = false;

    // ── controls written by Update(), read by background thread ──────────
    private struct Controls
    {
        public float throttle, steer, brake, dt;
    }

    private Controls _controls;
    private readonly object _controlsLock = new object();

    // ── socket & thread ───────────────────────────────────────────────────
    private TcpClient  _client;
    private NetworkStream _stream;
    private Thread     _ioThread;
    private volatile bool _running = false;

    // ─────────────────────────────────────────────────────────────────────
    void Start()
    {
        _client = new TcpClient();
        _client.NoDelay = true;           // disable Nagle — low latency matters
        _client.Connect(serverIP, serverPort);
        _stream  = _client.GetStream();
        _running = true;

        _ioThread = new Thread(IOLoop) { IsBackground = true };
        _ioThread.Start();
        Debug.Log("[IPC] Connected to Python server");
    }

    void OnDestroy()
    {
        _running = false;
        _stream?.Close();
        _client?.Close();
        _ioThread?.Join(500);
    }

    // ── called by Unity every frame ───────────────────────────────────────
    void Update()
    {
        // 1. Write this frame's driver inputs for the IO thread to pick up
        lock (_controlsLock)
        {
            _controls = new Controls
            {
                throttle = Input.GetAxis("Vertical"),    // W/S or left stick Y
                steer    = Input.GetAxis("Horizontal"),  // A/D or left stick X
                brake    = Input.GetKey(KeyCode.Space) ? 1f : 0f,
                dt       = Time.deltaTime,               // actual frame time
            };
        }

        // 2. Apply latest physics state to the car's Transform
        if (!_hasState) return;

        VehicleState s;
        lock (_stateLock) { s = _state; }

        // Python works in metres; Unity in metres too — no scaling needed.
        // If your physics uses a different origin convention, adjust here.
        transform.position = new Vector3(s.x, s.y, s.z);
        transform.rotation = Quaternion.Euler(0f, s.yaw * Mathf.Rad2Deg, 0f);

        // Optional: drive speedometer UI, engine audio, etc.
        // speedLabel.text = $"{s.speed * 3.6f:F0} km/h";
    }

    // ── background thread: send controls → block → receive state ─────────
    private void IOLoop()
    {
        while (_running)
        {
            try
            {
                // grab the latest controls the main thread wrote
                Controls ctrl;
                lock (_controlsLock) { ctrl = _controls; }

                // serialize to JSON
                string json = $"{{\"throttle\":{ctrl.throttle:F4}," +
                              $"\"steer\":{ctrl.steer:F4},"          +
                              $"\"brake\":{ctrl.brake:F4},"          +
                              $"\"dt\":{ctrl.dt:F6}}}";

                SendMsg(json);

                // block here until Python replies — this is fine on a bg thread
                string reply = RecvMsg();
                if (reply == null) break;

                // parse and cache the state
                VehicleState s = ParseState(reply);
                lock (_stateLock)
                {
                    _state    = s;
                    _hasState = true;
                }
            }
            catch (Exception e)
            {
                if (_running)
                    Debug.LogWarning($"[IPC] Socket error: {e.Message}");
                break;
            }
        }
        Debug.Log("[IPC] IO thread exiting");
    }

    // ── framing: 4-byte big-endian length prefix ──────────────────────────
    private void SendMsg(string text)
    {
        byte[] payload = Encoding.UTF8.GetBytes(text);
        byte[] header  = new byte[4];
        int    len     = payload.Length;
        header[0] = (byte)((len >> 24) & 0xFF);
        header[1] = (byte)((len >> 16) & 0xFF);
        header[2] = (byte)((len >>  8) & 0xFF);
        header[3] = (byte)( len        & 0xFF);
        _stream.Write(header,  0, 4);
        _stream.Write(payload, 0, payload.Length);
        _stream.Flush();
    }

    private string RecvMsg()
    {
        byte[] header = RecvExactly(4);
        if (header == null) return null;

        int len = (header[0] << 24) | (header[1] << 16)
                | (header[2] <<  8) |  header[3];

        byte[] payload = RecvExactly(len);
        return payload == null ? null : Encoding.UTF8.GetString(payload);
    }

    private byte[] RecvExactly(int n)
    {
        byte[] buf   = new byte[n];
        int    total = 0;
        while (total < n)
        {
            int read = _stream.Read(buf, total, n - total);
            if (read == 0) return null;   // connection closed
            total += read;
        }
        return buf;
    }

    // ── minimal JSON parser — avoids adding a JSON library dependency ─────
    // For a real project, use JsonUtility or Newtonsoft.Json instead.
    private static VehicleState ParseState(string json)
    {
        var s = new VehicleState();
        s.x     = ExtractFloat(json, "x");
        s.y     = ExtractFloat(json, "y");
        s.z     = ExtractFloat(json, "z");
        s.yaw   = ExtractFloat(json, "yaw");
        s.speed = ExtractFloat(json, "speed");
        return s;
    }

    private static float ExtractFloat(string json, string key)
    {
        // looks for "key": value
        string search = $"\"{key}\":";
        int idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return 0f;
        int start = idx + search.Length;
        int end   = start;
        while (end < json.Length && (char.IsDigit(json[end])
               || json[end] == '.' || json[end] == '-' || json[end] == 'e'))
            end++;
        return float.TryParse(json.Substring(start, end - start),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out float val) ? val : 0f;
    }
}