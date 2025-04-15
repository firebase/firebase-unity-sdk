/*
 * Copyright 2025 Google LLC
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
using System.Collections.ObjectModel;
using Google.MiniJSON;
using Firebase.VertexAI.Internal;
using System.Linq;
using System;

namespace Firebase.VertexAI {

/// <summary>
/// Represents the response from the model for live content updates.
/// </summary>
public readonly struct LiveContentResponse {
  /// <summary>
  /// Represents the status of the `LiveContentResponse`.
  /// </summary>
  public enum LiveResponseStatus {
    /// <summary>
    /// Indicates response has no special status associated with it.
    /// </summary>
    Normal,
    /// <summary>
    /// Indicates the model is done generating content.
    /// </summary>
    TurnComplete,
    /// <summary>
    /// Indicates a client message has interrupted the current generation.
    /// </summary>
    Interrupted
  }

  /// <summary>
  /// The main content data of the response. This can be `null` if there is no content.
  /// </summary>
  public ModelContent? Content { get; }

  /// <summary>
  /// The status of the response.
  /// </summary>
  public LiveResponseStatus Status { get; }

  private readonly ReadOnlyCollection<ModelContent.FunctionCallPart> _functionCalls;

  /// <summary>
  /// A list of [FunctionCallPart] included in the response, if any.
  ///
  /// This will be empty if no function calls are present.
  /// </summary>
  public IEnumerable<ModelContent.FunctionCallPart> FunctionCalls =>
      _functionCalls ?? new ReadOnlyCollection<ModelContent.FunctionCallPart>(
          new List<ModelContent.FunctionCallPart>());

  // TODO: Add this
  //public IEnumerable<string> FunctionIdsToCancel { get; }

  /// <summary>
  /// The response's content as text, if it exists.
  /// </summary>
  public string Text {
    get {
      string str = "";
      if (Content != null) {
        foreach (var part in Content?.Parts) {
          if (part is ModelContent.TextPart textPart) {
            str += textPart.Text;
          }
        }
      }
      return str;
    }
  }

  /// <summary>
  /// The response's content that was audio, if it exists.
  /// </summary>
  public IEnumerable<byte[]> Audio {
    get {
      return Content?.Parts
          .OfType<ModelContent.InlineDataPart>()
          .Where(part => part.MimeType == "audio/pcm")
          .Select(part => part.Data.ToArray());
    }
  }

  /// <summary>
  /// The response's content that was audio, if it exists, converted into floats.
  /// </summary>
  public IEnumerable<float[]> AudioAsFloat {
    get {
      return Audio?.Select(ConvertBytesToFloat);
    }
  }

  private float[] ConvertBytesToFloat(byte[] byteArray) {
    // Assumes 16 bit encoding, which would be two bytes per sample.
    int sampleCount = byteArray.Length / 2;
    float[] floatArray = new float[sampleCount];

    for (int i = 0; i < sampleCount; i++) {
      float sample = (short)(byteArray[i * 2] | (byteArray[i * 2 + 1] << 8)) / 32768f;
      floatArray[i] = Math.Clamp(sample, -1f, 1f); // Ensure values are within the valid range
    }

    return floatArray;
  }

  private LiveContentResponse(ModelContent? content, LiveResponseStatus status) {
    Content = content;
    Status = status;
    _functionCalls = new ReadOnlyCollection<ModelContent.FunctionCallPart>(
        new List<ModelContent.FunctionCallPart>());
  }

  private LiveContentResponse(List<ModelContent.FunctionCallPart> functionCalls) {
    Content = null;
    Status = LiveResponseStatus.Normal;
    _functionCalls = new ReadOnlyCollection<ModelContent.FunctionCallPart>(
        functionCalls ?? new List<ModelContent.FunctionCallPart>());
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for deserializing JSON responses and should not be called directly.
  /// </summary>
  internal static LiveContentResponse? FromJson(string jsonString) {
    return FromJson(Json.Deserialize(jsonString) as Dictionary<string, object>);
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for deserializing JSON responses and should not be called directly.
  /// </summary>
  internal static LiveContentResponse? FromJson(Dictionary<string, object> jsonDict) {
    if (jsonDict.ContainsKey("setupComplete")) {
      // We don't want to pass this along to the user, so return null instead.
      return null;
    } else if (jsonDict.TryParseValue("serverContent", out Dictionary<string, object> serverContent)) {
      bool turnComplete = serverContent.ParseValue<bool>("turnComplete");
      bool interrupted = serverContent.ParseValue<bool>("interrupted");
      LiveResponseStatus status = interrupted ? LiveResponseStatus.Interrupted :
          turnComplete ? LiveResponseStatus.TurnComplete : LiveResponseStatus.Normal;
      return new LiveContentResponse(
        serverContent.ParseNullableObject("modelTurn", ModelContent.FromJson),
        status);
    } else if (jsonDict.TryParseValue("toolCall", out Dictionary<string, object> toolCall)) {
      return new LiveContentResponse(
          jsonDict.ParseObjectList("functionCalls", ModelContentJsonParsers.FunctionCallPartFromJson));
    } else {
      // TODO: Do we want to log this, or just ignore it?
      return null;
    }
  }
}

}
