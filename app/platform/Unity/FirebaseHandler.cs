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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Platform {

// The FirebaseHandler class is used to interact with Unity specific behavior,
// and is included in the Firebase.Platform.dll. Note that while it does not
// implement an interface, it effectively needs to, as it's methods are called
// from other Firebase libraries, and non-Unity specific versions of this class
// do exist.
//
// During runtime outside of the Editor (or when in play mode in the Editor),
// a MonoBehaviour is spun up to handle the necessary calls, such as invoking
// things on the main Unity thread, update ticks, etc.
//
// This implementation has Unity Editor functionality as well, handled through
// FirebaseEditorDispatcher, which uses reflection. Reflection is necessary as this
// class is also used in builds, which do not include the UnityEngine.dll, and as
// such we need to assume that UnityEngine will not necessarily be available.
internal sealed class FirebaseHandler {
  // The MonoBehaviour that is added to the Unity game, used to receive Update ticks, etc.
  private static Firebase.Platform.FirebaseMonoBehaviour firebaseMonoBehaviour = null;

  // Utility class that provides functions to interact with the FirebaseApp dll,
  // since the Platform cannot directly depend on it because of circular dependencies.
  public static Firebase.Platform.IFirebaseAppUtils AppUtils { get; private set; }

  private static int tickCount = 0;
  // Gets the number of times Update has been called since the construction of this class.
  public static int TickCount { get { return tickCount; } }

  // Dispatcher used to preserve calls that need to be done on the main Unity thread.
  private static Dispatcher ThreadDispatcher { get; set; }

  // Tracks if the FirebaseHandler is configured for Play mode (using the MonoBehaviour),
  // or edit mode, using UnityEditor's update call.
  public bool IsPlayMode { get; set; }

  static FirebaseHandler() {
    AppUtils = Firebase.Platform.FirebaseAppUtilsStub.Instance;
  }

  private FirebaseHandler() {
    // If we are in the Unity editor, we need to check if we are actually in play
    // mode or not. We also need to listen to the play mode state, so that if it
    // changes we can tear down and initialize the proper support.
    if (UnityEngine.Application.isEditor) {
      IsPlayMode = FirebaseEditorDispatcher.EditorIsPlaying;
      FirebaseEditorDispatcher.ListenToPlayState();
    } else {
      // If we aren't in the editor, we can assume that we are in play mode, and
      // that is not going to change.
      IsPlayMode = true;
    }

    if (IsPlayMode) {
      StartMonoBehaviour();
    } else {
      FirebaseEditorDispatcher.StartEditorUpdate();
    }
  }

  internal void StartMonoBehaviour() {
    // If the default instance isn't initialized, set it up now so that the behavior does not
    // stop itself on creation.
    if (firebaseHandler == null) firebaseHandler = this;
    // Create the Unity object.
    UnityEngine.GameObject firebaseHandlerGameObject =
        new UnityEngine.GameObject("Firebase Services");
    firebaseMonoBehaviour =
      firebaseHandlerGameObject.AddComponent<Firebase.Platform.FirebaseMonoBehaviour>();
    Firebase.Unity.UnitySynchronizationContext.Create(firebaseHandlerGameObject);
    UnityEngine.Object.DontDestroyOnLoad(firebaseHandlerGameObject);
  }

  internal void StopMonoBehaviour() {
    // Check the condition before potentially waiting on another thread.
    if (firebaseMonoBehaviour != null) {
      RunOnMainThread(() => {
        if (firebaseMonoBehaviour != null) {
          Firebase.Unity.UnitySynchronizationContext.Destroy();
          UnityEngine.Object.Destroy(firebaseMonoBehaviour.gameObject);
        }
        // Needs to return something.
        return true;
      });
    }
  }

  // Blocks and waits for the function provided to execute on the same thread
  // as this instance of FirebaseHandler, and returns the result.
  // NOTE: This only works once a FirebaseHandler instance constructed
  // with CreatePartialOnMainThread. Otherwise, it will run the given function
  // on the calling thread.
  public static TResult RunOnMainThread<TResult>(System.Func<TResult> f) {
    if (ThreadDispatcher != null) {
      TResult r = ThreadDispatcher.Run(f);
      return r;
    } else {
      return f();
    }
  }

  // Queue the function provided and execute on the same thread as this instance of
  // FirebaseHandler asynchronously.  Returns a Task which is complete once the
  // function is executed.
  // NOTE: This only works once a FirebaseHandler instance constructed
  // with CreatePartialOnMainThread. Otherwise, it will run the given function
  // on the calling thread.
  public static Task<TResult> RunOnMainThreadAsync<TResult>(System.Func<TResult> f) {
    if (ThreadDispatcher != null) {
      return ThreadDispatcher.RunAsync(f);
    } else {
      return Dispatcher.RunAsyncNow(f);
    }
  }

  // Returns whether the current thread is the main thread.
  // NOTE: This only works once a FirebaseHandler instance constructed
  // with CreatePartialOnMainThread.
  internal bool IsMainThread() {
    if (ThreadDispatcher != null) {
      return ThreadDispatcher.ManagesThisThread();
    } else {
      return false;
    }
  }

  private static FirebaseHandler firebaseHandler = null;

  // Passed to the ApplicationFocusChanged event.
  internal class ApplicationFocusChangedEventArgs : System.EventArgs {
    public bool HasFocus { get; set; }
  };

  // Called each time this behavior is updated by Unity.
  internal event System.EventHandler<System.EventArgs> Updated;
  // Wrapper action used to capture exceptions while calling Updated.
  internal Action UpdatedEventWrapper = null;

  // Called when the application focus changes.
  internal event System.EventHandler<ApplicationFocusChangedEventArgs>
      ApplicationFocusChanged;

  // Get the singleton.
  internal static FirebaseHandler DefaultInstance {
    get {
      return firebaseHandler;
    }
  }

  // This is an optional "Pre" Create() call, that handles all main-thread
  // work. It contains the bulk of the Create logic that has to be done on the
  // main thread, however it does not completely setup all of the Platform
  // Services.
  // To finish creation you must still call Create() after calling this,
  // however that can safely be done on another thread. If this is not used,
  // you can still call Create() as usual from the main thread.
  internal static void CreatePartialOnMainThread(Platform.IFirebaseAppUtils appUtils) {
    appUtils.TranslateDllNotFoundException(() => {
      lock (typeof(FirebaseHandler)) {
        if (firebaseHandler != null) return;

        AppUtils = appUtils;

        if (ThreadDispatcher == null) {
          ThreadDispatcher = new Dispatcher();
        }

        firebaseHandler = new FirebaseHandler();
      }
    });
  }

  // Create the FirebaseHandler if it hasn't been created and configure
  // logging.
  //
  // This can throw Firebase.InitializationException if the C/C++ dependencies
  // are not included in the application.
  internal static void Create(Platform.IFirebaseAppUtils appUtils) {
    CreatePartialOnMainThread(appUtils);

    // The first hit to the Services static object is slow, but it's
    // fast (cached) afterwards, so we'll always do it in a full creation.
    Firebase.Unity.UnityPlatformServices.SetupServices();
  }

  internal void Update() {
    ThreadDispatcher.PollJobs();
    AppUtils.PollCallbacks();
    if (Updated != null) {
        if (UpdatedEventWrapper == null) {
            UpdatedEventWrapper = () => {
            Updated(this, null);
          };
        }
        ExceptionAggregator.Wrap(UpdatedEventWrapper);
    }
    ExceptionAggregator.ThrowAndClearPendingExceptions();
    tickCount++;
  }

  internal void OnApplicationFocus(bool hasFocus) {
    if (ApplicationFocusChanged != null) {
      ApplicationFocusChanged(null, new ApplicationFocusChangedEventArgs { HasFocus = hasFocus });
    }
  }

  internal static void Terminate() {
    if (firebaseHandler != null) {
      // The FirebaseEditorDispatcher handles if the current platform is not the Editor.
      FirebaseEditorDispatcher.Terminate(firebaseHandler.IsPlayMode);
      // Stop the MonoBehaviour, since it depends on the FirebaseHandler.
      // Note, this can potentially block for the main Unity thread.
      firebaseHandler.StopMonoBehaviour();
    }
    firebaseHandler = null;
    // TODO(amaurice): Need to fix the creation / destruction of ThreadDispatcher.  If
    // any C# calls need to use it to run items on the main thread.
    // ThreadDispatcher = null;
  }

  // Invalid the behaviour referenced by this class if it matches the specified beaviour instance.
  internal static void OnMonoBehaviourDestroyed(Firebase.Platform.FirebaseMonoBehaviour behaviour) {
    if (behaviour == firebaseMonoBehaviour) firebaseMonoBehaviour = null;
  }
}

}  // namespace Firebase

