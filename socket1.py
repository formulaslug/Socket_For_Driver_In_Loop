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

true_wheelbase = Parameters.get("wheelBase", 1.589989) #pulls wheelbase from params file
front_weight_dist = 0.4632 #percentage of car's total mass on front axel


calculated_Lf = true_wheelbase * (1.0 - front_weight_dist)  # Distance from center of gravity to front axle
calculated_Lr = true_wheelbase * front_weight_dist # Distance from center of gravity to rear axle

vehicle_params = VehicleParameters(
    mass=Parameters.get("Mass", 300.0),
    wheelbase=true_wheelbase,
    Lf=calculated_Lf,
    Lr=calculated_Lr
)
bicycle_model = DoubleBicycleModel(params=vehicle_params, tire_model="linear")

MAX_STEER_DEGREES = 25.0
BRAKE_GAIN_FRONT = 1500.0  # this is braking force, make sure this matches FS-4 brake bite
BRAKE_GAIN_REAR = 1000.0

HOST = "127.0.0.1"
PORT = 9001 

# send_msg function take a python dict, converts it to JSON, and sends it to Unity with 4-byte tagline infront(so Unity knows exactly how many bytes to read per message)
def send_msg(sock, data: dict):
    try:
        payload = json.dumps(data).encode("utf-8")
        sock.sendall(struct.pack(">I", len(payload)) + payload)
    except Exception as e:
        print(f"[server] send error: {e}")

#reads 4 bytes from unity and converts it back into a python dict
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

# sometimes we may not get all 4 bytes in one message(recv_msg), so this function loops until we get all bytes or the connection closes
def _recv_exactly(sock, n):
    buf = b""
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk: return None
        buf += chunk
    return buf

'''this funciton initalize a 44 slot array and itilizes coordinates to 0. while loop is to get inputs from unity 
and put those values into array. We dont use brakes calculations written in dynamicstepstate function because it expected results
to be in Newtons but unity sends 0 to 1, so we have to calculate it ourselves. Also steering in unity is from -1 to 1so we convert that into
radians. Used the bicycle model to calculate yaw. in function calculateheading(in sims data) yaw is always 0 so heading is never changed, 
so we use yaw rate from bicycle model to get rotation angle. We send telemetry message every 30 frames, and then send all updated values 
back to unity'''
def handle_client(conn, addr):
    print(f"[server] Unity connected from {addr}")
    
    state = np.zeros(44) 
    server_pos_x = 0.0 
    server_pos_y = 0.0
    server_pos_z = 0.0
    server_heading = np.array([1.0, 0.0])
    server_yaw_accumulated = 0.0
    bicycle_model.reset()
    
    dt = 1 / Parameters["stepsPerSecond"] 
    frame_counter = 0

    try:
        while True:
            msg = recv_msg(conn)
            if msg is None:
                break
                
            unity_steer = float(msg.get("steer", 0.0))
            front_brakes = float(msg.get("frontBrakes", 0.0))
            rear_brakes = float(msg.get("backBrakes", 0.0))
            
        
            state[varThrottle]           = float(msg.get("throttle", 0.0))
            state[varSteerAngle]         = unity_steer
            state[varBrakePressureFront] = front_brakes
            state[varBrakePressureRear]  = rear_brakes
            
            try:
                state = dynamicStepState(state)
                state = np.nan_to_num(state, nan=0.0)
                
                raw_engine_speed = float(state[varSpeed])
                
                total_brake_force = (front_brakes * BRAKE_GAIN_FRONT) + (rear_brakes * BRAKE_GAIN_REAR)
                brake_deceleration = total_brake_force / Parameters.get("Mass", 300.0)
                
                current_speed = max(0.0, raw_engine_speed - (brake_deceleration * dt))
                
                steering_radians = unity_steer * MAX_STEER_DEGREES * (np.pi / 180.0)
                
                bicycle_model.integrate_step(v_x=current_speed, delta=steering_radians, dt=dt, method="rk4")
                current_yaw_rate = float(bicycle_model.state[1])
                
                if current_speed > 0.01:
                    # Car is moving
                    rotation_angle = current_yaw_rate * dt
                else:
                    # Car is stopped
                    rotation_angle = 0.0
                    current_yaw_rate = 0.0
                
                server_yaw_accumulated += rotation_angle
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
                
                displacement = current_speed * dt
                server_pos_x += server_heading[0] * displacement
                server_pos_z += server_heading[1] * displacement
                server_pos_y = float(state[varPosY]) 
                
                state[varSpeed] = current_speed
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
                
            frame_counter += 1
            if frame_counter % 30 == 0:
                print(f"[Live Telemetry] Speed: {current_speed:.2f} m/s | Total Heading Angle: {np.degrees(server_yaw_accumulated):.1f}° | Brakes: F:{front_brakes:.1f}/R:{rear_brakes:.1f}")
                
            send_msg(conn, {
                "x":        float(server_pos_x),
                "y":        float(server_pos_y),
                "z":        float(server_pos_z),
                "yaw":      float(server_yaw_accumulated),
                "speed":    float(current_speed),
                "headingX": float(server_heading[0]),
                "headingZ": float(server_heading[1]),
            })

    except (ConnectionResetError, BrokenPipeError):
        print(f"[server] Connection closed by Unity client.")
    finally:
        conn.close()

#binds the host to the port, listens for any connections
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