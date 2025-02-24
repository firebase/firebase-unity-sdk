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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Firebase.VertexAI {

/// <summary>
/// A type describing data in media formats interpretable by an AI model. Each generative AI
/// request or response contains a list of `ModelContent`s, and each `ModelContent` value
/// may comprise multiple heterogeneous `ModelContent.Part`s.
/// </summary>
public readonly struct ModelContent {
  private readonly string _role;
  private readonly ReadOnlyCollection<Part> _parts;

  /// <summary>
  /// The role of the entity creating the `ModelContent`. For user-generated client requests,
  /// for example, the role is `user`.
  /// </summary>
  public string Role => string.IsNullOrWhiteSpace(_role) ? "user" : _role;

  /// <summary>
  /// The data parts comprising this `ModelContent` value.
  /// </summary>
  public IEnumerable<Part> Parts => _parts ?? new ReadOnlyCollection<Part>(new List<Part>());

  /// <summary>
  /// Creates a `ModelContent` with the given `Part`s, using the default `user` role.
  /// </summary>
  public ModelContent(params Part[] parts) : this("user", parts) { }

  /// <summary>
  /// Creates a `ModelContent` with the given `Part`s, using the default `user` role.
  /// </summary>
  public ModelContent(IEnumerable<Part> parts) : this("user", parts) { }

  /// <summary>
  /// Creates a `ModelContent` with the given role and `Part`s.
  /// </summary>
  public ModelContent(string role, params Part[] parts) {
    _role = role;
    _parts = new ReadOnlyCollection<Part>(parts.ToList());
  }

  /// <summary>
  /// Creates a `ModelContent` with the given role and `Part`s.
  /// </summary>
  public ModelContent(string role, IEnumerable<Part> parts) {
    _role = role;
    _parts = new ReadOnlyCollection<Part>(parts.ToList());
  }

#region Helper Factories

  /// <summary>
  /// Creates a new `ModelContent` with the default `user` role, and a
  /// `TextPart` containing the given text.
  /// </summary>
  public static ModelContent Text(string text) {
    return new ModelContent(new TextPart(text));
  }

  /// <summary>
  /// Creates a new `ModelContent` with the default `user` role, and an
  /// `InlineDataPart` containing the given mimeType and data.
  /// </summary>
  public static ModelContent InlineData(string mimeType, byte[] data) {
    return new ModelContent(new InlineDataPart(mimeType, data));
  }

  /// <summary>
  /// Creates a new `ModelContent` with the default `user` role, and a
  /// `FileDataPart` containing the given mimeType and data.
  /// </summary>
  public static ModelContent FileData(string mimeType, System.Uri uri) {
    return new ModelContent(new FileDataPart(mimeType, uri));
  }

  /// <summary>
  /// Creates a new `ModelContent` with the default `user` role, and a
  /// `FunctionResponsePart` containing the given name and args.
  /// </summary>
  public static ModelContent FunctionResponse(
      string name, IDictionary<string, object> response) {
    return new ModelContent(new FunctionResponsePart(name, response));
  }

  // TODO: Possibly more, like Multi, Model, FunctionResponses, System (only on Dart?)

  // TODO: Do we want to include helper factories for common C# or Unity types?
#endregion

#region Parts

  /// <summary>
  /// A discrete piece of data in a media format interpretable by an AI model. Within a
  /// single value of `Part`, different data types may not mix.
  /// </summary>
  public interface Part {
#if !DOXYGEN
    /// <summary>
    /// Intended for internal use only.
    /// </summary>
    Dictionary<string, object> ToJson();
#endif
  }

  /// <summary>
  /// A text part containing a string value.
  /// </summary>
  public readonly struct TextPart : Part {
    public string Text { get; }

    public TextPart(string text) { Text = text; }

    Dictionary<string, object> Part.ToJson() {
      return new Dictionary<string, object>() { { "text", Text } };
    }
  }

  /// <summary>
  /// Data with a specified media type.
  /// Note: Not all media types may be supported by the AI model.
  /// </summary>
  public readonly struct InlineDataPart : Part {
    public string Mimetype { get; }
    public ReadOnlyMemory<byte> Data { get; }

    public InlineDataPart(string mimetype, byte[] data) { Mimetype = mimetype; Data = data; }

    Dictionary<string, object> Part.ToJson() {
      throw new NotImplementedException();
    }
  }

  /// <summary>
  /// File data stored in Cloud Storage for Firebase, referenced by a URI.
  /// </summary>
  public readonly struct FileDataPart : Part {
    public string MimeType { get; }
    public System.Uri Uri { get; }

    /// <summary>
    /// Constructs a new file data part.
    /// </summary>
    /// <param name="mimeType">The IANA standard MIME type of the uploaded file, for example, `"image/jpeg"`
    ///     or `"video/mp4"`; see [media requirements
    ///     ](https://cloud.google.com/vertex-ai/generative-ai/docs/multimodal/send-multimodal-prompts#media_requirements)
    ///     for supported values.</param>
    /// <param name="uri">The `"gs://"`-prefixed URI of the file in Cloud Storage for Firebase, for example,
    ///     `"gs://bucket-name/path/image.jpg"`</param>
    public FileDataPart(string mimeType, System.Uri uri) { MimeType = mimeType; Uri = uri; }

    Dictionary<string, object> Part.ToJson() {
      throw new NotImplementedException();
    }
  }

  /// <summary>
  /// A predicted function call returned from the model.
  /// </summary>
  public readonly struct FunctionCallPart : Part {
    /// <summary>
    /// The name of the registered function to call.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// The function parameters and values, matching the registered schema.
    /// </summary>
    public IReadOnlyDictionary<string, object> Args { get; }

    public FunctionCallPart(string name, IDictionary<string, object> args) {
      Name = name;
      Args = new Dictionary<string, object>(args);
    }

    Dictionary<string, object> Part.ToJson() {
      throw new NotImplementedException();
    }
  }

  /// <summary>
  /// Result output from a function call.
  ///
  /// Contains a string representing the `FunctionDeclaration.name` and a structured JSON object
  /// containing any output from the function is used as context to the model. This should contain the
  /// result of a `FunctionCallPart` made based on model prediction.
  /// </summary>
  public readonly struct FunctionResponsePart : Part {
    public string Name { get; }
    public IReadOnlyDictionary<string, object> Response { get; }

    /// <summary>
    /// Constructs a new `FunctionResponsePart`.
    /// </summary>
    /// <param name="name">The name of the function that was called.</param>
    /// <param name="response">The function's response.</param>
    public FunctionResponsePart(string name, IDictionary<string, object> response) {
      Name = name;
      Response = new Dictionary<string, object>(response);
    }

    Dictionary<string, object> Part.ToJson() {
      throw new NotImplementedException();
    }
  }

#endregion

  internal Dictionary<string, object> ToJson() {
    return new Dictionary<string, object>() {
      ["role"] = Role,
      ["parts"] = Parts.Select(p => p.ToJson()).ToList()
    };
  }

  internal static ModelContent FromJson(Dictionary<string, object> jsonDict) {
    // Parse the Role, which is required
    string role;
    if (jsonDict.TryGetValue("role", out object roleObj) && (roleObj is string roleStr)) {
      role = roleStr;
    } else {
      throw new VertexAISerializationException("'content' missing valid 'role' field.");
    }

    // Parse the Parts, which are required
    List<Part> partList = new();
    if (jsonDict.TryGetValue("parts", out object partsObj) && (partsObj is List<object> partsObjList)) {
      partList = partsObjList
          .Select(o => o as Dictionary<string, object>)
          .Where(d => d != null)
          .Select(PartFromJson)
          .ToList();
    } else {
      throw new VertexAISerializationException("'content' missing valid 'parts' field.");
    }

    return new ModelContent(role, partList);
  }

  private static Part PartFromJson(Dictionary<string, object> jsonDict) {
    if (jsonDict.ContainsKey("text") && jsonDict["text"] is string text) {
      return new TextPart(text);
    } else if (jsonDict.ContainsKey("functionCall") && jsonDict["functionCall"] is Dictionary<string, object> functionCallDict) {
      if (!functionCallDict.TryGetValue("name", out var name) || name is not string) {
        throw new VertexAISerializationException("Invalid JSON format: 'name' is not a string.");
      }
      if (!functionCallDict.TryGetValue("args", out var args) || args is not Dictionary<string, object>) {
        throw new VertexAISerializationException("Invalid JSON format: 'args' is not a dictionary.");
      }
      return new FunctionCallPart(name as string, args as Dictionary<string, object>);
    } else {
      throw new VertexAISerializationException("Unable to parse given 'part' into a known Part.");
    }
  }
}

}
