// Copyright 2026 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase {

#if UNITY_6000_6_OR_NEWER
[global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
internal static class MobileAppBridge {
  internal static Func<AppOptions> DefaultOptionsOverride { get; set; }
  internal static Func<DependencyStatus> CheckDependenciesOverride { get; set; }
  internal static Func<Task> FixDependenciesOverride { get; set; }
  internal static Action<string, AppOptions> NativeAppInitObserver { get; set; }

  internal static DependencyStatus MapGmsConnectionResult(int connectionResult) => connectionResult switch {
    0 => DependencyStatus.Available,
    1 => DependencyStatus.UnavilableMissing,
    2 => DependencyStatus.UnavailableUpdaterequired,
    3 => DependencyStatus.UnavailableDisabled,
    9 => DependencyStatus.UnavailableInvalid,
    18 => DependencyStatus.UnavailableUpdating,
    19 => DependencyStatus.UnavailablePermission,
    _ => DependencyStatus.UnavailableOther,
  };

  internal static bool IsValidAppleApiKey(string key) {
    if (key == null || key.Length != 39 || key[0] != 'A') return false;
    foreach (char c in key) {
      if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_')) return false;
    }
    return true;
  }

  internal static bool IsValidAppleAppName(string name) {
    if (string.IsNullOrEmpty(name)) return false;
    foreach (char c in name) {
      if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_')) return false;
    }
    return true;
  }

  internal static bool IsValidAppleAppId(string appId) {
    if (string.IsNullOrEmpty(appId)) return false;
    string[] parts = appId.Split(':');
    if (parts.Length != 4 || parts[0] != "1" || parts[1].Length == 0 || parts[3].Length == 0) return false;
    if (parts[2] != "ios" && parts[2] != "tvos" && parts[2] != "macos") return false;
    foreach (char d in parts[1]) { if (d < '0' || d > '9') return false; }
    foreach (char h in parts[3]) {
      if (!((h >= '0' && h <= '9') || (h >= 'a' && h <= 'f') || (h >= 'A' && h <= 'F'))) return false;
    }
    return true;
  }

  internal static bool UsesAsyncAndroidDependencyCheck() {
    if (CheckDependenciesOverride != null) return true;
#if (UNITY_ANDROID && !UNITY_EDITOR) || FIREBASE_APP_ANDROID_BRIDGE
    return IsAndroidPlayer();
#else
    return false;
#endif
  }

  internal static AppOptions TryLoadDefaultOptions() {
    if (DefaultOptionsOverride is Func<AppOptions> hook) return hook();
#if (UNITY_ANDROID && !UNITY_EDITOR) || FIREBASE_APP_ANDROID_BRIDGE
    return IsAndroidPlayer() ? RunJni(TryLoadDefaultOptionsAndroidCore) : null;
#elif (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
    return IsApplePlayer() ? TryLoadDefaultOptionsApple() : null;
#else
    return null;
#endif
  }

  internal static void TryInitializeNativeApp(string name, AppOptions options, ref bool dataCollectionEnabled) {
    NativeAppInitObserver?.Invoke(name, options);
#if (UNITY_ANDROID && !UNITY_EDITOR) || FIREBASE_APP_ANDROID_BRIDGE
    if (IsAndroidPlayer()) {
      bool initial = dataCollectionEnabled;
      dataCollectionEnabled = RunJni(() => TryInitializeNativeAppAndroidCore(name, options, initial));
    }
#elif (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
    if (IsApplePlayer()) TryInitializeNativeAppApple(name, options, ref dataCollectionEnabled);
#endif
  }

  internal static void TrySetDataCollectionDefaultEnabled(string name, bool enabled) {
#if (UNITY_ANDROID && !UNITY_EDITOR) || FIREBASE_APP_ANDROID_BRIDGE
    if (IsAndroidPlayer()) RunJni(() => { TrySetDataCollectionAndroidCore(name, enabled); return true; });
#elif (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
    if (IsApplePlayer()) TrySetDataCollectionApple(name, enabled);
#endif
  }

  internal static DependencyStatus CheckDependencies() {
    if (CheckDependenciesOverride is Func<DependencyStatus> hook) return hook();
#if (UNITY_ANDROID && !UNITY_EDITOR) || FIREBASE_APP_ANDROID_BRIDGE
    if (IsAndroidPlayer()) return RunJni(CheckAndroidDependenciesCore);
#endif
    return DependencyStatus.Available;
  }

  internal static Task FixDependenciesAsync() {
    if (FixDependenciesOverride is Func<Task> hook) return hook();
#if (UNITY_ANDROID && !UNITY_EDITOR) || FIREBASE_APP_ANDROID_BRIDGE
    if (IsAndroidPlayer()) return FixAndroidDependenciesCoreAsync();
#endif
    return Task.CompletedTask;
  }

  internal static void CaptureMainThread() {
#if (UNITY_ANDROID && !UNITY_EDITOR) || FIREBASE_APP_ANDROID_BRIDGE
    _mainThreadId = Thread.CurrentThread.ManagedThreadId;
#endif
  }

#if (UNITY_ANDROID && !UNITY_EDITOR) || FIREBASE_APP_ANDROID_BRIDGE
  private static volatile int _mainThreadId;
  private static volatile int _androidPlayerState;

  private static bool IsAndroidPlayer() {
    int cached = _androidPlayerState;
    if (cached != 0) return cached > 0;
    try {
      bool result = IsAndroidPlayerCore();
      _androidPlayerState = result ? 1 : -1;
      return result;
    } catch (Exception) {
      _androidPlayerState = -1;
      return false;
    }
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static bool IsAndroidPlayerCore() =>
      !UnityEngine.Application.isEditor &&
      UnityEngine.Application.platform == UnityEngine.RuntimePlatform.Android;

  // Runs JNI work directly on Unity's main thread, or on a dedicated owned worker thread so
  // AttachCurrentThread/DetachCurrentThread never touches a Java binder/executor or shared pool thread.
  private static T RunJni<T>(Func<T> action) {
    int mainId = _mainThreadId;
    if ((mainId != 0 && Thread.CurrentThread.ManagedThreadId == mainId) ||
        Platform.FirebaseHandler.DefaultInstance?.IsMainThread() == true) {
      return action();
    }
    T result = default;
    ExceptionDispatchInfo captured = null;
    var worker = new Thread(() => {
      UnityEngine.AndroidJNI.AttachCurrentThread();
      try {
        result = action();
      } catch (Exception ex) {
        captured = ExceptionDispatchInfo.Capture(ex);
      } finally {
        UnityEngine.AndroidJNI.DetachCurrentThread();
      }
    }) { IsBackground = true, Name = "FirebaseJniBridge" };
    worker.Start();
    worker.Join();
    captured?.Throw();
    return result;
  }

  private static UnityEngine.AndroidJavaObject GetCurrentActivity() {
    using var unityPlayer = new UnityEngine.AndroidJavaClass("com.unity3d.player.UnityPlayer");
    return unityPlayer.GetStatic<UnityEngine.AndroidJavaObject>("currentActivity");
  }

  private static AppOptions TryLoadDefaultOptionsAndroidCore() {
    try {
      using var activity = GetCurrentActivity();
      if (activity == null) return null;
      string pkgName = activity.Call<string>("getPackageName");
      try {
        using var optsClass = new UnityEngine.AndroidJavaClass("com.google.firebase.FirebaseOptions");
        using var fbOpts = optsClass.CallStatic<UnityEngine.AndroidJavaObject>("fromResource", activity);
        if (fbOpts != null) {
          return new AppOptions {
            AppId = fbOpts.Call<string>("getApplicationId"),
            ApiKey = fbOpts.Call<string>("getApiKey"),
            DatabaseUrl = FirebaseApp.UrlStringToUri(fbOpts.Call<string>("getDatabaseUrl")),
            MessageSenderId = fbOpts.Call<string>("getGcmSenderId"),
            StorageBucket = fbOpts.Call<string>("getStorageBucket"),
            ProjectId = fbOpts.Call<string>("getProjectId"),
            PackageName = pkgName,
          };
        }
      } catch (Exception) {
        // FirebaseOptions absent or string resources not bundled; fall back to APK assets.
      }
      return TryLoadOptionsFromAndroidAssets(activity, pkgName);
    } catch (Exception) {
      return null;
    }
  }

  private static AppOptions TryLoadOptionsFromAndroidAssets(UnityEngine.AndroidJavaObject activity, string pkgName) {
    using var assets = activity.Call<UnityEngine.AndroidJavaObject>("getAssets");
    if (assets == null) return null;
    foreach (string assetName in new[] { "google-services-desktop.json", "google-services.json" }) {
      try {
        using var stream = assets.Call<UnityEngine.AndroidJavaObject>("open", assetName);
        if (stream == null) continue;
        using var scanner = new UnityEngine.AndroidJavaObject("java.util.Scanner", stream, "UTF-8");
        using var delimited = scanner.Call<UnityEngine.AndroidJavaObject>("useDelimiter", "\\A");
        string json = delimited != null && delimited.Call<bool>("hasNext") ? delimited.Call<string>("next") : string.Empty;
        AppOptions parsed = AppConfigLoader.LoadFromJsonConfig(json, pkgName);
        if (parsed != null) return parsed;
      } catch (Exception) {
        // Asset file not present in APK StreamingAssets; try next candidate.
      }
    }
    return null;
  }

  private static bool TryInitializeNativeAppAndroidCore(string name, AppOptions options, bool defaultCollection) {
    try {
      using var appCls = new UnityEngine.AndroidJavaClass("com.google.firebase.FirebaseApp");
      using var activity = GetCurrentActivity();
      if (activity == null) return defaultCollection;

      try {
        using var regCls = new UnityEngine.AndroidJavaClass("com.google.firebase.platforminfo.GlobalLibraryVersionRegistrar");
        using var reg = regCls.CallStatic<UnityEngine.AndroidJavaObject>("getInstance");
        reg?.Call("registerVersion", "fire-unity", VersionInfo.SdkVersion);
      } catch (Exception) {
        // Optional version registrar class not present.
      }

      bool isDefault = string.IsNullOrEmpty(name) || name == FirebaseApp.DefaultName;
      UnityEngine.AndroidJavaObject nativeApp = null;
      try {
        nativeApp = isDefault
            ? appCls.CallStatic<UnityEngine.AndroidJavaObject>("getInstance")
            : appCls.CallStatic<UnityEngine.AndroidJavaObject>("getInstance", name);
      } catch (Exception) {
        nativeApp = null;
      }
      using (nativeApp) {
        if (nativeApp != null) return ReadDataCollectionFlag(nativeApp, defaultCollection);
      }
      if (string.IsNullOrEmpty(options?.AppId) || string.IsNullOrEmpty(options.ApiKey)) return defaultCollection;

      using var builder = new UnityEngine.AndroidJavaObject("com.google.firebase.FirebaseOptions$Builder");
      SetBuilderOption(builder, "setApiKey", options.ApiKey);
      SetBuilderOption(builder, "setApplicationId", options.AppId);
      SetBuilderOption(builder, "setDatabaseUrl", options.DatabaseUrl != null ? FirebaseApp.UriToUrlString(options.DatabaseUrl) : null);
      SetBuilderOption(builder, "setGcmSenderId", options.MessageSenderId);
      SetBuilderOption(builder, "setStorageBucket", options.StorageBucket);
      SetBuilderOption(builder, "setProjectId", options.ProjectId);

      using var jOpts = builder.Call<UnityEngine.AndroidJavaObject>("build");
      using var created = isDefault
          ? appCls.CallStatic<UnityEngine.AndroidJavaObject>("initializeApp", activity, jOpts)
          : appCls.CallStatic<UnityEngine.AndroidJavaObject>("initializeApp", activity, jOpts, name);
      if (created != null) return ReadDataCollectionFlag(created, defaultCollection);
    } catch (Exception) {
      // Native com.google.firebase.FirebaseApp not linked on classpath; pure-C# mode active.
    }
    return defaultCollection;
  }

  private static void SetBuilderOption(UnityEngine.AndroidJavaObject builder, string setter, string value) {
    if (!string.IsNullOrEmpty(value)) {
      using (builder.Call<UnityEngine.AndroidJavaObject>(setter, value)) { }
    }
  }

  private static bool ReadDataCollectionFlag(UnityEngine.AndroidJavaObject jApp, bool fallback) {
    try { return jApp.Call<bool>("isDataCollectionDefaultEnabled"); }
    catch (Exception) {
      // Fallback when method unavailable.
      return fallback;
    }
  }

  private static void TrySetDataCollectionAndroidCore(string name, bool enabled) {
    try {
      using var appCls = new UnityEngine.AndroidJavaClass("com.google.firebase.FirebaseApp");
      bool isDefault = string.IsNullOrEmpty(name) || name == FirebaseApp.DefaultName;
      using var jApp = isDefault
          ? appCls.CallStatic<UnityEngine.AndroidJavaObject>("getInstance")
          : appCls.CallStatic<UnityEngine.AndroidJavaObject>("getInstance", name);
      jApp?.Call("setDataCollectionDefaultEnabled", enabled);
    } catch (Exception) {
      // Native FirebaseApp not initialized or absent on classpath.
    }
  }

  private static DependencyStatus CheckAndroidDependenciesCore() {
    try {
      using var activity = GetCurrentActivity();
      if (activity == null) return DependencyStatus.UnavailableOther;
      using var gaaCls = new UnityEngine.AndroidJavaClass("com.google.android.gms.common.GoogleApiAvailability");
      using var gaa = gaaCls.CallStatic<UnityEngine.AndroidJavaObject>("getInstance");
      return gaa == null
          ? DependencyStatus.UnavailableOther
          : MapGmsConnectionResult(gaa.Call<int>("isGooglePlayServicesAvailable", activity));
    } catch (Exception) {
      return DependencyStatus.UnavailableOther;
    }
  }

  private sealed class GmsCompleteListener : UnityEngine.AndroidJavaProxy {
    private readonly TaskCompletionSource<bool> _tcs;
    internal GmsCompleteListener(TaskCompletionSource<bool> tcs)
        : base("com.google.android.gms.tasks.OnCompleteListener") => _tcs = tcs;

    public void onComplete(UnityEngine.AndroidJavaObject task) {
      try {
        using (task) {
          if (task != null && task.Call<bool>("isSuccessful")) {
            _tcs.TrySetResult(true);
          } else {
            using var ex = task?.Call<UnityEngine.AndroidJavaObject>("getException");
            _tcs.TrySetException(new FirebaseException(-1, ex?.Call<string>("getMessage") ?? "Failed to make Google Play services available."));
          }
        }
      } catch (Exception e) {
        _tcs.TrySetException(new FirebaseException(-1, e.Message, e));
      }
    }
  }

  private static Task FixAndroidDependenciesCoreAsync() {
    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    RunJni(() => {
      try {
        using var activity = GetCurrentActivity();
        if (activity == null) {
          tcs.TrySetException(new FirebaseException(-2, "Android currentActivity is unavailable."));
          return false;
        }
        activity.Call("runOnUiThread", new UnityEngine.AndroidJavaRunnable(() => {
          try {
            using var act = GetCurrentActivity();
            using var gaaCls = new UnityEngine.AndroidJavaClass("com.google.android.gms.common.GoogleApiAvailability");
            using var gaa = gaaCls.CallStatic<UnityEngine.AndroidJavaObject>("getInstance");
            using var gmsTask = gaa?.Call<UnityEngine.AndroidJavaObject>("makeGooglePlayServicesAvailable", act);
            if (gmsTask == null) {
              tcs.TrySetException(new FirebaseException(-2, "GoogleApiAvailability.makeGooglePlayServicesAvailable returned null."));
              return;
            }
            using var _ = gmsTask.Call<UnityEngine.AndroidJavaObject>("addOnCompleteListener", new GmsCompleteListener(tcs));
          } catch (Exception e) {
            tcs.TrySetException(new FirebaseException(-2, e.Message, e));
          }
        }));
      } catch (Exception e) {
        tcs.TrySetException(new FirebaseException(-2, e.Message, e));
      }
      return true;
    });
    return tcs.Task;
  }

#elif (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
  private const string LibObjC = "/usr/lib/libobjc.A.dylib";

  [DllImport(LibObjC)] private static extern IntPtr objc_getClass(string name);
  [DllImport(LibObjC)] private static extern IntPtr sel_registerName(string name);
  [DllImport(LibObjC)] private static extern IntPtr objc_autoreleasePoolPush();
  [DllImport(LibObjC)] private static extern void objc_autoreleasePoolPop(IntPtr pool);
  [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector);
  [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtrPtr(IntPtr receiver, IntPtr selector, IntPtr a1);
  [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtrPtrPtr(IntPtr receiver, IntPtr selector, IntPtr a1, IntPtr a2);
  [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void SendVoidPtr(IntPtr receiver, IntPtr selector, IntPtr a1);
  [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void SendVoidPtrPtr(IntPtr receiver, IntPtr selector, IntPtr a1, IntPtr a2);
  [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void SendVoidBool(IntPtr receiver, IntPtr selector, byte a1);
  [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern byte SendBool(IntPtr receiver, IntPtr selector);
  [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern byte SendBoolPtr(IntPtr receiver, IntPtr selector, IntPtr a1);

  private static bool IsApplePlayer() {
    try { return IsApplePlayerCore(); }
    catch (Exception) {
      // Headless .NET runtime without UnityEngine ECalls.
      return false;
    }
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static bool IsApplePlayerCore() => !UnityEngine.Application.isEditor && Platform.PlatformInformation.IsIOS;

  private static IntPtr CreateNSString(string value) {
    if (value == null) return IntPtr.Zero;
    IntPtr nsStringCls = objc_getClass("NSString");
    if (nsStringCls == IntPtr.Zero) return IntPtr.Zero;
    byte[] utf8 = Encoding.UTF8.GetBytes(value + "\0");
    GCHandle handle = GCHandle.Alloc(utf8, GCHandleType.Pinned);
    try { return SendPtrPtr(nsStringCls, sel_registerName("stringWithUTF8String:"), handle.AddrOfPinnedObject()); }
    finally { handle.Free(); }
  }

  private static string ReadNSString(IntPtr nsStr) {
    if (nsStr == IntPtr.Zero) return null;
    IntPtr utf8Ptr = SendPtr(nsStr, sel_registerName("UTF8String"));
    if (utf8Ptr == IntPtr.Zero) return null;
    int len = 0;
    while (Marshal.ReadByte(utf8Ptr, len) != 0) {
      len++;
    }
    if (len == 0) return string.Empty;
    byte[] buffer = new byte[len];
    Marshal.Copy(utf8Ptr, buffer, 0, len);
    return Encoding.UTF8.GetString(buffer);
  }

  private static string GetAppleMainBundlePathAndId(out string bundleId) {
    IntPtr bundleCls = objc_getClass("NSBundle");
    IntPtr mainBundle = bundleCls != IntPtr.Zero ? SendPtr(bundleCls, sel_registerName("mainBundle")) : IntPtr.Zero;
    bundleId = mainBundle != IntPtr.Zero ? ReadNSString(SendPtr(mainBundle, sel_registerName("bundleIdentifier"))) : null;
    return mainBundle != IntPtr.Zero
        ? ReadNSString(SendPtrPtrPtr(mainBundle, sel_registerName("pathForResource:ofType:"), CreateNSString("GoogleService-Info"), CreateNSString("plist")))
        : null;
  }

  private static string ReadNSDictionaryScalar(IntPtr dict, IntPtr objForKeySel, IntPtr kindSel, IntPtr strCls, IntPtr numCls, string key) {
    IntPtr val = SendPtrPtr(dict, objForKeySel, CreateNSString(key));
    if (val == IntPtr.Zero) return null;
    if (strCls != IntPtr.Zero && SendBoolPtr(val, kindSel, strCls) != 0) return ReadNSString(val);
    if (numCls != IntPtr.Zero && SendBoolPtr(val, kindSel, numCls) != 0) return ReadNSString(SendPtr(val, sel_registerName("stringValue")));
    return null;
  }

  private static AppOptions TryLoadOptionsFromNSDictionary(string plistPath, string bundleId) {
    IntPtr dictCls = objc_getClass("NSDictionary");
    IntPtr nsDict = dictCls != IntPtr.Zero
        ? SendPtrPtr(dictCls, sel_registerName("dictionaryWithContentsOfFile:"), CreateNSString(plistPath))
        : IntPtr.Zero;
    if (nsDict == IntPtr.Zero) return null;
    IntPtr objForKeySel = sel_registerName("objectForKey:");
    IntPtr kindSel = sel_registerName("isKindOfClass:");
    IntPtr strCls = objc_getClass("NSString"), numCls = objc_getClass("NSNumber");
    string appId = ReadNSDictionaryScalar(nsDict, objForKeySel, kindSel, strCls, numCls, "GOOGLE_APP_ID");
    string apiKey = ReadNSDictionaryScalar(nsDict, objForKeySel, kindSel, strCls, numCls, "API_KEY");
    string projId = ReadNSDictionaryScalar(nsDict, objForKeySel, kindSel, strCls, numCls, "PROJECT_ID");
    string bucket = ReadNSDictionaryScalar(nsDict, objForKeySel, kindSel, strCls, numCls, "STORAGE_BUCKET");
    string sender = ReadNSDictionaryScalar(nsDict, objForKeySel, kindSel, strCls, numCls, "GCM_SENDER_ID");
    string dbUrl = ReadNSDictionaryScalar(nsDict, objForKeySel, kindSel, strCls, numCls, "DATABASE_URL");
    string pkg = ReadNSDictionaryScalar(nsDict, objForKeySel, kindSel, strCls, numCls, "BUNDLE_ID");
    if (string.IsNullOrEmpty(appId) && string.IsNullOrEmpty(apiKey) && string.IsNullOrEmpty(projId)) return null;
    return new AppOptions {
      AppId = appId, ApiKey = apiKey, ProjectId = projId, StorageBucket = bucket,
      MessageSenderId = sender, DatabaseUrl = FirebaseApp.UrlStringToUri(dbUrl),
      PackageName = !string.IsNullOrEmpty(pkg) ? pkg : bundleId,
    };
  }

  private static AppOptions TryLoadDefaultOptionsApple() {
    IntPtr pool = objc_autoreleasePoolPush();
    try {
      string plistPath = GetAppleMainBundlePathAndId(out string bundleId);
      if (!string.IsNullOrEmpty(plistPath)) {
        // NSDictionary handles Xcode's compiled binary bplist00 as well as XML/OpenStep plists.
        AppOptions fromDict = TryLoadOptionsFromNSDictionary(plistPath, bundleId);
        if (fromDict != null) return fromDict;
        if (File.Exists(plistPath)) {
          AppOptions fromText = AppConfigLoader.LoadFromPlistConfig(File.ReadAllText(plistPath, Encoding.UTF8));
          if (fromText != null) {
            if (string.IsNullOrEmpty(fromText.PackageName)) fromText.PackageName = bundleId;
            return fromText;
          }
        }
      }

      IntPtr firOptsCls = objc_getClass("FIROptions");
      IntPtr defOpts = firOptsCls != IntPtr.Zero ? SendPtr(firOptsCls, sel_registerName("defaultOptions")) : IntPtr.Zero;
      if (defOpts != IntPtr.Zero) {
        return new AppOptions {
          AppId = ReadNSString(SendPtr(defOpts, sel_registerName("googleAppID"))),
          ApiKey = ReadNSString(SendPtr(defOpts, sel_registerName("APIKey"))),
          ProjectId = ReadNSString(SendPtr(defOpts, sel_registerName("projectID"))),
          StorageBucket = ReadNSString(SendPtr(defOpts, sel_registerName("storageBucket"))),
          MessageSenderId = ReadNSString(SendPtr(defOpts, sel_registerName("GCMSenderID"))),
          DatabaseUrl = FirebaseApp.UrlStringToUri(ReadNSString(SendPtr(defOpts, sel_registerName("databaseURL")))),
          PackageName = ReadNSString(SendPtr(defOpts, sel_registerName("bundleID"))) ?? bundleId,
        };
      }
      return null;
    } catch (Exception) {
      return null;
    } finally {
      if (pool != IntPtr.Zero) objc_autoreleasePoolPop(pool);
    }
  }

  private static void TryInitializeNativeAppApple(string name, AppOptions options, ref bool dataCollectionEnabled) {
    IntPtr pool = objc_autoreleasePoolPush();
    try {
      IntPtr firAppCls = objc_getClass("FIRApp"), firOptsCls = objc_getClass("FIROptions");
      if (firAppCls == IntPtr.Zero || firOptsCls == IntPtr.Zero) return;

      bool isDefault = string.IsNullOrEmpty(name) || name == FirebaseApp.DefaultName;
      IntPtr allApps = SendPtr(firAppCls, sel_registerName("allApps"));
      IntPtr existing = allApps != IntPtr.Zero
          ? SendPtrPtr(allApps, sel_registerName("objectForKey:"), CreateNSString(isDefault ? FirebaseApp.DefaultName : name))
          : IntPtr.Zero;
      if (existing != IntPtr.Zero) {
        dataCollectionEnabled = SendBool(existing, sel_registerName("isDataCollectionDefaultEnabled")) != 0;
        return;
      }

      // Guard against FIRApp.m NSExceptions (uncaught across P/Invoke on IL2CPP) and off-main-thread init.
      if ((!isDefault && !IsValidAppleAppName(name)) ||
          options == null || !IsValidAppleAppId(options.AppId) || !IsValidAppleApiKey(options.ApiKey)) {
        return;
      }
      IntPtr nsThreadCls = objc_getClass("NSThread");
      if (nsThreadCls == IntPtr.Zero || SendBool(nsThreadCls, sel_registerName("isMainThread")) == 0) return;

      IntPtr valSel = sel_registerName("validateAppID:");
      if (SendBoolPtr(firAppCls, sel_registerName("respondsToSelector:"), valSel) != 0 &&
          SendBoolPtr(firAppCls, valSel, CreateNSString(options.AppId)) == 0) {
        return;
      }

      string bundleId = options.PackageName;
      if (string.IsNullOrEmpty(bundleId)) GetAppleMainBundlePathAndId(out bundleId);
      if (string.IsNullOrEmpty(bundleId)) return;

      IntPtr regSel = sel_registerName("registerLibrary:withVersion:");
      if (SendBoolPtr(firAppCls, sel_registerName("respondsToSelector:"), regSel) != 0) {
        SendVoidPtrPtr(firAppCls, regSel, CreateNSString("fire-unity"), CreateNSString(VersionInfo.SdkVersion));
      }

      IntPtr firOpts = SendPtrPtrPtr(
          SendPtr(firOptsCls, sel_registerName("alloc")),
          sel_registerName("initWithGoogleAppID:GCMSenderID:"),
          CreateNSString(options.AppId),
          CreateNSString(options.MessageSenderId ?? string.Empty));
      if (firOpts == IntPtr.Zero) return;
      SendPtr(firOpts, sel_registerName("autorelease"));

      SendVoidPtr(firOpts, sel_registerName("setAPIKey:"), CreateNSString(options.ApiKey));
      SendVoidPtr(firOpts, sel_registerName("setBundleID:"), CreateNSString(bundleId));
      if (!string.IsNullOrEmpty(options.ProjectId)) SendVoidPtr(firOpts, sel_registerName("setProjectID:"), CreateNSString(options.ProjectId));
      if (!string.IsNullOrEmpty(options.StorageBucket)) SendVoidPtr(firOpts, sel_registerName("setStorageBucket:"), CreateNSString(options.StorageBucket));
      if (options.DatabaseUrl != null) SendVoidPtr(firOpts, sel_registerName("setDatabaseURL:"), CreateNSString(FirebaseApp.UriToUrlString(options.DatabaseUrl)));

      if (isDefault) SendVoidPtr(firAppCls, sel_registerName("configureWithOptions:"), firOpts);
      else SendVoidPtrPtr(firAppCls, sel_registerName("configureWithName:options:"), CreateNSString(name), firOpts);

      IntPtr configured = isDefault
          ? SendPtr(firAppCls, sel_registerName("defaultApp"))
          : SendPtrPtr(firAppCls, sel_registerName("appNamed:"), CreateNSString(name));
      if (configured != IntPtr.Zero) {
        dataCollectionEnabled = SendBool(configured, sel_registerName("isDataCollectionDefaultEnabled")) != 0;
      }
    } catch (Exception) {
      // Native FIRApp not linked or unavailable; pure-C# mode remains active.
    } finally {
      if (pool != IntPtr.Zero) objc_autoreleasePoolPop(pool);
    }
  }

  private static void TrySetDataCollectionApple(string name, bool enabled) {
    IntPtr pool = objc_autoreleasePoolPush();
    try {
      IntPtr firAppCls = objc_getClass("FIRApp");
      if (firAppCls == IntPtr.Zero) return;
      IntPtr allApps = SendPtr(firAppCls, sel_registerName("allApps"));
      IntPtr appObj = allApps != IntPtr.Zero
          ? SendPtrPtr(allApps, sel_registerName("objectForKey:"), CreateNSString(string.IsNullOrEmpty(name) ? FirebaseApp.DefaultName : name))
          : IntPtr.Zero;
      if (appObj != IntPtr.Zero) {
        SendVoidBool(appObj, sel_registerName("setDataCollectionDefaultEnabled:"), enabled ? (byte)1 : (byte)0);
      }
    } catch (Exception) {
      // Native FIRApp unavailable.
    } finally {
      if (pool != IntPtr.Zero) objc_autoreleasePoolPop(pool);
    }
  }
#endif
}

}  // namespace Firebase
