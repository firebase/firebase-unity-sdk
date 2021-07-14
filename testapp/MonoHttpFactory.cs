using System;
using Firebase;
using Firebase.Platform;

namespace Firebase.Mono {
  /// <summary>Creates FirebaseHttpRequests.</summary>
  internal class MonoHttpFactory : IHttpFactoryService {
    private static MonoHttpFactory _instance = new MonoHttpFactory();
    public static MonoHttpFactory Instance { get { return _instance; } }

    public FirebaseHttpRequest OpenConnection(Uri url) {
      return new MonoHttpRequest(url);
    }
  }
}
