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

namespace Firebase.AI.Internal {

// Contains internal helper methods for interacting with other Firebase libraries.
internal static class FirebaseInterops {
  // The cached fields for FirebaseApp reflection.
  private static PropertyInfo _dataCollectionProperty = null;

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

  // The various Auth types needed to retrieve the token, cached via reflection on startup.
  private static Type _authType;
  private static MethodInfo _authGetAuthMethod;
  private static PropertyInfo _authCurrentUserProperty;
  private static MethodInfo _userTokenAsyncMethod;
  private static PropertyInfo _userTokenTaskResultProperty;
  // Used to determine if the Auth reflection initialized successfully, and should work.
  private static bool _authReflectionInitialized = false;
  // The header used by the AppCheck token.
  private const string authHeader = "Authorization";

  static FirebaseInterops() {
    InitializeAppReflection();
    InitializeAppCheckReflection();
    InitializeAuthReflection();
  }

  private static void LogError(string message) {
#if FIREBASEAI_DEBUG_LOGGING
    UnityEngine.Debug.LogError(message);
#endif
  }

  // Cache the methods needed for FirebaseApp reflection.
  private static void InitializeAppReflection() {
    try {
      _dataCollectionProperty = typeof(FirebaseApp).GetProperty(
          "IsDataCollectionDefaultEnabled",
          BindingFlags.Instance | BindingFlags.NonPublic);
      if (_dataCollectionProperty == null) {
        LogError("Could not find FirebaseApp.IsDataCollectionDefaultEnabled property via reflection.");
        return;
      }
      if (_dataCollectionProperty.PropertyType != typeof(bool)) {
        LogError("FirebaseApp.IsDataCollectionDefaultEnabled is not a bool, " +
                 $"but is {_dataCollectionProperty.PropertyType}");
        return;
      }
    } catch (Exception e) {
      LogError($"Failed to initialize FirebaseApp reflection: {e}");
    }
  }

  // Gets the property FirebaseApp.IsDataCollectionDefaultEnabled.
  public static bool GetIsDataCollectionDefaultEnabled(FirebaseApp firebaseApp) {
    if (firebaseApp == null || _dataCollectionProperty == null) {
      return false;
    }

    try {
      return (bool)_dataCollectionProperty.GetValue(firebaseApp);
    } catch (Exception e) {
      LogError($"Error accessing 'IsDataCollectionDefaultEnabled': {e}");
      return false;
    }
  }

  // SDK version to use if unable to find it.
  private const string _unknownSdkVersion = "unknown";
  private static readonly Lazy<string> _sdkVersionFetcher = new(() => {
    try {
      // Get the type Firebase.VersionInfo from the assembly that defines FirebaseApp.
      Type versionInfoType = typeof(FirebaseApp).Assembly.GetType("Firebase.VersionInfo");
      if (versionInfoType == null) {
        LogError("Firebase.VersionInfo type not found via reflection");
        return _unknownSdkVersion;
      }

      // Firebase.VersionInfo.SdkVersion
      PropertyInfo sdkVersionProperty = versionInfoType.GetProperty(
              "SdkVersion",
              BindingFlags.Static | BindingFlags.NonPublic);
      if (sdkVersionProperty == null) {
        LogError("Firebase.VersionInfo.SdkVersion property not found via reflection.");
        return _unknownSdkVersion;
      }

      return sdkVersionProperty.GetValue(null) as string ?? _unknownSdkVersion;
    } catch (Exception e) {
      LogError($"Error accessing SdkVersion via reflection: {e}");
      return _unknownSdkVersion;
    }
  });

