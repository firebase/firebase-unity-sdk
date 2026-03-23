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
  /// <summary>
  /// An object that represents a back-and-forth chat with a template model, capturing the history
  /// and saving the context in memory between each message sent.
  /// </summary>
  public class TemplateChatSession
  {
    private readonly TemplateGenerativeModel _generativeModel;
    private readonly string _templateId;
    private readonly IDictionary<string, object> _inputs;
    private readonly List<ModelContent> _chatHistory;
    private readonly List<TemplateTool> _tools;
    private readonly TemplateToolConfig _toolConfig;
    private readonly Dictionary<string, TemplateAutoFunctionDeclaration> _autoFunctions;
    private readonly int _maxTurns;

    // Use a SemaphoreSlim as a mutex lock.
    private readonly SemaphoreSlim _mutex = new SemaphoreSlim(1, 1);

    /// <summary>
    /// The previous content from the chat that has been successfully sent and received from the
    /// model. This will be provided to the model for each message sent as context for the discussion.
    /// </summary>
    public IReadOnlyList<ModelContent> History => _chatHistory;

    // Note: No public constructor, get one through TemplateGenerativeModel.StartChat
    private TemplateChatSession(
        TemplateGenerativeModel model, 
        string templateId,
        IDictionary<string, object> inputs,
        IEnumerable<ModelContent> initialHistory, 
        IEnumerable<TemplateTool> tools,
        TemplateToolConfig toolConfig,
        int maxTurns)
    {
      _generativeModel = model;
      _templateId = templateId;
      _inputs = inputs ?? new Dictionary<string, object>();
      _tools = tools?.ToList() ?? new List<TemplateTool>();
      _toolConfig = toolConfig;
      _maxTurns = maxTurns;

      if (initialHistory != null)
      {
        _chatHistory = new List<ModelContent>(initialHistory);
      }
      else
      {
        _chatHistory = new List<ModelContent>();
      }

      _autoFunctions = new Dictionary<string, TemplateAutoFunctionDeclaration>();
      if (tools != null)
      {
        foreach (var tool in tools)
        {
          foreach (var function in tool.GetAutoFunctionDeclarations())
          {
             _autoFunctions[function.Name] = function;
          }
        }
      }
    }

    /// <summary>
    /// Intended for internal use only.
    /// Use `TemplateGenerativeModel.StartChat` instead to ensure proper initialization.
    /// </summary>
    internal static TemplateChatSession InternalCreateChat(
        TemplateGenerativeModel model, 
        string templateId, 
        IDictionary<string, object> inputs, 
        IEnumerable<ModelContent> initialHistory, 
        IEnumerable<TemplateTool> tools, 
        TemplateToolConfig toolConfig,
        int maxTurns)
    {
      return new TemplateChatSession(model, templateId, inputs, initialHistory, tools, toolConfig, maxTurns);
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

    /// <summary>
    /// Sends a message using the existing history of this chat as context.
    /// </summary>
    /// <param name="content">The input given to the model as a prompt.</param>
    /// <param name="cancellationToken">An optional token to cancel the operation.</param>
    /// <returns>A stream of generated content responses from the model.</returns>
    public IAsyncEnumerable<GenerateContentResponse> SendMessageStreamAsync(
        ModelContent content, CancellationToken cancellationToken = default)
    {
      return SendMessageStreamAsyncInternal(content, cancellationToken);
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
      return SendMessageStreamAsync(ModelContent.Text(text), cancellationToken);
    }

    private async Task<GenerateContentResponse> SendMessageAsyncInternal(
        ModelContent requestContent, CancellationToken cancellationToken = default)
    {
      await _mutex.WaitAsync(cancellationToken);
      try
      {
        ModelContent message = FirebaseAIExtensions.ConvertToUser(requestContent);
        List<ModelContent> requestHistory = new List<ModelContent> { message };
        
        int turn = 0;
        while (turn < _maxTurns)
        {
          List<ModelContent> fullRequest = new List<ModelContent>(_chatHistory);
          fullRequest.AddRange(requestHistory);

          var response = await _generativeModel.GenerateContentAsyncInternal(
              _templateId, _inputs, fullRequest, _tools, _toolConfig, cancellationToken);

          if (!response.Candidates.Any())
          {
            return response;
          }

          var candidate = response.Candidates.First();
          var parts = candidate.Content.Parts;
          var functionCalls = parts.OfType<ModelContent.FunctionCallPart>().ToList();

          bool shouldAutoExecute = _autoFunctions.Count > 0 && functionCalls.Count > 0 && 
              functionCalls.All(c => _autoFunctions.ContainsKey(c.Name));

          if (!shouldAutoExecute)
          {
            _chatHistory.Add(message);
            _chatHistory.Add(candidate.Content.ConvertToModel());
            return response;
          }

          // Auto function execution
          requestHistory.Add(candidate.Content.ConvertToModel());
          var functionResponses = new List<ModelContent.Part>();
          
          foreach (var call in functionCalls)
          {
             var function = _autoFunctions[call.Name];
             object result;
             try
             {
               if (function.Callable != null) 
               {
                 result = await function.Callable(call.Args.ToDictionary(k => k.Key, k => k.Value));
               }
               else 
               {
                 result = null;
               }
             }
             catch (Exception ex)
             {
               result = ex.Message;
             }
             
             // Wrap the result in a {"result": ...} dictionary as expected by the Dart implementation
             functionResponses.Add(new ModelContent.FunctionResponsePart(
                 call.Name, 
                 new Dictionary<string, object> { { "result", result } }, 
                 call.Id));
          }
          
          requestHistory.Add(new ModelContent("function", functionResponses));
          turn++;
        }
        
        throw new InvalidOperationException($"Max turns of {_maxTurns} reached.");
      }
      finally
      {
        _mutex.Release();
      }
    }

    private async IAsyncEnumerable<GenerateContentResponse> SendMessageStreamAsyncInternal(
        ModelContent requestContent,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      await _mutex.WaitAsync(cancellationToken);
      try
      {
        ModelContent message = FirebaseAIExtensions.ConvertToUser(requestContent);
        List<ModelContent> fullRequest = new List<ModelContent>(_chatHistory) { message };
        
        List<ModelContent> responseContents = new List<ModelContent>();
        bool saveHistory = true;

        await foreach (GenerateContentResponse response in
            _generativeModel.GenerateContentStreamAsyncInternal(_templateId, _inputs, fullRequest, _tools, _toolConfig, cancellationToken))
        {
          if (response.Candidates.Any())
          {
            ModelContent responseContent = response.Candidates.First().Content;
            responseContents.Add(responseContent.ConvertToModel());
          }
          else
          {
            saveHistory = false;
          }

          yield return response;
        }

        if (saveHistory && responseContents.Count > 0)
        {
          _chatHistory.Add(message);
          _chatHistory.AddRange(responseContents);
        }
      }
      finally
      {
        _mutex.Release();
      }
    }
  }
}
