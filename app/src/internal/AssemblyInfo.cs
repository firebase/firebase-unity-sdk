using System.Runtime.CompilerServices;

// Grant native C# Unity packages access to Firebase.App's internal helpers
[assembly: InternalsVisibleTo("Firebase.Functions")]
[assembly: InternalsVisibleTo("Firebase.FirebaseAI")]
[assembly: InternalsVisibleTo("Firebase.FirebaseAI.TestApp")]
