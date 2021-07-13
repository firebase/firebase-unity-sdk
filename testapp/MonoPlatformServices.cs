using Firebase.Platform.Default;
using Firebase.Platform;
using System;

namespace Firebase.Mono {
  public class MonoPlatformServices {
    public static void Install() {
      Services.HttpFactory = MonoHttpFactory.Instance;
    }
  }
}