  // Gets the internal property Firebase.VersionInfo.SdkVersion
  internal static string GetVersionInfoSdkVersion() {
    return _sdkVersionFetcher.Value;
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

  // Cache the various types and methods needed for Auth token retrieval.
  private static void InitializeAuthReflection() {
    const string firebaseAuthTypeName = "Firebase.Auth.FirebaseAuth, Firebase.Auth";
    const string getTokenMethodName = "TokenAsync";

    try {
      // Set this to false, to allow easy failing out via return.
      _authReflectionInitialized = false;

      _authType = Type.GetType(firebaseAuthTypeName);
      if (_authType == null) {
        // Auth assembly likely not present, fine to skip
        return;
      }

      // Get the static method GetAuth(FirebaseApp app):
      _authGetAuthMethod = _authType.GetMethod(
          "GetAuth", BindingFlags.Static | BindingFlags.Public, null,
          new Type[] { typeof(FirebaseApp) }, null);
      if (_authGetAuthMethod == null) {
        LogError("Could not find FirebaseAuth.GetAuth method via reflection.");
        return;
      }

      // Get the CurrentUser property from FirebaseAuth instance
      _authCurrentUserProperty = _authType.GetProperty("CurrentUser", BindingFlags.Instance | BindingFlags.Public);
      if (_authCurrentUserProperty == null) {
        LogError("Could not find FirebaseAuth.CurrentUser property via reflection.");
        return;
      }

      // This should be FirebaseUser type
      Type userType = _authCurrentUserProperty.PropertyType;

      // Get the TokenAsync(bool) method from FirebaseUser
      _userTokenAsyncMethod = userType.GetMethod(
          getTokenMethodName, BindingFlags.Instance | BindingFlags.Public, null,
          new Type[] { typeof(bool) }, null);
      if (_userTokenAsyncMethod == null) {
        LogError($"Could not find FirebaseUser.{getTokenMethodName}(bool) method via reflection.");
        return;
      }

      // The return type is Task<string>
      Type tokenTaskType = _userTokenAsyncMethod.ReturnType;

      // Get the Result property from Task<string>
      _userTokenTaskResultProperty = tokenTaskType.GetProperty("Result");
      if (_userTokenTaskResultProperty == null) {
        LogError("Could not find Result property on Auth token Task.");
        return;
      }

      // Check if Result property is actually a string
      if (_userTokenTaskResultProperty.PropertyType != typeof(string)) {
          LogError("Auth token Task's Result property is not a string, " +
              $"but is {_userTokenTaskResultProperty.PropertyType}");
          return;
      }

      _authReflectionInitialized = true;
    } catch (Exception e) {
      LogError($"Exception during static initialization of Auth reflection in FirebaseInterops: {e}");
      _authReflectionInitialized = false;
    }
  }

  // Gets the Auth Token, assuming there is one. Otherwise, returns null.
  internal static async Task<string> GetAuthTokenAsync(FirebaseApp firebaseApp) {
    // If Auth reflection failed for any reason, nothing to do.
    if (!_authReflectionInitialized) {
      return null;
    }

    try {
      // Get the FirebaseAuth instance for the given FirebaseApp.
      object authInstance = _authGetAuthMethod.Invoke(null, new object[] { firebaseApp });
      if (authInstance == null) {
        LogError("Failed to get FirebaseAuth instance via reflection.");
        return null;
      }

      // Get the CurrentUser property
      object currentUser = _authCurrentUserProperty.GetValue(authInstance);
      if (currentUser == null) {
        // No user logged in, so no token
        return null;
      }

      // Invoke TokenAsync(false) - returns a Task<string>
      object taskObject = _userTokenAsyncMethod.Invoke(currentUser, new object[] { false });
      if (taskObject is not Task tokenTask) {
        LogError("Invoking TokenAsync did not return a Task.");
        return null;
      }

      // Await the task to get the token result
      await tokenTask;

      // Check for exceptions in the task
      if (tokenTask.IsFaulted) {
        LogError($"Error getting Auth token: {tokenTask.Exception}");
        return null;
      }

      // Get the Result property (which is the string token)
      return _userTokenTaskResultProperty.GetValue(tokenTask) as string;
    } catch (Exception e) {
      // Log any exceptions during the reflection/invocation process
      LogError($"An error occurred while trying to fetch Auth token: {e}");
    }
    return null;
  }

  // Adds the other Firebase tokens to the HttpRequest, as available.
  internal static async Task AddFirebaseTokensAsync(HttpRequestMessage request, FirebaseApp firebaseApp) {
    string appCheckToken = await GetAppCheckTokenAsync(firebaseApp);
    if (!string.IsNullOrEmpty(appCheckToken)) {
      request.Headers.Add(appCheckHeader, appCheckToken);
    }

    string authToken = await GetAuthTokenAsync(firebaseApp);
    if (!string.IsNullOrEmpty(authToken)) {
      request.Headers.Add(authHeader, $"Firebase {authToken}");
    }
  }

  // Adds the other Firebase tokens to the WebSocket, as available.
  internal static async Task AddFirebaseTokensAsync(ClientWebSocket socket, FirebaseApp firebaseApp) {
    string appCheckToken = await GetAppCheckTokenAsync(firebaseApp);
    if (!string.IsNullOrEmpty(appCheckToken)) {
      socket.Options.SetRequestHeader(appCheckHeader, appCheckToken);
    }

    string authToken = await GetAuthTokenAsync(firebaseApp);
    if (!string.IsNullOrEmpty(authToken)) {
      socket.Options.SetRequestHeader(authHeader, $"Firebase {authToken}");
    }
  }
}

}
