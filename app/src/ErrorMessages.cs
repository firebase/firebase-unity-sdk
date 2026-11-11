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

using System;

namespace Firebase {

// Contains various error messages to be used for shared error cases.
internal static class ErrorMessages {
  // DEPENDENCY_NOT_FOUND_ERROR_* are error strings to display when a
  // InitializationException is thrown during feature initialization due
  // to a missing dependency.
  private const string DependencyNotFoundAndroid =
      "On Android, Firebase requires Java components distributed with the Firebase and Android SDKs. " +
      "Run 'Assets -> External Dependency Manager -> Android Resolver -> Force Resolve'.\n";
  private const string DependencyNotFoundIos =
      "On iOS and tvOS, Firebase requires Swift Package or framework components from the Firebase Apple SDK.\n";
  private const string DependencyNotFoundGeneric =
      "Required Firebase platform dependencies could not be loaded.\n";

  // Get the dependency not found error message
  // (DEPENDENCY_NOT_FOUND_ERROR_*) for the current platform.
  internal static string DependencyNotFoundErrorMessage {
    get {
      try {
        if (Platform.PlatformInformation.IsAndroid) return DependencyNotFoundAndroid;
        if (Platform.PlatformInformation.IsIOS) return DependencyNotFoundIos;
      } catch (Exception) {
        // Headless .NET runtime without UnityEngine platform ECall initialized.
      }
      return DependencyNotFoundGeneric;
    }
  }

  // Retrieve an error message that describes how to resolve a
  // DllNotFoundException.
  internal static string DllNotFoundExceptionErrorMessage {
    get {
      string prefix = "A required native library (.dll / .so / .dylib) could not be loaded.\n";
      try {
        if (Platform.PlatformInformation.IsAndroid) prefix = "A required Android native dependency was not found.\n";
        else if (Platform.PlatformInformation.IsIOS) prefix = "A required native library was not linked on iOS/tvOS.\n";
      } catch (Exception) {
        // Headless .NET runtime without UnityEngine platform ECall initialized.
      }
      return prefix + DependencyNotFoundErrorMessage;
    }
  }
}

}  // namespace Firebase
