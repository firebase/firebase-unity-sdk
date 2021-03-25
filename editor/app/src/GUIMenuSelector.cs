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

namespace Firebase.Editor {

// Helper class to keep track of what type of menu should be displayed
internal class GUIMenuSelector {

  /// <summary>
  /// Enum represents the type of menu to show.
  /// </summary>
  internal enum MenuOption {
    ///<summary>Small menu on top of frame.</summary>
    Top,
    ///<summary>Left menu but icon only.</summary>
    LeftIcon,
    ///<summary>Left menu with full text.</summary>
    LeftFull
  }

  private readonly int minWidth;
  private readonly int iconWidth;
  private readonly int iconTextWidth;
  private readonly int deadZoneWidth;

  MenuOption lastMenu = MenuOption.Top;

  internal GUIMenuSelector(int minWidth, int iconWidth, int iconTextWidth, int deadZoneWidth) {
    this.minWidth = minWidth;
    this.iconWidth = iconWidth;
    this.iconTextWidth = iconTextWidth;
    this.deadZoneWidth = deadZoneWidth;
  }

  // Gets what menu variation should be used based on current window width
  internal MenuOption GetMenuOption(float currWidth) {
    var diffFull = currWidth - minWidth;
    var diffIcon = currWidth - minWidth;

    if (lastMenu == MenuOption.LeftFull) {
      diffIcon -= deadZoneWidth;
    }

    if (lastMenu == MenuOption.LeftIcon) {
      diffFull -= deadZoneWidth;
    }

    if (diffFull > iconTextWidth) {
      lastMenu = MenuOption.LeftFull;
    } else if (diffIcon > iconWidth) {
      lastMenu = MenuOption.LeftIcon;
    } else {
      lastMenu = MenuOption.Top;
    }

    return lastMenu;
  }
}

}
