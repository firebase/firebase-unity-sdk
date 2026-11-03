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
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Storage.Internal {
  /// <summary>
  /// Local file and stream helpers shared by the upload and download engines.
  /// </summary>
  internal static class IoUtil {
    private const int SkipBufferSize = 256 * 1024;

    /// <summary>
    /// Converts a <c>file://</c> URI into a local file system path; any other input is returned
    /// unchanged.
    /// </summary>
    internal static string ResolveLocalFilePath(string filePath) {
      if (string.IsNullOrEmpty(filePath) ||
          !filePath.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) {
        return filePath;
      }
      try {
        // Uri.LocalPath is already unescaped; do not unescape again or literal '%' sequences
        // in filenames will be corrupted.
        return new Uri(filePath).LocalPath;
      } catch (UriFormatException) {
        return filePath.Substring("file://".Length);
      }
    }

    /// <summary>
    /// Reads and discards up to <paramref name="count" /> bytes from a forward-only
    /// <paramref name="stream" /> through a pooled buffer. Returns the number of bytes actually
    /// skipped, which is smaller than <paramref name="count" /> only if the stream ended first.
    /// </summary>
    internal static async Task<long> SkipAsync(
        Stream stream,
        long count,
        CancellationToken cancellationToken) {
      long skipped = 0;
      if (count <= 0) {
        return skipped;
      }
      byte[] buffer = ArrayPool<byte>.Shared.Rent(SkipBufferSize);
      try {
        while (skipped < count) {
          int toRead = (int)Math.Min(count - skipped, buffer.Length);
          int read = await stream.ReadAsync(buffer, 0, toRead, cancellationToken)
              .ConfigureAwait(false);
          if (read <= 0) {
            break;
          }
          skipped += read;
        }
        return skipped;
      } finally {
        ArrayPool<byte>.Shared.Return(buffer);
      }
    }
  }
}
