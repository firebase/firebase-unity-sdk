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

namespace Firebase.VertexAI {

public readonly struct ModelContent {
  public string Role { get; }
  public IEnumerable<Part> Parts { get; }

  public ModelContent(params Part[] parts) {
    throw new NotImplementedException();
  }
  public ModelContent(IEnumerable<Part> parts) {
    throw new NotImplementedException();
  }
  public ModelContent(string role, params Part[] parts) {
    throw new NotImplementedException();
  }
  public ModelContent(string role, IEnumerable<Part> parts) {
    throw new NotImplementedException();
  }

  // Helper functions
  public static ModelContent Text(string text) {
    throw new NotImplementedException();
  }
  public static ModelContent InlineData(string mimeType, byte[] data) {
    throw new NotImplementedException();
  }
  public static ModelContent FileData(string mimeType, System.Uri uri) {
    throw new NotImplementedException();
  }
  public static ModelContent FunctionResponse(
      string name, IDictionary<string, object> response) {
    throw new NotImplementedException();
  }

  // TODO: Possibly more, like Multi, Model, FunctionResponses, System (only on Dart?)

  // TODO: Do we want to include helper factories for common C# or Unity types?


  public interface Part { }

  public readonly struct TextPart : Part {
    public string Text { get; }

    public TextPart(string text) {
      throw new NotImplementedException();
    }
  }

  public readonly struct InlineDataPart : Part {
    public string Mimetype { get; }
    public byte[] Data { get; }

    public InlineDataPart(string mimetype, byte[] data) {
      throw new NotImplementedException();
    }
  }

  public readonly struct FileDataPart : Part {
    public string MimeType { get; }
    public System.Uri Uri { get; }

    public FileDataPart(string mimeType, System.Uri uri) {
      throw new NotImplementedException();
    }
  }

  public readonly struct FunctionCallPart : Part {
    public string Name { get; }
    public IReadOnlyDictionary<string, object> Args { get; }

    public FunctionCallPart(string name, IDictionary<string, object> args) {
      throw new NotImplementedException();
    }
  }

  public readonly struct FunctionResponsePart : Part {
    public string Name { get; }
    public IReadOnlyDictionary<string, object> Response { get; }

    public FunctionResponsePart(string name, IDictionary<string, object> response) {
      throw new NotImplementedException();
    }
  }
}

}
