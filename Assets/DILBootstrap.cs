using System.Collections.Generic;
using UnityEngine;

// fills in whatever the scene is missing, so you can press Play and drive right away.
//
// the scene in the repo is a single GameObject with the team's CarPhysicsIPC on it: no car body,
// camera, light or track. when you press Play, this adds only the pieces that aren't there yet:
//   DILCarClient, a car model (the real FS-4 if its file is in Resources/CarModel, else the box car),
//   the dashboard, car sound, a camera with CameraSwitcher, a light, the track, the lap screen.
// anything you've already added yourself is left alone. nothing is saved into the scene file,
// and no existing script is changed: it all disappears when you press Stop, and comes back next Play.
//
// it only runs in scenes that have a CarPhysicsIPC or a DILCarClient, so other scenes are untouched.
// don't want it? delete this file.
public static class DILBootstrap
{
    // Unity calls this by itself after the scene loads, before any Start() runs.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void FillInScene()
    {
        var added = new List<string>();

        DILCarClient car = Object.FindFirstObjectByType<DILCarClient>();
        if (car == null)
        {
            CarPhysicsIPC original = Object.FindFirstObjectByType<CarPhysicsIPC>();
            if (original == null) return;            // not a DIL scene

            // switch the original client off for this Play session only (its file and the scene
            // stay as they are), and put DILCarClient on the same object with the same server.
            original.enabled = false;
            car = original.gameObject.AddComponent<DILCarClient>();
            car.serverIP = original.serverIP;
            car.serverPort = original.serverPort;
            added.Add("DILCarClient (original CarPhysicsIPC switched off while playing)");
        }

        // car model, unless the car already has something you can see (like your own cube).
        // the real FS-4 model if Assets/Resources/CarModel/FS-4.fbx is there, otherwise the box car.
        if (car.GetComponentInChildren<Renderer>() == null && car.GetComponent<CarVisual>() == null &&
            car.GetComponent<CarModel>() == null)
        {
            if (CarModel.IsAvailable())
            {
                car.gameObject.AddComponent<CarModel>();
                added.Add("FS-4 3D model");
            }
            else
            {
                car.gameObject.AddComponent<CarVisual>();
                added.Add("box car model");
            }
        }

        if (Object.FindFirstObjectByType<Dashboard>() == null)
        {
            car.gameObject.AddComponent<Dashboard>();
            added.Add("dashboard");
        }

        // camera. it must not be a child of the car, or the chase view fights the parent.
        Camera cam = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
        if (cam == null)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            added.Add("camera");
        }
        if (cam.transform.IsChildOf(car.transform))
            cam.transform.SetParent(null, true);
        if (cam.GetComponent<CameraSwitcher>() == null)
        {
            cam.gameObject.AddComponent<CameraSwitcher>();
            added.Add("camera switching");
        }

        // sound needs something to hear it: an AudioListener, normally on the camera.
        if (Object.FindFirstObjectByType<AudioListener>() == null)
            cam.gameObject.AddComponent<AudioListener>();
        if (Object.FindFirstObjectByType<CarSound>() == null)
        {
            car.gameObject.AddComponent<CarSound>();
            added.Add("car sound");
        }

        if (Object.FindFirstObjectByType<Light>() == null)
        {
            Light sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);   // high afternoon sun
            added.Add("light");
        }

        TrackLoader track = Object.FindFirstObjectByType<TrackLoader>();
        if (track == null)
        {
            track = new GameObject("Track").AddComponent<TrackLoader>();
            added.Add("track");
        }
        if (Object.FindFirstObjectByType<LapScreen>() == null)
        {
            track.gameObject.AddComponent<LapScreen>();
            added.Add("lap screen");
        }

        if (added.Count > 0)
            Debug.Log("[DIL] Added for this Play session: " + string.Join(", ", added) +
                      ". Add your own in the scene to replace any of them.");
    }
}
