# Driver In Loop

There are two sides: client and server which communicate over a TCP socket. The server is written in python, and runs vehicle physics. The client is in Unity game engine and is coded in C#. It handles driver inputs. We should keep working on Unity so we have visuals as well. 

**Note:** 
use python_server branch until it is merged into main

**Basic Overview:**
Python server(file name socket1.py) listens on local port and waits for Unity to connect. Client(file named Client_Socket.cs in Assets folder) side connects to server, reads inputs every frame, and sends it to the server. The server updates the car's variables and sends it back to the client. Client moves the car accordingly. This loop repeats until client is disconnected.

**Set up:**
Install Unity, version 6000.4.3f1
Make sure to have python 3
Install json5 and numpy

1) choose where you want the project to live( Documents, Downloads, etc.) then clone repo and make sure you are in correct branch:
   
      _cd Documents_
   
      _git clone --recurse-submodules https://github.com/formulaslug/Socket_For_Driver_In_Loop.git_
   
     _cd Socket_For_Driver_In_Loop_
   
     _git checkout python_server_

3) Open Unity Projects and add the cloned folder

4) start up the python server by running following command in terminal(MUST do this before we connect Unity). If it successfully connected, the terminal will print something like "Listening on ...."
   
     _python3 socket1.py_

5) Press play in Unity.  If it successfully connected, the terminal will print something like "Unity connected from...."

**Keys:**
Once both client and server are successfully connected, we can press the following buttons to make the car move:

   W means forward

   S means backward

   A means left

   D means right

   X means rear brakes

   Space means front brakes
       


