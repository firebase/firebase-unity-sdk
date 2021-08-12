/*
 * Copyright 2021 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

#ifndef FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_API_HEADERS_H_
#define FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_API_HEADERS_H_

#include "firestore/src/include/firebase/firestore.h"

namespace firebase {
namespace firestore {
namespace csharp {

// This class allows limited access to private functions of `Firestore` related
// to sending Cloud headers.
class ApiHeaders {
 public:
  static void SetClientLanguage(const std::string& language_token) {
    Firestore::SetClientLanguage(language_token);
  }
};

}  // namespace csharp
}  // namespace firestore
}  // namespace firebase

#endif  // FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_API_HEADERS_H_
