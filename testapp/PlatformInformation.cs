namespace Firebase.Platform {

// Helper class that contains functions that handle platform specific behavior.
// This is done through the UnityEngine API.
internal static class PlatformInformation {
  // Is the current platform Android?
  internal static bool IsAndroid {
    get {
      return false;
    }
  }

  // Is the current platform iOS?
  internal static bool IsIOS {
    get {
      return false;
    }
  }

  internal static string DefaultConfigLocation {
    get {
      return ".";
    }
  }

  // The synchronization context that should be used.
  internal static System.Threading.SynchronizationContext SynchronizationContext {
    get { return null; }
  }

  internal static float RealtimeSinceStartup {
    get { return 0.0f; }
  }
}

}  // namespace Firebase
