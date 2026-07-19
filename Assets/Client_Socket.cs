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

    private struct VehicleState
    {
        public float x, y, z, yaw, speed;
    }

    private VehicleState _state;
    private readonly object _stateLock = new object();
    private volatile bool _hasState = false;

    private struct Controls
    {
        public float throttle, steer,  frontBrakes , backBrakes, dt;
    }

    private Controls _controls;
    private readonly object _controlsLock = new object();

    private volatile bool _connected = false;

    private TcpClient  _client;
    private NetworkStream _stream;
    private Thread     _ioThread;
    private volatile bool _running = false;

  
    void Start()
    {
        _client = new TcpClient();
        _client.NoDelay = true;          
        _client.Connect(serverIP, serverPort);
        _stream  = _client.GetStream();
        _running = true;
        _connected = true;

        _ioThread = new Thread(IOLoop) { IsBackground = true };
        _ioThread.Start();
        Debug.Log("[IPC] Connected to Python server");
    }

    void OnDestroy()
    {
        _running = false;
        _connected = false;
        _stream?.Close();
        _client?.Close();
        _ioThread?.Join(500);
    }

    void Update()
    {
        if (!_connected)
        {
            if (_hasState)             
            {
                _hasState = false;
                transform.position = Vector3.zero;
                transform.rotation = Quaternion.identity;
                Debug.LogWarning("[IPC] Disconnected from physics server — car reset");
            }
            return;
        }

        lock (_controlsLock)
        {
            _controls = new Controls
            {
                throttle = Input.GetAxis("Vertical"),    //W key or up arrow to go forward, S key or down arrow to go backwards
                steer    = Input.GetAxis("Horizontal"),  // A key or left pointing arrow to go left, D key or right pointing arrow to go right
                frontBrakes    = Input.GetKey(KeyCode.Space) ? 1f : 0f, // space bar
                backBrakes = Input.GetKey(KeyCode.X) ? 1f : 0f, // X
                dt       = Time.deltaTime,               // actual frame time
            };
        }

        Debug.Log($"[IPC] throttle: {_controls.throttle}");

        
        if (!_hasState) return;

        VehicleState s;
        lock (_stateLock) { s = _state; }

        transform.position = new Vector3(s.x, s.y, s.z);
        transform.rotation = Quaternion.Euler(0f, s.yaw * Mathf.Rad2Deg, 0f);

    }


    private void IOLoop()
    {
        while (_running)
        {
            Debug.Log("[IPC] IOLoop running");
            try
            {
        
                Controls ctrl;
                lock (_controlsLock) { ctrl = _controls; }

        

                string json = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{{\"throttle\":{0:F4},\"steer\":{1:F4},\"frontBrakes\":{2:F4},\"backBrakes\":{3:F4},\"dt\":0.003333}}",
                    ctrl.throttle, ctrl.steer, ctrl.frontBrakes, ctrl.backBrakes
                );

                Debug.Log($"[IPC] sending throttle: {ctrl.throttle}");
                SendMsg(json);

               
                string reply = RecvMsg();
                Debug.Log($"[IPC] raw reply: {reply}");
                if (reply == null) break;

               
                VehicleState s = ParseState(reply);
                Debug.Log($"[IPC] parsed state: x={s.x}, y={s.y}, z={s.z}");
                lock (_stateLock)
                {
                    _state    = s;
                    _hasState = true;
                }
                 System.Threading.Thread.Sleep(3);
            }
            catch (Exception e)
            {
                if (_running)
                    Debug.LogWarning($"[IPC] Socket error: {e.Message}\n{e.StackTrace}");
                _connected = false;
                break;
            }
        }
        _connected = false;
        Debug.Log("[IPC] IO thread exiting");
    }


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
            if (read == 0) return null;  
            total += read;
        }
        return buf;
    }


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
    string search = $"\"{key}\":";
    int idx = json.IndexOf(search, StringComparison.Ordinal);
    if (idx < 0) return 0f;
    int start = idx + search.Length;
    while (start < json.Length && json[start] == ' ') start++;
    int end = start;
    while (end < json.Length && (char.IsDigit(json[end])
           || json[end] == '.' || json[end] == '-' || json[end] == 'e' || json[end] == '+'))
        end++;
    return double.TryParse(json.Substring(start, end - start),
        System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture,
        out double val) ? (float)val : 0f;
    }
}