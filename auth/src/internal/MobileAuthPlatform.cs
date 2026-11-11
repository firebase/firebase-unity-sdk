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
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Auth.Internal {
  /// <summary>
  /// Platform-adaptive mobile hooks for interactive OAuth, Phone Auth, and Game Center.
  /// Probes native mobile SDKs dynamically when linked on Android/iOS without introducing
  /// mandatory C++/JNI/CocoaPods/SPM binary dependencies.
  /// </summary>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal static class MobileAuthPlatform {
    internal static Func<bool> GameCenterIsAuthenticatedOverrideForTesting { get; set; }
    internal static Func<Task<Credential>> GameCenterCredentialOverrideForTesting { get; set; }

    internal static bool IsGameCenterPlayerAuthenticated() {
      if (GameCenterIsAuthenticatedOverrideForTesting is Func<bool> testHook) {
        return testHook();
      }
#if (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
      return IsAppleGameCenterAuthenticated();
#else
      return false;
#endif
    }

    internal static async Task<Credential> GetGameCenterCredentialAsync() {
      if (GameCenterCredentialOverrideForTesting is Func<Task<Credential>> testHook) {
        return await testHook().ConfigureAwait(false);
      }
#if (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
      return await FetchAppleGameCenterCredentialAsync().ConfigureAwait(false);
#else
      await Task.CompletedTask.ConfigureAwait(false);
      throw AuthErrorMapper.CreateException(AuthError.InvalidCredential);
#endif
    }

    internal static async Task<Credential> TryInteractiveFederatedAuthAsync(
        FirebaseAuth auth,
        FederatedAuthProvider provider) {
      if (provider == null) throw AuthErrorMapper.CreateException(AuthError.InvalidCredential);
      var oauthProvider = provider as FederatedOAuthProvider;
      FederatedOAuthProviderData data = oauthProvider?.Data;
      if (data == null || string.IsNullOrEmpty(data.ProviderId)) {
        throw AuthErrorMapper.CreateException(AuthError.InvalidProviderId);
      }

#if UNITY_ANDROID && !UNITY_EDITOR
      if (!auth.Client.HasEmulator) {
        Task<Credential> androidTask = await Firebase.Platform.FirebaseHandler.RunOnMainThreadAsync(
            () => TryAndroidFederatedOAuthAsync(auth, data)).ConfigureAwait(false);
        Credential androidCred = androidTask != null ? await androidTask.ConfigureAwait(false) : null;
        if (androidCred != null) return androidCred;
      }
#endif
      await Task.CompletedTask.ConfigureAwait(false);
      throw AuthErrorMapper.CreateException(AuthError.Unimplemented);
    }

#if (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjCBlockDescriptor { public IntPtr reserved, size; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjCBlockLiteral {
      public IntPtr isa;
      public int flags, reserved;
      public IntPtr invoke, descriptor, context;
    }

    private sealed class GkBlockContext {
      internal TaskCompletionSource<Credential> Tcs;
      internal string PlayerId;
      internal string DisplayName;
      internal string TeamPlayerId;
      internal string GamePlayerId;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GkFetchItemsBlock(
        IntPtr block, IntPtr publicKeyUrl, IntPtr signatureData, IntPtr saltData, ulong timestamp, IntPtr error);

    [DllImport(LibObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(LibObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(LibObjC)] private static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(LibObjC)] private static extern void objc_autoreleasePoolPop(IntPtr pool);
    [DllImport(LibSystem)] private static extern IntPtr dlsym(IntPtr handle, string symbol);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr r, IntPtr s);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern nuint SendNUInt(IntPtr r, IntPtr s);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBool(IntPtr r, IntPtr s);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBoolSel(IntPtr r, IntPtr s, IntPtr argSel);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidBlock(IntPtr r, IntPtr s, IntPtr block);

    private static readonly int ContextOffset = Marshal.OffsetOf<ObjCBlockLiteral>(nameof(ObjCBlockLiteral.context)).ToInt32();
    private static readonly GkFetchItemsBlock GkFetchCb = OnGkFetchCompleted;
    private static IntPtr _gkDescriptorPtr;

    private static bool IsAppleGameCenterAuthenticated() {
      IntPtr pool = objc_autoreleasePoolPush();
      try {
        IntPtr clsGk = objc_getClass("GKLocalPlayer");
        if (clsGk == IntPtr.Zero) return false;
        IntPtr localPlayer = SendPtr(clsGk, sel_registerName("localPlayer"));
        return localPlayer != IntPtr.Zero && SendBool(localPlayer, sel_registerName("isAuthenticated"));
      } catch (Exception) {
        // GameKit unavailable in restricted environment.
        return false;
      } finally {
        objc_autoreleasePoolPop(pool);
      }
    }

    private static Task<Credential> FetchAppleGameCenterCredentialAsync() {
      IntPtr pool = objc_autoreleasePoolPush();
      try {
        IntPtr clsGk = objc_getClass("GKLocalPlayer");
        IntPtr stackIsa = dlsym(new IntPtr(-2), "_NSConcreteStackBlock");
        if (clsGk == IntPtr.Zero || stackIsa == IntPtr.Zero) {
          return Task.FromException<Credential>(AuthErrorMapper.CreateException(AuthError.InvalidCredential));
        }
        IntPtr localPlayer = SendPtr(clsGk, sel_registerName("localPlayer"));
        if (localPlayer == IntPtr.Zero || !SendBool(localPlayer, sel_registerName("isAuthenticated"))) {
          return Task.FromException<Credential>(AuthErrorMapper.CreateException(AuthError.InvalidCredential));
        }

        string playerId = ReadNSString(SendPtr(localPlayer, sel_registerName("playerID")));
        string displayName = ReadNSString(SendPtr(localPlayer, sel_registerName("displayName")));
        IntPtr selTeam = sel_registerName("teamPlayerID");
        string teamPlayerId = SendBoolSel(localPlayer, sel_registerName("respondsToSelector:"), selTeam)
            ? ReadNSString(SendPtr(localPlayer, selTeam))
            : string.Empty;
        IntPtr selGame = sel_registerName("gamePlayerID");
        string gamePlayerId = SendBoolSel(localPlayer, sel_registerName("respondsToSelector:"), selGame)
            ? ReadNSString(SendPtr(localPlayer, selGame))
            : string.Empty;

        var ctx = new GkBlockContext {
          Tcs = new TaskCompletionSource<Credential>(TaskCreationOptions.RunContinuationsAsynchronously),
          PlayerId = playerId,
          DisplayName = displayName,
          TeamPlayerId = teamPlayerId,
          GamePlayerId = gamePlayerId,
        };
        GCHandle gch = GCHandle.Alloc(ctx);
        IntPtr blockPtr = AllocGkStackBlock(stackIsa, Marshal.GetFunctionPointerForDelegate(GkFetchCb), GCHandle.ToIntPtr(gch));
        try {
          SendVoidBlock(localPlayer, sel_registerName("fetchItemsForIdentityVerificationSignature:"), blockPtr);
          return ctx.Tcs.Task;
        } catch (Exception) {
          if (gch.IsAllocated) gch.Free();
          throw;
        } finally {
          Marshal.FreeHGlobal(blockPtr);
        }
      } catch (Exception ex) {
        return Task.FromException<Credential>(AuthErrorMapper.CreateException(AuthError.InvalidCredential, ex));
      } finally {
        objc_autoreleasePoolPop(pool);
      }
    }

    private static IntPtr AllocGkStackBlock(IntPtr isa, IntPtr invokePtr, IntPtr context) {
      if (_gkDescriptorPtr == IntPtr.Zero) {
        IntPtr d = Marshal.AllocHGlobal(Marshal.SizeOf<ObjCBlockDescriptor>());
        Marshal.StructureToPtr(new ObjCBlockDescriptor { reserved = IntPtr.Zero, size = (IntPtr)Marshal.SizeOf<ObjCBlockLiteral>() }, d, false);
        if (Interlocked.CompareExchange(ref _gkDescriptorPtr, d, IntPtr.Zero) != IntPtr.Zero) Marshal.FreeHGlobal(d);
      }
      IntPtr m = Marshal.AllocHGlobal(Marshal.SizeOf<ObjCBlockLiteral>());
      Marshal.StructureToPtr(new ObjCBlockLiteral { isa = isa, invoke = invokePtr, descriptor = _gkDescriptorPtr, context = context }, m, false);
      return m;
    }

    private static string ReadNSString(IntPtr ns) =>
        ns != IntPtr.Zero && SendPtr(ns, sel_registerName("UTF8String")) is IntPtr p && p != IntPtr.Zero
            ? (Firebase.Internal.FirebaseInterops.PtrToStringUtf8(p) ?? string.Empty)
            : string.Empty;

    private static string ReadNSDataBase64Url(IntPtr nsData) {
      if (nsData == IntPtr.Zero) return string.Empty;
      int len = (int)SendNUInt(nsData, sel_registerName("length"));
      IntPtr ptr = SendPtr(nsData, sel_registerName("bytes"));
      if (len <= 0 || ptr == IntPtr.Zero) return string.Empty;
      byte[] buf = new byte[len];
      Marshal.Copy(ptr, buf, 0, len);
      return Credential.ToUnpaddedBase64Url(Convert.ToBase64String(buf));
    }

    [AOT.MonoPInvokeCallback(typeof(GkFetchItemsBlock))]
    private static void OnGkFetchCompleted(
        IntPtr block, IntPtr publicKeyUrl, IntPtr signatureData, IntPtr saltData, ulong timestamp, IntPtr error) {
      IntPtr pool = objc_autoreleasePoolPush();
      GkBlockContext ctx = null;
      try {
        GCHandle g = GCHandle.FromIntPtr(Marshal.ReadIntPtr(block, ContextOffset));
        ctx = (GkBlockContext)g.Target;
        g.Free();

        if (error != IntPtr.Zero) {
          string errMsg = ReadNSString(SendPtr(error, sel_registerName("localizedDescription")));
          ctx.Tcs.TrySetException(new FirebaseException((int)AuthError.InvalidCredential,
              string.IsNullOrEmpty(errMsg) ? AuthErrorMapper.GetMessage(AuthError.InvalidCredential) : errMsg));
          return;
        }

        string keyUrlStr = publicKeyUrl != IntPtr.Zero
            ? ReadNSString(SendPtr(publicKeyUrl, sel_registerName("absoluteString")))
            : string.Empty;
        string sigB64Url = ReadNSDataBase64Url(signatureData);
        string saltB64Url = ReadNSDataBase64Url(saltData);
        Credential cred = Credential.CreateGameCenter(
            ctx.PlayerId, keyUrlStr, sigB64Url, saltB64Url, (long)timestamp,
            ctx.DisplayName, ctx.TeamPlayerId, ctx.GamePlayerId);
        if (!cred.IsValid()) {
          ctx.Tcs.TrySetException(AuthErrorMapper.CreateException(AuthError.InvalidCredential));
        } else {
          ctx.Tcs.TrySetResult(cred);
        }
      } catch (Exception ex) {
        ctx?.Tcs?.TrySetException(AuthErrorMapper.CreateException(AuthError.InvalidCredential, ex));
      } finally {
        objc_autoreleasePoolPop(pool);
      }
    }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
    private static Task<Credential> TryAndroidFederatedOAuthAsync(FirebaseAuth auth, FederatedOAuthProviderData data) {
      try {
        using var oauthClass = new UnityEngine.AndroidJavaClass("com.google.firebase.auth.OAuthProvider");
        using var unityPlayer = new UnityEngine.AndroidJavaClass("com.unity3d.player.UnityPlayer");
        using UnityEngine.AndroidJavaObject activity = unityPlayer.GetStatic<UnityEngine.AndroidJavaObject>("currentActivity");
        using var fbAppClass = new UnityEngine.AndroidJavaClass("com.google.firebase.FirebaseApp");
        string appName = auth?.App?.Name;
        bool isDefault = string.IsNullOrEmpty(appName) || appName == FirebaseApp.DefaultName;
        using UnityEngine.AndroidJavaObject javaApp = isDefault
            ? fbAppClass.CallStatic<UnityEngine.AndroidJavaObject>("getInstance")
            : fbAppClass.CallStatic<UnityEngine.AndroidJavaObject>("getInstance", appName);
        using var fbAuthClass = new UnityEngine.AndroidJavaClass("com.google.firebase.auth.FirebaseAuth");
        using UnityEngine.AndroidJavaObject nativeAuth = javaApp != null
            ? fbAuthClass.CallStatic<UnityEngine.AndroidJavaObject>("getInstance", javaApp)
            : null;
        if (activity == null || nativeAuth == null) {
          return Task.FromResult<Credential>(null);
        }

        using UnityEngine.AndroidJavaObject builder =
            oauthClass.CallStatic<UnityEngine.AndroidJavaObject>("newBuilder", data.ProviderId);
        if (data.Scopes != null) {
          using var scopesList = new UnityEngine.AndroidJavaObject("java.util.ArrayList");
          foreach (string scope in data.Scopes) {
            if (!string.IsNullOrEmpty(scope)) scopesList.Call<bool>("add", scope);
          }
          using UnityEngine.AndroidJavaObject unusedScopes =
              builder.Call<UnityEngine.AndroidJavaObject>("setScopes", scopesList);
        }
        if (data.CustomParameters != null) {
          foreach (KeyValuePair<string, string> kv in data.CustomParameters) {
            if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null) {
              using UnityEngine.AndroidJavaObject unusedParam =
                  builder.Call<UnityEngine.AndroidJavaObject>("addCustomParameter", kv.Key, kv.Value);
            }
          }
        }

        using UnityEngine.AndroidJavaObject builtProvider = builder.Call<UnityEngine.AndroidJavaObject>("build");
        var tcs = new TaskCompletionSource<Credential>(TaskCreationOptions.RunContinuationsAsynchronously);
        using UnityEngine.AndroidJavaObject gmsTask =
            nativeAuth.Call<UnityEngine.AndroidJavaObject>("startActivityForSignInWithProvider", activity, builtProvider);
        using UnityEngine.AndroidJavaObject unusedSuccess = gmsTask.Call<UnityEngine.AndroidJavaObject>(
            "addOnSuccessListener", new AndroidGmsSuccessProxy(resultObj => {
              try {
                using (resultObj) {
                  using UnityEngine.AndroidJavaObject credObj =
                      resultObj?.Call<UnityEngine.AndroidJavaObject>("getCredential");
                  string idToken = credObj?.Call<string>("getIdToken") ?? string.Empty;
                  string accessToken = credObj?.Call<string>("getAccessToken") ?? string.Empty;
                  if (string.Equals(data.ProviderId, TwitterAuthProvider.ProviderId, StringComparison.Ordinal) &&
                      !string.IsNullOrEmpty(accessToken)) {
                    string secret = credObj?.Call<string>("getSecret") ?? string.Empty;
                    tcs.TrySetResult(TwitterAuthProvider.GetCredential(accessToken, secret));
                  } else {
                    tcs.TrySetResult(OAuthProvider.GetCredential(data.ProviderId, idToken, accessToken));
                  }
                }
              } catch (Exception ex) {
                tcs.TrySetException(AuthErrorMapper.CreateException(AuthError.Failure, ex));
              }
            }));
        using UnityEngine.AndroidJavaObject unusedFailure = gmsTask.Call<UnityEngine.AndroidJavaObject>(
            "addOnFailureListener", new AndroidGmsFailureProxy(errMsg => {
              tcs.TrySetException(new FirebaseException((int)AuthError.Cancelled, errMsg ?? "Federated sign-in failed."));
            }));
        return tcs.Task;
      } catch (Exception) {
        // Native Android FirebaseAuth library is not linked in this APK; fall through cleanly.
        return Task.FromResult<Credential>(null);
      }
    }

    private sealed class AndroidGmsSuccessProxy : UnityEngine.AndroidJavaProxy {
      private readonly Action<UnityEngine.AndroidJavaObject> _onSuccess;
      internal AndroidGmsSuccessProxy(Action<UnityEngine.AndroidJavaObject> onSuccess)
          : base("com.google.android.gms.tasks.OnSuccessListener") {
        _onSuccess = onSuccess;
      }
      public void onSuccess(UnityEngine.AndroidJavaObject result) => _onSuccess?.Invoke(result);
    }

    private sealed class AndroidGmsFailureProxy : UnityEngine.AndroidJavaProxy {
      private readonly Action<string> _onFailure;
      internal AndroidGmsFailureProxy(Action<string> onFailure)
          : base("com.google.android.gms.tasks.OnFailureListener") {
        _onFailure = onFailure;
      }
      public void onFailure(UnityEngine.AndroidJavaObject exception) {
        string message;
        try {
          using (exception) {
            message = exception?.Call<string>("getMessage");
          }
        } catch (Exception) {
          message = "Native Android federated authentication failed.";
        }
        _onFailure?.Invoke(message);
      }
    }
