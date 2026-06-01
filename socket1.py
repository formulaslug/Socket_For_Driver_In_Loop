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
    varSpeed, varYawRate , varHeadingX, varHeadingZ, Parameters
)
from yaw_rate_model.double_bicycle_model import DoubleBicycleModel, VehicleParameters

true_wheelbase = Parameters.get("wheelBase", 1.589989)

front_weight_dist = 0.4632 

# 3. Dynamically calculate the correct, scaled Lf and Lr
calculated_Lf = true_wheelbase * (1.0 - front_weight_dist)  # Distance from CG to front axle
calculated_Lr = true_wheelbase * front_weight_dist

vehicle_params = VehicleParameters(
    mass=Parameters.get("Mass", 300.0),
    wheelbase=true_wheelbase,
    Lf=calculated_Lf,
    Lr=calculated_Lr
)
bicycle_model = DoubleBicycleModel(params=vehicle_params, tire_model="linear")

# Adjust this to your FSAE car's real maximum steering lock in degrees
MAX_STEER_DEGREES = 25.0

HOST = "127.0.0.1"
PORT = 9001

def send_msg(sock, data: dict):
    try:
        payload = json.dumps(data).encode("utf-8")
        sock.sendall(struct.pack(">I", len(payload)) + payload)
    except Exception as e:
        print(f"[server] send error: {e}")

def recv_msg(sock):
    try:
        raw_len = _recv_exactly(sock, 4)
        if not raw_len: return None
        n = struct.unpack(">I", raw_len)[0]
        raw = _recv_exactly(sock, n)
        if not raw: return None
        return json.loads(raw)
    except Exception as e:
        print(f"[server] recv error: {e}")
        return None

def _recv_exactly(sock, n):
    buf = b""
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk: return None
        buf += chunk
    return buf

def handle_client(conn, addr):
    print(f"[server] Unity connected from {addr}")
    
    # Initialize state
    state = np.zeros(44)
    
    # We maintain our own positioning and heading variables on the server side
    # to completely bypass the engine's broken straight-line calculations.
    server_pos_x = 0.0
    server_pos_y = 0.0
    server_pos_z = 0.0
    server_heading = np.array([1.0, 0.0]) # [X, Z] alignment
    bicycle_model.reset()
    
    dt = 1 / Parameters["stepsPerSecond"]

    try:
        while True:
            msg = recv_msg(conn)
            if msg is None:
                print("[server] Client disconnected.")
                break
                
            unity_steer = float(msg.get("steer", 0.0))
            
            # Pass controls to the engine array
            state[varThrottle]           = float(msg.get("throttle", 0.0))
            state[varSteerAngle]         = unity_steer
            state[varBrakePressureFront] = float(msg.get("frontBrakes", 0.0))
            state[varBrakePressureRear]  = float(msg.get("backBrakes",  0.0))
            
            try:
                # 1. Run the black box engine to get the updated speed and forces
                state = dynamicStepState(state)
                state = np.nan_to_num(state, nan=0.0)
                
                current_speed = float(state[varSpeed])
                
                # 2. Convert Unity steering (-1 to 1) to physical radians
                steering_radians = unity_steer * MAX_STEER_DEGREES * (np.pi / 180.0)
                
                # 3. Integrate the bicycle model to find the true yaw rate
                bicycle_model.integrate_step(v_x=current_speed, delta=steering_radians, dt=dt, method="rk4")
                
                if unity_steer == 0:
                    current_yaw_rate = 0.0
                    bicycle_model.state[0] = 0.0 # clear lateral velocity
                    bicycle_model.state[1] = 0.0 # clear yaw rate
                else:
                    current_yaw_rate = float(bicycle_model.state[1])
                
                # 4. Update our custom heading tracking using the calculated yaw rate
                rotation_angle = current_yaw_rate * dt
                cos_theta = np.cos(rotation_angle)
                sin_theta = np.sin(rotation_angle)
                
                rotation_matrix = np.array([
                    [cos_theta, -sin_theta],
                    [sin_theta,  cos_theta]
                ])
                
                server_heading = rotation_matrix @ server_heading
                norm = np.linalg.norm(server_heading)
                if norm > 0:
                    server_heading = server_heading / norm
                
                # 5. Overwrite coordinates based on our updated heading direction
                # Distance = speed * time
                displacement = current_speed * dt
                server_pos_x += server_heading[0] * displacement
                server_pos_z += server_heading[1] * displacement
                server_pos_y = float(state[varPosY]) # Keep whatever Y pitch the engine does
                
                # Keep the internal state synced for the next iteration step
                state[varYawRate] = current_yaw_rate
                state[varPosX] = server_pos_x
                state[varPosZ] = server_pos_z
                state[varHeadingX] = server_heading[0]
                state[varHeadingZ] = server_heading[1]

            except Exception as e:
                print(f"[server] physics engine calculation error: {e}")
                import traceback
                traceback.print_exc()
                break
                
            # Send our overridden, correctly curved trajectory back to Unity
            send_msg(conn, {
                "x":        float(server_pos_x),
                "y":        float(server_pos_y),
                "z":        float(server_pos_z),
                "yaw":      float(state[varYawRate]),
                "speed":    float(current_speed),
                "headingX": float(server_heading[0]),
                "headingZ": float(server_heading[1]),
            })

    except (ConnectionResetError, BrokenPipeError):
        print(f"[server] Connection closed by Unity client.")
    finally:
        conn.close()

def main():
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as srv:
        srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        srv.bind((HOST, PORT))
        srv.listen(1)
        print(f"[server] Listening on {HOST}:{PORT}")
        while True:
            conn, addr = srv.accept()
            threading.Thread(target=handle_client, args=(conn, addr), daemon=True).start()

if __name__ == "__main__":
    main()