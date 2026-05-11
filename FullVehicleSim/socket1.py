import socket
import struct
import json
import threading
import numpy as np

from engine import dynamicStepState
from paramLoader import (
    varThrottle, varBrakePressureFront, varBrakePressureRear,
    varSteerAngle, varPosX, varPosY, varPosZ,
    varSpeed, varYawRate
)

HOST = "127.0.0.1"
PORT = 9001

def send_msg(sock, data: dict):
    payload = json.dumps(data).encode("utf-8")
    sock.sendall(struct.pack(">I", len(payload)) + payload)

def recv_msg(sock):
    raw_len = _recv_exactly(sock, 4)
    if not raw_len:
        return None
    n = struct.unpack(">I", raw_len)[0]
    raw = _recv_exactly(sock, n)
    return json.loads(raw) if raw else None

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
    
    # create a 44-element array of zeros — this is the car's starting state
    state = np.zeros(44)

    try:
        while True:
            msg = recv_msg(conn)
            if msg is None:
                break

            # plug Unity's controls into the correct slots in the array
            state[varThrottle]            = float(msg.get("throttle", 0.0))
            state[varSteerAngle]          = float(msg.get("steer",    0.0))
            state[varBrakePressureFront]  = float(msg.get("brake",    0.0))
            state[varBrakePressureRear]   = float(msg.get("brake",    0.0))

            # step the simulation
            state = dynamicStepState(state)

            # send back position, yaw, speed
            send_msg(conn, {
                "x":     float(state[varPosX]),
                "y":     float(state[varPosY]),
                "z":     float(state[varPosZ]),
                "yaw":   float(state[varYawRate]),
                "speed": float(state[varSpeed]),
            })

    except (ConnectionResetError, BrokenPipeError):
        pass
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