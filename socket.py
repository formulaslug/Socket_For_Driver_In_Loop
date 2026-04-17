import socket
import struct # used for binary packing
import json #used for encoding/decoding messages
import threading #multiple clients can attach to this

from your_car_physics import CarPhysics   # your GitHub module, how would this work, ask aaron

HOST = "127.0.0.1"
PORT = 9001 

def send_msg(sock, data: dict):
    payload = json.dumps(data).encode("utf-8")
    sock.sendall(struct.pack(">I", len(payload)) + payload)
# takes python dictionary and converts into JSON text then bytes
#frames the message so the receiver knows how many bytes to read

def recv_msg(sock):
    raw_len = _recv_exactly(sock, 4)
    if not raw_len:
        return None
    n = struct.unpack(">I", raw_len)[0]
    raw = _recv_exactly(sock, n)
    return json.loads(raw) if raw else None
#reads one message from socket that is four bytes, return as a python dictionary

def _recv_exactly(sock, n):
    buf = b""
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk:
            return None
        buf += chunk
    return buf
#makes sure we get exactly n bytes, keeps reading socket until we do,or the connection ends. We would not get all the data/some of the data if we dont do this.

def handle_client(conn, addr):
    print(f"[server] Unity connected from {addr}")
    physics = CarPhysics()
    #connects to client, and makes objects
    try:
        while True:
            msg = recv_msg(conn)
            if msg is None:
                break
            #calls recv_msg function to get message from unity, if none(no message) it exits the loop

            physics.set_controls(
                throttle = float(msg.get("throttle", 0.0)),
                steer    = float(msg.get("steer",    0.0)),
                brake    = float(msg.get("brake",    0.0)),
            )
            #reads messages from unity and converts everything into floats
            
            dt = float(msg.get("dt", 1.0 / 60.0))
            physics.step(dt)
            # use dt sent by Unity so sim matches actual frame time
            #this is basically so Unity updates every frame, like ticks, we have to be in sync 

            send_msg(conn, {
                "x":     physics.x,
                "y":     physics.y,
                "z":     physics.z,
                "yaw":   physics.yaw,
                "speed": physics.speed,
            })
            #makes current simualtion state into a dictionary, sends it back to Unity using send_msg
    except (ConnectionResetError, BrokenPipeError):
        pass
    finally:
        conn.close()
        print(f"[server] {addr} disconnected")
        #if it closes the server also neatly disconnects
#this funciton is basically receiving controls from Unity, We simulate the physics and sends state back, we repeat this every frame

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