import sys
import os
sys.path.append(os.path.join(os.path.dirname(__file__), "Sims-Data", "FullVehicleSim"))
os.chdir(os.path.join(os.path.dirname(__file__), "Sims-Data", "FullVehicleSim"))
import socket
import struct
import json
import json5
import threading
import numpy as np
from engine import dynamicStepState
from paramLoader import (
    varThrottle, varBrakePressureFront, varBrakePressureRear,
    varSteerAngle, varPosX, varPosY, varPosZ,
    varSpeed, varYawRate , varHeadingX
)

HOST = "127.0.0.1"
PORT = 9001

def send_msg(sock, data: dict):
    try:
        payload = json.dumps(data).encode("utf-8")
        sock.sendall(struct.pack(">I", len(payload)) + payload)
        print(f"[server] sent: {data}")
    except Exception as e:
        print(f"[server] send error: {e}")

def recv_msg(sock):
    try:
        raw_len = _recv_exactly(sock, 4)
        if not raw_len:
            print("[server] recv_msg: no length received")
            return None
        n = struct.unpack(">I", raw_len)[0]
        print(f"[server] expecting {n} bytes")
        raw = _recv_exactly(sock, n)
        if not raw:
            print("[server] recv_msg: no data received")
            return None
        print(f"[server] raw message: {raw}")
        msg = json.loads(raw)
        print(f"[server] received: {msg}")
        return msg
    except Exception as e:
        print(f"[server] recv error: {e}")
        return None

def _recv_exactly(sock, n):
    buf = b""
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk:
            return None
        buf += chunk
    return buf

def handle_client(conn, addr):
    print(f"[server] Unity connected from {addr}")
    state = np.zeros(44)
    state[varHeadingX] = 1.0
    try:
        while True:
            msg = recv_msg(conn)
            if msg is None:
                print("[server] msg is None, disconnecting")
                break
            state[varThrottle]           = float(msg.get("throttle", 0.0))
            state[varSteerAngle]         = float(msg.get("steer",    0.0))
            state[varBrakePressureFront] = float(msg.get("frontBrakes", 0.0))
            state[varBrakePressureRear]  = float(msg.get("backBrakes",  0.0))
            try:
                state = dynamicStepState(state)
                state = np.nan_to_num(state, nan=0.0)
            except Exception as e:
                print(f"[server] physics error: {e}")
                import traceback
                traceback.print_exc()
                break
            send_msg(conn, {
                "x":     float(state[varPosX]),
                "y":     float(state[varPosY]),
                "z":     float(state[varPosZ]),
                "yaw":   float(state[varYawRate]),
                "speed": float(state[varSpeed]),
            })
    except (ConnectionResetError, BrokenPipeError) as e:
        print(f"[server] connection error: {e}")
    except Exception as e:
        print(f"[server] unexpected error: {e}")
        import traceback
        traceback.print_exc()
    finally:
        conn.close()
        print(f"[server] {addr} disconnected")

def main():
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as srv:
        srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        srv.bind((HOST, PORT))
        srv.listen(1)
        print(f"[server] listening on {HOST}:{PORT}")
        while True:
            conn, addr = srv.accept()
            threading.Thread(target=handle_client, args=(conn, addr), daemon=True).start()

if __name__ == "__main__":
    main()