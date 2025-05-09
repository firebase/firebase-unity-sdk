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
using Firebase.AI.Internal;

namespace Firebase.AI {

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
  public string Role {
    get {
      return string.IsNullOrWhiteSpace(_role) ? "user" : _role;
    }
  }

  /// <summary>
  /// The data parts comprising this `ModelContent` value.
  /// </summary>
  public IEnumerable<Part> Parts {
    get {
      return _parts ?? new ReadOnlyCollection<Part>(new List<Part>());
    }
  }

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
  public ModelContent(string role, params Part[] parts) : this(role, (IEnumerable<Part>)parts) { }

  /// <summary>
  /// Creates a `ModelContent` with the given role and `Part`s.
  /// </summary>
  public ModelContent(string role, IEnumerable<Part> parts) {
    _role = role;
    _parts = new ReadOnlyCollection<Part>(parts == null ? new List<Part>() : parts.ToList());
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
    /// This method is used for serializing the object to JSON for the API request.
    /// </summary>
    Dictionary<string, object> ToJson();
#endif
  }

  /// <summary>
  /// A text part containing a string value.
  /// </summary>
  public readonly struct TextPart : Part {
    /// <summary>
    /// Text value.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Creates a `TextPart` with the given text.
    /// </summary>
    /// <param name="text">The text value to use.</param>
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
    /// <summary>
    /// The IANA standard MIME type of the data.
    /// </summary>
    public string MimeType { get; }
    /// <summary>
    /// The data provided in the inline data part.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>
    /// Creates an `InlineDataPart` from data and a MIME type.
    ///
    /// > Important: Supported input types depend on the model on the model being used; see [input
    ///  files and requirements](https://firebase.google.com/docs/vertex-ai/input-file-requirements)
    ///  for more details.
    /// </summary>
    /// <param name="mimeType">The IANA standard MIME type of the data, for example, `"image/jpeg"` or
    ///     `"video/mp4"`; see [input files and
    ///     requirements](https://firebase.google.com/docs/vertex-ai/input-file-requirements) for
    ///     supported values.</param>
    /// <param name="data">The data representation of an image, video, audio or document; see [input files and
    ///     requirements](https://firebase.google.com/docs/vertex-ai/input-file-requirements) for
    ///     supported media types.</param>
    public InlineDataPart(string mimeType, byte[] data) { MimeType = mimeType; Data = data; }

    Dictionary<string, object> Part.ToJson() {
      return new Dictionary<string, object>() {
        { "inlineData", new Dictionary<string, object>() {
            { "mimeType", MimeType },
            { "data", Convert.ToBase64String(Data.ToArray()) }
          }
        }
      };
    }
  }

  /// <summary>
  /// File data stored in Cloud Storage for Firebase, referenced by a URI.
  /// </summary>
  public readonly struct FileDataPart : Part {
    /// <summary>
    /// The IANA standard MIME type of the data.
    /// </summary>
    public string MimeType { get; }
    /// <summary>
    /// The URI of the file.
    /// </summary>
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
      return new Dictionary<string, object>() {
        { "fileData", new Dictionary<string, object>() {
            { "mimeType", MimeType },
            { "fileUri", Uri.AbsoluteUri }
          }
        }
      };
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
    
    /// <summary>
    /// Intended for internal use only.
    /// </summary>
    internal FunctionCallPart(string name, IDictionary<string, object> args) {
      Name = name;
      Args = new Dictionary<string, object>(args);
    }

    Dictionary<string, object> Part.ToJson() {
      return new Dictionary<string, object>() {
        { "functionCall", new Dictionary<string, object>() {
            { "name", Name },
            { "args", Args }
          }
        }
      };
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
    /// <summary>
    /// The name of the function that was called.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// The function's response or return value.
    /// </summary>
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
      return new Dictionary<string, object>() {
        { "functionResponse", new Dictionary<string, object>() {
            { "name", Name },
            { "response", Response }
          }
        }
      };
    }
  }

#endregion

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for serializing the object to JSON for the API request.
  /// </summary>
  internal Dictionary<string, object> ToJson() {
    return new Dictionary<string, object>() {
      ["role"] = Role,
      ["parts"] = Parts.Select(p => p.ToJson()).ToList()
    };
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for deserializing JSON responses and should not be called directly.
  /// </summary>
  internal static ModelContent FromJson(Dictionary<string, object> jsonDict) {
    // Both role and parts are required keys
    return new ModelContent(
      jsonDict.ParseValue<string>("role", JsonParseOptions.ThrowEverything),
      jsonDict.ParseObjectList("parts", PartFromJson, JsonParseOptions.ThrowEverything));
  }

  private static InlineDataPart InlineDataPartFromJson(Dictionary<string, object> jsonDict) {
    return new InlineDataPart(
      jsonDict.ParseValue<string>("mimeType", JsonParseOptions.ThrowEverything),
      Convert.FromBase64String(jsonDict.ParseValue<string>("data", JsonParseOptions.ThrowEverything)));
  }

  private static Part PartFromJson(Dictionary<string, object> jsonDict) {
    if (jsonDict.TryParseValue("text", out string text)) {
      return new TextPart(text);
    } else if (jsonDict.TryParseObject("functionCall", ModelContentJsonParsers.FunctionCallPartFromJson,
                                       out var fcPart)) {
      return fcPart;
    } else if (jsonDict.TryParseObject("inlineData", InlineDataPartFromJson,
                                       out var inlineDataPart)) {
      return inlineDataPart;
    } else {
      throw new NotSupportedException("Unable to parse given 'part' into a known Part.");
    }
  }
}

namespace Internal {

// Class for parsing Parts that need to be called from other files as well.
internal static class ModelContentJsonParsers {
  internal static ModelContent.FunctionCallPart FunctionCallPartFromJson(Dictionary<string, object> jsonDict) {
    return new ModelContent.FunctionCallPart(
      jsonDict.ParseValue<string>("name", JsonParseOptions.ThrowEverything),
      jsonDict.ParseValue<Dictionary<string, object>>("args", JsonParseOptions.ThrowEverything));
  }
}

}

}
