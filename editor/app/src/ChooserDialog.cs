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
using System;
using System.Collections;
using System.IO;
using System.Xml;
using UnityEditor;
using UnityEngine;

namespace Firebase.Editor {
  /// <summary>
  /// A reusable chooser dialog that provides a choice from an array of strings.
  /// </summary>
  public class ChooserDialog : EditorWindow {
    static string[] options;
    static int selectedOption = 0;
    static string longHelpText;
    static string shortHelpText;
    static string applyButtonText;
    static string cancelButtonText;
    static new string title;
    static Action<string> onClickHandler;

    /// <summary>
    /// Displays a modal chooser dialog centered in Unity.
    /// </summary>
    /// <param name="title">The title of the window.</param>
    /// <param name="longHelpText">A longer description shown inside the window.</param>
    /// <param name="shortHelpText">Text displayed right before the dropdown chooser.</param>
    /// <param name="options">The list of available values to choose from.</param>
    /// <param name="initialSelection">The initial selection.</param>
    /// <param name="applyButtonText">Text to display on the apply button.</param>
    /// <param name="cancelButtonText">Text to display on the cancel button.</param>
    /// <param name="onClickHandler">On click handler called for both apply and cancel.</param>
    public static void Show(string title,
                            string longHelpText,
                            string shortHelpText,
                            string[] options,
                            int initialSelection,
                            string applyButtonText,
                            string cancelButtonText,
                            Action<string> onClickHandler) {
      ChooserDialog.longHelpText = longHelpText;
      ChooserDialog.shortHelpText = shortHelpText;
      ChooserDialog.options = options;
      ChooserDialog.selectedOption = initialSelection;
      ChooserDialog.applyButtonText = applyButtonText;
      ChooserDialog.cancelButtonText = cancelButtonText;
      ChooserDialog.onClickHandler = onClickHandler;
      ChooserDialog.title = title;

      RunOnMainThread.Run(() => { ShowImpl(); }, runNow: false);
    }

    /// <summary>
    /// Displays a modal chooser dialog centered in Unity.
    /// </summary>
    private static void ShowImpl() {

      EditorWindow window = EditorWindow.GetWindow(
        typeof(ChooserDialog), true, title);
      window.minSize = new Vector2(255, 200);
      // This is the heuristic used by the TextAreaDialog.
      window.position = new Rect(UnityEngine.Screen.width / 3, UnityEngine.Screen.height / 3,
                                 window.minSize.x, window.minSize.y);
    }

    /// <summary>
    /// Called when the GUI should be rendered.
    /// </summary>
    public void OnGUI() {
      GUI.skin.label.wordWrap = true;
      GUILayout.BeginVertical();

      GUIStyle link = new GUIStyle(GUI.skin.label);
      link.normal.textColor = new Color(.7f, .7f, 1f);

      GUILayout.Space(10);
      var boldtext = new GUIStyle (GUI.skin.label);
      boldtext.fontStyle = FontStyle.Bold;
      GUILayout.Label(longHelpText, boldtext);
      GUILayout.Space(10);

      GUILayout.Label(shortHelpText);
      GUILayout.Space(5);

      GUILayout.BeginHorizontal();
      GUILayout.Space(15);
      if (options != null) {
        selectedOption = EditorGUILayout.Popup(selectedOption, options);
      }
      GUILayout.Space(5);
      GUILayout.EndHorizontal();

      GUILayout.Space(10);

      GUILayout.FlexibleSpace();
      GUILayout.BeginHorizontal();
      GUILayout.FlexibleSpace();
      if (GUILayout.Button(applyButtonText, GUILayout.Width(100))) {
        if (onClickHandler != null) {
          onClickHandler(options[selectedOption]);
          onClickHandler = null;
        }
        this.Close();
      }
      if (GUILayout.Button(cancelButtonText, GUILayout.Width(100))) {
        if (onClickHandler != null) {
          onClickHandler(null);
          onClickHandler = null;
        }

        this.Close();
      }
      GUILayout.FlexibleSpace();
      GUILayout.EndHorizontal();

      GUILayout.Space(10);
      GUILayout.EndVertical();
    }

    /// <summary>
    /// On close, we'll call the callback just in case something needs to be done.
    /// </summary>
    public void OnDestroy()
    {
        if (onClickHandler != null) {
          onClickHandler(null);
          onClickHandler = null;
        }
    }

    /// <summary>
    /// Modal dialog simulation courtesy of smiles@
    /// This simulates a real modal dialog by keeping focus within the window if the developer
    /// attempts to focus something else.
    /// </summary>
    protected void OnLostFocus()
    {
      Focus();
    }
  }
}
