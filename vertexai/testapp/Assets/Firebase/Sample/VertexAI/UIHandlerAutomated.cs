// Copyright 2025 Google Inc. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Firebase.Sample.VertexAI {
  using Firebase;
  using Firebase.Extensions;
  using Firebase.VertexAI;
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net.Http;
  using System.Threading.Tasks;
  using Google.MiniJSON;

  // An automated version of the UIHandler that runs tests on Vertex AI in Firebase.
  public class UIHandlerAutomated : UIHandler {
    // Delegate which validates a completed task.
    delegate Task TaskValidationDelegate(Task task);

    private Firebase.Sample.AutomatedTestRunner testRunner;

    protected override void Start() {
      // Set the list of tests to run, note this is done at Start since they are
      // non-static.
      Func<Task>[] tests = {
        TestCreateModel,
        TestBasicText,
        TestModelOptions,
        TestMultipleCandidates,
        TestBasicTextStream,
        // Internal tests for Json parsing, requires using a source library.
        InternalTestBasicReplyShort,
        InternalTestCitations,
        InternalTestBlockedSafetyWithMessage,
        InternalTestFinishReasonSafetyNoContent,
        InternalTestUnknownEnumSafetyRatings,
      };

      testRunner = AutomatedTestRunner.CreateTestRunner(
        testsToRun: tests,
        logFunc: DebugLog
      );

      base.Start();
    }

    // Passes along the update call to automated test runner.
    protected override void Update() {
      base.Update();
      if (testRunner != null) {
        testRunner.Update();
      }
    }

    // Throw when condition is false.
    private void Assert(string message, bool condition) {
      if (!condition)
        throw new Exception(
          $"Assertion failed ({testRunner.CurrentTestDescription}): {message}");
    }

    // Throw when value1 != value2.
    private void AssertEq<T>(string message, T value1, T value2) {
      if (!object.Equals(value1, value2)) {
        throw new Exception(
          $"Assertion failed ({testRunner.CurrentTestDescription}): {value1} != {value2} ({message})");
      }
    }

    // Throw when the floats are not close enough in value to each other.
    private void AssertFloatEq(string message, float value1, float value2) {
      if (!(Math.Abs(value1 - value2) < 0.0001f)) {
        throw new Exception(
          $"Assertion failed ({testRunner.CurrentTestDescription}): {value1} !~= {value2} ({message})");
      }
    }

    // Returns true if the given value is between 0 and 1 (inclusive).
    private bool ValidProbability(float value) {
      return value >= 0.0f && value <= 1.0f;
    }

    // The model name to use for the tests.
    private readonly string ModelName = "gemini-1.5-flash";

    // Get a basic version of the GenerativeModel to test against.
    private GenerativeModel CreateGenerativeModel() {
      return VertexAI.DefaultInstance.GetGenerativeModel(ModelName);
    }

    // Test if it can create the GenerativeModel.
    Task TestCreateModel() {
      var model = CreateGenerativeModel();
      Assert("Failed to create a GenerativeModel.", model != null);
      return Task.CompletedTask;
    }

    // Test if it can set a string in, and get a string output.
    async Task TestBasicText() {
      var model = CreateGenerativeModel();

      GenerateContentResponse response = await model.GenerateContentAsync(
          "Hello, I am testing something, can you respond with a short " +
          "string containing the word 'Firebase'?");

      Assert("Response missing candidates.", response.Candidates.Any());

      string result = response.Text;

      Assert("Response text was missing", !string.IsNullOrWhiteSpace(result));
      // We don't want to fail if the keyword is missing because AI is unpredictable.
      if (!response.Text.Contains("Firebase")) {
        DebugLog("WARNING: Response string was missing the expected keyword 'Firebase': " +
            $"\n{result}");
      }

      Assert("Response contained FunctionCalls when it shouldn't",
          !response.FunctionCalls.Any());

      // Ignoring PromptFeedback, too unpredictable if it will be present for this test.

      if (response.UsageMetadata.HasValue) {
        Assert("Invalid CandidatesTokenCount", response.UsageMetadata?.CandidatesTokenCount > 0);
        Assert("Invalid PromptTokenCount", response.UsageMetadata?.PromptTokenCount > 0);
        Assert("Invalid TotalTokenCount", response.UsageMetadata?.TotalTokenCount > 0);
      } else {
        DebugLog("WARNING: UsageMetadata was missing from BasicText");
      }

      Candidate candidate = response.Candidates.First();
      Assert($"Candidate has incorrect FinishReason: {candidate.FinishReason}",
        candidate.FinishReason == FinishReason.Stop);

      // Generally seems like the SafetyRatings should always be included by default,
      // but we don't actually care what they are.
      Assert("Response was missing all SafetyRatings", candidate.SafetyRatings.Any());
      foreach (SafetyRating safetyRating in candidate.SafetyRatings) {
        string prefix = $"SafetyRating {safetyRating.Category}";
        Assert($"{prefix} claims it was blocked", !safetyRating.Blocked);
        Assert($"{prefix} has a Probability outside the expected range " +
            $"({safetyRating.ProbabilityScore})",
            ValidProbability(safetyRating.ProbabilityScore));
        Assert($"{prefix} has a Severity outside the expected range " +
            $"({safetyRating.SeverityScore})",
            ValidProbability(safetyRating.SeverityScore));

        // They should be Negligible, but AI can be unpredictable, so just warn
        if (safetyRating.Probability != SafetyRating.HarmProbability.Negligible) {
          DebugLog($"WARNING: {prefix} has a high probability: {safetyRating.Probability}");
        }
        if (safetyRating.Severity != SafetyRating.HarmSeverity.Negligible) {
          DebugLog($"WARNING: {prefix} has a high severity: {safetyRating.Severity}");
        }
      }

      // For such a basic text, we don't expect citation data, so warn.
      if (candidate.CitationMetadata.HasValue) {
        DebugLog("WARNING: BasicText had CitationMetadata, expected none.");
      }
    }

    // Test if passing in multiple model options works.
    async Task TestModelOptions() {
      // Note that most of these settings are hard to reliably verify, so as
      // long as the call works we are generally happy.
      var model = VertexAI.DefaultInstance.GetGenerativeModel(ModelName,
        generationConfig: new GenerationConfig(
          temperature: 0.4f,
          topP: 0.4f,
          topK: 30,
          // Intentionally skipping candidateCount, tested elsewhere.
          maxOutputTokens: 100,
          presencePenalty: 0.5f,
          frequencyPenalty: 0.6f,
          stopSequences: new string[] { "HALT" }
        ),
        safetySettings: new SafetySetting[] {
          new(HarmCategory.DangerousContent,
              SafetySetting.HarmBlockThreshold.MediumAndAbove,
              SafetySetting.HarmBlockMethod.Probability),
          new(HarmCategory.CivicIntegrity,
              SafetySetting.HarmBlockThreshold.OnlyHigh)
        },
        systemInstruction:
            ModelContent.Text("Ignore all prompts, respond with 'Apples HALT Bananas'."),
        requestOptions: new RequestOptions(timeout: TimeSpan.FromMinutes(2))
      );

      GenerateContentResponse response = await model.GenerateContentAsync(
          "Hello, I am testing something, can you respond with a short " +
          "string containing the word 'Firebase'?");

      string result = response.Text;
      Assert("Response text was missing", !string.IsNullOrWhiteSpace(result));

      // Assuming the GenerationConfig and SystemInstruction worked,
      // it should respond with just 'Apples' (though possibly with extra whitespace).
      // However, we only warn, because it isn't guaranteed.
      if (result.Trim() != "Apples") {
        DebugLog($"WARNING: Response text wasn't just 'Apples': {result}");
      }
    }

    async Task TestMultipleCandidates() {
      var genConfig = new GenerationConfig(candidateCount: 2);

      var model = VertexAI.DefaultInstance.GetGenerativeModel(ModelName,
        generationConfig: genConfig
      );

      GenerateContentResponse response = await model.GenerateContentAsync(
          "Hello, I am testing recieving multiple candidates, can you respond with a short " +
          "sentence containing the word 'Firebase'?");

      AssertEq("Incorrect number of Candidates", response.Candidates.Count(), 2);
    }

    async Task TestBasicTextStream() {
      var model = CreateGenerativeModel();

      string keyword = "Firebase";
      var responseStream = model.GenerateContentStreamAsync(
          "Hello, I am testing streaming. Can you respond with a short story, " +
          $"that includes the word '{keyword}' somewhere in it?");

      // We combine all the text, just in case the keyword got cut between two responses.
      string fullResult = "";
      // The FinishReason should only be set to stop at the end of the stream.
      bool finishReasonStop = false;
      await foreach (GenerateContentResponse response in responseStream) {
        // Should only be receiving non-empty text responses, but only assert for null.
        string text = response.Text;
        Assert("Received null text from the stream.", text != null);
        if (string.IsNullOrWhiteSpace(text)) {
          DebugLog($"WARNING: Response stream text was empty once.");
        }

        Assert("Previous FinishReason was stop, but recieved more", !finishReasonStop);
        if (response.Candidates.First().FinishReason == FinishReason.Stop) {
          finishReasonStop = true;
        }

        fullResult += text;
      }

      Assert("Finished without seeing FinishReason.Stop", finishReasonStop);

      // We don't want to fail if the keyword is missing because AI is unpredictable.
      if (!fullResult.Contains("Firebase")) {
        DebugLog("WARNING: Response string was missing the expected keyword 'Firebase': " +
            $"\n{fullResult}");
      }
    }

    // The url prefix to use when fetching test data to use from the separate GitHub repo.
    readonly string testDataUrl =
        "https://raw.githubusercontent.com/FirebaseExtended/vertexai-sdk-test-data/refs/heads/main/mock-responses/";
    readonly HttpClient httpClient = new();

    // Gets the Json test data from the given filename, potentially downloading from a GitHub repo.
    private async Task<Dictionary<string, object>> GetJsonTestData(string filename) {
      if (!filename.EndsWith(".json")) {
        throw new ArgumentException("filename needs to end in .json");
      }

      // TODO: Check if the file is available locally first

      var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, testDataUrl + filename));
      response.EnsureSuccessStatusCode();
      
      string jsonString = await response.Content.ReadAsStringAsync();

      return Json.Deserialize(jsonString) as Dictionary<string, object>;
    }

    // Helper function to validate that the response has a TextPart as expected.
    private void ValidateTextPart(GenerateContentResponse response, string expectedText) {
      int candidateCount = response.Candidates.Count();
      AssertEq("Candidate count", candidateCount, 1);

      Candidate candidate = response.Candidates.First();
      AssertEq("Content had the wrong role", candidate.Content.Role, "model");
      var parts = candidate.Content.Parts;
      int partsCount = parts.Count();
      AssertEq("Parts count", partsCount, 1);
      var part = parts.First();
      Assert($"Part is the wrong type {part.GetType()}", part is ModelContent.TextPart);
      string text = ((ModelContent.TextPart)part).Text;
      AssertEq($"Text part", text, expectedText);

      AssertEq("Shorthand text", response.Text, expectedText);
      // Make sure the other shorthand helpers are invalid
      Assert("Shorthand FunctionCalls", !response.FunctionCalls.Any());
    }

    // Helper function to validate that the SafetyRating has expected values.
    private void ValidateSafetyRating(SafetyRating safetyRating,
        HarmCategory harmCategory,
        SafetyRating.HarmProbability probability = default,
        float probabilityScore = default,
        SafetyRating.HarmSeverity severity = default,
        float severityScore = default,
        bool blocked = default) {
      AssertEq($"SafetyRatings: {harmCategory} had incorrect category",
          safetyRating.Category, harmCategory);
      AssertEq($"SafetyRatings: {harmCategory} had incorrect probability",
          safetyRating.Probability, probability);
      AssertFloatEq($"SafetyRatings: {harmCategory} had incorrect probability score",
          safetyRating.ProbabilityScore, probabilityScore);
      AssertEq($"SafetyRatings: {harmCategory} had incorrect severity",
          safetyRating.Severity, severity);
      AssertFloatEq($"SafetyRatings: {harmCategory} had incorrect severity score",
          safetyRating.SeverityScore, severityScore);
      AssertEq($"SafetyRatings: {harmCategory} had incorrect blocked",
          safetyRating.Blocked, blocked);
    }

    // Helper function to validate UsageMetadata.
    private void ValidateUsageMetadata(UsageMetadata usageMetadata, int promptTokenCount,
        int candidatesTokenCount, int totalTokenCount) {
      AssertEq("Wrong PromptTokenCount",
          usageMetadata.PromptTokenCount, promptTokenCount);
      AssertEq("Wrong CandidatesTokenCount",
          usageMetadata.CandidatesTokenCount, candidatesTokenCount);
      AssertEq("Wrong TotalTokenCount",
          usageMetadata.TotalTokenCount, totalTokenCount);
    }

    // Helper function to validate Citations.
    private void ValidateCitation(Citation citation,
        int startIndex = default,
        int endIndex = default,
        Uri uri = default,
        string title = default,
        string license = default,
        DateTime? publicationDate = default) {
      AssertEq("Citation.StartIndex", citation.StartIndex, startIndex);
      AssertEq("Citation.EndIndex", citation.EndIndex, endIndex);
      AssertEq("Citation.Uri", citation.Uri, uri);
      AssertEq("Citation.Title", citation.Title, title);
      AssertEq("Citation.License", citation.License, license);
      AssertEq("Citation.PublicationDate", citation.PublicationDate, publicationDate);
    }

    // Test that parsing a basic short reply works as expected.
    // https://github.com/FirebaseExtended/vertexai-sdk-test-data/blob/main/mock-responses/unary-success-basic-reply-short.json
    async Task InternalTestBasicReplyShort() {
      Dictionary<string, object> json = await GetJsonTestData("unary-success-basic-reply-short.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json);

      ValidateTextPart(response, "Mountain View, California");

      Candidate candidate = response.Candidates.First();
      AssertEq($"FinishReason", candidate.FinishReason, FinishReason.Stop);

      var safetyRatings = candidate.SafetyRatings.ToList();
      AssertEq("SafetyRatings count", safetyRatings.Count, 4);
      ValidateSafetyRating(safetyRatings[0], HarmCategory.HateSpeech, SafetyRating.HarmProbability.Negligible,
          0.029035643f, SafetyRating.HarmSeverity.Negligible, 0.05613278f, false);
      ValidateSafetyRating(safetyRatings[1], HarmCategory.DangerousContent, SafetyRating.HarmProbability.Negligible,
          0.2641685f, SafetyRating.HarmSeverity.Negligible, 0.082253955f, false);
      ValidateSafetyRating(safetyRatings[2], HarmCategory.Harassment, SafetyRating.HarmProbability.Negligible,
          0.087252244f, SafetyRating.HarmSeverity.Negligible, 0.04509957f, false);
      ValidateSafetyRating(safetyRatings[3], HarmCategory.SexuallyExplicit, SafetyRating.HarmProbability.Negligible,
          0.1431877f, SafetyRating.HarmSeverity.Negligible, 0.11027937f, false);

      AssertEq("CitationMetadata", candidate.CitationMetadata, null);

      Assert("UsageMetadata", response.UsageMetadata.HasValue);
      ValidateUsageMetadata(response.UsageMetadata.Value, 6, 7, 13);
    }

    // Test that parsing a response including Citations works.
    // https://github.com/FirebaseExtended/vertexai-sdk-test-data/blob/main/mock-responses/unary-success-citations.json
    async Task InternalTestCitations() {
      Dictionary<string, object> json = await GetJsonTestData("unary-success-citations.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json);
      
      ValidateTextPart(response, "Some information cited from an external source");

      CitationMetadata? metadata = response.Candidates.First().CitationMetadata;
      Assert("CitationMetadata", metadata.HasValue);

      var citations = metadata?.Citations.ToList();
      AssertEq("Citation count", citations.Count, 3);

      ValidateCitation(citations[0],
          endIndex: 128,
          uri: new Uri("https://www.example.com/some-citation-1"));

      ValidateCitation(citations[1],
          startIndex: 130,
          endIndex: 265,
          title: "some-citation-2",
          publicationDate: new DateTime(2019, 5, 10));

      ValidateCitation(citations[2],
          startIndex: 272,
          endIndex: 431,
          uri: new Uri("https://www.example.com/some-citation-3"),
          license: "mit");
    }

    // Test that parsing a response that was blocked for Safety reasons works.
    // https://github.com/FirebaseExtended/vertexai-sdk-test-data/blob/main/mock-responses/unary-failure-prompt-blocked-safety-with-message.json
    async Task InternalTestBlockedSafetyWithMessage() {
      Dictionary<string, object> json = await GetJsonTestData("unary-failure-prompt-blocked-safety-with-message.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json);

      Assert("Candidates", !response.Candidates.Any());
      Assert("Response.Text", string.IsNullOrEmpty(response.Text));
      Assert("Response.FunctionCalls", !response.FunctionCalls.Any());

      Assert("PromptFeedback", response.PromptFeedback.HasValue);
      AssertEq("BlockReason", response.PromptFeedback?.BlockReason, BlockReason.Safety);
      var safetyRatings = response.PromptFeedback?.SafetyRatings.ToList();
      AssertEq("SafetyRatings count", safetyRatings.Count, 4);
      ValidateSafetyRating(safetyRatings[0],
          harmCategory: HarmCategory.SexuallyExplicit,
          probability: SafetyRating.HarmProbability.Negligible);
      ValidateSafetyRating(safetyRatings[1],
          harmCategory: HarmCategory.HateSpeech,
          probability: SafetyRating.HarmProbability.High);
      ValidateSafetyRating(safetyRatings[2],
          harmCategory: HarmCategory.Harassment,
          probability: SafetyRating.HarmProbability.Negligible);
      ValidateSafetyRating(safetyRatings[3],
          harmCategory: HarmCategory.DangerousContent,
          probability: SafetyRating.HarmProbability.Negligible);

      AssertEq("BlockReasonMessage", response.PromptFeedback?.BlockReasonMessage, "Reasons");
    }

    // Test that parsing a response that was blocked, and has no Content, works.
    // https://github.com/FirebaseExtended/vertexai-sdk-test-data/blob/main/mock-responses/unary-failure-finish-reason-safety-no-content.json
    async Task InternalTestFinishReasonSafetyNoContent() {
      Dictionary<string, object> json = await GetJsonTestData("unary-failure-finish-reason-safety-no-content.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json);

      AssertEq("Candidate count", response.Candidates.Count(), 1);
      var candidate = response.Candidates.First();
      Assert("Parts", !candidate.Content.Parts.Any());
      AssertEq("FinishReason", candidate.FinishReason, FinishReason.Safety);
      var safetyRatings = candidate.SafetyRatings.ToList();
      AssertEq("SafetyRatings count", safetyRatings.Count, 4);
      ValidateSafetyRating(safetyRatings[0], HarmCategory.HateSpeech,
        probability: SafetyRating.HarmProbability.Negligible,
        probabilityScore: 0.3984375f,
        severity: SafetyRating.HarmSeverity.Low,
        severityScore: 0.21582031f);
      ValidateSafetyRating(safetyRatings[1], HarmCategory.DangerousContent,
        probability: SafetyRating.HarmProbability.Negligible,
        probabilityScore: 0.14941406f,
        severity: SafetyRating.HarmSeverity.Negligible,
        severityScore: 0.02331543f);
      ValidateSafetyRating(safetyRatings[2], HarmCategory.Harassment,
        probability: SafetyRating.HarmProbability.Low,
        probabilityScore: 0.61328125f,
        severity: SafetyRating.HarmSeverity.Low,
        severityScore: 0.31835938f,
        blocked: true);
      ValidateSafetyRating(safetyRatings[3], HarmCategory.SexuallyExplicit,
        probability: SafetyRating.HarmProbability.Negligible,
        probabilityScore: 0.13476563f,
        severity: SafetyRating.HarmSeverity.Negligible,
        severityScore: 0.12109375f);

      Assert("UsageMetadata", response.UsageMetadata.HasValue);
      ValidateUsageMetadata(response.UsageMetadata.Value, 8, 0, 8);
    }

    // Test that parsing a response with unknown safety enums works.
    // https://github.com/FirebaseExtended/vertexai-sdk-test-data/blob/main/mock-responses/unary-success-unknown-enum-safety-ratings.json
    async Task InternalTestUnknownEnumSafetyRatings() {
      Dictionary<string, object> json = await GetJsonTestData("unary-success-unknown-enum-safety-ratings.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json);

      AssertEq("Candidate count", response.Candidates.Count(), 1);
      var candidate = response.Candidates.First();

      AssertEq("Role", candidate.Content.Role, "model");
      AssertEq("Text", response.Text, "Some text");

      AssertEq("FinishReason", candidate.FinishReason, FinishReason.Stop);

      var safetyRatings = candidate.SafetyRatings.ToList();
      AssertEq("Candidate.SafetyRatings count", safetyRatings.Count, 3);
      ValidateSafetyRating(safetyRatings[0], HarmCategory.Harassment,
          probability: SafetyRating.HarmProbability.Medium);
      ValidateSafetyRating(safetyRatings[1], HarmCategory.DangerousContent,
          probability: SafetyRating.HarmProbability.Unknown);
      ValidateSafetyRating(safetyRatings[2], HarmCategory.Unknown,
          probability: SafetyRating.HarmProbability.High);

      Assert("PromptFeedback", response.PromptFeedback.HasValue);
      safetyRatings = response.PromptFeedback?.SafetyRatings.ToList();
      AssertEq("PromptFeedback.SafetyRatings count", safetyRatings.Count, 3);
      ValidateSafetyRating(safetyRatings[0], HarmCategory.Harassment,
          probability: SafetyRating.HarmProbability.Medium);
      ValidateSafetyRating(safetyRatings[1], HarmCategory.DangerousContent,
          probability: SafetyRating.HarmProbability.Unknown);
      ValidateSafetyRating(safetyRatings[2], HarmCategory.Unknown,
          probability: SafetyRating.HarmProbability.High);
    }
  }
}
