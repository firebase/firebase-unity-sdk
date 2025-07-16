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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Firebase.AI.Internal;

namespace Firebase.AI {

/// <summary>
/// An object that represents a back-and-forth chat with a model, capturing the history and saving
/// the context in memory between each message sent.
/// </summary>
public class Chat {
  private readonly GenerativeModel generativeModel;
  private readonly List<ModelContent> chatHistory;

  /// <summary>
  /// The previous content from the chat that has been successfully sent and received from the
  /// model. This will be provided to the model for each message sent as context for the discussion.
  /// </summary>
  public IReadOnlyList<ModelContent> History => chatHistory;

  // Note: No public constructor, get one through GenerativeModel.StartChat
  private Chat(GenerativeModel model, IEnumerable<ModelContent> initialHistory) {
    generativeModel = model;

    if (initialHistory != null) {
      chatHistory = new List<ModelContent>(initialHistory);
    } else {
      chatHistory = new List<ModelContent>();
    }
  }

  /// <summary>
  /// Intended for internal use only.
  /// Use `GenerativeModel.StartChat` instead to ensure proper initialization and configuration of the `Chat`.
  /// </summary>
  internal static Chat InternalCreateChat(GenerativeModel model, IEnumerable<ModelContent> initialHistory) {
    return new Chat(model, initialHistory);
  }

  /// <summary>
  /// Sends a message using the existing history of this chat as context. If successful, the message
  /// and response will be added to the history. If unsuccessful, history will remain unchanged.
  /// </summary>
  /// <param name="content">The input given to the model as a prompt.</param>
  /// <param name="cancellationToken">An optional token to cancel the operation.</param>
  /// <returns>The model's response if no error occurred.</returns>
  /// <exception cref="HttpRequestException">Thrown when an error occurs during content generation.</exception>
  public Task<GenerateContentResponse> SendMessageAsync(
      ModelContent content, CancellationToken cancellationToken = default) {
    return SendMessageAsync(new[] { content }, cancellationToken);
  }
  /// <summary>
  /// Sends a message using the existing history of this chat as context. If successful, the message
  /// and response will be added to the history. If unsuccessful, history will remain unchanged.
  /// </summary>
  /// <param name="text">The text given to the model as a prompt.</param>
  /// <param name="cancellationToken">An optional token to cancel the operation.</param>
  /// <returns>The model's response if no error occurred.</returns>
  /// <exception cref="HttpRequestException">Thrown when an error occurs during content generation.</exception>
  public Task<GenerateContentResponse> SendMessageAsync(
      string text, CancellationToken cancellationToken = default) {
    return SendMessageAsync(new ModelContent[] { ModelContent.Text(text) }, cancellationToken);
  }
  /// <summary>
  /// Sends a message using the existing history of this chat as context. If successful, the message
  /// and response will be added to the history. If unsuccessful, history will remain unchanged.
  /// </summary>
  /// <param name="content">The input given to the model as a prompt.</param>
  /// <param name="cancellationToken">An optional token to cancel the operation.</param>
  /// <returns>The model's response if no error occurred.</returns>
  /// <exception cref="HttpRequestException">Thrown when an error occurs during content generation.</exception>
  public Task<GenerateContentResponse> SendMessageAsync(
      IEnumerable<ModelContent> content, CancellationToken cancellationToken = default) {
    return SendMessageAsyncInternal(content, cancellationToken);
  }

