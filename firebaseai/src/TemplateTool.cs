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
using System.Linq;
using System.Threading.Tasks;

namespace Firebase.AI
{
  /// <summary>
  /// A class representing a generic function declaration used with a template model.
  /// </summary>
  public class TemplateFunctionDeclaration
  {
    /// <summary>
    /// The name of the function.
    /// </summary>
    public string Name { get; }

    private readonly Schema _schemaObject;

    /// <summary>
    /// Constructs a TemplateFunctionDeclaration.
    /// </summary>
    /// <param name="name">The name of the function.</param>
    /// <param name="parameters">Optional dictionary of parameters schema.</param>
    /// <param name="optionalParameters">Optional list of parameter names that are not required.</param>
    public TemplateFunctionDeclaration(
        string name, 
        IDictionary<string, Schema> parameters = null, 
        IEnumerable<string> optionalParameters = null)
    {
      Name = name;
      _schemaObject = parameters != null ? Schema.Object(parameters, optionalParameters) : null;
    }

    /// <summary>
    /// Intended for internal use only.
    /// This method is used for serializing the object to JSON for the API request.
    /// </summary>
    internal Dictionary<string, object> ToJson()
    {
      return new Dictionary<string, object>()
      {
        { "name", Name },
        { "input_schema", _schemaObject != null ? _schemaObject.ToJson() : "" }
      };
    }
  }

  /// <summary>
  /// A class representing an auto-function declaration that provides an execution callable for template chat.
  /// </summary>
  public class TemplateAutoFunctionDeclaration : TemplateFunctionDeclaration
  {
    /// <summary>
    /// The callable function that this declaration represents.
    /// </summary>
    public Func<IDictionary<string, object>, Task<IDictionary<string, object>>> Callable { get; }

    /// <summary>
    /// Constructs a TemplateAutoFunctionDeclaration.
    /// </summary>
    /// <param name="name">The name of the function.</param>
    /// <param name="callable">The function to execute when requested by the model.</param>
    /// <param name="parameters">Optional dictionary of parameters schema.</param>
    /// <param name="optionalParameters">Optional list of parameter names that are not required.</param>
    public TemplateAutoFunctionDeclaration(
        string name,
        Func<IDictionary<string, object>, Task<IDictionary<string, object>>> callable,
        IDictionary<string, Schema> parameters = null,
        IEnumerable<string> optionalParameters = null)
        : base(name, parameters, optionalParameters)
    {
      Callable = callable;
    }
  }

  /// <summary>
  /// Describes a set of template tools that can be passed to a <see cref="TemplateChatSession"/>.
  /// </summary>
  public readonly struct TemplateTool
  {
    private readonly List<TemplateFunctionDeclaration> _functionDeclarations;

    /// <summary>
    /// Creates a TemplateTool containing a collection of TemplateFunctionDeclarations.
    /// </summary>
    public static TemplateTool FunctionDeclarations(IEnumerable<TemplateFunctionDeclaration> functionDeclarations)
    {
      return new TemplateTool(functionDeclarations);
    }

    /// <summary>
    /// Creates a TemplateTool containing a collection of TemplateFunctionDeclarations.
    /// </summary>
    public static TemplateTool FunctionDeclarations(params TemplateFunctionDeclaration[] functionDeclarations)
    {
      return new TemplateTool(functionDeclarations);
    }

    private TemplateTool(IEnumerable<TemplateFunctionDeclaration> functionDeclarations)
    {
      _functionDeclarations = functionDeclarations?.ToList() ?? new List<TemplateFunctionDeclaration>();
    }

    /// <summary>
    /// Intended for internal use only.
    /// Returns the subset of TemplateFunctionDeclarations that are TemplateAutoFunctionDeclarations.
    /// </summary>
    internal IEnumerable<TemplateAutoFunctionDeclaration> GetAutoFunctionDeclarations()
    {
      if (_functionDeclarations == null) return Enumerable.Empty<TemplateAutoFunctionDeclaration>();
      return _functionDeclarations.OfType<TemplateAutoFunctionDeclaration>();
    }

    /// <summary>
    /// Intended for internal use only.
    /// This method is used for serializing the object to JSON for the API request.
    /// </summary>
    internal Dictionary<string, object> ToJson()
    {
      var json = new Dictionary<string, object>();
      if (_functionDeclarations != null && _functionDeclarations.Any())
      {
        json["functionDeclarations"] = _functionDeclarations.Select(f => f.ToJson()).ToList();
      }
      return json;
    }
  }

  /// <summary>
  /// Tool configuration for any <see cref="TemplateTool"/> specified in the request.
  /// </summary>
  public class TemplateToolConfig
  {
    /// <summary>
    /// Constructs a new <see cref="TemplateToolConfig"/>.
    /// </summary>
    public TemplateToolConfig()
    {
    }

    /// <summary>
    /// Intended for internal use only.
    /// This method is used for serializing the object to JSON for the API request.
    /// </summary>
    internal Dictionary<string, object> ToJson()
    {
      return new Dictionary<string, object>();
    }
  }
}
