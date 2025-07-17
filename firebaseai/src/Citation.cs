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

namespace Firebase.AI {

/// <summary>
/// A collection of source attributions for a piece of content.
/// </summary>
public readonly struct CitationMetadata {
  private readonly IReadOnlyList<Citation> _citations;

  /// <summary>
  /// A list of individual cited sources and the parts of the content to which they apply.
  /// </summary>
  public IReadOnlyList<Citation> Citations {
    get {
      return _citations ?? new List<Citation>();
    }
  }

  // Hidden constructor, users don't need to make this.
  private CitationMetadata(List<Citation> citations) {
    _citations = citations;
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for deserializing JSON responses and should not be called directly.
  /// </summary>
  internal static CitationMetadata FromJson(Dictionary<string, object> jsonDict,
      FirebaseAI.Backend.InternalProvider backend) {
    string citationKey = backend switch {
      FirebaseAI.Backend.InternalProvider.GoogleAI => "citationSources",
      FirebaseAI.Backend.InternalProvider.VertexAI => "citations",
      _ => throw new ArgumentOutOfRangeException(nameof(backend), backend,
                         "Unsupported or unhandled backend provider encountered.")
    };
    return new CitationMetadata(
      jsonDict.ParseObjectList(citationKey, Citation.FromJson));
  }
}

/// <summary>
/// A struct describing a source attribution.
/// </summary>
public readonly struct Citation {
  /// <summary>
  /// The inclusive beginning of a sequence in a model response that derives from a cited source.
  /// </summary>
  public int StartIndex { get; }
  /// <summary>
  /// The exclusive end of a sequence in a model response that derives from a cited source.
  /// </summary>
  public int EndIndex { get; }
  /// <summary>
  /// A link to the cited source, if available.
  /// </summary>
  public System.Uri Uri { get; }
  /// <summary>
  /// The title of the cited source, if available.
  /// </summary>
  public string Title { get; }
  /// <summary>
  /// The license the cited source work is distributed under, if specified.
  /// </summary>
  public string License { get; }
  /// <summary>
  /// The publication date of the cited source, if available.
  /// </summary>
  public System.DateTime? PublicationDate { get; }

  // Hidden constructor, users don't need to make this.
  private Citation(int startIndex, int endIndex, Uri uri, string title,
      string license, DateTime? publicationDate) {
    StartIndex = startIndex;
    EndIndex = endIndex;
    Uri = uri;
    Title = title;
    License = license;
    PublicationDate = publicationDate;
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for deserializing JSON responses and should not be called directly.
  /// </summary>
  internal static Citation FromJson(Dictionary<string, object> jsonDict) {
    // If there is a Uri, need to convert it.
    Uri uri = null;
    if (jsonDict.TryParseValue("uri", out string uriString)) {
      uri = new Uri(uriString);
    }

    // If there is a publication date, we need to convert it.
    DateTime? pubDate = null;
    if (jsonDict.TryParseValue("publicationDate", out Dictionary<string, object> dateDict)) {
      // Make sure that if any key is missing, it has a default value that will work with DateTime.
      pubDate = new DateTime(
        dateDict.ParseValue<int>("year", defaultValue: 1),
        dateDict.ParseValue<int>("month", defaultValue: 1),
        dateDict.ParseValue<int>("day", defaultValue: 1));
    }

    return new Citation(
      jsonDict.ParseValue<int>("startIndex"),
      jsonDict.ParseValue<int>("endIndex"),
      uri,
      jsonDict.ParseValue<string>("title"),
      jsonDict.ParseValue<string>("license"),
      pubDate);
  }
}

}
