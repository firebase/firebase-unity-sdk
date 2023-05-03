/*
 * Copyright 2023 Google LLC
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
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Firebase.AppCheck {

internal class BuiltInProviderWrapper : IAppCheckProvider {

  private static int s_pendingGetTokenKey = 0;
  private static Dictionary<int, TaskCompletionSource<AppCheckToken>> s_pendingGetTokens =
    new Dictionary<int, TaskCompletionSource<AppCheckToken>>();

  // Function for C++ to call when it needs to finish a GetToken call.
  private static AppCheckUtil.CompleteBuiltInGetTokenDelegate completeGetTokenDelegate =
    new AppCheckUtil.CompleteBuiltInGetTokenDelegate(CompleteBuiltInGetTokenMethod);

  static BuiltInProviderWrapper() {
    // Register the callback to complete GetTokens
    AppCheckUtil.SetCompleteBuiltInGetTokenCallback(completeGetTokenDelegate);
  }

  AppCheckProviderInternal providerInternal;

  public BuiltInProviderWrapper(AppCheckProviderInternal providerInternal) {
    this.providerInternal = providerInternal;
  }

  private void ThrowIfNull() {
    if (providerInternal == null ||
        AppCheckProviderInternal.getCPtr(providerInternal).Handle == System.IntPtr.Zero) {
      throw new System.NullReferenceException();
    }
  }

  public System.Threading.Tasks.Task<AppCheckToken> GetTokenAsync() {
    ThrowIfNull();

    int key;
    TaskCompletionSource<AppCheckToken> tcs;
    lock (s_pendingGetTokens) {
      key = s_pendingGetTokenKey++;
      tcs = new TaskCompletionSource<AppCheckToken>();
      s_pendingGetTokens[key] = tcs;
    }
    AppCheckUtil.GetTokenFromBuiltInProvider(providerInternal, key);
    return tcs.Task;
  }

  // This is called from the C++ implementation, on the Unity main thread.
  [MonoPInvokeCallback(typeof(AppCheckUtil.CompleteBuiltInGetTokenDelegate))]
  private static void CompleteBuiltInGetTokenMethod(int key, System.IntPtr tokenCPtr,
                                                    int error, string errorMessage) {
    TaskCompletionSource<AppCheckToken> tcs;
    lock (s_pendingGetTokens) {
      if (!s_pendingGetTokens.TryGetValue(key, out tcs)) {
        return;
      }
      s_pendingGetTokens.Remove(key);
    }

    if (error == 0) {
      // Create the C# object that wraps the Token's C++ pointer, passing false for ownership
      // to prevent the cleanup of the C# object from deleting the C++ object when done.
      AppCheckTokenInternal tokenInternal = new AppCheckTokenInternal(tokenCPtr, false);
      AppCheckToken token = AppCheckToken.FromAppCheckTokenInternal(tokenInternal);
      tcs.TrySetResult(token);
    } else {
      tcs.TrySetException(new FirebaseException(error, errorMessage));
    }
  }

}

}
