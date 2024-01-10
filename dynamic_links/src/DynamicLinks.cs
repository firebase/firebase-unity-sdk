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

namespace Firebase.DynamicLinks {

using System.Threading.Tasks;

/// @brief Firebase Dynamic Links API.
///
/// Firebase Dynamic Links is a cross-platform solution for generating and
/// receiving links, whether or not the app is already installed.
[System.Obsolete("Firebase Dynamic Links is deprecated and will be removed in a future release..", false)]
public sealed class DynamicLinks {
  // Keep a reference to FirebaseApp as it initializes this SDK.
  private static Firebase.FirebaseApp app;

  // The class is not meant to be instantiated in that way.
  private DynamicLinks() {}

  // Initialize this module.
  static DynamicLinks() {
      app = Firebase.FirebaseApp.DefaultInstance;
  }

#if DOXYGEN
  /// Called on the client when a Dynamic Link is received.
  public static event System.EventHandler<ReceivedDynamicLinkEventArgs> DynamicLinkReceived;
#else
  public static event System.EventHandler<ReceivedDynamicLinkEventArgs> DynamicLinkReceived {
    add {
      lock (typeof(FirebaseDynamicLinks.Listener)) {
        DynamicLinkReceivedInternal += value;
        CreateOrDestroyListener();
      }
    }

    remove {
      lock (typeof(FirebaseDynamicLinks.Listener)) {
        DynamicLinkReceivedInternal -= value;
        CreateOrDestroyListener();
      }
    }
  }
#endif  // DOXYGEN

  // Backing store for the DynamicLinkReceived event.
  // This is called from FirebaseDynamicLinks.DynamicLinkReceivedMethod().
  internal static event System.EventHandler<ReceivedDynamicLinkEventArgs>
      DynamicLinkReceivedInternal;

  // Pass a dynamic link to the DynamicLinkReceivedInternal event.
  internal static void NotifyDynamicLinkReceived(ReceivedDynamicLink dynamicLink) {
    if (DynamicLinkReceivedInternal != null) {
      DynamicLinkReceivedInternal(null, new ReceivedDynamicLinkEventArgs {
          ReceivedDynamicLink = dynamicLink
        });
    }
  }

  /// Creates a shortened Dynamic Link from the given parameters.
  ///
  /// @param components The components that define the Dynamic Link to create
  /// and shorten.
  /// @param options Optionally provided options to tweak the short link generation.
  /// If this is not specified the default behavior is for PathLength = PathLength.Unguessable.
  ///
  /// @deprecated Dynamic Links is now deprecated. Please see the support
  /// documentation at https://firebase.google.com/support/dynamic-links-faq
  /// for more information.
  public static Task<ShortDynamicLink> GetShortLinkAsync(DynamicLinkComponents components,
      DynamicLinkOptions options) {
    return ConvertFromInternalTask(FirebaseDynamicLinks.GetShortLinkInternalAsync(
        components.ConvertToInternal(), options.ConvertToInternal()));
  }

  /// Creates a shortened Dynamic Link from the given parameters.
  ///
  /// @param components The components that define the Dynamic Link to create
  /// and shorten.
  ///
  /// @deprecated Dynamic Links is now deprecated. Please see the support
  /// documentation at https://firebase.google.com/support/dynamic-links-faq
  /// for more information.
  public static Task<ShortDynamicLink> GetShortLinkAsync(DynamicLinkComponents components) {
    return GetShortLinkAsync(components, new DynamicLinkOptions());
  }

  /// Creates a shortened Dynamic Link from the given long dynamic link.
  ///
  /// @param url A properly-formatted long Dynamic Link to shorten.
  /// @param options Optionally provided options to tweak the short link generation.
  /// If this is not specified the default behavior is for PathLength = PathLength.Unguessable.
  ///
  /// @deprecated Dynamic Links is now deprecated. Please see the support
  /// documentation at https://firebase.google.com/support/dynamic-links-faq
  /// for more information.
  public static Task<ShortDynamicLink> GetShortLinkAsync(System.Uri longDynamicLink,
      DynamicLinkOptions options) {
    return ConvertFromInternalTask(FirebaseDynamicLinks.GetShortLinkInternalAsync(
        Firebase.FirebaseApp.UriToUrlString(longDynamicLink),
        options.ConvertToInternal()));
  }

  /// Creates a shortened Dynamic Link from the given long dynamic link.
  ///
  /// @param url A properly-formatted long Dynamic Link to shorten.
  ///
  /// @deprecated Dynamic Links is now deprecated. Please see the support
  /// documentation at https://firebase.google.com/support/dynamic-links-faq
  /// for more information.
  public static Task<ShortDynamicLink> GetShortLinkAsync(System.Uri long_dynamic_link) {
    return GetShortLinkAsync(long_dynamic_link, new DynamicLinkOptions());
  }

  // Generate a Task<ShortDynamicLink> from Task<GeneratedDynamicLinkInternal>.
  private static Task<ShortDynamicLink> ConvertFromInternalTask(
      Task<GeneratedDynamicLinkInternal> generatedDynamicLinkInternalTask) {
    var taskCompletionSource = new System.Threading.Tasks.TaskCompletionSource<ShortDynamicLink>();
    generatedDynamicLinkInternalTask.ContinueWith((taskInternal) => {
        if (taskInternal.IsCanceled) {
          taskCompletionSource.SetCanceled();
        } else if (taskInternal.IsFaulted) {
          Firebase.Internal.TaskCompletionSourceCompat<ShortDynamicLink>.SetException(
            taskCompletionSource, taskInternal.Exception);
        } else {
          taskCompletionSource.SetResult(ShortDynamicLink.ConvertFromInternal(taskInternal.Result));
        }
      });
    return taskCompletionSource.Task;
  }

  // Create the listener if messaging events are set, otherwise destroy it.
  private static void CreateOrDestroyListener() {
    lock (typeof(FirebaseDynamicLinks.Listener)) {
      if (DynamicLinkReceivedInternal != null) {
        FirebaseDynamicLinks.Listener.Create();
      } else {
        FirebaseDynamicLinks.Listener.Destroy();
      }
    }
  }
}

}
