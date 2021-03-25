/*
 * Copyright 2019 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *    http://www.apache.org/licenses/LICENSE-2.0
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
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// This interface allows GUI rendering code to be able to swap styles based on the active unity
/// editor theme.
/// </summary>
internal interface IGUITheme {
  /// <summary>Load a firebase editor image for this theme based on image name.</summary>
  /// <param name="name">Image file name with out extension and without theme postfix.</param>
  Texture2D LoadImage(string name);

  /// <summary>Color to use for menu background when not selected.</summary>
  Color MenuBackgroundNormal { get; }

  /// <summary>Color to use for menu background when selected.</summary>
  Color MenuBackgroundSelected { get; }

  /// <summary>Color to use for text font when not selected.</summary>
  Color TextNormal { get; }

  /// <summary>Color to use for text font when selected.</summary>
  Color TextSelected { get; }

  /// <summary>Color to use for the border around tab segments.</summary>
  Color TabBorder { get; }

  /// <summary>Color to use for the background of tab segments.</summary>
  Color TabBackground { get; }
}

/// <summary>
/// Unity Editor UI Theme class to use when the user has the normal light theme enabled
/// </summary>
internal class GUILightTheme : IGUITheme {
  public Color MenuBackgroundNormal { get { return new Color32(194, 194, 194, 255); } }

  public Color MenuBackgroundSelected { get { return new Color32(143, 143, 143, 255); } }

  public Color TextNormal { get { return Color.black; } }

  public Color TextSelected { get { return Color.black; } }

  public Color TabBorder { get { return new Color32(153, 153, 153, 255); } }

  public Color TabBackground { get { return new Color32(222, 222, 222, 255); } }

  public Texture2D LoadImage(string name) {
    return (Texture2D) EditorGUIUtility.Load("Firebase/" + name + ".png");
  }
}

/// <summary>
/// Unity Editor UI Theme class to use when the user has the Pro Dark theme enabled
/// </summary>
internal class GUIDarkTheme : IGUITheme {
  public Color MenuBackgroundNormal { get { return new Color32(63, 63, 63, 255); } }

  public Color MenuBackgroundSelected { get { return new Color32(62, 95, 150, 255); } }

  public Color TextNormal { get { return new Color32(159, 159, 159, 255); } }

  public Color TextSelected { get { return Color.white; } }

  public Color TabBorder { get { return Color.black; } }

  public Color TabBackground { get { return new Color32(56, 56, 56, 255); } }

  public Texture2D LoadImage(string name) {
    return (Texture2D) EditorGUIUtility.Load("Firebase/" + name + "_dark.png");
  }
}
