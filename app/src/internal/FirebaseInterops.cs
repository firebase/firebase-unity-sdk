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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Firebase.Internal {
  // Contains internal helper methods for interacting with other Firebase libraries.
  //
  // The static state here is reflection metadata (Types / MethodInfos / PropertyInfos) that is
  // valid for the lifetime of the scripting domain, and it is populated by the static
  // constructor. Opt out of Unity's automatic statics cleanup (Unity 6.6+), which cannot be
  // combined with a static constructor and would otherwise leave the caches null after the
  // first play session when domain reload is disabled.
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal static class FirebaseInterops {
    private const string AppCheckHeader = "X-Firebase-AppCheck", AuthHeader = "Authorization";
    private static volatile Func<FirebaseApp, bool, Task<string>> _authProvider, _fidTokenProvider;
    private static volatile Func<FirebaseApp, bool, bool, Task<string>> _appCheckProvider;
    private static volatile Func<FirebaseApp, Task<string>> _fidIdProvider;

    private static readonly PropertyInfo _dataCollectionProp, _acResultProp, _acTokenProp, _authCurrentUser;
    private static readonly MethodInfo _acGetInstance, _acGetToken, _acGetLimitedToken, _authGetAuth, _userTokenAsync, _fidGetInstance, _fidGetIdAsync, _fidGetTokenAsync;
    private static readonly Lazy<string> _sdkVersion = new(() => { try { return VersionInfo.SdkVersion ?? "unknown"; } catch (Exception) { return "unknown"; } });
    private static readonly Lazy<string> _appVersion = new(() => { try { return Firebase.Platform.PlatformInformation.AppVersion ?? string.Empty; } catch (Exception) { return string.Empty; } });

    static FirebaseInterops() {
      Type[] appArg = { typeof(FirebaseApp) }, boolArg = { typeof(bool) };
      _dataCollectionProp = typeof(FirebaseApp).GetProperty("IsDataCollectionDefaultEnabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      // Cache the various types and methods needed for AppCheck token retrieval.
      Type acType = Type.GetType("Firebase.AppCheck.FirebaseAppCheck, Firebase.AppCheck");
      // Get the static method GetInstance(FirebaseApp app)
      _acGetInstance = GetPubMethod(acType, "GetInstance", isStatic: true, appArg);
      // Get the instance method GetAppCheckTokenAsync(bool forceRefresh)
      _acGetToken = GetPubMethod(acType, "GetAppCheckTokenAsync", isStatic: false, boolArg);
      // Get the instance method GetLimitedUseAppCheckTokenAsync()
      _acGetLimitedToken = GetPubMethod(acType, "GetLimitedUseAppCheckTokenAsync", isStatic: false, Type.EmptyTypes);
      // Get the Result property from the Task<AppCheckToken>
      _acResultProp = _acGetToken?.ReturnType.GetProperty("Result");
      _acTokenProp = _acResultProp?.PropertyType.GetProperty("Token");
      // Cache the various types and methods needed for Auth token retrieval.
      Type authType = Type.GetType("Firebase.Auth.FirebaseAuth, Firebase.Auth");
      // Get the static method GetAuth(FirebaseApp app):
      _authGetAuth = GetPubMethod(authType, "GetAuth", isStatic: true, appArg);
      // Get the CurrentUser property from FirebaseAuth instance
      _authCurrentUser = authType?.GetProperty("CurrentUser", BindingFlags.Instance | BindingFlags.Public);
      // Get the TokenAsync(bool) method from FirebaseUser
      _userTokenAsync = GetPubMethod(_authCurrentUser?.PropertyType, "TokenAsync", isStatic: false, boolArg);
      Type fidType = Type.GetType("Firebase.Installations.FirebaseInstallations, Firebase.Installations");
      _fidGetInstance = GetPubMethod(fidType, "GetInstance", isStatic: true, appArg);
      _fidGetIdAsync = GetPubMethod(fidType, "GetIdAsync", isStatic: false, Type.EmptyTypes);
      _fidGetTokenAsync = GetPubMethod(fidType, "GetTokenAsync", isStatic: false, boolArg);
    }

    private static MethodInfo GetPubMethod(Type t, string name, bool isStatic, Type[] args) =>
        t?.GetMethod(name, BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance), null, args, null);

    internal static void RegisterAuthTokenProvider(Func<FirebaseApp, bool, Task<string>> provider) => _authProvider = provider;
    internal static void RegisterAppCheckTokenProvider(Func<FirebaseApp, bool, bool, Task<string>> provider) => _appCheckProvider = provider;
    internal static void RegisterInstallationsProvider(Func<FirebaseApp, Task<string>> idProvider, Func<FirebaseApp, bool, Task<string>> tokenProvider) {
      _fidIdProvider = idProvider;
      _fidTokenProvider = tokenProvider;
    }

    // Gets the property FirebaseApp.IsDataCollectionDefaultEnabled.
    public static bool GetIsDataCollectionDefaultEnabled(FirebaseApp firebaseApp) {
      if (firebaseApp == null || _dataCollectionProp?.PropertyType != typeof(bool)) return false;
      try { return (bool)_dataCollectionProp.GetValue(firebaseApp); } catch (Exception) { return false; }
    }

    // Gets the internal property Firebase.VersionInfo.SdkVersion
    internal static string GetVersionInfoSdkVersion() => _sdkVersion.Value;

    // Gets the application version from PlatformInformation.
    internal static string GetApplicationVersion() => _appVersion.Value;

    internal static string PtrToStringUtf8(IntPtr ptr) {
      if (ptr == IntPtr.Zero) return null;
      int len = 0;
      while (Marshal.ReadByte(ptr, len) != 0) {
        len++;
      }
      if (len == 0) return string.Empty;
      byte[] buffer = new byte[len];
      Marshal.Copy(ptr, buffer, 0, len);
      return Encoding.UTF8.GetString(buffer);
    }

    // Gets the AppCheck Token, assuming there is one. Otherwise, returns null.
    internal static Task<string> GetAppCheckTokenAsync(FirebaseApp app, bool limitedUse = false, bool forceRefresh = false) {
      if (app == null) return Task.FromResult<string>(null);
      if (_appCheckProvider is var direct && direct != null) return SafeCallAsync(() => direct(app, limitedUse, forceRefresh));
      // If AppCheck reflection failed for any reason, nothing to do.
      if (_acGetInstance == null || _acTokenProp == null) return Task.FromResult<string>(null);
      return SafeCallAsync(async () => {
        // Get the FirebaseAppCheck instance for the current FirebaseApp
        object inst = _acGetInstance.Invoke(null, new object[] { app });
        object taskObj = inst == null ? null : (limitedUse ? _acGetLimitedToken?.Invoke(inst, null) : _acGetToken?.Invoke(inst, new object[] { forceRefresh }));
        if (taskObj is not Task task) return null;
        // Await the task to get the AppCheckToken result
        await task.ConfigureAwait(false);
        // Get the Result property from the Task<AppCheckToken>
        // Get the Token property from the AppCheckToken struct
        return _acResultProp.GetValue(task) is object res ? _acTokenProp.GetValue(res) as string : null;
      });
    }

    // Gets the Auth Token, assuming there is one. Otherwise, returns null.
    internal static Task<string> GetAuthTokenAsync(FirebaseApp app, bool forceRefresh = false) {
      if (app == null) return Task.FromResult<string>(null);
      if (_authProvider is var direct && direct != null) return SafeCallAsync(() => direct(app, forceRefresh));
      // If Auth reflection failed for any reason, nothing to do.
      return _authGetAuth == null || _userTokenAsync == null ? Task.FromResult<string>(null)
          : SafeCallAsync(() => _authGetAuth.Invoke(null, new object[] { app }) is object a && _authCurrentUser.GetValue(a) is object u
              ? _userTokenAsync.Invoke(u, new object[] { forceRefresh }) as Task<string> : null);
    }

    internal static Task<string> GetInstallationIdAsync(FirebaseApp app) =>
        InvokeFidAsync(app, _fidIdProvider is var d && d != null ? () => d(app) : null, _fidGetIdAsync, null);
    internal static Task<string> GetInstallationTokenAsync(FirebaseApp app, bool forceRefresh = false) =>
        InvokeFidAsync(app, _fidTokenProvider is var d && d != null ? () => d(app, forceRefresh) : null, _fidGetTokenAsync, new object[] { forceRefresh });
    internal static async Task<(string Id, string AuthToken)> GetInstallationIdentityAsync(FirebaseApp app, bool forceRefresh = false) =>
        (await GetInstallationIdAsync(app).ConfigureAwait(false), await GetInstallationTokenAsync(app, forceRefresh).ConfigureAwait(false));

    // Adds the other Firebase tokens to the HttpRequest, as available.
    internal static async Task AddFirebaseTokensAsync(HttpRequestMessage request, FirebaseApp app, string authTokenPrefix = "Firebase", bool limitedUseAppCheckTokens = false) {
      if (await GetAppCheckTokenAsync(app, limitedUseAppCheckTokens).ConfigureAwait(false) is string ac && ac.Length > 0) request.Headers.TryAddWithoutValidation(AppCheckHeader, ac);
      if (await GetAuthTokenAsync(app).ConfigureAwait(false) is string auth && auth.Length > 0) request.Headers.TryAddWithoutValidation(AuthHeader, $"{authTokenPrefix} {auth}");
    }

    // Adds the other Firebase tokens to the WebSocket, as available.
    internal static async Task AddFirebaseTokensAsync(ClientWebSocket socket, FirebaseApp app, string authTokenPrefix = "Firebase", bool limitedUseAppCheckTokens = false) {
      if (await GetAppCheckTokenAsync(app, limitedUseAppCheckTokens).ConfigureAwait(false) is string ac && ac.Length > 0) socket.Options.SetRequestHeader(AppCheckHeader, ac);
      if (await GetAuthTokenAsync(app).ConfigureAwait(false) is string auth && auth.Length > 0) socket.Options.SetRequestHeader(AuthHeader, $"{authTokenPrefix} {auth}");
    }

    private static Task<string> InvokeFidAsync(FirebaseApp app, Func<Task<string>> direct, MethodInfo method, object[] args) =>
        app == null ? Task.FromResult<string>(null) : direct != null ? SafeCallAsync(direct)
        : (_fidGetInstance == null || method == null ? Task.FromResult<string>(null)
            : SafeCallAsync(() => _fidGetInstance.Invoke(null, new object[] { app }) is object i ? method.Invoke(i, args) as Task<string> : null));

    private static async Task<string> SafeCallAsync(Func<Task<string>> call) {
      try { return call() is Task<string> t ? await t.ConfigureAwait(false) : null; } catch (Exception) { return null; }
    }
  }
}
