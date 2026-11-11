// Copyright 2026 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Internal {
  internal static class SseLineReader {
    internal static async IAsyncEnumerable<string> ReadDataFramesAsync(
        HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken = default, bool multiLineEvents = false) {
      using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
      await foreach (string frame in ReadDataFramesAsync(stream, cancellationToken, response, multiLineEvents).ConfigureAwait(false)) yield return frame;
    }

    internal static async IAsyncEnumerable<string> ReadDataFramesAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken = default, IDisposable cancelTarget = null, bool multiLineEvents = false) {
      var cancelTcs = cancellationToken.CanBeCanceled ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) : null;
      using CancellationTokenRegistration reg = cancellationToken.CanBeCanceled ? cancellationToken.Register(() => {
        cancelTcs.TrySetResult(true);
        try { cancelTarget?.Dispose(); }
        catch (Exception) {
          // Best-effort socket abort on Mono.
        }
      }) : default;
      using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
      var eventBuf = multiLineEvents ? new StringBuilder(256) : null;
      while (true) {
        cancellationToken.ThrowIfCancellationRequested();
        string line;
        try {
          Task<string> readTask = reader.ReadLineAsync();
          if (cancelTcs != null && !readTask.IsCompleted && await Task.WhenAny(readTask, cancelTcs.Task).ConfigureAwait(false) == cancelTcs.Task) cancellationToken.ThrowIfCancellationRequested();
          line = await readTask.ConfigureAwait(false);
        } catch (Exception) when (cancellationToken.IsCancellationRequested) { cancellationToken.ThrowIfCancellationRequested(); throw; }
        if (line == null) {
          if (eventBuf != null && eventBuf.Length > 0) { string last = eventBuf.ToString(); eventBuf.Clear(); yield return last; }
          yield break;
        }
        if (string.IsNullOrWhiteSpace(line)) {
          if (multiLineEvents && eventBuf.Length > 0) { string ev = eventBuf.ToString(); eventBuf.Clear(); yield return ev; }
          continue;
        }
        if (line[0] == ':' || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
        string data = line.Length > 5 && line[5] == ' ' ? line.Substring(6) : line.Substring(5);
        if (!multiLineEvents) yield return data; else eventBuf.Append(data);
      }
    }
  }
}
