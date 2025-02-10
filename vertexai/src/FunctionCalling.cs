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

public readonly struct FunctionDeclaration {
  // No public properties, on purpose since it is meant for user input only

  public FunctionDeclaration(string name, string description,
      IDictionary<string, Schema> parameters,
      IEnumerable<string> optionalParameters = null) {
    throw new NotImplementedException();
  }
}

public readonly struct Tool {
  // No public properties, on purpose since it is meant for user input only

  public Tool(params FunctionDeclaration[] functionDeclarations) {
    throw new NotImplementedException();
  }
  public Tool(IEnumerable<FunctionDeclaration> functionDeclarations) {
    throw new NotImplementedException();
  }
}

public readonly struct ToolConfig {
  // No public properties, on purpose since it is meant for user input only

  public ToolConfig(FunctionCallingConfig? functionCallingConfig = null) {
    throw new NotImplementedException();
  }
}

public readonly struct FunctionCallingConfig {
  // No public properties, on purpose since it is meant for user input only

  public static FunctionCallingConfig Auto() {
    throw new NotImplementedException();
  }

  public static FunctionCallingConfig Any(params string[] allowedFunctionNames) {
    throw new NotImplementedException();
  }
  public static FunctionCallingConfig Any(IEnumerable<string> allowedFunctionNames) {
    throw new NotImplementedException();
  }

  public static FunctionCallingConfig None() {
    throw new NotImplementedException();
  }
}

}