  /// <summary>
  /// Sends a message using the existing history of this chat as context. If successful, the message
  /// and response will be added to the history. If unsuccessful, history will remain unchanged.
  /// </summary>
  /// <param name="content">The input given to the model as a prompt.</param>
  /// <param name="cancellationToken">An optional token to cancel the operation.</param>
  /// <returns>A stream of generated content responses from the model.</returns>
  /// <exception cref="HttpRequestException">Thrown when an error occurs during content generation.</exception>
  public IAsyncEnumerable<GenerateContentResponse> SendMessageStreamAsync(
      ModelContent content, CancellationToken cancellationToken = default) {
    return SendMessageStreamAsync(new[] { content }, cancellationToken);
  }
  /// <summary>
  /// Sends a message using the existing history of this chat as context. If successful, the message
  /// and response will be added to the history. If unsuccessful, history will remain unchanged.
  /// </summary>
  /// <param name="text">The text given to the model as a prompt.</param>
  /// <param name="cancellationToken">An optional token to cancel the operation.</param>
  /// <returns>A stream of generated content responses from the model.</returns>
  /// <exception cref="HttpRequestException">Thrown when an error occurs during content generation.</exception>
  public IAsyncEnumerable<GenerateContentResponse> SendMessageStreamAsync(
      string text, CancellationToken cancellationToken = default) {
    return SendMessageStreamAsync(new ModelContent[] { ModelContent.Text(text) }, cancellationToken);
  }
  /// <summary>
  /// Sends a message using the existing history of this chat as context. If successful, the message
  /// and response will be added to the history. If unsuccessful, history will remain unchanged.
  /// </summary>
  /// <param name="content">The input given to the model as a prompt.</param>
  /// <param name="cancellationToken">An optional token to cancel the operation.</param>
  /// <returns>A stream of generated content responses from the model.</returns>
  /// <exception cref="HttpRequestException">Thrown when an error occurs during content generation.</exception>
  public IAsyncEnumerable<GenerateContentResponse> SendMessageStreamAsync(
      IEnumerable<ModelContent> content, CancellationToken cancellationToken = default) {
    return SendMessageStreamAsyncInternal(content, cancellationToken);
  }

  private async Task<GenerateContentResponse> SendMessageAsyncInternal(
      IEnumerable<ModelContent> requestContent, CancellationToken cancellationToken = default) {
    // Make sure that the requests are set to to role "user".
    List<ModelContent> fixedRequests = requestContent.Select(FirebaseAIExtensions.ConvertToUser).ToList();
    // Set up the context to send in the request
    List<ModelContent> fullRequest = new(chatHistory);
    fullRequest.AddRange(fixedRequests);

    // Note: GenerateContentAsync can throw exceptions if there was a problem, but
    // we allow it to just be passed back to the user.
    GenerateContentResponse response = await generativeModel.GenerateContentAsync(fullRequest, cancellationToken);

    // Only after getting a valid response, add both to the history for later.
    // But either way pass the response along to the user.
    if (response.Candidates.Any()) {
      ModelContent responseContent = response.Candidates.First().Content;

      chatHistory.AddRange(fixedRequests);
      chatHistory.Add(responseContent.ConvertToModel());
    }

    return response;
  }

  private async IAsyncEnumerable<GenerateContentResponse> SendMessageStreamAsyncInternal(
      IEnumerable<ModelContent> requestContent,
      [EnumeratorCancellation] CancellationToken cancellationToken = default) {
    // Make sure that the requests are set to to role "user".
    List<ModelContent> fixedRequests = requestContent.Select(FirebaseAIExtensions.ConvertToUser).ToList();
    // Set up the context to send in the request
    List<ModelContent> fullRequest = new(chatHistory);
    fullRequest.AddRange(fixedRequests);

    List<ModelContent> responseContents = new();
    bool saveHistory = true;
    // Note: GenerateContentStreamAsync can throw exceptions if there was a problem, but
    // we allow it to just be passed back to the user.
    await foreach (GenerateContentResponse response in
        generativeModel.GenerateContentStreamAsync(fullRequest, cancellationToken)) {
      // If the response had a problem, we still want to pass it along to the user for context,
      // but we don't want to save the history anymore.
      if (response.Candidates.Any()) {
        ModelContent responseContent = response.Candidates.First().Content;
        responseContents.Add(responseContent.ConvertToModel());
      } else {
        saveHistory = false;
      }

      yield return response;
    }

    // After getting all the responses, and they were all valid, add everything to the history
    if (saveHistory) {
      chatHistory.AddRange(fixedRequests);
      chatHistory.AddRange(responseContents);
    }
  }
}

}
