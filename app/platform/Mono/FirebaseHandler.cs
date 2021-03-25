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

using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Platform {

internal sealed class FirebaseHandler {
  // Utility class that provides functions to interact with the FirebaseApp dll,
  // since the Platform cannot directly depend on it because of circular dependencies.
  public static Firebase.Platform.IFirebaseAppUtils AppUtils { get; private set; }

  // Gets the number of times Update has been called since the construction of this class.
  private static int tickCount = 0;
  public static int TickCount { get { return tickCount; } }

  private static FirebaseHandler firebaseHandler = null;

  // Manages whether the update thread should be running. We use this, as opposed
  // to killing the thread, as it may be holding a lock on something, such that
  // killing it would be problematic.
  public bool RunUpdateThread { get; set; }

  public Dispatcher ThreadDispatcher { get; private set; }
  public Thread UpdateThread { get; private set; }

  static FirebaseHandler() {
    AppUtils = Firebase.Platform.FirebaseAppUtilsStub.Instance;
  }

  private FirebaseHandler() {
    RunUpdateThread = true;
    UpdateThread = new Thread(new ThreadStart(this.ThreadUpdater));
    UpdateThread.Start();
  }

  // Simple update loop that occasionally polls.
  public void ThreadUpdater() {
    ThreadDispatcher = new Dispatcher();
    while (RunUpdateThread) {
      Update();
      // The update rate is 33 ms (30 Hz), as these are primarily going to be for
      // checking the Firebase asynchronous functions, which don't need as frequent
      // ticks.
      Thread.Sleep(33);
      tickCount++;
    }
  }

  // Blocks and waits for the function provided to execute on the same thread
  // as this instance of FirebaseHandler, and returns the result.
  // Note that the "MainThread" is the thread that is created by the FirebaseHandler.
  public static TResult RunOnMainThread<TResult>(System.Func<TResult> f) {
    return firebaseHandler.ThreadDispatcher.Run(f);
  }

  // Queue the function provided and execute on the same thread as this instance of
  // FirebaseHandler asynchronously.  Returns a Task which is complete once the
  // function is executed.
  // NOTE: This only works once a FirebaseHandler instance constructed
  // with CreatePartialOnMainThread. Otherwise, it will run the given function
  // on the calling thread.
  public static Task<TResult> RunOnMainThreadAsync<TResult>(System.Func<TResult> f) {
    return firebaseHandler.ThreadDispatcher.RunAsync(f);
  }

  // Returns whether the current thread is the main thread.
  // NOTE: This only works once a FirebaseHandler instance constructed
  // with CreatePartialOnMainThread.
  // Note that the "MainThread" is the thread that is created by the FirebaseHandler.
  internal bool IsMainThread() {
    return ThreadDispatcher.ManagesThisThread();
  }

  // Passed to the ApplicationFocusChanged event.
  internal class ApplicationFocusChangedEventArgs : System.EventArgs {
    public bool HasFocus { get; set; }
  };

  // Called each time the update thread ticks.
  internal event System.EventHandler<System.EventArgs> Updated;
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
    Firebase.Mono.MonoPlatformServices.Install();
  }

  internal void Update() {
    ThreadDispatcher.PollJobs();
    AppUtils.PollCallbacks();
    if (Updated != null) {
        ExceptionAggregator.Wrap(() => {
            Updated(this, null);
          });
    }
    ExceptionAggregator.ThrowAndClearPendingExceptions();
  }

  internal void OnApplicationFocus(bool hasFocus) {
    if (ApplicationFocusChanged != null) {
      ApplicationFocusChanged(null, new ApplicationFocusChangedEventArgs { HasFocus = hasFocus });
    }
  }

  internal static void Terminate() {
    firebaseHandler.RunUpdateThread = false;
    firebaseHandler.UpdateThread.Join();
    firebaseHandler = null;
  }
}

}  // namespace Firebase.Platform

