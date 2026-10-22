using System.Collections.Generic;
using UnityEngine;

// Fills in whatever the scene is missing, so you can press Play and drive right away.
//
// The scene in the repo is a single GameObject with the team's CarPhysicsIPC on it: no car body,
// camera, light or track. When you press Play, this adds only the pieces that aren't there yet:
//   DILCarClient, a car model, the dashboard, a camera with CameraSwitcher, a light, the track.
// Anything you've already added yourself is left alone. Nothing is saved into the scene file,
// and no existing script is changed: it all disappears when you press Stop, and comes back next Play.
//
// It only runs in scenes that have a CarPhysicsIPC or a DILCarClient, so other scenes are untouched.
// Don't want it? Delete this file.
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

            // Switch the original client off for this Play session only (its file and the scene
            // stay as they are), and put DILCarClient on the same object with the same server.
            original.enabled = false;
            car = original.gameObject.AddComponent<DILCarClient>();
            car.serverIP = original.serverIP;
            car.serverPort = original.serverPort;
            added.Add("DILCarClient (original CarPhysicsIPC switched off while playing)");
        }

        // Car model, unless the car already has something you can see (like your own cube).
        if (car.GetComponentInChildren<Renderer>() == null && car.GetComponent<CarVisual>() == null)
        {
            car.gameObject.AddComponent<CarVisual>();
            added.Add("car model");
        }

        if (Object.FindFirstObjectByType<Dashboard>() == null)
        {
            car.gameObject.AddComponent<Dashboard>();
            added.Add("dashboard");
        }

        // Camera. It must not be a child of the car, or the chase view fights the parent.
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

        if (Object.FindFirstObjectByType<Light>() == null)
        {
            Light sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);   // high afternoon sun
            added.Add("light");
        }

        if (Object.FindFirstObjectByType<TrackLoader>() == null)
        {
            new GameObject("Track").AddComponent<TrackLoader>();
            added.Add("track");
        }

        if (added.Count > 0)
            Debug.Log("[DIL] Added for this Play session: " + string.Join(", ", added) +
                      ". Add your own in the scene to replace any of them.");
    }
}
