// Copyright 2023 Google Inc. All rights reserved.
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

namespace Firebase.Sample.AppCheck {
  using Firebase;
  using Firebase.Extensions;
  using Firebase.AppCheck;
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Text;
  using UnityEngine;

  // Handler for UI buttons on the scene.  Also performs some
  // necessary setup (initializing the firebase app, etc) on
  // startup.
  public class UIHandler : MonoBehaviour {
    private const int kMaxLogSize = 16382;

    public GUISkin fb_GUISkin;
    private Vector2 controlsScrollViewVector = Vector2.zero;
    private string logText = "";
    private Vector2 scrollViewVector = Vector2.zero;
    protected bool UIEnabled = false;
    private float textAreaLineHeight;

    private DependencyStatus dependencyStatus = DependencyStatus.UnavailableOther;

    // Your Firebase project's Debug token goes here.
    // You can get this from Firebase Console, in the App Check settings.
    private string appCheckDebugToken = "REPLACE_WITH_APP_CHECK_TOKEN";

    // If the App Check factory has been set
    protected bool factoryConfigured = false;

    // If the testapp is running automated tests
    protected bool runningAutomatedTests = false;

    public class TestAppCheckProvider : IAppCheckProvider {
      public TestAppCheckProvider() {}

      public System.Threading.Tasks.Task<AppCheckToken> GetTokenAsync() {
        // In a normal app, you would connect to the attestation service,
        // and get a valid token to return.
        AppCheckToken token = new AppCheckToken() {
          Token = "TEST_TOKEN",
          ExpireTime = DateTime.UtcNow.AddMinutes(60)
        };
        return Task<AppCheckToken>.FromResult(token);
      }
    }

    public class TestAppCheckProviderFactory : IAppCheckProviderFactory {
      public TestAppCheckProvider provider;

      public TestAppCheckProviderFactory() {
        provider = new TestAppCheckProvider();
      }

      public IAppCheckProvider CreateProvider(FirebaseApp app) {
        return provider;
      }
    }

    protected void PrintToken(string prefix, AppCheckToken token) {
      DebugLog(prefix + "\n" +
               "  " + token.Token + "\n" +
               "  " + token.ExpireTime);
    }

    void OnTokenChanged(object sender, TokenChangedEventArgs tokenArgs) {
      PrintToken("OnTokenChanged called:", tokenArgs.Token);
    }

    // When the app starts, check to make sure that we have
    // the required dependencies to use Firebase, and if not,
    // add them if possible.
    protected virtual void Start() {
      UIEnabled = true;

      // Configure the Debug Factory with the Token
      DebugAppCheckProviderFactory.Instance.SetDebugToken(appCheckDebugToken);
    }

    void UseTestFactory() {
      DebugLog("Using Test Factory");
      FirebaseAppCheck.SetAppCheckProviderFactory(new TestAppCheckProviderFactory());
      InitializeFirebase();
      factoryConfigured = true;
    }

    void UseDebugFactory() {
      DebugLog("Using Debug Factory");
      FirebaseAppCheck.SetAppCheckProviderFactory(DebugAppCheckProviderFactory.Instance);
      InitializeFirebase();
      factoryConfigured = true;
    }

    void GetTokenFromDebug() {
      DebugLog("Getting token from Debug factory");
      IAppCheckProvider provider = DebugAppCheckProviderFactory.Instance.CreateProvider(FirebaseApp.DefaultInstance);
      provider.GetTokenAsync().ContinueWithOnMainThread(task => {
        if (task.IsFaulted) {
          DebugLog("GetTokenFromDebug failed: " + task.Exception);
        } else {
          PrintToken("GetTokenFromDebug:", task.Result);
        }
      });
    }

    void AddTokenChangedListener() {
      DebugLog("Adding token changed listener");
      FirebaseAppCheck.DefaultInstance.TokenChanged += OnTokenChanged;
    }

    void RemoveTokenChangedListener() {
      DebugLog("Removing token changed listener");
      FirebaseAppCheck.DefaultInstance.TokenChanged -= OnTokenChanged;
    }

