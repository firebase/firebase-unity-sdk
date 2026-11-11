using System.Runtime.CompilerServices;

// Grant native C# Unity packages access to Firebase.App's internal helpers
[assembly: InternalsVisibleTo("Firebase.Analytics"), InternalsVisibleTo("Firebase.App"), InternalsVisibleTo("Firebase.AppCheck")]
[assembly: InternalsVisibleTo("Firebase.Auth"), InternalsVisibleTo("Firebase.Crashlytics"), InternalsVisibleTo("Firebase.Database")]
[assembly: InternalsVisibleTo("Firebase.FirebaseAI"), InternalsVisibleTo("Firebase.Firestore"), InternalsVisibleTo("Firebase.Functions")]
[assembly: InternalsVisibleTo("Firebase.Installations"), InternalsVisibleTo("Firebase.Messaging"), InternalsVisibleTo("Firebase.RemoteConfig")]
[assembly: InternalsVisibleTo("Firebase.Storage"), InternalsVisibleTo("Firebase.FirebaseAI.TestApp")]
[assembly: InternalsVisibleTo("Firebase.App.Internal.Tests"), InternalsVisibleTo("Firebase.Tests"), InternalsVisibleTo("Firebase.Benchmarks")]
[assembly: InternalsVisibleTo("Firebase.TestHarness"), InternalsVisibleTo("Assembly-CSharp"), InternalsVisibleTo("Assembly-CSharp-Editor"), InternalsVisibleTo("Tests")]
