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

namespace Firebase.Storage.Internal {
  /// <summary>
  /// Parses gs:// and http(s):// Firebase Storage URIs into <see cref="StoragePath" /> components.
  /// </summary>
  internal static class StorageUriParser {
    private const string GsScheme = "gs://";
    private const string HttpScheme = "http://";
    private const string HttpsScheme = "https://";
    private const string DomainBucketSeparator = "/v0/b/";
    private const string BucketPathSeparator = "/o/";

    /// <summary>
    /// Validates a URI scheme and extracts the bucket and object path into a <see cref="StoragePath" />.
    /// </summary>
    /// <param name="storage">The <see cref="FirebaseStorage" /> instance.</param>
    /// <param name="uri">The URI string to parse (gs://, http://, or https://).</param>
    /// <param name="logger">Optional logger for error diagnostics.</param>
    /// <param name="allowObjectPath">If false, throws <see cref="ArgumentException" /> when the URI contains a non-empty object path.</param>
    /// <returns>The parsed <see cref="StoragePath" />.</returns>
    /// <exception cref="ArgumentException">Thrown when the URI is null, empty, malformed, has an invalid scheme, has an empty bucket, or contains an object path when <paramref name="allowObjectPath" /> is false.</exception>
    public static StoragePath UriToComponents(FirebaseStorage storage, string uri,
                                              ModuleLogger logger = null,
                                              bool allowObjectPath = true) {
      if (string.IsNullOrEmpty(uri)) {
        logger?.LogMessage(LogLevel.Error, "URI cannot be null or empty.");
        throw new ArgumentException("URI cannot be null or empty.", nameof(uri));
      }

      string bucket;
      string objectPath;

      if (uri.StartsWith(GsScheme, StringComparison.OrdinalIgnoreCase)) {
        string remainder = uri.Substring(GsScheme.Length);
        int slashIndex = remainder.IndexOf('/');
        if (slashIndex < 0) {
          bucket = remainder;
          objectPath = string.Empty;
        } else {
          bucket = remainder.Substring(0, slashIndex);
          objectPath = remainder.Substring(slashIndex + 1);
        }
      } else if (uri.StartsWith(HttpScheme, StringComparison.OrdinalIgnoreCase) ||
                 uri.StartsWith(HttpsScheme, StringComparison.OrdinalIgnoreCase)) {
        int schemeLength = uri.StartsWith(HttpsScheme, StringComparison.OrdinalIgnoreCase)
            ? HttpsScheme.Length
            : HttpScheme.Length;
        string remainder = uri.Substring(schemeLength);

        // Strip query string and fragment if present.
        int queryOrFragmentIndex = remainder.IndexOfAny(new[] { '?', '#' });
        if (queryOrFragmentIndex >= 0) {
          remainder = remainder.Substring(0, queryOrFragmentIndex);
        }

        int firstSlash = remainder.IndexOf('/');
        if (firstSlash < 0) {
          logger?.LogMessage(LogLevel.Error, $"Invalid HTTP(S) Storage URI '{uri}'.");
          throw new ArgumentException($"Invalid HTTP(S) Storage URI '{uri}'.", nameof(uri));
        }

        string pathPart = remainder.Substring(firstSlash);
        int domainBucketIndex = pathPart.IndexOf(DomainBucketSeparator, StringComparison.Ordinal);
        if (domainBucketIndex < 0) {
          logger?.LogMessage(LogLevel.Error,
              $"Unable to parse bucket from HTTP(S) Storage URI '{uri}'.");
          throw new ArgumentException(
              $"Unable to parse bucket from HTTP(S) Storage URI '{uri}'.", nameof(uri));
        }

        string afterV0B = pathPart.Substring(domainBucketIndex + DomainBucketSeparator.Length);
        int bucketPathIndex = afterV0B.IndexOf(BucketPathSeparator, StringComparison.Ordinal);
        if (bucketPathIndex >= 0) {
          bucket = afterV0B.Substring(0, bucketPathIndex).TrimEnd('/');
          string escapedObjectPath = afterV0B.Substring(bucketPathIndex + BucketPathSeparator.Length);
          objectPath = Uri.UnescapeDataString(escapedObjectPath);
        } else if (afterV0B.EndsWith("/o", StringComparison.Ordinal)) {
          bucket = afterV0B.Substring(0, afterV0B.Length - 2).TrimEnd('/');
          objectPath = string.Empty;
        } else {
          bucket = afterV0B.TrimEnd('/');
          objectPath = string.Empty;
          if (bucket.IndexOf('/') >= 0) {
            logger?.LogMessage(LogLevel.Error,
                $"Malformed HTTP(S) Storage URI '{uri}'.");
            throw new ArgumentException($"Malformed HTTP(S) Storage URI '{uri}'.", nameof(uri));
          }
        }
      } else {
        logger?.LogMessage(LogLevel.Error,
            $"Unable to parse URI '{uri}'. URI should start with gs://, http://, or https://.");
        throw new ArgumentException(
            $"Unable to parse URI '{uri}'. URI should start with gs://, http://, or https://.",
            nameof(uri));
      }

      if (string.IsNullOrEmpty(bucket)) {
        logger?.LogMessage(LogLevel.Error, $"Bucket cannot be empty in URI '{uri}'.");
        throw new ArgumentException($"Bucket cannot be empty in URI '{uri}'.", nameof(uri));
      }

      string normalizedObjectPath = StoragePath.NormalizePath(objectPath);
      if (!allowObjectPath && !string.IsNullOrEmpty(normalizedObjectPath)) {
        logger?.LogMessage(LogLevel.Error,
            $"Storage bucket URI '{uri}' cannot contain an object path.");
        throw new ArgumentException(
            $"Storage bucket URI '{uri}' cannot contain an object path.", nameof(uri));
      }

      return new StoragePath(storage, bucket, normalizedObjectPath);
    }
  }
}
