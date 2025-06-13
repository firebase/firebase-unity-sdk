namespace Firebase.AI {
  public readonly struct ImagenSafetySettings {
    public enum SafetyFilterLevel {
      BlockLowAndAbove,
      BlockMediumAndAbove,
      BlockOnlyHigh,
      BlockNone
    }

    public enum PersonFilterLevel {
      BlockAll,
      AllowAdult,
      AllowAll
    }

    public SafetyFilterLevel? SafetyFilter { get; }
    public PersonFilterLevel? PersonFilter { get; }

    public ImagenSafetySettings(
      SafetyFilterLevel? safetyFilterLevel = null,
      PersonFilterLevel? personFilterLevel = null
    ) {
      SafetyFilter = safetyFilterLevel;
      PersonFilter = personFilterLevel;
    }

    // Helper method to convert to JSON dictionary for requests
    internal System.Collections.Generic.Dictionary<string, object> ToJson() {
      var jsonDict = new System.Collections.Generic.Dictionary<string, object>();
      if (SafetyFilter.HasValue) {
        jsonDict["safetyFilter"] = SafetyFilter.Value.ToString();
      }
      if (PersonFilter.HasValue) {
        jsonDict["personFilter"] = PersonFilter.Value.ToString();
      }
      return jsonDict;
    }
  }
}
