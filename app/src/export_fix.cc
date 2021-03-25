/*
 * Copyright 2019 Google LLC
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

// Since most linkers wont pull in symbols from a static library if
// nothing is used from said library and we are trying to link in all
// the generated swig libraries into this one shared library with no
// source code, we have added a empty function "ExportFix" to swig and
// refere to it here to persuade the linker to do what we want and pull
// in all symbols from all the swig libraries

#include "firebase_project_list.h"  // NOLINT

#ifdef _WIN32
  #define STDCALL __stdcall
#else
  #define STDCALL
#endif

#define GEN_EXTERN(NAME) \
  extern void STDCALL Firebase_##NAME##_CSharp_ExportFix();

#define GEN_CALL(NAME)  \
  Firebase_##NAME##_CSharp_ExportFix();

extern "C" {
  PROJECT_LIST(GEN_EXTERN)
}

namespace internal {
  void ExportFixImpl() {
    PROJECT_LIST(GEN_CALL)
  }
}  // namespace internal
