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
using System.Linq;

namespace Firebase.VertexAI {

public class VertexAIException : Exception {
  internal VertexAIException(string message) : base(message) { }

  internal VertexAIException(string message, Exception exception) : base(message, exception) { }
}

public class VertexAISerializationException : VertexAIException {
  internal VertexAISerializationException(string message) : base(message) { }

  internal VertexAISerializationException(string message, Exception exception) : base(message, exception) { }
}

public class VertexAIServerException : VertexAIException {
  internal VertexAIServerException(string message) : base(message) { }
}

public class VertexAIInvalidAPIKeyException : VertexAIException {
  internal VertexAIInvalidAPIKeyException(string message) : base(message) { }
}

public class VertexAIPromptBlockedException : VertexAIException {
  internal VertexAIPromptBlockedException(GenerateContentResponse response) :
      base("Prompt was blocked:" +
           $"{response.PromptFeedback?.BlockReason?.ToString() ?? "Unknown"}") { }
}

public class VertexAIUnsupportedUserLocationException : VertexAIException {
  internal VertexAIUnsupportedUserLocationException() :
      base("User location is not supported for the API use.") { }
}

public class VertexAIInvalidStateException : VertexAIException {
  internal VertexAIInvalidStateException(string message) : base(message) { }
}

public class VertexAIResponseStoppedException : VertexAIException {
  internal VertexAIResponseStoppedException(GenerateContentResponse response) :
    base("Content generation stopped. Reason: " +
         $"{response.Candidates.FirstOrDefault().FinishReason?.ToString() ?? "Unknown"}") { }
}

public class VertexAIRequestTimeoutException : VertexAIException {
  internal VertexAIRequestTimeoutException(string message) : base(message) { }

  internal VertexAIRequestTimeoutException(string message, Exception e) : base(message, e) { }
}

public class VertexAIInvalidLocationException : VertexAIException {
  internal VertexAIInvalidLocationException(string location) :
      base($"Invalid location \"{location}\"") { }
}

public class VertexAIServiceDisabledException : VertexAIException {
  internal VertexAIServiceDisabledException(string message) : base(message) { }
}

public class VertexAIUnknownException : VertexAIException {
  internal VertexAIUnknownException(string message) : base(message) { }
}

}
