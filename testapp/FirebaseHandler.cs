namespace Firebase.Platform {

internal sealed class FirebaseHandler {
  // Utility class that provides functions to interact with the FirebaseApp dll,
  // since the Platform cannot directly depend on it because of circular dependencies.
  public static Firebase.Platform.IFirebaseAppUtils AppUtils { get; private set; }

  static FirebaseHandler() {
    AppUtils = Firebase.Platform.FirebaseAppUtilsStub.Instance;
  }

  // Blocks and waits for the function provided to execute on the same thread
  // as this instance of FirebaseHandler, and returns the result.
  public static TResult RunOnMainThread<TResult>(System.Func<TResult> f) {
    return f();
  }

  // Returns whether the current thread is the main thread.
  // NOTE: This only works once a FirebaseHandler instance constructed
  // with CreatePartialOnMainThread.
  internal bool IsMainThread() {
    return true;
  }

  private static FirebaseHandler firebaseHandler = null;

  // Passed to the ApplicationFocusChanged event.
  internal class ApplicationFocusChangedEventArgs : System.EventArgs {
    public bool HasFocus { get; set; }
  };

  // Called each time this behavior is updated by Unity.
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
  }

  internal void Update() {
    if (Updated != null) Updated(this, null);
  }

  internal void OnApplicationFocus(bool hasFocus) {
    if (ApplicationFocusChanged != null) {
      ApplicationFocusChanged(null, new ApplicationFocusChangedEventArgs { HasFocus = hasFocus });
    }
  }

  internal static void OnDestroy() {
    AppUtils.OnDestroy();

    firebaseHandler = null;
  }
}

}  // namespace Firebase

