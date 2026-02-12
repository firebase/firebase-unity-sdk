// Copyright 2016 Google Inc. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Firebase.Sample.Database {
  using Firebase;
  using Firebase.Database;
  using Firebase.Extensions;
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using UnityEngine;
  using System.Threading.Tasks;

  // Handler for UI buttons on the scene.  Also performs some
  // necessary setup (initializing the firebase app, etc) on
  // startup.
  public class UIHandler : MonoBehaviour {

    Vector2 scrollPosition = Vector2.zero;
    private Vector2 controlsScrollViewVector = Vector2.zero;

    public GUISkin fb_GUISkin;

    private string logText = "";
    private Vector2 scrollViewVector = Vector2.zero;
    protected bool UIEnabled = true;

    const int kMaxLogSize = 16382;
    DependencyStatus dependencyStatus = DependencyStatus.UnavailableOther;
    protected bool isFirebaseInitialized = false;

    // Repro specific variables
    private bool isReproRunning = false;
    private int refreshCount = 0;
    private List<DatabaseReference> gameReferences = new List<DatabaseReference>();
    private const int GameCount = 20; // Number of games to simulate

    // When the app starts, check to make sure that we have
    // the required dependencies to use Firebase, and if not,
    // add them if possible.
    protected virtual void Start() {
      FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task => {
        dependencyStatus = task.Result;
        if (dependencyStatus == DependencyStatus.Available) {
          InitializeFirebase();
        } else {
          Debug.LogError(
            "Could not resolve all Firebase dependencies: " + dependencyStatus);
        }
      });
    }

    // Initialize the Firebase database:
    protected virtual void InitializeFirebase() {
      FirebaseApp app = FirebaseApp.DefaultInstance;
      isFirebaseInitialized = true;
      DebugLog("Firebase Initialized. Ready to start reproduction.");
    }

    // Exit if escape (or back, on mobile) is pressed.
    protected virtual void Update() {
      if (Input.GetKeyDown(KeyCode.Escape)) {
        Application.Quit();
      }
    }

    // Output text to the debug log text field, as well as the console.
    public void DebugLog(string s) {
      Debug.Log(s);
      logText += s + "\n";

      while (logText.Length > kMaxLogSize) {
        int index = logText.IndexOf("\n");
        logText = logText.Substring(index + 1);
      }

      scrollViewVector.y = int.MaxValue;
    }

    // Step 1: Populate database with dummy data
    public void PopulateData() {
      if (!isFirebaseInitialized) {
        DebugLog("Firebase not initialized.");
        return;
      }

      DebugLog("Populating data...");
      DatabaseReference gamesRef = FirebaseDatabase.DefaultInstance.GetReference("games");
      Dictionary<string, object> updates = new Dictionary<string, object>();

      for (int i = 0; i < GameCount; i++) {
        string gameId = "game_" + i;
        Dictionary<string, object> gameData = new Dictionary<string, object>();
        gameData["name"] = "Game " + i;
        gameData["score"] = i * 100;
        // Adding players sub-node to attach listeners to
        gameData["players"] = new Dictionary<string, object> {
          { "player1", "ready" },
          { "player2", "waiting" }
        };
        updates[gameId] = gameData;
      }

      gamesRef.UpdateChildrenAsync(updates).ContinueWithOnMainThread(task => {
        if (task.IsFaulted) {
          DebugLog("Failed to populate data: " + task.Exception);
        } else {
          DebugLog("Data populated successfully.");
        }
      });
    }

    public void StartRepro() {
      if (isReproRunning) return;
      if (!isFirebaseInitialized) {
        DebugLog("Firebase not initialized.");
        return;
      }
      isReproRunning = true;
      DebugLog("Reproduction loop started.");
      StartCoroutine(ReproLoop());
    }

    public void StopRepro() {
      isReproRunning = false;
      DebugLog("Reproduction loop stopped.");
    }

    IEnumerator ReproLoop() {
      while (isReproRunning) {
        refreshCount++;
        // DebugLog($"Starting Refresh Cycle #{refreshCount}"); // Reduced logging to avoid lag

        // 1. Remove all listeners
        foreach (var refNode in gameReferences) {
            // Detach from game node
            refNode.ValueChanged -= OnValueChanged;
            refNode.ChildAdded -= OnChildAdded;
            refNode.ChildRemoved -= OnChildRemoved;
        }
        gameReferences.Clear();

        // 2. Fetch a new snapshot (GetValueAsync)
        var gamesRef = FirebaseDatabase.DefaultInstance.GetReference("games");
        var task = gamesRef.GetValueAsync();
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.IsFaulted) {
          DebugLog("Failed to fetch data: " + task.Exception);
          isReproRunning = false;
          yield break;
        }

        DataSnapshot snapshot = task.Result;

        // 3. Rebuilds the UI (simulated)
        // 4. Re-attaches the same listeners for each game
        if (snapshot != null && snapshot.ChildrenCount > 0) {
            foreach (var gameSnapshot in snapshot.Children) {
                DatabaseReference gameRef = gameSnapshot.Reference;

                // Attach listeners
                gameRef.ValueChanged += OnValueChanged;
                gameRef.ChildAdded += OnChildAdded;
                gameRef.ChildRemoved += OnChildRemoved;

                gameReferences.Add(gameRef);
            }
        }

        // Delay before next cycle to simulate frame updates or user interaction speed
        // The issue says "The crash can occur during or shortly after re-attaching many listeners"
        // A small delay simulates the 'frame' cycle.
        yield return new WaitForSeconds(0.1f);
      }
    }

    // Callback handlers
    void OnValueChanged(object sender, ValueChangedEventArgs args) {
      // Minimal logic to simulate processing
      if (args.DatabaseError != null) {
        Debug.LogError(args.DatabaseError.Message);
        return;
      }
      // Do nothing to keep performance high for repro
    }

    void OnChildAdded(object sender, ChildChangedEventArgs args) {
      if (args.DatabaseError != null) {
        Debug.LogError(args.DatabaseError.Message);
        return;
      }
    }

    void OnChildRemoved(object sender, ChildChangedEventArgs args) {
        if (args.DatabaseError != null) {
        Debug.LogError(args.DatabaseError.Message);
        return;
      }
    }

    // Render the log output in a scroll view.
    void GUIDisplayLog() {
      scrollViewVector = GUILayout.BeginScrollView(scrollViewVector);
      GUILayout.Label(logText);
      GUILayout.EndScrollView();
    }

    // Render the buttons and other controls.
    void GUIDisplayControls() {
      if (UIEnabled) {
        controlsScrollViewVector =
            GUILayout.BeginScrollView(controlsScrollViewVector);
        GUILayout.BeginVertical();

        if (GUILayout.Button("Populate Data (Step 1)")) {
          PopulateData();
        }

        GUILayout.Space(20);

        if (GUILayout.Button(isReproRunning ? "Stop Repro Loop" : "Start Repro Loop (Steps 2-4)")) {
          if (isReproRunning) StopRepro();
          else StartRepro();
        }

        GUILayout.Space(20);

        GUILayout.Label($"Refresh Count: {refreshCount}");
        GUILayout.Label($"Active Listeners: {gameReferences.Count * 3}");

        GUILayout.Space(20);

        if (GUILayout.Button("Go Offline")) {
          FirebaseDatabase.DefaultInstance.GoOffline();
        }

        if (GUILayout.Button("Go Online")) {
          FirebaseDatabase.DefaultInstance.GoOnline();
        }

        GUILayout.EndVertical();
        GUILayout.EndScrollView();
      }
    }

    // Render the GUI:
    void OnGUI() {
      GUI.skin = fb_GUISkin;
      if (dependencyStatus != DependencyStatus.Available) {
        GUILayout.Label("One or more Firebase dependencies are not present.");
        GUILayout.Label("Current dependency status: " + dependencyStatus.ToString());
        return;
      }
      Rect logArea, controlArea;

      if (Screen.width < Screen.height) {
        // Portrait mode
        controlArea = new Rect(0.0f, 0.0f, Screen.width, Screen.height * 0.5f);
        logArea = new Rect(0.0f, Screen.height * 0.5f, Screen.width, Screen.height * 0.5f);
      } else {
        // Landscape mode
        controlArea = new Rect(0.0f, 0.0f, Screen.width * 0.5f, Screen.height);
        logArea = new Rect(Screen.width * 0.5f, 0, Screen.width * 0.5f, Screen.height);
      }

      GUILayout.BeginArea(logArea);
      GUIDisplayLog();
      GUILayout.EndArea();

      GUILayout.BeginArea(controlArea);
      GUIDisplayControls();
      GUILayout.EndArea();
    }
  }
}
