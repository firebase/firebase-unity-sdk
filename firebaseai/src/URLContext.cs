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
using Firebase.AI.Internal;

namespace Firebase.AI
{
  /// <summary>
  /// A tool that allows you to provide additional context to the models in the form of
  /// public web URLs.
  ///
  /// By including URLs in your request, the Gemini model will access the content from those pages
  /// to inform and enhance its response.
  /// </summary>
  public readonly struct UrlContext { }

  /// <summary>
  /// Metadata for a single URL retrieved by the UrlContext tool.
  /// </summary>
  public readonly struct UrlMetadata
  {
    /// <summary>
    /// Status of the URL retrieval.
    /// </summary>
    public enum UrlRetrievalStatus
    {
      /// <summary>
      /// Unspecified retrieval status
      /// </summary>
      Unspecified = 0,
      /// <summary>
      /// The URL retrieval was successful.
      /// </summary>
      Success,
      /// <summary>
      /// The URL retrieval failed.
      /// </summary>
      Error,
      /// <summary>
      /// The URL retrieval failed because the content is behind a paywall.
      /// </summary>
      Paywall,
      /// <summary>
      /// The URL retrieval failed because the content is unsafe.
      /// </summary>
      Unsafe,
    }

    /// <summary>
    /// The retrieved URL.
    /// </summary>
    public System.Uri Url { get; }
    /// <summary>
    /// The status of the URL retrieval.
    /// </summary>
    public UrlRetrievalStatus RetrievalStatus { get; }

    private UrlMetadata(string urlString, UrlRetrievalStatus? retrievalStatus)
    {
      if (string.IsNullOrEmpty(urlString))
      {
        Url = null;
      }
      else
      {
        Url = new Uri(urlString);
      }
      RetrievalStatus = retrievalStatus ?? UrlRetrievalStatus.Unspecified;
    }

    private static UrlRetrievalStatus ParseUrlRetrievalStatus(string str)
    {
      return str switch
      {
        "URL_RETRIEVAL_STATUS_SUCCESS" => UrlRetrievalStatus.Success,
        "URL_RETRIEVAL_STATUS_ERROR" => UrlRetrievalStatus.Error,
        "URL_RETRIEVAL_STATUS_PAYWALL" => UrlRetrievalStatus.Paywall,
        "URL_RETRIEVAL_STATUS_UNSAFE" => UrlRetrievalStatus.Unsafe,
        _ => UrlRetrievalStatus.Unspecified,
      };
    }

    /// <summary>
    /// Intended for internal use only.
    /// This method is used for deserializing JSON responses and should not be called directly.
    /// </summary>
    internal static UrlMetadata FromJson(Dictionary<string, object> jsonDict)
    {
      return new UrlMetadata(
        jsonDict.ParseValue<string>("retrievedUrl"),
        jsonDict.ParseNullableEnum("urlRetrievalStatus", ParseUrlRetrievalStatus)
      );
    }
  }

  /// <summary>
  /// Metadata related to the UrlContext tool.
  /// </summary>
  public readonly struct UrlContextMetadata
  {
    private readonly IReadOnlyList<UrlMetadata> _urlMetadata;
    /// <summary>
    /// List of URL metadata used to provide context to the Gemini model.
    /// </summary>
    public IReadOnlyList<UrlMetadata> UrlMetadata
    {
      get
      {
        return _urlMetadata ?? new List<UrlMetadata>();
      }
    }

    private UrlContextMetadata(List<UrlMetadata> urlMetadata)
    {
      _urlMetadata = urlMetadata;
    }

    /// <summary>
    /// Intended for internal use only.
    /// This method is used for deserializing JSON responses and should not be called directly.
    /// </summary>
    internal static UrlContextMetadata FromJson(Dictionary<string, object> jsonDict)
    {
      return new UrlContextMetadata(
        jsonDict.ParseObjectList("urlMetadata", Firebase.AI.UrlMetadata.FromJson)
      );
    }
  }

}
