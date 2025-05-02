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

namespace Firebase.FirebaseAI {

public class FirebaseAIException : Exception {
  internal FirebaseAIException(string message) : base(message) { }

  internal FirebaseAIException(string message, Exception exception) : base(message, exception) { }
}

public class FirebaseAISerializationException : FirebaseAIException {
  internal FirebaseAISerializationException(string message) : base(message) { }

  internal FirebaseAISerializationException(string message, Exception exception) : base(message, exception) { }
}

public class FirebaseAIServerException : FirebaseAIException {
  internal FirebaseAIServerException(string message) : base(message) { }
}

public class FirebaseAIInvalidAPIKeyException : FirebaseAIException {
  internal FirebaseAIInvalidAPIKeyException(string message) : base(message) { }
}

public class FirebaseAIPromptBlockedException : FirebaseAIException {
  internal FirebaseAIPromptBlockedException(GenerateContentResponse response) :
      base("Prompt was blocked:" +
           $"{response.PromptFeedback?.BlockReason?.ToString() ?? "Unknown"}") { }
}

public class FirebaseAIUnsupportedUserLocationException : FirebaseAIException {
  internal FirebaseAIUnsupportedUserLocationException() :
      base("User location is not supported for the API use.") { }
}

public class FirebaseAIInvalidStateException : FirebaseAIException {
  internal FirebaseAIInvalidStateException(string message) : base(message) { }
}

public class FirebaseAIResponseStoppedException : FirebaseAIException {
  internal FirebaseAIResponseStoppedException(GenerateContentResponse response) :
    base("Content generation stopped. Reason: " +
         $"{response.Candidates.FirstOrDefault().FinishReason?.ToString() ?? "Unknown"}") { }
}

public class FirebaseAIRequestTimeoutException : FirebaseAIException {
  internal FirebaseAIRequestTimeoutException(string message) : base(message) { }

  internal FirebaseAIRequestTimeoutException(string message, Exception e) : base(message, e) { }
}

public class FirebaseAIInvalidLocationException : FirebaseAIException {
  internal FirebaseAIInvalidLocationException(string location) :
      base($"Invalid location \"{location}\"") { }
}

public class FirebaseAIServiceDisabledException : FirebaseAIException {
  internal FirebaseAIServiceDisabledException(string message) : base(message) { }
}

public class FirebaseAIUnknownException : FirebaseAIException {
  internal FirebaseAIUnknownException(string message) : base(message) { }
}

}
