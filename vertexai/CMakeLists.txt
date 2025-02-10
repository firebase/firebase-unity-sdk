# Copyright 2025 Google
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#      http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

# CMake file for the Vertex AI in Firebase library

# Vertex AI in Firebase CSharp files
set(firebase_vertexai_src
  src/Candidate.cs
  src/Chat.cs
  src/Citation.cs
  src/CountTokensResponse.cs
  src/FunctionCalling.cs
  src/GenerateContentResponse.cs
  src/GenerationConfig.cs
  src/GenerativeModel.cs
  src/ModelContent.cs
  src/RequestOptions.cs
  src/Safety.cs
  src/Schema.cs
  src/VertexAI.cs
  src/VertexAIException.cs
)

unity_pack_documentation_sources(vertexai
  DOCUMENTATION_SOURCES
    ${firebase_vertexai_src}
)

mono_add_library(firebase_vertexai
  MODULE
    Firebase.VertexAI
  SOURCES
    ${firebase_vertexai_src}
  REFERENCES
    ${FIREBASE_PLATFORM_REF}
)

unity_pack_cs(firebase_vertexai)

set_property(TARGET firebase_vertexai
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
