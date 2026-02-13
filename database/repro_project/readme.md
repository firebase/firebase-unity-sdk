# Firebase Database Reproduction Project - Issue #1391

This project is a reproduction case for [Issue #1391: Native crash in Realtime Database (uS WebSocket thread)](https://github.com/firebase/firebase-unity-sdk/issues/1391).

It simulates the conditions described in the issue:
1.  Loads a list of games (20 items).
2.  Triggers a "refresh" loop that:
    *   Removes all listeners.
    *   Fetches a new snapshot (`GetValueAsync`).
    *   Re-attaches `ValueChanged`, `ChildAdded`, and `ChildRemoved` listeners to each game node.
    *   Repeats this cycle rapidly to stress the WebSocket/TLS layer.

## How to use

1.  Open the project in Unity (Unity 6 / 6000.3.0f1 recommended as per issue, but should work on others).
2.  Import the Firebase Database SDK (version 13.7.0 or similar).
3.  Configure `Assets/GoogleService-Info.plist` (iOS) or `Assets/google-services.json` (Android/Desktop) with your Firebase project details.
4.  Open `Assets/Firebase/Sample/Database/MainScene.unity`.
5.  Play the scene.
6.  Click **"Populate Data (Step 1)"** to seed the database with test data ("games" node).
7.  Click **"Start Repro Loop (Steps 2-4)"** to begin the stress test.
    *   This will repeatedly fetch data and re-attach listeners.
    *   Watch for crashes in the Unity Editor or logs indicating native failures.

## Reproduction Logic

The logic is contained in `Assets/Firebase/Sample/Database/UIHandler.cs`.

*   **PopulateData()**: Creates 20 games with `name`, `score`, and a `players` dictionary.
*   **ReproLoop()**: The coroutine that runs the cycle described in the issue.
    *   Clears listeners.
    *   `GetValueAsync("games")`.
    *   Iterates result and adds 3 listeners per game.
    *   Waits 0.1s (can be adjusted) and repeats.
*   **SimulateDataChanges()**: A background loop that rapidly updates random game scores and statuses (every 0.05s) to generate `ValueChanged` events during the refresh cycle, increasing the likelihood of race conditions.

## Original Readme

See `database/testapp/readme.md` for the original sample documentation.
