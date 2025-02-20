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
using System.Collections.ObjectModel;
using Firebase.VertexAI.Internal;

namespace Firebase.VertexAI {

public readonly struct CitationMetadata {
  private readonly ReadOnlyCollection<Citation> _citations;

  public IEnumerable<Citation> Citations =>
      _citations ?? new ReadOnlyCollection<Citation>(new List<Citation>());

  // Hidden constructor, users don't need to make this
  private CitationMetadata(List<Citation> citations) {
    _citations = new ReadOnlyCollection<Citation>(citations ?? new List<Citation>());
  }

  internal static CitationMetadata FromJson(Dictionary<string, object> jsonDict) {
    return new CitationMetadata(
      jsonDict.ParseObjectList("citations", Citation.FromJson));
  }
}

public readonly struct Citation {
  public int StartIndex { get; }
  public int EndIndex { get; }
  public System.Uri Uri { get; }
  public string Title { get; }
  public string License { get; }
  public System.DateTime? PublicationDate { get; }

  // Hidden constructor, users don't need to make this
  private Citation(int startIndex, int endIndex, Uri uri, string title,
      string license, DateTime? publicationDate) {
    StartIndex = startIndex;
    EndIndex = endIndex;
    Uri = uri;
    Title = title;
    License = license;
    PublicationDate = publicationDate;
  }

  internal static Citation FromJson(Dictionary<string, object> jsonDict) {
    // If there is a Uri, need to convert it
    Uri uri = null;
    if (jsonDict.TryParseValue("uri", out string uriString)) {
      uri = new Uri(uriString);
    }

    // If there is a publication date, we need to convert it
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
