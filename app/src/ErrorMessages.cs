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

namespace Firebase {

// Contains various error messages to be used for shared error cases.
internal class ErrorMessages {
  // DEPENDENCY_NOT_FOUND_ERROR_* are error strings to display when a
  // InitializationException is thrown during feature initialization due
  // to a missing dependency.
  private static string DEPENDENCY_NOT_FOUND_ERROR_ANDROID =
      "On Android, Firebase requires C/C++ and Java components\n" +
      "that are distributed with the Firebase and Android SDKs.\n" +
      "\n" +
      "It's likely the required dependencies for Firebase were not included\n" +
      "in your Unity project.\n" +
      "Assets/Plugins/Android/ in your Unity project should contain\n" +
      "AAR files in the form firebase-*.aar\n" +
      "You may have disabled the Android Resolver which would\n" +
      "have added the AAR dependencies for you.\n" +
      "\n" +
      "Do the following to enable the Android Resolver in Unity:\n" +
      "* Select the menu option 'Assets -> Play Services Resolver -> \n" +
      "  Android Resolver -> Settings'\n" +
      "* In the Android Resolver settings check\n" +
      "  'Enable Background Resolution'\n" +
      "* Select the menu option 'Assets -> Play Services Resolver ->\n" +
      "  Android Resolver -> Resolve Client Jars' to force Android\n" +
      "  dependency resolution.\n" +
      "* Rebuild your APK and deploy.\n";

  private static string DEPENDENCY_NOT_FOUND_ERROR_IOS =
      "On iOS and tvOS Firebase requires native (C/C++) and Cocoapod\n" +
      "components that are distributed with the Firebase SDK and via\n" +
      "Cocoapods.\n" +
      "\n" +
      "It's likely that you did not include the require Cocoapod\n" +
      "dependencies for Firebase in your Unity project.\n" +
      "You may have disabled the iOS Resolver which would have added\n" +
      "the Cocoapod dependencies for you.\n" +
      "\n" +
      "Do the following to enable the iOS Resolver in Unity:\n" +
      "* Select the menu option 'Assets -> Play Services Resolver ->\n" +
      "  iOS Resolver -> Settings'\n" +
      "* In the iOS Resolver settings check 'Podfile Generation' and\n" +
      "  'Add Cocoapods to Generated Xcode Project'.\n" +
      "* Build your iOS or tvOS project and check the Unity console for\n" +
      "  any errors associated with Cocoapod tool execution.\n"  +
      "  You will need to correctly install Cocoapods tools to generate\n" +
      "  a working build.\n";

  private static string DEPENDENCY_NOT_FOUND_ERROR_GENERIC =
      "Firebase is distributed with native (C/C++) dependencies\n" +
      "that are required by the SDK.\n" +
      "\n" +
      "It's possible that parts of Firebase SDK have been removed from\n" +
      "your Unity project.\n" +
      "\n" +
      "To resolve the problem, try re-importing your Firebase plugins and\n" +
      "building again.\n" +
      "\n" +
      "Alternatively, you may be trying to use Firebase on an unsupported\n" +
      "platform.  See the Firebase website for the list of supported\n" +
      "platforms.\n";

  // DLL_NOT_FOUND_ERROR_* are error strings to display when a
  // DllNotFoundException is thrown by a pinvoke operation and how to
  // potentially fix it.
  private static string DLL_NOT_FOUND_ERROR_ANDROID =
      "Firebase's libApp.so was not found for this device's architecture\n" +
      "in your APK.\n";

  private static string DLL_NOT_FOUND_ERROR_IOS =
      "A Firebase static library (e.g libApp.a) was not linked with your\n" +
      "iOS or tvOS application.\n";

  private static string DLL_NOT_FOUND_ERROR_GENERIC =
      "A Firebase shared library (.dll / .so) could not be loaded.\n";

  // Get the dependency not found error message
  // (DEPENDENCY_NOT_FOUND_ERROR_*) for the current platform.
  internal static string DependencyNotFoundErrorMessage {
    get {
      if (Platform.PlatformInformation.IsAndroid) {
        return DEPENDENCY_NOT_FOUND_ERROR_ANDROID;
      } else if (Platform.PlatformInformation.IsIOS) {
        return DEPENDENCY_NOT_FOUND_ERROR_IOS;
      } else {
        return DEPENDENCY_NOT_FOUND_ERROR_GENERIC;
      }
    }
  }

  // Retrieve an error message that describes how to resolve a
  // DllNotFoundException.
  internal static string DllNotFoundExceptionErrorMessage {
    get {
      string error;
      if (Platform.PlatformInformation.IsAndroid) {
        error = DLL_NOT_FOUND_ERROR_ANDROID;
      } else if (Platform.PlatformInformation.IsIOS) {
        error = DLL_NOT_FOUND_ERROR_IOS;
      } else {
        error = DLL_NOT_FOUND_ERROR_GENERIC;
      }
      return error + DependencyNotFoundErrorMessage;
    }
  }
}

}  // namespace Firebase
