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
using System.Net.Http;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading.Tasks;

namespace Firebase.VertexAI.Internal {

// Contains internal helper methods for interacting with other Firebase libraries.
internal static class FirebaseInterops {

  // The various App Check types needed to retrieve the token, cached via reflection on startup.
  private static Type _appCheckType;
  private static MethodInfo _appCheckGetInstanceMethod;
  private static MethodInfo _appCheckGetTokenMethod;
  private static PropertyInfo _appCheckTokenResultProperty;
  private static PropertyInfo _appCheckTokenTokenProperty;
  // Used to determine if the App Check reflection initialized successfully, and should work.
  private static bool _appCheckReflectionInitialized = false;
  // The header used by the AppCheck token.
  private const string appCheckHeader = "X-Firebase-AppCheck";

  static FirebaseInterops() {
    InitializeAppCheckReflection();
  }

  private static void LogError(string message) {
#if FIREBASE_VERTEXAI_DEBUG_LOGGING
    UnityEngine.Debug.LogError(message);
#endif
  }

  // Cache the various types and methods needed for AppCheck token retrieval.
  private static void InitializeAppCheckReflection() {
    const string firebaseAppCheckTypeName = "Firebase.AppCheck.FirebaseAppCheck, Firebase.AppCheck";
    const string getAppCheckTokenMethodName = "GetAppCheckTokenAsync";

    try {
      // Set this to false, to allow easy failing out via return.
      _appCheckReflectionInitialized = false;

      _appCheckType = Type.GetType(firebaseAppCheckTypeName);
      if (_appCheckType == null) {
        return;
      }

      // Get the static method GetInstance(FirebaseApp app)
      _appCheckGetInstanceMethod = _appCheckType.GetMethod(
          "GetInstance", BindingFlags.Static | BindingFlags.Public, null,
          new Type[] { typeof(FirebaseApp) }, null);
      if (_appCheckGetInstanceMethod == null) {
        LogError("Could not find FirebaseAppCheck.GetInstance method via reflection.");
        return;
      }

      // Get the instance method GetAppCheckTokenAsync(bool forceRefresh)
      _appCheckGetTokenMethod = _appCheckType.GetMethod(
          getAppCheckTokenMethodName, BindingFlags.Instance | BindingFlags.Public, null,
          new Type[] { typeof(bool) }, null);
      if (_appCheckGetTokenMethod == null) {
        LogError($"Could not find {getAppCheckTokenMethodName} method via reflection.");
        return;
      }

      // Should be Task<AppCheckToken>
      Type appCheckTokenTaskType = _appCheckGetTokenMethod.ReturnType;

      // Get the Result property from the Task<AppCheckToken>
      _appCheckTokenResultProperty = appCheckTokenTaskType.GetProperty("Result");
      if (_appCheckTokenResultProperty == null) {
        LogError("Could not find Result property on App Check token Task.");
        return;
      }

      // Should be AppCheckToken
      Type appCheckTokenType = _appCheckTokenResultProperty.PropertyType;

      _appCheckTokenTokenProperty = appCheckTokenType.GetProperty("Token");
      if (_appCheckTokenTokenProperty == null) {
        LogError($"Could not find Token property on AppCheckToken.");
        return;
      }

      _appCheckReflectionInitialized = true;
    } catch (Exception e) {
      LogError($"Exception during static initialization of FirebaseInterops: {e}");
    }
  }

  // Gets the AppCheck Token, assuming there is one. Otherwise, returns null.
  internal static async Task<string> GetAppCheckTokenAsync(FirebaseApp firebaseApp) {
    // If AppCheck reflection failed for any reason, nothing to do.
    if (!_appCheckReflectionInitialized) {
      return null;
    }

    try {
      // Get the FirebaseAppCheck instance for the current FirebaseApp
      object appCheckInstance = _appCheckGetInstanceMethod.Invoke(null, new object[] { firebaseApp });
      if (appCheckInstance == null) {
        LogError("Failed to get FirebaseAppCheck instance via reflection.");
        return null;
      }

      // Invoke GetAppCheckTokenAsync(false) - returns a Task<AppCheckToken>
      object taskObject = _appCheckGetTokenMethod.Invoke(appCheckInstance, new object[] { false });
      if (taskObject is not Task appCheckTokenTask) {
        LogError($"Invoking GetToken did not return a Task.");
        return null;
      }

      // Await the task to get the AppCheckToken result
      await appCheckTokenTask;

      // Check for exceptions in the task
      if (appCheckTokenTask.IsFaulted) {
        LogError($"Error getting App Check token: {appCheckTokenTask.Exception}");
        return null;
      }

      // Get the Result property from the Task<AppCheckToken>
      object tokenResult = _appCheckTokenResultProperty.GetValue(appCheckTokenTask); // This is the AppCheckToken struct
      if (tokenResult == null) {
        LogError("App Check token result was null.");
        return null;
      }

      // Get the Token property from the AppCheckToken struct
      return _appCheckTokenTokenProperty.GetValue(tokenResult) as string;
    } catch (Exception e) {
      // Log any exceptions during the reflection/invocation process
      LogError($"An error occurred while trying to fetch App Check token: {e}");
    }
    return null;
  }

  // Adds the other Firebase tokens to the HttpRequest, as available.
  internal static async Task AddFirebaseTokensAsync(HttpRequestMessage request, FirebaseApp firebaseApp) {
    string tokenString = await GetAppCheckTokenAsync(firebaseApp);

    // Add the header if the token is valid
    if (!string.IsNullOrEmpty(tokenString)) {
      request.Headers.Add(appCheckHeader, tokenString);
    }
  }

  // Adds the other Firebase tokens to the WebSocket, as available.
  internal static async Task AddFirebaseTokensAsync(ClientWebSocket socket, FirebaseApp firebaseApp) {
    string tokenString = await GetAppCheckTokenAsync(firebaseApp);

    // Add the header if the token is valid
    if (!string.IsNullOrEmpty(tokenString)) {
      socket.Options.SetRequestHeader(appCheckHeader, tokenString);
    }   
  }

}

}