    protected void InitializeFirebase() {
      FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task => {
        dependencyStatus = task.Result;
        if (dependencyStatus == DependencyStatus.Available) {
          DebugLog("Firebase Ready: " + FirebaseApp.DefaultInstance);
        } else {
          Debug.LogError(
            "Could not resolve all Firebase dependencies: " + dependencyStatus);
        }
      });
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

    // Render the log output in a scroll view.
    void GUIDisplayLog() {
      scrollViewVector = GUILayout.BeginScrollView(scrollViewVector);
      GUILayout.Label(logText);
      GUILayout.EndScrollView();
    }

    void HandleGetAppCheckToken(Task<AppCheckToken> task) {
      if (task.IsFaulted) {
        DebugLog("GetAppCheckToken failed: " + task.Exception);
      } else {
        PrintToken("GetAppCheckToken succeeded:", task.Result);
      }
    }

    // Render the buttons and other controls.
    void GUIDisplayControls() {
      if (UIEnabled) {

        if (runningAutomatedTests) {
          GUILayout.Label("Running automated tests");
          return;
        }

        controlsScrollViewVector = GUILayout.BeginScrollView(controlsScrollViewVector);

        GUILayout.BeginVertical();

        if (!factoryConfigured) {
          if (GUILayout.Button("Use Test Provider")) {
            UseTestFactory();
          }
          if (GUILayout.Button("Use Debug Provider")) {
            UseDebugFactory();
          }
        } else {
          if (GUILayout.Button("Get App Check Token")) {
            DebugLog("GetAppCheckTokenAsync(false) triggered!");
            FirebaseAppCheck.DefaultInstance.GetAppCheckTokenAsync(false).ContinueWithOnMainThread(HandleGetAppCheckToken);
          }
          if (GUILayout.Button("Force New App Check Token")) {
            DebugLog("GetAppCheckTokenAsync(true) triggered!");
            FirebaseAppCheck.DefaultInstance.GetAppCheckTokenAsync(true).ContinueWithOnMainThread(HandleGetAppCheckToken);
          }
          if (GUILayout.Button("Add Token Changed Listener")) {
            AddTokenChangedListener();
          }
          if (GUILayout.Button("Remove Token Changed Listener")) {
            RemoveTokenChangedListener();
          }
        }

        // Can be called regardless of Factory status
        if (GUILayout.Button("Get App Check Token from Debug Provider")) {
          GetTokenFromDebug();
        }

        GUILayout.EndVertical();
        GUILayout.EndScrollView();
      }
    }

    // Render the GUI:
    void OnGUI() {
      GUI.skin = fb_GUISkin;

      GUI.skin.textArea.fontSize = GUI.skin.textField.fontSize;
      // Reduce the text size on the desktop.
      if (UnityEngine.Application.platform != RuntimePlatform.Android &&
          UnityEngine.Application.platform != RuntimePlatform.IPhonePlayer) {
        var fontSize = GUI.skin.textArea.fontSize / 4;
        GUI.skin.textArea.fontSize = fontSize;
        GUI.skin.button.fontSize = fontSize;
        GUI.skin.label.fontSize = fontSize;
        GUI.skin.textField.fontSize = fontSize;
      }
      GUI.skin.textArea.stretchHeight = true;
      // Calculate the height of line of text in a text area.
      if (textAreaLineHeight == 0.0f) {
        textAreaLineHeight = GUI.skin.textArea.CalcSize(new GUIContent("Hello World")).y;
      }

      Rect logArea, controlArea;

      if (Screen.width < Screen.height) {
        // Portrait mode
        controlArea = new Rect(0.0f, 0.0f, Screen.width, Screen.height * 0.5f);
        logArea = new Rect(0.0f, Screen.height * 0.5f, Screen.width, Screen.height * 0.5f);
      } else {
        // Landscape mode
        controlArea = new Rect(0.0f, 0.0f, Screen.width * 0.5f, Screen.height);
        logArea = new Rect(Screen.width * 0.5f, 0.0f, Screen.width * 0.5f, Screen.height);
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
