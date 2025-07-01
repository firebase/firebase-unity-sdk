/*
 * Copyright 2024 Google LLC
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

using System.Collections.Generic;

namespace Firebase.Analytics {

/// @brief Event parameter.
///
/// Parameters supply information that contextualize events (see @ref LogEvent).
/// You can associate up to 25 unique Parameters with each event type (name).
///
/// Common event types are provided as static properties of the
/// FirebaseAnalytics class (e.g FirebaseAnalytics.EventPostScore) where
/// parameters of these events are also provided in this FirebaseAnalytics
/// class (e.g FirebaseAnalytics.ParameterScore).
///
/// You are not limited to the set of event types and parameter names
/// suggested in FirebaseAnalytics class properties.  Additional Parameters can
/// be supplied for suggested event types or custom Parameters for custom event
/// types.
///
/// Parameter names must be a combination of letters and digits
/// (matching the regular expression [a-zA-Z0-9]) between 1 and 40 characters
/// long starting with a letter [a-zA-Z] character.  The "firebase_",
/// "google_" and "ga_" prefixes are reserved and should not be used.
///
/// Parameter string values can be up to 100 characters long.
///
/// An array of Parameter class instances can be passed to LogEvent in order
/// to associate parameters's of an event with values where each value can be
/// a double, 64-bit integer or string.
///
/// For example, a game may log an achievement event along with the
/// character the player is using and the level they're currently on:
///
/// @code{.cs}
/// using Firebase.Analytics;
///
/// int currentLevel = GetCurrentLevel();
/// Parameter[] AchievementParameters = {
///   new Parameter(FirebaseAnalytics.ParameterAchievementID,
///                 "ultimate_wizard"),
///   new Parameter(FirebaseAnalytics.ParameterCharacter, "mysterion"),
///   new Parameter(FirebaseAnalytics.ParameterLevel, currentLevel),
/// };
/// FirebaseAnalytics.LogEvent(FirebaseAnalytics.EventLevelUp,
///                            AchievementParameters);
/// @endcode
///
public class Parameter {

  internal string Name { get; set; }

  internal object Value { get; set; }

  public Parameter(string parameterName, string parameterValue) {
    Name = parameterName;
    Value = parameterValue;
  }

  public Parameter(string parameterName, long parameterValue) {
    Name = parameterName;
    Value = parameterValue;
  }

  public Parameter(string parameterName, double parameterValue) {
    Name = parameterName;
    Value = parameterValue;
  }

  public Parameter(string parameterName, IDictionary<string, object> parameterValue) {
    Name = parameterName;
    Value = parameterValue;
  }

  public Parameter(string parameterName, IEnumerable<IDictionary<string, object>> parameterValue) {
    Name = parameterName;
    Value = parameterValue;
  }
}

}
