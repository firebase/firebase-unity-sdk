/*
 * Copyright 2016 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Google;
using UnityEditor;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Firebase.Editor {

  // Repaint the settings window when any assets are imported or removed.
  [InitializeOnLoad]
  internal class SettingsWindowUpdater : AssetPostprocessor {
    public static System.Object assetsChangedLock = new System.Object();
    public static bool assetsChanged = false;
    // Called when any asset is imported, deleted, or moved.
    private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromPath) {
      lock (assetsChangedLock) {
        assetsChanged = true;
      }
    }
  }

  internal class SettingsWindow : EditorWindow {
    [MenuItem("Window/Firebase/Documentation")]
    public static void ShowWindow() {
      EditorWindow.GetWindow(typeof(SettingsWindow));
    }

    // Reset the custom styles to null, to force them to reload.
    void ResetStyles() {
      labelStyle = null;
    }

    // Repaint the window if any assets have been modified.
    void OnInspectorUpdate() {
      lock (SettingsWindowUpdater.assetsChangedLock) {
        if (SettingsWindowUpdater.assetsChanged) {
          OnFocus();
        }
        SettingsWindowUpdater.assetsChanged = false;
      }
    }

    static GUIStyle labelStyle = null;
    // Custom label style used by default by the window.
    static GUIStyle LabelStyle
    {
      get
      {
        if (labelStyle == null) {
          labelStyle = new GUIStyle(EditorStyles.label);
          // Enable rich text, as we want to be able to bold parts of it.
          labelStyle.richText = true;
          labelStyle.wordWrap = true;
        }
        return labelStyle;
      }
    }

    // Interface of a state for the menu, to handle rendering the whole window.
    interface IMenuState {
      // Render the menu state.
      // @param menus The current stack of menu states.
      void OnGUI(Stack<IMenuState> menus);
    }

    class MainMenuState : IMenuState {
      // The texture to render at the top of the window.
      Texture2D titleTexture = null;
      // The scrollbar value.
      Vector2 scrollBar;
      // The list of APIs to present information on.
      List<ApiInfo> apis;

      // If set, can render a footer in the scroll view.
      public Action Footer { get; set; }

      public MainMenuState(params ApiInfo[] apis) {
        titleTexture = (Texture2D)EditorGUIUtility.Load(
          "Firebase/firebase_lockup" + (EditorGUIUtility.isProSkin ? "_dark" : "") + ".png");
        this.apis = new List<ApiInfo>(apis);
      }

      void IMenuState.OnGUI(Stack<IMenuState> menus) {
        // Render the top section, which has the logo, and a short description.
        GUILayout.Label(titleTexture);
        IndentedLabel(20, DocRef.FirebaseDescription);

        DrawLine();

        // Render the scrollbar, hiding the horizontal one. Also, 0 out any changes in x.
        scrollBar = GUILayout.BeginScrollView(scrollBar, false, true, GUIStyle.none,
                                              GUI.skin.verticalScrollbar);
        scrollBar.x = 0;

        DrawConnectionInfo();

        DrawLine();

        // Render the information about the APIs.
        foreach (ApiInfo api in apis) {
          DisplayApi(api, menus);
        }

        if (Footer != null) Footer();

        EditorGUILayout.EndScrollView();
      }

      // Render information about the given API, including buttons to documentation links.
      void DisplayApi(ApiInfo api, Stack<IMenuState> menus) {
        GUIContent content = new GUIContent("<b>" + api.Name + "</b>\n" +
                                            api.Description, api.Image);

        Indent(10, () => GUILayout.Label(content));
        IndentedButtonLink(20, api.GuideButton, api.GuideLink);
        IndentedButtonLink(20, "Open API Reference", api.ApiReference);

        EditorGUILayout.Separator();
      }

      // Draw the status of whether Android and iOS are connected, providing links to either the
      // documentation on how to connect, or to the console of the connected project.
      void DrawConnectionInfo() {
        IndentedLabel(10, DocRef.Android, EditorStyles.boldLabel);
        Indent(20, () => {
          if (IsAndroidConnected()) {
            GUILayout.Label(DocRef.AndroidConnected);
            IndentedButtonProjectLink(0, DocRef.OpenConsole, s_androidProjectId, "android");
          }
          else {
            GUILayout.Label(DocRef.AndroidDisconnected);
            IndentedButtonLink(0, DocRef.LearnMore,
                               Link.AndroidSetup);
          }
        });

        IndentedLabel(10, DocRef.IOS, EditorStyles.boldLabel);
        Indent(20, () => {
          if (IsIosConnected()) {
            GUILayout.Label(DocRef.IOSConnected);
            IndentedButtonProjectLink(0, DocRef.OpenConsole, s_iosProjectId, "ios");
          }
          else {
            GUILayout.Label(DocRef.IOSDisconnected);
            IndentedButtonLink(0, DocRef.LearnMore,
                               Link.IOSSetup);
          }
        });
      }
    }

    // Renderer and cache of measurement settings.
    EditorMeasurement.Settings measurementSettings;
    // The top level menu.
    IMenuState mainMenu;
    // The stack of menu states, to allow diving into more information on topics.
    Stack<IMenuState> menuStateStack;

    // Returns the current top level menu, which should be rendered when OnGui is called.
    IMenuState CurrentMenuState
    {
      get
      {
        if (menuStateStack.Count == 0)
          return mainMenu;
        else
          return menuStateStack.Peek();
      }
    }

    // Set the title of the window in a way that is compatible with Unity 4.x.
    void SetTitle(string title) {
      // Unity 4.x has a string "title" property.
      var windowType = GetType();
      var titlePropertyInfo = windowType.GetProperty("title");
      if (titlePropertyInfo != null) {
        titlePropertyInfo.SetValue(this, title, null);
      } else {
        SetTitleUnity5x(title);
      }
    }

    void SetTitleUnity5x(string title) {
      // Unity 5.x and above has an EditorWindow that supports a titleContent
      // property which allows the title, image and tooltip to be customized.
      titleContent.text = title;
      titleContent.image = null;
    }

    // Called when the Window is opened, handles all initialization.
    void OnEnable() {
      measurementSettings = new EditorMeasurement.Settings(Measurement.analytics);
      mainMenu = new MainMenuState(
        ApiInfo.Analytics(),
        ApiInfo.Auth(),
        ApiInfo.CloudMessaging(),
        ApiInfo.Crashlytics(),
        ApiInfo.Database(),
        ApiInfo.DynamicLinks(),
        ApiInfo.Functions(),
        ApiInfo.RemoteConfig(),
        ApiInfo.Storage()) {
        Footer = () => {
          this.RenderSettings();
        }
      };
      menuStateStack = new Stack<IMenuState>();

      SetTitle("Firebase");

      ResetStyles();

      Measurement.ReportWithBuildTarget("settings/documentation", null, "Settings Show");
      Measurement.analytics.Report("settings/show", "Settings Show");
    }

    // Called when the Window gets focus.
    void OnFocus() {
      var androidProjectFields = GenerateXmlFromGoogleServicesJson.ReadProjectFields();
      if (!androidProjectFields.TryGetValue("project_id", out s_androidProjectId)) {
          s_androidProjectId = null;
      }
      XcodeProjectPatcher.ReadConfig(errorOnNoConfig: false);
      var iosProjectConfig = XcodeProjectPatcher.GetConfig();
      if (!iosProjectConfig.TryGetValue("PROJECT_ID", out s_iosProjectId)) {
          s_iosProjectId = null;
      }
      Repaint();
    }

    // Draw a single horizontal line.
    static void DrawLine() {
      EditorGUILayout.Separator();
      GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));
      EditorGUILayout.Separator();
    }

    // Indents by the given pixel amount, then performs the given callback.
    static void Indent(int pixels, System.Action toIndent) {
      GUILayout.BeginHorizontal();
      GUILayout.Space(pixels);
      GUILayout.BeginVertical();
      toIndent();
      GUILayout.EndVertical();
      GUILayout.EndHorizontal();
    }

    // Prints a label with the given text, indented by the given pixel amount.
    static void IndentedLabel(int pixels, string text) {
      GUILayout.BeginHorizontal();
      GUILayout.Space(pixels);
      GUILayout.Label(text);
      GUILayout.EndHorizontal();
    }

    // Prints a label with the given text, indented by the given pixel amount, with the given style.
    static void IndentedLabel(int pixels, string text, GUIStyle style) {
      GUILayout.BeginHorizontal();
      GUILayout.Space(pixels);
      GUILayout.Label(text, style);
      GUILayout.EndHorizontal();
    }

    // Displays a button with the given text, indented by the given pixel amount.
    static bool IndentedButton(int pixels, string text) {
      bool result;
      GUILayout.BeginHorizontal();
      GUILayout.Space(pixels);
      result = GUILayout.Button(text, GUILayout.ExpandWidth(false));
      GUILayout.EndHorizontal();
      return result;
    }

    // Displays a button with the given text, indented by the given pixel amount, that when clicked
    // opens the given link.
    static bool IndentedButtonLink(int pixels, string text, string link,
                                   Action<string, string> openUrl = null) {
      bool result = IndentedButton(pixels, text);
      if (result) {
        if (openUrl == null) openUrl = Measurement.analytics.OpenUrl;
        openUrl(link + "?utm_referrer=unity", text);
      }
      return result;
    }

    /// <summary>
    /// Displays a button with the given text, indented by the given pixel amount, that when clicked
    /// opens the given project in the Firebase console.
    /// </summary>
    /// <param name="pixels">Number of pixels to indent.</param>
    /// <param name="text">Text to display in the button.</param>
    /// <param name="projectId">Firebase project to open displayed.</param>
    /// <param name="platform">Platform overview being displayed (e.g "ios", "android").</param>
    static bool IndentedButtonProjectLink(int pixels, string text, string projectId,
                                          string platform) {
      return IndentedButtonLink(
          pixels, text,
          String.Format("https://console.firebase.google.com/project/{0}/overview", projectId),
          openUrl:
          (string url, string title) => {
            Measurement.analytics.Report(String.Format("showproject/{0}", platform),
                                         "Show Project Information");
            Application.OpenURL(url);
          });
    }

    /// <summary>
    /// Render a global settings panel.
    /// </summary>
    void RenderSettings() {
      DrawLine();
      GUILayout.BeginHorizontal();
      GUILayout.Space(10);
      measurementSettings.RenderGui();
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GUILayout.Space(20);
      if (GUILayout.Button("Reset to Defaults", GUILayout.ExpandWidth(false))) {
          Settings.RestoreDefaultSettings();
          Measurement.analytics.Report("settings/reset", "Settings Reset");
      }
      if (GUILayout.Button("Cancel", GUILayout.ExpandWidth(false))) {
          Measurement.analytics.Report("settings/cancel", "Settings Cancel");
          Close();
      }
      if (GUILayout.Button("OK", GUILayout.ExpandWidth(false))) {
          Measurement.analytics.Report("settings/save", "Settings Save");
          measurementSettings.Save();
          Close();
      }
      GUILayout.EndHorizontal();
    }

    // Called automatically by Unity when needing to render the menu.
    void OnGUI() {
      // Set up custom GUI styles before rendering.
      GUIStyle oldLabelStyle = GUI.skin.label;
      GUI.skin.label = LabelStyle;

      CurrentMenuState.OnGUI(menuStateStack);

      // Reset the original GUI styles.
      GUI.skin.label = oldLabelStyle;
    }

    static string s_androidProjectId = null;
    static string s_iosProjectId = null;

    static bool IsAndroidConnected() {
      return s_androidProjectId != null;
    }

    static bool IsIosConnected() {
      return s_iosProjectId != null;
    }
  }

}  // namespace Firebase.Editor
