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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Firebase.AI.Internal;

namespace Firebase.AI
{
  // Because of how the SDK is distributed, 'internal' isn't really internal
  // So hide the internal calls from generativeModel that are needed, they are passed in instead
  using GenerateContentFunc = Func<string, IDictionary<string, object>,
      IEnumerable<ModelContent>, IEnumerable<ITemplateTool>,
      TemplateToolConfig?, CancellationToken,
      Task<GenerateContentResponse>>;
  using GenerateContentStreamFunc = Func<string, IDictionary<string, object>,
      IEnumerable<ModelContent>, IEnumerable<ITemplateTool>,
      TemplateToolConfig?, CancellationToken,
      IAsyncEnumerable<GenerateContentResponse>>;

  /// <summary>
  /// An object that represents a back-and-forth chat with a template model, capturing the history
  /// and saving the context in memory between each message sent.
  /// </summary>
  public class TemplateChatSession
  {
    private readonly TemplateGenerativeModel generativeModel;
    private readonly GenerateContentFunc generateContentFunc;
    private readonly GenerateContentStreamFunc generateContentStreamFunc;
    private readonly string templateId;
    private readonly Dictionary<string, object> inputs;
    private readonly List<ModelContent> chatHistory;
    private readonly List<ITemplateTool> tools;
    private readonly TemplateToolConfig? toolConfig;

    private readonly Dictionary<string, BaseAutoFunctionDeclaration> autoFunctionDeclarations;
    private readonly int autoFunctionTurnLimit;

    /// <summary>
    /// The previous content from the chat that has been successfully sent and received from the
    /// model. This will be provided to the model for each message sent as context for the discussion.
    /// </summary>
    public IReadOnlyList<ModelContent> History => chatHistory;

    // Note: No public constructor, get one through GenerativeModel.StartChat
    private TemplateChatSession(
        TemplateGenerativeModel model,
        GenerateContentFunc generateContentFunc,
        GenerateContentStreamFunc generateContentStreamFunc,
        string templateId,
        IDictionary<string, object> inputs,
        IEnumerable<ModelContent> initialHistory,
        IEnumerable<ITemplateTool> tools,
        TemplateToolConfig? toolConfig,
        int autoFunctionTurnLimit)
    {
      generativeModel = model;
      this.generateContentFunc = generateContentFunc;
      this.generateContentStreamFunc = generateContentStreamFunc;
      this.templateId = templateId;
      if (inputs != null)
      {
        this.inputs = new Dictionary<string, object>(inputs);
      }
      else
      {
        this.inputs = new Dictionary<string, object>();
      }
      chatHistory = initialHistory?.ToList() ?? new List<ModelContent>();
      this.tools = tools?.ToList() ?? new List<ITemplateTool>();
      this.toolConfig = toolConfig;

      // Pull out the Automatic Functions
      var autoFunctions = tools?.OfType<BaseAutoFunctionDeclaration>();
      if (autoFunctions != null && autoFunctions.Any())
      {
        autoFunctionDeclarations = autoFunctions.ToDictionary(afd => afd.Name);
      }
      else
      {
        autoFunctionDeclarations = null;
      }
      this.autoFunctionTurnLimit = autoFunctionTurnLimit;
    }

    /// <summary>
    /// Intended for internal use only.
    /// Use `TemplateGenerativeModel.StartChat` instead to ensure proper initialization.
    /// </summary>
    internal static TemplateChatSession InternalCreateChat(
        TemplateGenerativeModel model,
        GenerateContentFunc generateContentFunc,
        GenerateContentStreamFunc generateContentStreamFunc,
        string templateId,
        IDictionary<string, object> inputs,
        IEnumerable<ModelContent> initialHistory,
        IEnumerable<ITemplateTool> tools,
        TemplateToolConfig? toolConfig,
        int autoFunctionTurnLimit)
    {
      return new TemplateChatSession(
        model, generateContentFunc, generateContentStreamFunc, templateId, inputs,
        initialHistory, tools, toolConfig, autoFunctionTurnLimit);
    }

