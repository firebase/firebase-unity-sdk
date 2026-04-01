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
        TemplateToolConfig? toolConfig)
    {
      generativeModel = model;
      this.generateContentFunc = generateContentFunc;
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
        TemplateToolConfig? toolConfig)
    {
      return new TemplateChatSession(
        model, generateContentFunc, generateContentStreamFunc, templateId, inputs,
        initialHistory, tools, toolConfig);
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
      return SendMessageAsyncInternal(content, cancellationToken);
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
      return SendMessageAsync(ModelContent.Text(text), cancellationToken);
    }

    private async Task<GenerateContentResponse> SendMessageAsyncInternal(
        ModelContent content, CancellationToken cancellationToken)
    {
      // Make sure that the request is set to to role "user".
      ModelContent fixedRequest = FirebaseAIExtensions.ConvertToUser(content);
      // Set up the context to send in the history and request
      List<ModelContent> fullRequest = new(chatHistory) { fixedRequest };

      // Note: GenerateContentAsync can throw exceptions if there was a problem, but
      // we allow it to just be passed back to the user.
      GenerateContentResponse response = await generateContentFunc(
          templateId, inputs, fullRequest, tools, toolConfig, cancellationToken);

      // Only after getting a valid response, add both to the history for later.
      // But either way pass the response along to the user.
      if (response.Candidates.Any())
      {
        ModelContent responseContent = response.Candidates.First().Content;

        chatHistory.Add(fixedRequest);
        chatHistory.Add(responseContent.ConvertToModel());
      }

      return response;
    }
  }
}
