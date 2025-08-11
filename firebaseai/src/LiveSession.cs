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
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.MiniJSON;

namespace Firebase.AI {

/// <summary>
/// Manages asynchronous communication with Gemini model over a WebSocket
/// connection.
/// </summary>
public class LiveSession : IDisposable {

  private readonly ClientWebSocket _clientWebSocket;

  private readonly SemaphoreSlim _sendLock = new(1, 1);

  private bool _disposed = false;
  private readonly object _disposeLock = new();

  /// <summary>
  /// Intended for internal use only.
  /// Use `LiveGenerativeModel.ConnectAsync` instead to ensure proper initialization.
  /// </summary>
  internal LiveSession(ClientWebSocket clientWebSocket) {
    if (clientWebSocket.State != WebSocketState.Open) {
      throw new InvalidOperationException(
        $"ClientWebSocket failed to connect, can't create LiveSession. Current state: {clientWebSocket.State}");
    }

    _clientWebSocket = clientWebSocket;
  }

  protected virtual void Dispose(bool disposing) {
    lock (_disposeLock) {
      if (!_disposed) {
        if (_clientWebSocket.State == WebSocketState.Open) {
          _clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "LiveSession disposed", CancellationToken.None);
        }

        _disposed = true;
      }
    }
  }

  public void Dispose() {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  ~LiveSession() {
    Dispose(false);
  }

  private async Task InternalSendBytesAsync(
      ArraySegment<byte> bytes,
      CancellationToken cancellationToken) {
    // WebSockets should only have a single Send active at once, so lock around it.
    await _sendLock.WaitAsync(cancellationToken);
    try {
      cancellationToken.ThrowIfCancellationRequested();

      if (_clientWebSocket.State != WebSocketState.Open) {
        throw new InvalidOperationException("WebSocket is not open, cannot send message.");
      }

      await _clientWebSocket.SendAsync(bytes,
          WebSocketMessageType.Binary, true,
          cancellationToken);
    } finally {
      _sendLock.Release();
    }
  }

  /// <summary>
  /// Sends a single piece of content to the server.
  /// </summary>
  /// <param name="content">The content to send.</param>
  /// <param name="turnComplete">Indicates to the server that the client's turn is complete.</param>
  /// <param name="cancellationToken">A token to cancel the send operation.</param>
  public async Task SendAsync(
      ModelContent? content = null,
      bool turnComplete = false,
      CancellationToken cancellationToken = default) {
    // If the content has FunctionResponseParts, we handle those separately.
    if (content.HasValue) {
      var functionParts = content?.Parts.OfType<ModelContent.FunctionResponsePart>().ToList();
      if (functionParts.Count > 0) {
        Dictionary<string, object> toolResponse = new() {
          { "toolResponse", new Dictionary<string, object>() {
            { "functionResponses", functionParts.Select(frPart => (frPart as ModelContent.Part).ToJson()["functionResponse"]).ToList() }
          }}
        };
        var toolResponseBytes = Encoding.UTF8.GetBytes(Json.Serialize(toolResponse));

        await InternalSendBytesAsync(new ArraySegment<byte>(toolResponseBytes), cancellationToken);
        if (functionParts.Count < content?.Parts.Count) {
          // There are other parts to send, so send them with the other method.
          content = new ModelContent(role: content?.Role,
              parts: content?.Parts.Where(p => p is not ModelContent.FunctionResponsePart));
        } else {
          return;
        }
      }
    }

    // Prepare the message payload
    Dictionary<string, object> contentDict = new() {
      { "turnComplete", turnComplete }
    };
    if (content.HasValue) {
      contentDict["turns"] = new List<object>(new [] { content?.ToJson() });
    }
    Dictionary<string, object> jsonDict = new() {
      { "clientContent", contentDict }
    };
    var byteArray = Encoding.UTF8.GetBytes(Json.Serialize(jsonDict));

    await InternalSendBytesAsync(new ArraySegment<byte>(byteArray), cancellationToken);
  }

  /// <summary>
  /// Send realtime input to the server.
  /// </summary>
  /// <param name="mediaChunks">A list of media chunks to send.</param>
  /// <param name="cancellationToken">A token to cancel the send operation.</param>
  public async Task SendMediaChunksAsync(
      List<ModelContent.InlineDataPart> mediaChunks,
      CancellationToken cancellationToken = default) {
    if (mediaChunks == null) return;

    // Prepare the message payload.
    Dictionary<string, object> jsonDict = new() {
      {
        "realtimeInput", new Dictionary<string, object>() {
          {
            // InlineDataPart inherits from Part, so this conversion should be safe.
            "mediaChunks", mediaChunks.Select(mc => (mc as ModelContent.Part).ToJson()["inlineData"]).ToList()
          }
        }
      }
    };
    var byteArray = Encoding.UTF8.GetBytes(Json.Serialize(jsonDict));

    await InternalSendBytesAsync(new ArraySegment<byte>(byteArray), cancellationToken);
  }

  private static byte[] ConvertTo16BitPCM(float[] samples) {
    short[] shortBuffer = new short[samples.Length];
    byte[] pcmBytes = new byte[samples.Length * 2];

    for (int i = 0; i < samples.Length; i++) {
      float sample = samples[i] * 32767.0f;
      sample = Math.Clamp(sample, -32768.0f, 32767.0f);
      shortBuffer[i] = (short)sample;
    }

    // Efficiently copy short array to byte array (respects system endianness - usually little)
    Buffer.BlockCopy(shortBuffer, 0, pcmBytes, 0, pcmBytes.Length);

    return pcmBytes;
  }

  /// <summary>
  /// Convenience function for sending audio data in a float[] to the server.
  /// </summary>
  /// <param name="audioData">The audio data to send. Expected format: 16 bit PCM audio at 16kHz little-endian.</param>
  /// <param name="cancellationToken">A token to cancel the send operation.</param>
  public Task SendAudioAsync(float[] audioData, CancellationToken cancellationToken = default) {
    ModelContent.InlineDataPart inlineDataPart = new("audio/pcm", ConvertTo16BitPCM(audioData));
    return SendMediaChunksAsync(new List<ModelContent.InlineDataPart>(new []{inlineDataPart}), cancellationToken);
  }

  /// <summary>
  /// Receives a stream of responses from the server. Having multiple of these ongoing will result in unexpected behavior.
  /// Closes upon receiving a TurnComplete from the server.
  /// </summary>
  /// <param name="cancellationToken">A token to cancel the operation.</param>
  /// <returns>A stream of `LiveContentResponse`s from the backend.</returns>
  public async IAsyncEnumerable<LiveSessionResponse> ReceiveAsync(
      [EnumeratorCancellation] CancellationToken cancellationToken = default) {
    if (_clientWebSocket.State != WebSocketState.Open) {
      throw new InvalidOperationException("WebSocket is not open. Cannot start receiving.");
    }

    StringBuilder messageBuilder = new();
    byte[] receiveBuffer = new byte[4096];
    Memory<byte> buffer = new(receiveBuffer);
    while (!cancellationToken.IsCancellationRequested) {
      ValueWebSocketReceiveResult result = await _clientWebSocket.ReceiveAsync(buffer, cancellationToken);

      if (result.MessageType == WebSocketMessageType.Close) {
        // Close initiated by the server
        // TODO: Should this just close without logging anything?
        break;
      } else if (result.MessageType == WebSocketMessageType.Text) {
        // We shouldn't get a Text response from the backend
        throw new NotSupportedException("Text responses from the backend are not supported.");
      } else if (result.MessageType == WebSocketMessageType.Binary) {
        messageBuilder.Append(Encoding.UTF8.GetString(receiveBuffer, 0, result.Count));

        if (result.EndOfMessage) {
          LiveSessionResponse? response = LiveSessionResponse.FromJson(messageBuilder.ToString());
          // Reset for the next message.
          messageBuilder.Clear();

          if (response != null) {
            yield return response.Value;

            // On receiving TurnComplete we close the ongoing connection.
            if (response?.Message is LiveSessionContent serverContent &&
                serverContent.TurnComplete) {
              break;
            }
          }
        }
      }
    }
    // Check cancellation again, in case that is why it is finished.
    cancellationToken.ThrowIfCancellationRequested();
  }

  /// <summary>
  /// Close the `LiveSession`.
  /// </summary>
  /// <param name="cancellationToken">A token to cancel the operation.</param>
  public Task CloseAsync(CancellationToken cancellationToken = default) {
    return _clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure,
        "LiveSession CloseAsync called.", cancellationToken);
  }
}

}