    /// <summary>
    /// Sends a message using the existing history of this chat as context.
    /// </summary>
    /// <param name="content">The input given to the model as a prompt.</param>
    /// <param name="cancellationToken">An optional token to cancel the operation.</param>
    /// <returns>The model's response if no error occurred.</returns>
    public Task<GenerateContentResponse> SendMessageAsync(
        ModelContent content, CancellationToken cancellationToken = default)
    {
      return SendMessageAsyncInternal(new[] { content }, cancellationToken);
    }

    /// <summary>
    /// Sends a message using the existing history of this chat as context.
    /// </summary>
    /// <param name="text">The text given to the model as a prompt.</param>
    /// <param name="cancellationToken">An optional token to cancel the operation.</param>
    /// <returns>The model's response if no error occurred.</returns>
    public Task<GenerateContentResponse> SendMessageAsync(
        string text, CancellationToken cancellationToken = default)
    {
      return SendMessageAsync(new[] { ModelContent.Text(text) }, cancellationToken);
    }

    /// <summary>
    /// Sends a message using the existing history of this chat as context.
    /// </summary>
    /// <param name="content">The input given to the model as a prompt.</param>
    /// <param name="cancellationToken">An optional token to cancel the operation.</param>
    /// <returns>The model's response if no error occurred.</returns>
    public Task<GenerateContentResponse> SendMessageAsync(
        IEnumerable<ModelContent> content, CancellationToken cancellationToken = default)
    {
      return SendMessageAsyncInternal(content, cancellationToken);
    }


    /// <summary>
    /// Sends a message using the existing history of this chat as context.
    /// </summary>
    /// <param name="content">The input given to the model as a prompt.</param>
    /// <param name="cancellationToken">An optional token to cancel the operation.</param>
    /// <returns>A stream of generated content responses from the model.</returns>
    public IAsyncEnumerable<GenerateContentResponse> SendMessageStreamAsync(
        ModelContent content, CancellationToken cancellationToken = default)
    {
      return SendMessageStreamAsync(new[] { content }, cancellationToken);
    }
    /// <summary>
    /// Sends a message using the existing history of this chat as context.
    /// </summary>
    /// <param name="text">The text given to the model as a prompt.</param>
    /// <param name="cancellationToken">An optional token to cancel the operation.</param>
    /// <returns>A stream of generated content responses from the model.</returns>
    public IAsyncEnumerable<GenerateContentResponse> SendMessageStreamAsync(
        string text, CancellationToken cancellationToken = default)
    {
      return SendMessageStreamAsync(new ModelContent[] { ModelContent.Text(text) }, cancellationToken);
    }
    /// <summary>
    /// Sends a message using the existing history of this chat as context.
    /// </summary>
    /// <param name="content">The input given to the model as a prompt.</param>
    /// <param name="cancellationToken">An optional token to cancel the operation.</param>
    /// <returns>A stream of generated content responses from the model.</returns>
    public IAsyncEnumerable<GenerateContentResponse> SendMessageStreamAsync(
        IEnumerable<ModelContent> content, CancellationToken cancellationToken = default)
    {
      return SendMessageStreamAsyncInternal(content, cancellationToken);
    }

    private Task<GenerateContentResponse> SendMessageAsyncInternal(
        IEnumerable<ModelContent> requestContent, CancellationToken cancellationToken)
    {
      Task<GenerateContentResponse> wrappedGenerateContentFunc(List<ModelContent> fullRequest)
      {
        return generateContentFunc(templateId, inputs, fullRequest,
            tools, toolConfig, cancellationToken);
      }

      return ChatSessionHelpers.SendMessageAsync(chatHistory,
          autoFunctionDeclarations, autoFunctionTurnLimit,
          requestContent, wrappedGenerateContentFunc);
    }

    private IAsyncEnumerable<GenerateContentResponse> SendMessageStreamAsyncInternal(
        IEnumerable<ModelContent> requestContent,
        CancellationToken cancellationToken = default)
    {
      IAsyncEnumerable<GenerateContentResponse> wrappedGenerateContentStreamFunc(List<ModelContent> fullRequest)
      {
        return generateContentStreamFunc(templateId, inputs, fullRequest,
            tools, toolConfig, cancellationToken);
      }

      return ChatSessionHelpers.SendMessageStreamAsync(chatHistory,
          autoFunctionDeclarations, autoFunctionTurnLimit,
          requestContent, wrappedGenerateContentStreamFunc);
    }
  }
}
