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
  private readonly IReadOnlyList<Part> _parts;

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
  public IReadOnlyList<Part> Parts {
    get {
      return _parts ?? new List<Part>();
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
    _parts = parts?.ToList();
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
      string name, IDictionary<string, object> response, string id = null) {
    return new ModelContent(new FunctionResponsePart(name, response, id));
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
    /// <summary>
    /// Indicates whether this `Part` is a summary of the model's internal thinking process.
    /// 
    /// When `IncludeThoughts` is set to `true` in `ThinkingConfig`, the model may return one or
    /// more "thought" parts that provide insight into how it reasoned through the prompt to arrive
    /// at the final answer. These parts will have `IsThought` set to `true`.
    /// </summary>
    public bool IsThought { get; }

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
    
    private readonly bool? _isThought;
    public bool IsThought { get { return _isThought ?? false; } }
    
    private readonly string _thoughtSignature;

    /// <summary>
    /// Creates a `TextPart` with the given text.
    /// </summary>
    /// <param name="text">The text value to use.</param>
    public TextPart(string text) {
      Text = text;
      _isThought = null;
      _thoughtSignature = null;
    }

    /// <summary>
    /// Intended for internal use only.
    /// </summary>
    internal TextPart(string text, bool? isThought, string thoughtSignature) {
      Text = text;
      _isThought = isThought;
      _thoughtSignature = thoughtSignature;
    }

    Dictionary<string, object> Part.ToJson() {
      var jsonDict = new Dictionary<string, object>() {
        { "text", Text }
      };

      jsonDict.AddIfHasValue("thought", _isThought);
      jsonDict.AddIfHasValue("thoughtSignature", _thoughtSignature);
      return jsonDict;
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
    public byte[] Data { get; }
    
    private readonly bool? _isThought;
    public bool IsThought { get { return _isThought ?? false; } }
    
    private readonly string _thoughtSignature;

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
    public InlineDataPart(string mimeType, byte[] data) {
      MimeType = mimeType;
      Data = data;
      _isThought = null;
      _thoughtSignature = null;
    }

    internal InlineDataPart(string mimeType, byte[] data, bool? isThought, string thoughtSignature) {
      MimeType = mimeType;
      Data = data;
      _isThought = isThought;
      _thoughtSignature = thoughtSignature;
    }

    Dictionary<string, object> Part.ToJson() {
      var jsonDict = new Dictionary<string, object>() {
        { "inlineData", new Dictionary<string, object>() {
            { "mimeType", MimeType },
            { "data", Convert.ToBase64String(Data) }
          }
        }
      };
      jsonDict.AddIfHasValue("thought", _isThought);
      jsonDict.AddIfHasValue("thoughtSignature", _thoughtSignature);
      return jsonDict;
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
    
    // This Part can only come from the user, and thus will never be a thought.
    public bool IsThought { get { return false; } }

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
    /// An identifier that should be passed along in the FunctionResponsePart.
    /// </summary>
    public string Id { get; }
    
    private readonly bool? _isThought;
    public bool IsThought { get { return _isThought ?? false; } }
    
    private readonly string _thoughtSignature;
    
    /// <summary>
    /// Intended for internal use only.
    /// </summary>
    internal FunctionCallPart(string name, IDictionary<string, object> args, string id,
        bool? isThought, string thoughtSignature) {
      Name = name;
      Args = new Dictionary<string, object>(args);
      Id = id;
      _isThought = isThought;
      _thoughtSignature = thoughtSignature;
    }

    Dictionary<string, object> Part.ToJson() {
      var innerDict = new Dictionary<string, object>() {
        { "name", Name },
        { "args", Args }
      };
      innerDict.AddIfHasValue("id", Id);

      var jsonDict = new Dictionary<string, object>() {
        { "functionCall", innerDict }
      };
      jsonDict.AddIfHasValue("thought", _isThought);
      jsonDict.AddIfHasValue("thoughtSignature", _thoughtSignature);
      return jsonDict;
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
    /// The id from the FunctionCallPart this is in response to.
    /// </summary>
    public string Id { get; }
    
    // This Part can only come from the user, and thus will never be a thought.
    public bool IsThought { get { return false; } }

    /// <summary>
    /// Constructs a new `FunctionResponsePart`.
    /// </summary>
    /// <param name="name">The name of the function that was called.</param>
    /// <param name="response">The function's response.</param>
    /// <param name="id">The id from the FunctionCallPart this is in response to.</param>
    public FunctionResponsePart(string name, IDictionary<string, object> response, string id = null) {
      Name = name;
      Response = new Dictionary<string, object>(response);
      Id = id;
    }

    Dictionary<string, object> Part.ToJson() {
      var result = new Dictionary<string, object>() {
        { "name", Name },
        { "response", Response }
      };
      if (!string.IsNullOrEmpty(Id)) {
        result["id"] = Id;
      }
      return new Dictionary<string, object>() {
        { "functionResponse", result }
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
    return new ModelContent(
      // If the role is missing, default to model since this is likely coming from the backend.
      jsonDict.ParseValue("role", defaultValue: "model"),
      // Unknown parts are converted to null, which we then want to filter out here
      jsonDict.ParseObjectList("parts", PartFromJson)?.Where(p => p is not null));
  }

  private static InlineDataPart InlineDataPartFromJson(Dictionary<string, object> jsonDict,
      bool? isThought, string thoughtSignature) {
    return new InlineDataPart(
      jsonDict.ParseValue<string>("mimeType", JsonParseOptions.ThrowEverything),
      Convert.FromBase64String(jsonDict.ParseValue<string>("data", JsonParseOptions.ThrowEverything)),
      isThought,
      thoughtSignature);
  }

  private static Part PartFromJson(Dictionary<string, object> jsonDict) {
    bool? isThought = jsonDict.ParseNullableValue<bool>("thought");
    string thoughtSignature = jsonDict.ParseValue<string>("thoughtSignature");
    if (jsonDict.TryParseValue("text", out string text)) {
      return new TextPart(text, isThought, thoughtSignature);
    } else if (jsonDict.TryParseObject("functionCall",
        innerDict => ModelContentJsonParsers.FunctionCallPartFromJson(innerDict, isThought, thoughtSignature),
        out var fcPart)) {
      return fcPart;
    } else if (jsonDict.TryParseObject("inlineData",
        innerDict => InlineDataPartFromJson(innerDict, isThought, thoughtSignature),
        out var inlineDataPart)) {
      return inlineDataPart;
    } else {
#if FIREBASEAI_DEBUG_LOGGING
      UnityEngine.Debug.LogWarning($"Received unknown part, with keys: {string.Join(',', jsonDict.Keys)}");
#endif
      return null;
    }
  }
}

namespace Internal {

// Class for parsing Parts that need to be called from other files as well.
internal static class ModelContentJsonParsers {
  internal static ModelContent.FunctionCallPart FunctionCallPartFromJson(Dictionary<string, object> jsonDict,
      bool? isThought, string thoughtSignature) {
    return new ModelContent.FunctionCallPart(
      jsonDict.ParseValue<string>("name", JsonParseOptions.ThrowEverything),
      jsonDict.ParseValue<Dictionary<string, object>>("args", JsonParseOptions.ThrowEverything),
      jsonDict.ParseValue<string>("id"),
      isThought,
      thoughtSignature);
  }
}

}

}
