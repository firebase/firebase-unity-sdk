namespace Firebase.Platform {

internal class FirebaseLogger {
  // Log a message via Unity's debug logger.
  internal static void LogMessage(PlatformLogLevel logLevel,
                                  string message) {
    PlatformLogLevel currentLevel = FirebaseHandler.AppUtils.GetLogLevel();
    if (logLevel < currentLevel) return;
    System.Console.WriteLine(message);
  }
}

}  // namespace Firebase
