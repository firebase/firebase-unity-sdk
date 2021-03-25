/*
 * Copyright 2017 Google LLC
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

namespace Firebase.Platform {

  // An interface used by the Platform layer to interact with FirebaseApps,
  // as the Platform layer cannot depend on FirebaseApp, preventing
  // classes like FirebaseApp from being used directly.
  internal interface IFirebaseAppPlatform {
    // The wrapped FirebaseApp object, as a generic object.
    // This should only be used for cases when a FirebaseApp needs to be passed
    // into another function, such as GetAuth, in FirebaseAuth.
    object AppObject { get; }

    // The name field from the wrapped FirebaseApp object.
    string Name { get; }

    // The database url from the wrapped FirebaseApp object.
    System.Uri DatabaseUrl { get; }
  }
}
