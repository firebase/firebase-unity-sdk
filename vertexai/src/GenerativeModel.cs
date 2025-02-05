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
using System.Threading.Tasks;

namespace Firebase.VertexAI {

public class GenerativeModel {
  public Task<GenerateContentResponse> GenerateContentAsync(
      params ModelContent[] content) {
    throw new NotImplementedException();
  }
  public Task<GenerateContentResponse> GenerateContentAsync(
      IEnumerable<ModelContent> content) {
    throw new NotImplementedException();
  }
  public Task<GenerateContentResponse> GenerateContentAsync(
      string text) {
    throw new NotImplementedException();
  }

  public IAsyncEnumerable<GenerateContentResponse> GenerateContentStreamAsync(
      params ModelContent[] content) {
    throw new NotImplementedException();
  }
  public IAsyncEnumerable<GenerateContentResponse> GenerateContentStreamAsync(
      IEnumerable<ModelContent> content) {
    throw new NotImplementedException();
  }
  public IAsyncEnumerable<GenerateContentResponse> GenerateContentStreamAsync(
      string text) {
    throw new NotImplementedException();
  }

  public Task<CountTokensResponse> CountTokensAsync(
      params ModelContent[] content) {
    throw new NotImplementedException();
  }
  public Task<CountTokensResponse> CountTokensAsync(
      IEnumerable<ModelContent> content) {
    throw new NotImplementedException();
  }
  public Task<CountTokensResponse> CountTokensAsync(
      string text) {
    throw new NotImplementedException();
  }

  public Chat StartChat(params ModelContent[] history) {
    throw new NotImplementedException();
  }
  public Chat StartChat(IEnumerable<ModelContent> history) {
    throw new NotImplementedException();
  }

  // Note: No public constructor, get one through VertexAI.GetGenerativeModel
}

}
