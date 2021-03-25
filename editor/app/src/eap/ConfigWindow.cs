/*
 * Copyright 2019 Google LLC
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

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Events;

namespace Firebase.Editor {

// This class is the outer window for all the tab pages used to configure Firebase extensions.
// It handles finding the classes, creating instances, drawing the ui and loading/saving the config.
internal class ConfigWindow : EditorWindow {

  private static CategoryLogger logger = new CategoryLogger("ConfigWindow");

  // Redraw event
  private static UnityAction onRepaint;

  // Tab icon look up by tab name
  private static Dictionary<string, string> tabIconNames = new Dictionary<string, string>();

  // Icons look up by icon name
  private static Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();

  // Tab Gui callbacks keyed by sub tab name
  private static Dictionary<string, Dictionary<string, Action<IGUITheme>>> guiCallbacks =
    new Dictionary<string, Dictionary<string, Action<IGUITheme>>>();

  // Config objects instances look up by type
  private static Dictionary<Type, ScriptableObject> configObjects =
    new Dictionary<Type, ScriptableObject>();

  private static readonly int minWindowWidth = 250;
  private static readonly int iconOnlyListWidth = 40;
  private static readonly int iconTextListWidth = 150;
  private static readonly int widthDeadzone = 50;

  private GUIMenuSelector selector = new GUIMenuSelector(minWindowWidth, iconOnlyListWidth,
                                           iconTextListWidth, widthDeadzone);

  // Keeps track of the scroll position for the menu segment
  private Vector2 menuScrollPos = new Vector2();

  // Keeps track of the scroll position for the tab segment
  private Vector2 tabScrollPos = new Vector2();

  // Name of the currently selected tab
  private string selectedTab;

  // Theme used to render tab pages and sub segments
  private IGUITheme theme;

  // TODO: Hide configuration windows until this feature is ready.
#if false
  [MenuItem("Window/Firebase/Configuration")]
  private static void Init() {
    GetWindow<ConfigWindow>("Firebase Configuration");
  }
#endif

  /// <summary>
  /// Registers a new tab to display in the configuration window.
  /// </summary>
  /// <param name="tabName">Tab display name</param>
  /// <param name="icon">Icon name in the editor/firebase folder (no ext)</param>
  /// <param name="onGuiCallback">Callback that is invoked when the tab needs to show UI</param>
  internal static void RegisterTab<T>(string tabName, string icon, Action<T> onGuiCallback = null)
      where T : ScriptableObject {
    logger.LogDebug("Registering tab with name {0} and icon {1} with type {2}.",
            tabName, icon, typeof(T).FullName);

    if (tabIconNames.ContainsKey(tabName) == true) {
      logger.LogWarn("RegisterTab was called twice with the same tab name '{0}'. "
                     + "Skipping registration. Please ensure that RegisterTab is only called once "
                     + "per unique tab name.", tabName);
    }
    else {
      tabIconNames.Add(tabName, icon);
    }

    if (onGuiCallback != null) {
      RegisterSubTab<T>(tabName, "", onGuiCallback);
    }
  }

  /// <summary>
  /// Registers a new sub tab to display within a main tab in the configuration window.
  /// </summary>
  /// <param name="tabName">Tab display name</param>
  /// <param name="subTabName">Sub tab display name</param>
  /// <param name="onGuiCallback">Callback that is invoked when the tab needs to show UI</param>
  internal static void RegisterSubTab<T>(string tabName, string subTabName, Action<T> onGuiCallback)
      where T : ScriptableObject {
    logger.LogDebug("Registering sub tab with name {0} on tab {1} with type {2}.",
                    subTabName, tabName, typeof(T).FullName);

    Dictionary<string, Action<IGUITheme>> callbacks = null;

    if (guiCallbacks.TryGetValue(tabName, out callbacks) == false) {
      callbacks = new Dictionary<string, Action<IGUITheme>>();
      guiCallbacks.Add(tabName, callbacks);
    }

    if (callbacks.ContainsKey(subTabName) == true) {
      logger.LogWarn("RegisterSubTab was called twice with the same sub tab name '{0}' and tab name"
                     + " '{1}'. Skipping registration. Please ensure that RegisterSubTab is only"
                     + " called once per unique sub tab name.", subTabName, tabName);
      return;
    }

    var abool = new AnimBool(true);
    abool.valueChanged.AddListener(onRepaint);

    callbacks.Add(subTabName, delegate(IGUITheme theme){
      FirebaseGUILayout.SubTab(theme, subTabName, abool, delegate() {
        var obj = GetConfigObject<T>();
        onGuiCallback(obj);
      });
    });
  }

  // Gets a existing config object of type T or creates a new one and loads the config for it
  // before returning
  private static T GetConfigObject<T>() where T : ScriptableObject {
    var type = typeof(T);
    ScriptableObject obj = null;

    if (configObjects.TryGetValue(type, out obj) == false) {
      obj = ConfigApi.LoadConfigAsset(type);
      configObjects.Add(type, obj);
    }

    return obj as T;
  }

  // Unity Editor calls this when the window is shown for the first time.
  // Load the theme and setup any thing required for rendering the GUI
  private void OnEnable() {
    if (EditorGUIUtility.isProSkin == true) {
      theme = new GUIDarkTheme();
    } else {
      theme = new GUILightTheme();
    }

    // Clear icons as theme might of changed
    icons.Clear();

    // Load icons for each tab
    foreach (var icon in tabIconNames) {
      icons.Add(icon.Key, theme.LoadImage(icon.Value));
    }

    // Add a null icon for all tab
    icons.Add("All", null);

    // Reset config objects
    configObjects.Clear();

    // Reset current tab
    selectedTab = null;

    // Reset scroll pos
    tabScrollPos = new Vector2();

    // Add the all tab
    if (guiCallbacks.ContainsKey("All") == false) {
      guiCallbacks.Add("All", null);
    }

    onRepaint += base.Repaint;
  }

  // Unity Editor calls when GUI needs to be drawn
  private void OnGUI() {
    Action cleanup = null;

    var keyList = guiCallbacks.Keys.ToList();
    keyList.Sort();

    if (string.IsNullOrEmpty(selectedTab) ||
        keyList.Contains(selectedTab) == false) {
      selectedTab = keyList.First();
    }

    var index = keyList.FindIndex(a => a == selectedTab);

    var buttons =
        keyList.Select(a => new GUIContent(" " + a, icons[a]))
            .ToArray();

    var menuOption = selector.GetMenuOption(position.width);

    if (menuOption == GUIMenuSelector.MenuOption.LeftFull) {
      EditorGUILayout.BeginHorizontal();
      index = FirebaseGUILayout.IconMenu(buttons, index, iconTextListWidth, theme,
                                    ref menuScrollPos);

      cleanup = delegate() { EditorGUILayout.EndHorizontal(); };
    } else if (menuOption == GUIMenuSelector.MenuOption.LeftIcon) {
      Func<GUIContent, GUIContent> getImageButton =
          delegate(GUIContent content) {
        if (content.image != null) {
          return new GUIContent("", content.image, content.text);
        }

        return content;
      };

      var imageButtons = buttons.Select(a => getImageButton(a)).ToArray();

      EditorGUILayout.BeginHorizontal();
      index = FirebaseGUILayout.IconMenu(imageButtons, index, iconOnlyListWidth,
                                    theme, ref menuScrollPos);

      cleanup = delegate() { EditorGUILayout.EndHorizontal(); };
    } else {
      index = FirebaseGUILayout.ComboMenu(buttons, index);
    }

    if (selectedTab != keyList[index]) {
      selectedTab = keyList[index];
      tabScrollPos = new Vector2();
    }

    var style = new GUIStyle("scrollview");
    style.padding.bottom = 10;

    EditorGUILayout.BeginVertical();
    tabScrollPos = EditorGUILayout.BeginScrollView(tabScrollPos, style);

    if (selectedTab == "All") {
      foreach (var key in keyList) {
        if (key == "All") {
          continue;
        }

        foreach (var cb in guiCallbacks[key]) {
          cb.Value(theme);
        }
      }
    }
    else {
      foreach (var cb in guiCallbacks[selectedTab]) {
        cb.Value(theme);
      }
    }

    EditorGUILayout.EndScrollView();

    var horzStyle = new GUIStyle("scrollview");
    horzStyle.padding.bottom = 5;

    EditorGUILayout.BeginHorizontal(horzStyle);
    EditorGUILayout.Space();

    if (GUILayout.Button("Save") == true) {
        Save();
    }

    EditorGUILayout.EndHorizontal();
    EditorGUILayout.EndVertical();

    if (cleanup != null) {
      cleanup();
    }
  }

  // Unity Editor calls this when the window is being destoryed
  private void OnDestroy() {
    Save();
    onRepaint -= base.Repaint;
  }

  // Save out the configuration
  internal void Save() {
    foreach (var obj in configObjects) {
        ConfigApi.SaveConfigAsset(obj.Value);
    }
  }
}

}