#endif

    internal static async Task VerifyPhoneNumberAsync(
        FirebaseAuth auth,
        PhoneAuthOptions options,
        PhoneAuthProvider.VerificationCompleted verificationCompleted,
        PhoneAuthProvider.VerificationFailed verificationFailed,
        PhoneAuthProvider.CodeSent codeSent,
        PhoneAuthProvider.CodeAutoRetrievalTimeOut codeAutoRetrievalTimeOut) {
      if (options == null || string.IsNullOrWhiteSpace(options.PhoneNumber)) {
        FirebaseAuth.DispatchToMainThread(() =>
            verificationFailed?.Invoke(AuthErrorMapper.GetMessage(AuthError.MissingPhoneNumber)));
        return;
      }

      try {
        var req = new Dictionary<string, object> { ["phoneNumber"] = options.PhoneNumber };
        if (options.ForceResendingToken != null && !string.IsNullOrEmpty(options.ForceResendingToken.SessionInfo)) {
          req["sessionInfo"] = options.ForceResendingToken.SessionInfo;
        }
        Dictionary<string, object> resp = await auth.Client.PostAccountsAsync(
            "sendVerificationCode", req, auth.LanguageCode).ConfigureAwait(false);
        string sessionInfo = Firebase.Internal.JsonHelper.GetString(resp, "sessionInfo", string.Empty);
        if (string.IsNullOrEmpty(sessionInfo)) {
          FirebaseAuth.DispatchToMainThread(() =>
              verificationFailed?.Invoke(AuthErrorMapper.GetMessage(AuthError.Failure)));
          return;
        }

        var resendToken = new ForceResendingToken(sessionInfo);
        FirebaseAuth.DispatchToMainThread(() => codeSent?.Invoke(sessionInfo, resendToken));

        uint timeoutMs = options.TimeoutInMilliseconds;
        if (timeoutMs > 0) {
          await Task.Delay((int)Math.Min(timeoutMs, 120000u)).ConfigureAwait(false);
        }
        if (!auth.IsDisposed) {
          FirebaseAuth.DispatchToMainThread(() => codeAutoRetrievalTimeOut?.Invoke(sessionInfo));
        }
      } catch (AuthBackendErrorException backendEx) {
        string msg = AuthErrorMapper.GetMessage(backendEx.ErrorCode);
        FirebaseAuth.DispatchToMainThread(() => verificationFailed?.Invoke(msg));
      } catch (FirebaseException fbEx) {
        FirebaseAuth.DispatchToMainThread(() => verificationFailed?.Invoke(fbEx.Message));
      } catch (Exception ex) {
        FirebaseAuth.DispatchToMainThread(() => verificationFailed?.Invoke(ex.Message));
      }
    }
  }
}
