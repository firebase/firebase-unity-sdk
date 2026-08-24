/*
 * Copyright 2026 Google LLC
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
using Firebase.AI.Internal;

namespace Firebase.AI
{
  /// <summary>
  /// Configures model input behavior when generating content in the Live API via the realtime supported methods
  /// </summary>
  public readonly struct RealtimeInputConfig
  {
    private readonly ActivityDetectionConfig? _automaticActivityDetection;
    private readonly ActivityHandling? _activityHandling;
    private readonly TurnCoverage? _turnCoverage;

    /// <summary>
    /// How a model handles user input activity.
    /// </summary>
    public enum ActivityHandling
    {
      /// <summary>
      /// When the user sends input marking the start of activity, the model's current response will be cut-off immediately.
      /// </summary>
      Interrupt,
      /// <summary>
      /// When the user sends input marking the start of activity, the model will process it, but won't cut-off its current response.
      /// </summary>
      NoInterrupt
    }

    /// <summary>
    /// How the model considers which input is included in the user's turn.
    /// </summary>
    public enum TurnCoverage
    {
      /// <summary>
      /// The model will exclude inactivity (e.g, silence on the audio stream) from the user's input.
      /// </summary>
      OnlyActivity,
      /// <summary>
      /// The model will include all input (including inactivity) since the last turn as the user's input.
      /// </summary>
      AllInput,
      /// <summary>
      /// Includes audio activity and all video since the last turn. With automatic
      /// activity detection, audio activity means speech and excludes silence.
      /// </summary>
      AudioActivityAndAllVideo
    }

    /// <summary>
    /// Configures automatic activity detection on the model.
    /// </summary>
    public ActivityDetectionConfig? AutomaticActivityDetection => _automaticActivityDetection;

    /// <summary>
    /// Defines how the model treats user input activity.
    /// </summary>
    public ActivityHandling? Handling => _activityHandling;

    /// <summary>
    /// Defines which input is included in the user's turn.
    /// </summary>
    public TurnCoverage? Coverage => _turnCoverage;

    /// <summary>
    /// Creates a new `RealtimeInputConfig` value.
    /// </summary>
    /// <param name="automaticActivityDetection">Configures automatic activity detection on the model.</param>
    /// <param name="activityHandling">Defines how the model treats user input activity.</param>
    /// <param name="turnCoverage">Defines which input is included in the user's turn.</param>
    public RealtimeInputConfig(
      ActivityDetectionConfig? automaticActivityDetection = null,
      ActivityHandling? activityHandling = null,
      TurnCoverage? turnCoverage = null)
    {
      _automaticActivityDetection = automaticActivityDetection;
      _activityHandling = activityHandling;
      _turnCoverage = turnCoverage;
    }

    internal Dictionary<string, object> ToJson()
    {
      var dict = new Dictionary<string, object>();
      if (_automaticActivityDetection.HasValue)
      {
        dict["automaticActivityDetection"] = _automaticActivityDetection.Value.ToJson();
      }
      if (_activityHandling.HasValue)
      {
        dict["activityHandling"] = _activityHandling.Value switch
        {
          ActivityHandling.Interrupt => "START_OF_ACTIVITY_INTERRUPTS",
          ActivityHandling.NoInterrupt => "NO_INTERRUPTION",
          _ => "ACTIVITY_HANDLING_UNSPECIFIED"
        };
      }
      if (_turnCoverage.HasValue)
      {
        dict["turnCoverage"] = _turnCoverage.Value switch
        {
          TurnCoverage.OnlyActivity => "TURN_INCLUDES_ONLY_ACTIVITY",
          TurnCoverage.AllInput => "TURN_INCLUDES_ALL_INPUT",
          TurnCoverage.AudioActivityAndAllVideo => "TURN_INCLUDES_AUDIO_ACTIVITY_AND_ALL_VIDEO",
          _ => "TURN_COVERAGE_UNSPECIFIED"
        };
      }
      return dict;
    }
  }

  /// <summary>
  /// Configures the model's automatic detection of user activity.
  /// </summary>
  public readonly struct ActivityDetectionConfig
  {
    private readonly Sensitivity? _startSensitivity;
    private readonly Sensitivity? _endSensitivity;
    private readonly int? _prefixPaddingMS;
    private readonly int? _silenceDurationMS;
    private readonly bool? _disabled;

    /// <summary>
    /// Determines how likely the start of speech is detected.
    /// </summary>
    public Sensitivity? StartSensitivity => _startSensitivity;

    /// <summary>
    /// Determines how likely the end of speech is detected.
    /// </summary>
    public Sensitivity? EndSensitivity => _endSensitivity;

    /// <summary>
    /// How long detected speech should be present before start-of-speech is committed.
    /// </summary>
    public TimeSpan? PrefixPadding => _prefixPaddingMS.HasValue ? TimeSpan.FromMilliseconds(_prefixPaddingMS.Value) : null;

    /// <summary>
    /// How long silence should be present before end-of-speech is committed.
    /// </summary>
    public TimeSpan? SilenceDuration => _silenceDurationMS.HasValue ? TimeSpan.FromMilliseconds(_silenceDurationMS.Value) : null;

    /// <summary>
    /// Prefix padding in milliseconds.
    /// </summary>
    public int? PrefixPaddingMS => _prefixPaddingMS;

    /// <summary>
    /// Silence duration in milliseconds.
    /// </summary>
    public int? SilenceDurationMS => _silenceDurationMS;

    /// <summary>
    /// Whether automatic activity detection is disabled.
    /// </summary>
    public bool? IsDisabled => _disabled;

    /// <summary>
    /// How sensitive the model interprets speech activity.
    /// </summary>
    public enum Sensitivity
    {
      /// <summary>
      /// The model will detect speech less often.
      /// </summary>
      Low,
      /// <summary>
      /// The model will detect speech more often.
      /// </summary>
      High
    }

    private ActivityDetectionConfig(Sensitivity? start, Sensitivity? end, int? prefix, int? silence, bool? disabled)
    {
      if (prefix < 0)
      {
        throw new System.ArgumentOutOfRangeException(nameof(prefix), "Prefix padding must be non-negative.");
      }
      if (silence < 0)
      {
        throw new System.ArgumentOutOfRangeException(nameof(silence), "Silence duration must be non-negative.");
      }

      _startSensitivity = start;
      _endSensitivity = end;
      _prefixPaddingMS = prefix;
      _silenceDurationMS = silence;
      _disabled = disabled;
    }

    private static int? ConvertTimeSpanToMs(TimeSpan? timeSpan, string paramName)
    {
      if (!timeSpan.HasValue) return null;
      double ms = timeSpan.Value.TotalMilliseconds;
      if (ms < 0 || ms > int.MaxValue)
      {
        throw new System.ArgumentOutOfRangeException(paramName, $"{paramName} must be between 0 and {int.MaxValue} milliseconds.");
      }
      return (int)ms;
    }

    /// <summary>
    /// Creates a new `ActivityDetectionConfig` specifying durations in milliseconds.
    /// </summary>
    /// <param name="startSensitivity">Determines how likely the start of speech is detected.</param>
    /// <param name="endSensitivity">Determines how likely the end of speech is detected.</param>
    /// <param name="prefixPaddingMS">How long detected speech should be present before start-of-speech is committed (in milliseconds).</param>
    /// <param name="silenceDurationMS">How long silence should be present before end-of-speech is committed (in milliseconds).</param>
    public ActivityDetectionConfig(
      Sensitivity? startSensitivity = null,
      Sensitivity? endSensitivity = null,
      int? prefixPaddingMS = null,
      int? silenceDurationMS = null)
      : this(startSensitivity, endSensitivity, prefixPaddingMS, silenceDurationMS, null) { }

    /// <summary>
    /// Creates a new `ActivityDetectionConfig` specifying durations as `TimeSpan`.
    /// </summary>
    /// <param name="startSensitivity">Determines how likely the start of speech is detected.</param>
    /// <param name="endSensitivity">Determines how likely the end of speech is detected.</param>
    /// <param name="prefixPadding">How long detected speech should be present before start-of-speech is committed.</param>
    /// <param name="silenceDuration">How long silence should be present before end-of-speech is committed.</param>
    public ActivityDetectionConfig(
      Sensitivity? startSensitivity,
      Sensitivity? endSensitivity,
      TimeSpan? prefixPadding,
      TimeSpan? silenceDuration = null)
      : this(
          startSensitivity,
          endSensitivity,
          ConvertTimeSpanToMs(prefixPadding, nameof(prefixPadding)),
          ConvertTimeSpanToMs(silenceDuration, nameof(silenceDuration)),
          null) { }

    /// <summary>
    /// Creates a new `ActivityDetectionConfig` specifying durations as `TimeSpan`.
    /// </summary>
    /// <param name="prefixPadding">How long detected speech should be present before start-of-speech is committed.</param>
    /// <param name="silenceDuration">How long silence should be present before end-of-speech is committed.</param>
    /// <param name="startSensitivity">Determines how likely the start of speech is detected.</param>
    /// <param name="endSensitivity">Determines how likely the end of speech is detected.</param>
    public ActivityDetectionConfig(
      TimeSpan prefixPadding,
      TimeSpan? silenceDuration = null,
      Sensitivity? startSensitivity = null,
      Sensitivity? endSensitivity = null)
      : this(
          startSensitivity,
          endSensitivity,
          ConvertTimeSpanToMs(prefixPadding, nameof(prefixPadding)),
          ConvertTimeSpanToMs(silenceDuration, nameof(silenceDuration)),
          null) { }

    /// <summary>
    /// Disables automatic activity detection.
    /// </summary>
    public static ActivityDetectionConfig Disabled() => new ActivityDetectionConfig(null, null, null, null, true);

    internal Dictionary<string, object> ToJson()
    {
      var dict = new Dictionary<string, object>();
      if (_startSensitivity.HasValue)
      {
        dict["startOfSpeechSensitivity"] = _startSensitivity.Value switch
        {
          Sensitivity.Low => "START_SENSITIVITY_LOW",
          Sensitivity.High => "START_SENSITIVITY_HIGH",
          _ => "START_SENSITIVITY_UNSPECIFIED"
        };
      }
      if (_endSensitivity.HasValue)
      {
        dict["endOfSpeechSensitivity"] = _endSensitivity.Value switch
        {
          Sensitivity.Low => "END_SENSITIVITY_LOW",
          Sensitivity.High => "END_SENSITIVITY_HIGH",
          _ => "END_SENSITIVITY_UNSPECIFIED"
        };
      }
      if (_prefixPaddingMS.HasValue)
      {
        dict["prefixPaddingMs"] = _prefixPaddingMS.Value;
      }
      if (_silenceDurationMS.HasValue)
      {
        dict["silenceDurationMs"] = _silenceDurationMS.Value;
      }
      if (_disabled.HasValue && _disabled.Value)
      {
        dict["disabled"] = true;
      }
      return dict;
    }
  }
}
