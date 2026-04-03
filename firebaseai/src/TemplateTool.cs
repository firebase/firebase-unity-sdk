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

using System.Collections.Generic;
using Firebase.AI.Internal;

namespace Firebase.AI
{
  /// <summary>
  /// Tools that are intended to be used by server prompt templates.
  /// </summary>
  public interface ITemplateTool
  {
#if !DOXYGEN
    /// <summary>
    /// Intended for internal use only.
    /// This method is used for serializing the object to JSON for the API request.
    /// </summary>
    Dictionary<string, object> ToJson();
#endif
  }

  /// <summary>
  /// Static class containing the various ITemplateTool's that are supported.
  /// </summary>
  public static class TemplateTool
  {
    /// <summary>
    /// A class representing a generic function declaration used with a template model.
    /// </summary>
    public class FunctionDeclaration : ITemplateTool
    {
      public string Name { get; }
      public JsonSchema JsonParameters { get; }

      /// <summary>
      /// Constructs a TemplateTool.FunctionDeclaration
      /// </summary>
      /// <param name="name">The name of the function.</param>
      /// <param name="parameters">Dictionary of parameters schema.</param>
      /// <param name="optionalParameters">List of parameter names that are not required.</param>
      public FunctionDeclaration(string name,
          IDictionary<string, JsonSchema> parameters,
          IEnumerable<string> optionalParameters = null)
      {
        Name = name;
        JsonParameters = JsonSchema.Object(parameters, optionalParameters);
      }

      /// <summary>
      /// Intended for internal use only.
      /// This method is used for serializing the object to JSON for the API request.
      /// </summary>
      Dictionary<string, object> ITemplateTool.ToJson()
      {
        var jsonDict = new Dictionary<string, object>()
        {
          { "name", Name },
          { "inputSchema", JsonParameters.ToJson() }
        };
        return new Dictionary<string, object>()
        {
          { "templateFunctions", jsonDict }
        };
      }
    }
  }

  /// <summary>
  /// Tool configuration for any TemplateTool specified in the request.
  /// </summary>
  public readonly struct TemplateToolConfig
  {
    // This is intentionally empty, but things will be added in the future.

    /// <summary>
    /// Intended for internal use only.
    /// This method is used for serializing the object to JSON for the API request.
    /// </summary>
    internal IDictionary<string, object> ToJson()
    {
      return new Dictionary<string, object>();
    }
  }
}
