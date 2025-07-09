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
/// Structured representation of a function declaration.
///
/// This `FunctionDeclaration` is a representation of a block of code that can be used
/// as a `Tool` by the model and executed by the client.
/// 
/// Function calling can be used to provide data to the model that was not known at the time it
/// was trained (for example, the current date or weather conditions) or to allow it to interact
/// with external systems (for example, making an API request or querying/updating a database).
/// For more details and use cases, see [Introduction to function
/// calling](https://cloud.google.com/vertex-ai/generative-ai/docs/multimodal/function-calling).
/// </summary>
public readonly struct FunctionDeclaration {
  // No public properties, on purpose since it is meant for user input only

  private string Name { get; }
  private string Description { get; }
  private Schema Parameters { get; }

  /// <summary>
  /// Constructs a new `FunctionDeclaration`.
  /// </summary>
  /// <param name="name">The name of the function; must be a-z, A-Z, 0-9, or contain
  ///   underscores and dashes, with a maximum length of 63.</param>
  /// <param name="description">A brief description of the function.</param>
  /// <param name="parameters">Describes the parameters to this function.</param>
  /// <param name="optionalParameters">The names of parameters that may be omitted by the model
  ///   in function calls; by default, all parameters are considered required.</param>
  public FunctionDeclaration(string name, string description,
      IDictionary<string, Schema> parameters,
      IEnumerable<string> optionalParameters = null) {
    Name = name;
    Description = description;
    Parameters = Schema.Object(parameters, optionalParameters);
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for serializing the object to JSON for the API request.
  /// </summary>
  internal Dictionary<string, object> ToJson() {
    return new() {
      { "name", Name },
      { "description", Description },
      { "parameters", Parameters.ToJson() },
    };
  }
}

/// <summary>
/// A tool that allows the generative model to connect to Google Search to access and incorporate
/// up-to-date information from the web into its responses.
///
/// > Important: When using this feature, you are required to comply with the
/// "Grounding with Google Search" usage requirements for your chosen API provider:
/// [Gemini Developer API](https://ai.google.dev/gemini-api/terms#grounding-with-google-search)
/// or Vertex AI Gemini API (see [Service Terms](https://cloud.google.com/terms/service-terms)
/// section within the Service Specific Terms).
/// </summary>
public readonly struct GoogleSearch {}

/// <summary>
/// A helper tool that the model may use when generating responses.
///
/// A `Tool` is a piece of code that enables the system to interact with external systems to
/// perform an action, or set of actions, outside of knowledge and scope of the model.
/// </summary>
public readonly struct Tool {
  // No public properties, on purpose since it is meant for user input only

  private List<FunctionDeclaration> FunctionDeclarations { get; }
  private GoogleSearch? GoogleSearch { get; }

  /// <summary>
  /// Creates a tool that allows the model to perform function calling.
  /// </summary>
  /// <param name="functionDeclarations">A list of `FunctionDeclarations` available to the model
  ///   that can be used for function calling.</param>
  public Tool(params FunctionDeclaration[] functionDeclarations) {
    FunctionDeclarations = new List<FunctionDeclaration>(functionDeclarations);
    GoogleSearch = null;
  }
  /// <summary>
  /// Creates a tool that allows the model to perform function calling.
  /// </summary>
  /// <param name="functionDeclarations">A list of `FunctionDeclarations` available to the model
  ///   that can be used for function calling.</param>
  public Tool(IEnumerable<FunctionDeclaration> functionDeclarations) {
    FunctionDeclarations = new List<FunctionDeclaration>(functionDeclarations);
    GoogleSearch = null;
  }

  /// <summary>
  /// Creates a tool that allows the model to use Grounding with Google Search.
  /// </summary>
  /// <param name="googleSearch">An empty `GoogleSearch` object. The presence of this object
  ///     in the list of tools enables the model to use Google Search.</param>
  public Tool(GoogleSearch googleSearch) {
    FunctionDeclarations = null;
    GoogleSearch = googleSearch;
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for serializing the object to JSON for the API request.
  /// </summary>
  internal Dictionary<string, object> ToJson() {
    var json = new Dictionary<string, object>();
    if (FunctionDeclarations != null && FunctionDeclarations.Any()) {
      json["functionDeclarations"] = FunctionDeclarations.Select(f => f.ToJson()).ToList();
    }
    if (GoogleSearch.HasValue) {
      json["googleSearch"] = new Dictionary<string, object>();
    }
    return json;
  }
}

/// <summary>
/// Tool configuration for any `Tool` specified in the request.
/// </summary>
public readonly struct ToolConfig {
  // No public properties, on purpose since it is meant for user input only

  private FunctionCallingConfig? Config { get; }

  /// <summary>
  /// Constructs a new `ToolConfig`.
  /// </summary>
  /// <param name="functionCallingConfig">Configures how the model should use the
  ///   provided functions.</param>
  public ToolConfig(FunctionCallingConfig? functionCallingConfig = null) {
    Config = functionCallingConfig;
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for serializing the object to JSON for the API request.
  /// </summary>
  internal Dictionary<string, object> ToJson() {
    var json = new Dictionary<string, object>();
    if (Config.HasValue) {
      json["functionCallingConfig"] = Config?.ToJson();
    }
    return json;
  }
}

/// <summary>
/// Configuration for specifying function calling behavior.
/// </summary>
public readonly struct FunctionCallingConfig {
  // No public properties, on purpose since it is meant for user input only

  private string Mode { get; }
  private List<string> AllowedFunctionNames { get; }

  private FunctionCallingConfig(string mode, IEnumerable<string> allowedFunctionNames = null) {
    Mode = mode;
    if (allowedFunctionNames != null) {
      AllowedFunctionNames = new List<string>(allowedFunctionNames);
    } else {
      AllowedFunctionNames = null;
    }
  }

  /// <summary>
  /// Creates a function calling config where the model calls functions at its discretion.
  ///
  /// > Note: This is the default behavior.
  /// </summary>
  public static FunctionCallingConfig Auto() {
    return new FunctionCallingConfig("AUTO");
  }

  /// <summary>
  /// Creates a function calling config where the model will always call a provided function.
  /// </summary>
  /// <param name="allowedFunctionNames">A set of function names that, when provided, limits the
  ///   function that the model will call.</param>
  public static FunctionCallingConfig Any(params string[] allowedFunctionNames) {
    return new FunctionCallingConfig("ANY", allowedFunctionNames);
  }
  /// <summary>
  /// Creates a function calling config where the model will always call a provided function.
  /// </summary>
  /// <param name="allowedFunctionNames">A set of function names that, when provided, limits the
  ///   function that the model will call.</param>
  public static FunctionCallingConfig Any(IEnumerable<string> allowedFunctionNames) {
    return new FunctionCallingConfig("ANY", allowedFunctionNames);
  }

  /// Creates a function calling config where the model will never call a function.
  ///
  /// > Note: This can also be achieved by not passing any `FunctionDeclaration` tools when
  ///     instantiating the model.
  public static FunctionCallingConfig None() {
    return new FunctionCallingConfig("NONE");
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for serializing the object to JSON for the API request.
  /// </summary>
  internal Dictionary<string, object> ToJson() {
    var json = new Dictionary<string, object>() {
      { "mode", Mode }
    };
    if (AllowedFunctionNames != null) {
      json["allowedFunctionNames"] = AllowedFunctionNames;
    }
    return json;
  }
}

}
