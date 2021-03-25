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
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.AnimatedValues;

namespace Firebase.Editor {

// Helper class to perform common UI layout functions
internal static class FirebaseGUILayout {

  /// <summary>
  /// Display a property field on a serialized object
  /// </summary>
  /// <param name="so">Serialized object to find fields on</param>
  /// <param name="fieldName">Field name on so. Will assert if not valid.</param>
  /// <param name="label">Label to prefix property field with</param>
  /// <param name="tooltip">Tool tip to display on mouse over</param>
  internal static void PropertyField(SerializedObject so, string fieldName,
                                     string label, string tooltip = "") {
    var prop = so.FindProperty(fieldName);
    Debug.Assert(prop != null);

    EditorGUILayout.PropertyField(prop, new GUIContent(label, tooltip), true);
  }

  /// <summary>
  /// Show a combo box based on a array of strings
  /// </summary>
  /// <param name="label">Label to prefix popup with</param>
  /// <param name="selected">Currently selected option</param>
  /// <param name="options">Options to show in order provided</param>
  /// <returns>New selected value</returns>
  internal static string Popup(string label, string selected, string[] options) {
    var selectedIndex = options.ToList().FindIndex(a => a == selected);

    if (selectedIndex < 0 || selectedIndex >= options.Length) {
      selectedIndex = 0;
    }

    selectedIndex = EditorGUILayout.Popup(label, selectedIndex, options);
    return options[selectedIndex];
  }

  /// <summary>
  /// Renders a sub tab segment within a tab
  /// </summary>
  /// <param name="theme">Current theme to use for styles</param>
  /// <param name="name">Segment name</param>
  /// <param name="animBool">Animation bool if the segment should be shown</param>
  /// <param name="renderCallback">Callback to render the actual segment ui elements</param>
  internal static void SubTab(IGUITheme theme, string name,
                              AnimBool animBool, Action renderCallback) {
    GUIStyle bgStyle = new GUIStyle("scrollview");
    bgStyle.margin.left = 5;
    bgStyle.margin.right = 5;
    bgStyle.margin.top = 5;
    bgStyle.margin.bottom = 0;
    bgStyle.normal.background = GetColorTexture(theme.TabBorder);

    GUIStyle bgStyle2 = new GUIStyle("scrollview");
    bgStyle2.margin.left = 1;
    bgStyle2.margin.right = 1;
    bgStyle2.margin.top = 1;
    bgStyle2.margin.bottom = 1;
    bgStyle2.padding.bottom = 5;
    bgStyle2.normal.background = GetColorTexture(theme.TabBackground);

    GUIStyle style = new GUIStyle();
    style.padding.left = 10;
    style.padding.right = 5;
    style.padding.top = 5;
    style.padding.bottom = 5;
    style.normal.textColor = theme.TextNormal;
    style.fontStyle = FontStyle.Bold;

    EditorGUILayout.BeginVertical(bgStyle);
    EditorGUILayout.BeginVertical(bgStyle2);

    if (GUILayout.Button(name + (animBool.value ? "" : " ..."), style) == true) {
      animBool.target = !animBool.value;
    }

    using(var group = new EditorGUILayout.FadeGroupScope(animBool.faded)) {
      if (group.visible) {
        EditorGUI.indentLevel++;
        renderCallback();
        EditorGUI.indentLevel--;
      }
    }

    EditorGUILayout.EndVertical();
    EditorGUILayout.EndVertical();
  }

  /// <summary>
  /// Renders a veritcal icon menu
  /// </summary>
  /// <param name="buttons">List of text/icons to use to render menu</param>
  /// <param name="selectedIndex">Currently selected menu item</param>
  /// <param name="width">Display width of menu</param>
  /// <param name="theme">Current theme to use for styles</param>
  /// <param name="menuScrollPos">Scroll position if menu is too long to show at once</param>
  /// <returns>New selected value</returns>
  internal static int IconMenu(GUIContent[] buttons, int selectedIndex,
                                     int width, IGUITheme theme,
                                     ref Vector2 menuScrollPos) {
    GUIStyle bgStyle = new GUIStyle("scrollview");
    bgStyle.normal.background = GetColorTexture(theme.MenuBackgroundNormal);

    EditorGUILayout.BeginVertical(bgStyle, GUILayout.Width(width));
    EditorGUILayout.Space();

    GUIContent selected = buttons[selectedIndex];
    menuScrollPos =
        EditorGUILayout.BeginScrollView(menuScrollPos, GUILayout.Width(width));

    foreach (var b in buttons) {
      GUIStyle style = new GUIStyle();
      style.padding.left = 10;
      style.padding.top = 5;
      style.padding.bottom = 5;

      GUIStyle bgStyle2 = new GUIStyle("scrollview");

      if (b == selected) {
        bgStyle2.normal.background = GetColorTexture(theme.MenuBackgroundSelected);
        style.normal.textColor = theme.TextSelected;
      } else {
        style.normal.textColor = theme.TextNormal;
      }

      EditorGUILayout.BeginHorizontal(bgStyle2);

      if (GUILayout.Button(b, style)) {
        selected = b;
      }

      EditorGUILayout.EndHorizontal();
    }

    EditorGUILayout.EndScrollView();
    EditorGUILayout.EndVertical();
    return buttons.ToList().FindIndex(0, buttons.Length, a => a == selected);
  }

  /// <summary>
  /// Renders a PopUp menu with icons
  /// </summary>
  /// <param name="buttons">List of text/icons to use to render menu</param>
  /// <param name="selectedIndex">Currently selected menu item</param>
  /// <returns>New selected value</returns>
  internal static int ComboMenu(GUIContent[] buttons, int selectedIndex) {
    EditorGUILayout.BeginVertical();
    EditorGUILayout.Space();
    var selected = EditorGUILayout.Popup(selectedIndex, buttons);
    EditorGUILayout.EndVertical();

    return selected;
  }

  // Color texture cache
  private static Dictionary<Color, Texture2D> textureCache = new Dictionary<Color, Texture2D>();

  // Helper function to get an existing texture for a base color or create one and cache it for
  // next time.
  private static Texture2D GetColorTexture(Color col) {
    Texture2D texture = null;

    if (textureCache.TryGetValue(col, out texture) == true) {
      return texture;
    }

    texture = MakeTexture(1, 1, col);
    textureCache.Add(col, texture);
    return texture;
  }

  // Helper function to create textures for styling
  private static Texture2D MakeTexture(int width, int height, Color col) {
    Color[] pix = new Color[width * height];

    for (int i = 0; i < pix.Length; i++) {
      pix[i] = col;
    }

    Texture2D result = new Texture2D(width, height);
    result.SetPixels(pix);
    result.Apply();

    return result;
  }
}
}
