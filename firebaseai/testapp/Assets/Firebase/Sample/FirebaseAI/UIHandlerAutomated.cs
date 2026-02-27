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

// Uncomment to include logic to sign in to Auth as part of the tests
//#define INCLUDE_FIREBASE_AUTH

namespace Firebase.Sample.FirebaseAI
{
  using Firebase;
  using Firebase.Internal;
  using Firebase.AI;
  using Firebase.AI.Internal;
  using Firebase.Extensions;
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net.Http;
  using System.Net;
  using System.Threading.Tasks;
  using Google.MiniJSON;
  using UnityEngine;
  using UnityEngine.Networking;
  using System.IO;
#if INCLUDE_FIREBASE_AUTH
  using Firebase.Auth;
#endif

  // An automated version of the UIHandler that runs tests on Firebase AI.
  public class UIHandlerAutomated : UIHandler
  {
    // Delegate which validates a completed task.
    delegate Task TaskValidationDelegate(Task task);

    private Firebase.Sample.AutomatedTestRunner testRunner;

    // Texture used for tests involving images.
    public Texture2D RedBlueTexture;

    // Not reusing the ones from the SDK, since they are internal and only visible
    // because we are providing source libraries.
    private enum Backend
    {
      GoogleAI,
      VertexAI,
    }
    // Set of status codes that are retryable.
    private readonly HashSet<HttpStatusCode> RetryableCodes = new HashSet<HttpStatusCode> { HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable, HttpStatusCode.GatewayTimeout };
    // This should give us up to 30 seconds of delay in the last attempt.
    private const int MaxRetries = 5;
    private const int InitialRetryDelayMilliseconds = 2000;

    // Helper to check if an exception corresponds to a quota exhaustion error.
    private bool IsQuotaExhausted(Exception ex)
    {
      var msg = ex.Message;
      return !string.IsNullOrEmpty(msg) && msg.Contains("exceeded your current quota");
    }

    // Helper to check if an exception corresponds to a retryable error.
    private bool ShouldRetry(Exception ex)
    {
      if (ex == null) return false;

      if (ex is AggregateException agg)
      {
        return agg.InnerExceptions.Any(ShouldRetry);
      }

      // Retry UNLESS it is a 429 and the quota is exhausted
      var httpEx = ex as HttpRequestException;
      if (httpEx != null)
      {
        var statusCode = httpEx.GetStatusCode();
        if (statusCode.HasValue && RetryableCodes.Contains(statusCode.Value))
        {
          return !(statusCode.Value == HttpStatusCode.TooManyRequests && IsQuotaExhausted(ex));
        }
      }
      return false;
    }

    // Wraps a test function with retry logic for retryable errors (e.g. 429).
    // The AI SDK can return 429 errors when the service is rate limited.
    // Retrying at the github runner level will result in even more load and
    // therefore potentially more 429 errors. So we try this on per test level.
    private async Task RetryTestWithExponentialBackoff(string testName, Func<Task> testAction)
    {
      int delayMilliseconds = InitialRetryDelayMilliseconds;

      for (int i = 0; i <= MaxRetries; i++)
      {
        try
        {
          await testAction();
          return;
        }
        catch (Exception ex)
        {
            // Check if it's a retryable error and we have retries left.
            if (i < MaxRetries && ShouldRetry(ex))
            {
                // Log and wait
                DebugLog($"Retryable Error encountered in {testName}: Retrying attempt {i + 1} of {MaxRetries}...");
                DebugLog($"{testName} has error message: {ex.Message}");
                int jitter = UnityEngine.Random.Range(0, 1000);
                // As we tend to run multiple tests in parallel especially on github runners running desktop,
                // android, and ios all at the same time, we add jitter to the delay to avoid the tests
                // hammering the service at the same time.
                await Task.Delay(delayMilliseconds + jitter);
                delayMilliseconds *= 2;
            }
            else
            {
                throw;
            }
        }
      }
    }

    protected override void Start()
    {
      // Set of tests that use multiple backends.
      Func<Backend, Task>[] multiBackendTests = {
        TestCreateModel,
        TestBasicText,
        TestBasicImage,
        TestModelOptions,
        TestCountTokens,
#if !(FIREBASE_RUNNING_FROM_CI && !UNITY_EDITOR)
        // Disabled from CI, because of rate limit issues
        TestMultipleCandidates,
        TestBasicTextStream,
        TestFunctionCallingAny,
        TestFunctionCallingNone,
        TestEnumSchemaResponse,
        TestAnyOfSchemaResponse,
        TestSearchGrounding,
        TestChatBasicTextNoHistory,
        TestChatBasicTextPriorHistory,
        TestChatFunctionCalling,
        TestChatBasicTextStream,
        TestYoutubeLink,
        TestGenerateImage,
        TestImagenGenerateImage,
        TestImagenGenerateImageOptions,
        TestThinkingBudget,
        TestIncludeThoughts,
        TestCodeExecution,
        TestUrlContext,
        TestTemplateGenerateContent,
        TestTemplateGenerateContentStream,
        TestTemplateImagenGenerateImage,
#endif
      };
      // Set of tests that only run the single time.
      Func<Task>[] singleTests = {
        TestReadFile,
        TestReadSecureFile,
        // Internal tests for Json parsing, requires using a source library.
        InternalTestBasicReplyShort,
        InternalTestCitations,
        InternalTestBlockedSafetyWithMessage,
        InternalTestFinishReasonSafetyNoContent,
        InternalTestUnknownEnumSafetyRatings,
        InternalTestFunctionCallWithArguments,
        InternalTestVertexAIGrounding,
        InternalTestGoogleAIGrounding,
        InternalTestGoogleAIGroundingEmptyChunks,
        InternalTestGroundingMetadata_Empty,
        InternalTestSegment_Empty,
        InternalTestCountTokenResponse,
        InternalTestBasicResponseLongUsageMetadata,
        InternalTestGoogleAIBasicReplyShort,
        InternalTestGoogleAICitations,
        InternalTestGenerateImagesBase64,
        InternalTestGenerateImagesAllFiltered,
        InternalTestGenerateImagesBase64SomeFiltered,
        InternalTestThoughtSummary,
        InternalTestCodeExecution,
        InternalTestUrlContextMixedValidity,
        InternalTestUsageMetadataWithCaching,
      };

      // Create the set of tests, combining the above lists.
      List<Func<Task>> tests = new();
      List<string> testNames = new();
      foreach (Backend backend in Enum.GetValues(typeof(Backend)))
      {
        foreach (var testMethod in multiBackendTests)
        {
          string testName = $"{testMethod.Method.Name}_{backend}";
          tests.Add(() => RetryTestWithExponentialBackoff(testName, () => testMethod(backend)));
          testNames.Add(testName);
        }
      }
      foreach (var testMethod in singleTests)
      {
        tests.Add(() => RetryTestWithExponentialBackoff(testMethod.Method.Name, testMethod));
        testNames.Add(testMethod.Method.Name);
      }

      testRunner = AutomatedTestRunner.CreateTestRunner(
        testsToRun: tests.ToArray(),
        testNames: testNames.ToArray(),
        logFunc: DebugLog
      );

      // Some of the AI tests tend to take a bit longer, so increase the timeout.
      testRunner.TestTimeoutSeconds = 120f;

      base.Start();
    }

    // Passes along the update call to automated test runner.
    protected override void Update()
    {
      base.Update();
      if (testRunner != null)
      {
        testRunner.Update();
      }
    }

    // Throw when condition is false.
    private void Assert(string message, bool condition)
    {
      if (!condition)
      {
        throw new Exception(
          $"Assertion failed ({testRunner.CurrentTestDescription}): {message}");
      }
    }

    // Throw when value1 != value2.
    private void AssertEq<T>(string message, T value1, T value2)
    {
      if (!object.Equals(value1, value2))
      {
        throw new Exception(
          $"Assertion failed ({testRunner.CurrentTestDescription}): {value1} != {value2} ({message})");
      }
    }

    // Throw when the floats are not close enough in value to each other.
    private void AssertFloatEq(string message, float value1, float value2)
    {
      if (!(Math.Abs(value1 - value2) < 0.0001f))
      {
        throw new Exception(
          $"Assertion failed ({testRunner.CurrentTestDescription}): {value1} !~= {value2} ({message})");
      }
    }

    private void AssertType<T>(string message, object obj, out T output)
    {
      if (obj is T parsed)
      {
        output = parsed;
      }
      else
      {
        throw new Exception(
          $"Assertion failed ({testRunner.CurrentTestDescription}): {obj.GetType()} is wrong type ({message})");
      }
    }

    // Returns true if the given value is between 0 and 1 (inclusive).
    private bool ValidProbability(float value)
    {
      return value >= 0.0f && value <= 1.0f;
    }

    // The model name to use for the tests.
    private readonly string TestModelName = "gemini-2.5-flash";

    private FirebaseAI GetFirebaseAI(Backend backend, string location = "us-central1")
    {
      return backend switch
      {
        Backend.GoogleAI => FirebaseAI.GetInstance(FirebaseAI.Backend.GoogleAI()),
        Backend.VertexAI => FirebaseAI.GetInstance(FirebaseAI.Backend.VertexAI(location)),
        _ => throw new ArgumentOutOfRangeException(nameof(backend), backend,
                "Unhandled Backend type"),
      };
    }

    // Get a basic version of the GenerativeModel to test against.
    private GenerativeModel CreateGenerativeModel(Backend backend)
    {
      return GetFirebaseAI(backend).GetGenerativeModel(TestModelName);
    }

    // Test if it can create the GenerativeModel.
    Task TestCreateModel(Backend backend)
    {
      var model = CreateGenerativeModel(backend);
      Assert("Failed to create a GenerativeModel.", model != null);
      return Task.CompletedTask;
    }

    // Test if it can set a string in, and get a string output.
    async Task TestBasicText(Backend backend)
    {
      var model = CreateGenerativeModel(backend);

      GenerateContentResponse response = await model.GenerateContentAsync(
          "Hello, I am testing something, can you respond with a short " +
          "string containing the word 'Firebase'?");

      Assert("Response missing candidates.", response.Candidates.Any());

      string result = response.Text;

      Assert("Response text was missing", !string.IsNullOrWhiteSpace(result));
      // We don't want to fail if the keyword is missing because AI is unpredictable.
      if (!response.Text.Contains("Firebase"))
      {
        DebugLog("WARNING: Response string was missing the expected keyword 'Firebase': " +
            $"\n{result}");
      }

      Assert("Response contained FunctionCalls when it shouldn't",
          !response.FunctionCalls.Any());

      // Ignoring PromptFeedback, too unpredictable if it will be present for this test.

      if (response.UsageMetadata.HasValue)
      {
        Assert("Invalid CandidatesTokenCount", response.UsageMetadata?.CandidatesTokenCount > 0);
        Assert("Invalid PromptTokenCount", response.UsageMetadata?.PromptTokenCount > 0);
        Assert("Invalid TotalTokenCount", response.UsageMetadata?.TotalTokenCount > 0);
      }
      else
      {
        DebugLog("WARNING: UsageMetadata was missing from BasicText");
      }

      Candidate candidate = response.Candidates.First();
      Assert($"Candidate has incorrect FinishReason: {candidate.FinishReason}",
        candidate.FinishReason == FinishReason.Stop);

      // Test the SafetyRatings, if we got any.
      foreach (SafetyRating safetyRating in candidate.SafetyRatings)
      {
        string prefix = $"SafetyRating {safetyRating.Category}";
        Assert($"{prefix} claims it was blocked", !safetyRating.Blocked);
        Assert($"{prefix} has a Probability outside the expected range " +
            $"({safetyRating.ProbabilityScore})",
            ValidProbability(safetyRating.ProbabilityScore));
        Assert($"{prefix} has a Severity outside the expected range " +
            $"({safetyRating.SeverityScore})",
            ValidProbability(safetyRating.SeverityScore));

        // They should be Negligible, but AI can be unpredictable, so just warn
        if (safetyRating.Probability != SafetyRating.HarmProbability.Negligible)
        {
          DebugLog($"WARNING: {prefix} has a high probability: {safetyRating.Probability}");
        }
        if (safetyRating.Severity != SafetyRating.HarmSeverity.Negligible)
        {
          DebugLog($"WARNING: {prefix} has a high severity: {safetyRating.Severity}");
        }
      }

      // For such a basic text, we don't expect citation data, so warn.
      if (candidate.CitationMetadata.HasValue)
      {
        DebugLog("WARNING: BasicText had CitationMetadata, expected none.");
      }
    }

    // Test if passing an Image and Text works.
    async Task TestBasicImage(Backend backend)
    {
      var model = CreateGenerativeModel(backend);

      Assert("Missing RedBlueTexture", RedBlueTexture != null);

      byte[] imageData = ImageConversion.EncodeToPNG(RedBlueTexture);
      Assert("Image encoding failed", imageData != null && imageData.Length > 0);

      GenerateContentResponse response = await model.GenerateContentAsync(new ModelContent[] {
        ModelContent.Text("I am testing Image input. What two colors do you see in the included image?"),
        ModelContent.InlineData("image/png", imageData)
      });

      Assert("Response missing candidates.", response.Candidates.Any());

      string result = response.Text;

      Assert("Response text was missing", !string.IsNullOrWhiteSpace(result));
      // We don't want to fail if the colors are missing/wrong because AI is unpredictable.
      if (!response.Text.Contains("red", StringComparison.OrdinalIgnoreCase) ||
          !response.Text.Contains("blue", StringComparison.OrdinalIgnoreCase))
      {
        DebugLog("WARNING: Response string was missing the correct colors: " +
            $"\n{result}");
      }
    }

    // Test if passing in multiple model options works.
    async Task TestModelOptions(Backend backend)
    {
      // Note that most of these settings are hard to reliably verify, so as
      // long as the call works we are generally happy.
      var model = GetFirebaseAI(backend).GetGenerativeModel(TestModelName,
        generationConfig: new GenerationConfig(
          temperature: 0.4f,
          topP: 0.4f,
          topK: 30,
          // Intentionally skipping candidateCount, tested elsewhere.
          maxOutputTokens: 100,
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
      if (result.Trim() != "Apples")
      {
        DebugLog($"WARNING: Response text wasn't just 'Apples': {result}");
      }
    }

    // Test if requesting multiple candidates works.
    async Task TestMultipleCandidates(Backend backend)
    {
      var genConfig = new GenerationConfig(candidateCount: 2);

      var model = GetFirebaseAI(backend).GetGenerativeModel(TestModelName,
        generationConfig: genConfig
      );

      GenerateContentResponse response = await model.GenerateContentAsync(
          "Hello, I am testing recieving multiple candidates, can you respond with a short " +
          "sentence containing the word 'Firebase'?");

      AssertEq("Incorrect number of Candidates", response.Candidates.Count(), 2);
    }

    // Test if generating a stream of text works.
    async Task TestBasicTextStream(Backend backend)
    {
      var model = CreateGenerativeModel(backend);

      string keyword = "Firebase";
      var responseStream = model.GenerateContentStreamAsync(
          "Hello, I am testing streaming. Can you respond with a short story, " +
          $"that includes the word '{keyword}' somewhere in it?");

      // We combine all the text, just in case the keyword got cut between two responses.
      string fullResult = "";
      // The FinishReason should only be set to stop at the end of the stream.
      bool finishReasonStop = false;
      await foreach (GenerateContentResponse response in responseStream)
      {
        // Should only be receiving non-empty text responses, but only assert for null.
        string text = response.Text;
        Assert("Received null text from the stream.", text != null);
        if (string.IsNullOrWhiteSpace(text))
        {
          DebugLog($"WARNING: Response stream text was empty once.");
        }

        Assert("Previous FinishReason was stop, but received more", !finishReasonStop);
        if (response.Candidates.First().FinishReason == FinishReason.Stop)
        {
          finishReasonStop = true;
        }

        fullResult += text;
      }

      Assert("Finished without seeing FinishReason.Stop", finishReasonStop);

      // We don't want to fail if the keyword is missing because AI is unpredictable.
      if (!fullResult.Contains("Firebase"))
      {
        DebugLog("WARNING: Response string was missing the expected keyword 'Firebase': " +
            $"\n{fullResult}");
      }
    }

    private readonly string basicFunctionName = "MyBasicTestFunction";
    private readonly string basicParameterEnumName = "basicTestEnumParameter";
    private readonly string basicParameterEnumValue = "MyBasicTestEnum";
    private readonly string basicParameterIntName = "basicTestIntParameter";
    private readonly string basicParameterObjectName = "basicTestObjectParameter";
    private readonly string basicParameterObjectBoolean = "BasicTestObjectBoolean";
    private readonly string basicParameterObjectFloat = "BasicTestObjectFloat";

    // Create a GenerativeModel using the parameters above to test Function Calling.
    private GenerativeModel CreateGenerativeModelWithBasicFunctionCall(
      Backend backend,
      ToolConfig? toolConfig = null)
    {
      var tool = new Tool(new FunctionDeclaration(
        basicFunctionName, "A function used to test Function Calling.",
        new Dictionary<string, Schema>() {
          { basicParameterEnumName, Schema.Enum(new string[] { basicParameterEnumValue }) },
          { basicParameterIntName, Schema.Int("An integer value", minimum: 4) },
          { basicParameterObjectName, Schema.Object(new Dictionary<string, Schema>() {
              { basicParameterObjectBoolean, Schema.Boolean("Is the float you are including negative?") },
              { basicParameterObjectFloat, Schema.Float(nullable: true, maximum: 128f) }
            }) }
        }));

      return GetFirebaseAI(backend).GetGenerativeModel(TestModelName,
        tools: new Tool[] { tool },
        toolConfig: toolConfig
      );
    }

    // Test if FunctionCalling works, using Any to force it.
    async Task TestFunctionCallingAny(Backend backend)
    {
      // Setting this to Any should force my function call.
      var model = CreateGenerativeModelWithBasicFunctionCall(backend, new ToolConfig(FunctionCallingConfig.Any()));

      GenerateContentResponse response = await model.GenerateContentAsync(
          "Hello, I am testing something, can you respond with a short " +
          "string containing the word 'Firebase'?");

      Assert("Response missing candidates.", response.Candidates.Any());
      var functionCalls = response.FunctionCalls;
      AssertEq("Wrong number of Function Calls", functionCalls.Count(), 1);
      var functionCall = functionCalls.First();
      AssertEq("Wrong FunctionCall name", functionCall.Name, basicFunctionName);
      AssertEq("Wrong number of Args", functionCall.Args.Count, 3);
      Assert($"Missing parameter {basicParameterEnumName}", functionCall.Args.ContainsKey(basicParameterEnumName));
      Assert($"Missing parameter {basicParameterIntName}", functionCall.Args.ContainsKey(basicParameterIntName));
      Assert($"Missing parameter {basicParameterObjectName}", functionCall.Args.ContainsKey(basicParameterObjectName));
      AssertEq("Wrong parameter enum value", functionCall.Args[basicParameterEnumName], basicParameterEnumValue);
      // Ints are returned as longs
      AssertType("ParameterInt", functionCall.Args[basicParameterIntName], out long _);
      AssertType("ParameterObject", functionCall.Args[basicParameterObjectName], out Dictionary<string, object> parameterObject);
      Assert($"Missing object field {basicParameterObjectBoolean}", parameterObject.ContainsKey(basicParameterObjectBoolean));
      AssertType("ObjectBool", parameterObject[basicParameterObjectBoolean], out bool _);
      Assert($"Missing object field {basicParameterObjectFloat}", parameterObject.ContainsKey(basicParameterObjectFloat));
      // The float should be a double, but could be a null, or a long (if the response didn't include a decimal).
      var objectFloat = parameterObject[basicParameterObjectFloat];
      Assert($"Object float is the wrong type {objectFloat?.GetType()}", objectFloat == null || objectFloat is double || objectFloat is long);
    }

    // Test if setting None will prevent Function Calling.
    async Task TestFunctionCallingNone(Backend backend)
    {
      // Setting this to None should block my function call.
      var model = CreateGenerativeModelWithBasicFunctionCall(backend, new ToolConfig(FunctionCallingConfig.None()));

      GenerateContentResponse response = await model.GenerateContentAsync(
          "Hello, I am testing something, can you call my function?");

      Assert("Response missing candidates.", response.Candidates.Any());
      var functionCalls = response.FunctionCalls;
      AssertEq("Wrong number of Function Calls", functionCalls.Count(), 0);
    }

    // Test if setting a response schema with an enum works.
    async Task TestEnumSchemaResponse(Backend backend)
    {
      string enumValue = "MyTestEnum";
      var model = GetFirebaseAI(backend).GetGenerativeModel(TestModelName,
        generationConfig: new GenerationConfig(
          responseMimeType: "text/x.enum",
          responseSchema: Schema.Enum(new string[] { enumValue })));

      var response = await model.GenerateContentAsync(
        "Hello, I am testing setting the response schema to an enum.");

      AssertEq("Should only be returning the single enum given", response.Text, enumValue);
    }

    // Test if setting a response schema with an enum works.
    async Task TestAnyOfSchemaResponse(Backend backend)
    {
      var model = GetFirebaseAI(backend).GetGenerativeModel(TestModelName,
        generationConfig: new GenerationConfig(
          responseMimeType: "application/json",
          responseSchema: Schema.Array(
              Schema.AnyOf(new[] { Schema.Int(), Schema.String() }),
              minItems: 2,
              maxItems: 6)));

      var response = await model.GenerateContentAsync(
        "Hello, I am testing setting the response schema with an array, cause you give me some random values.");

      // There isn't much guarantee on what this will respond with. We just want non-empty.
      Assert("Response was empty.", !string.IsNullOrWhiteSpace(response.Text));
    }

    // Test grounding with Google Search.
    async Task TestSearchGrounding(Backend backend)
    {
      // Use a model that supports grounding.
      var model = GetFirebaseAI(backend).GetGenerativeModel(TestModelName,
        tools: new Tool[] { new Tool(new GoogleSearch()) }
      );

      // A prompt that requires recent information.
      GenerateContentResponse response = await model.GenerateContentAsync("What's the current weather in Toronto?");

      Assert("Response missing candidates.", response.Candidates.Any());

      string result = response.Text;
      Assert("Response text was missing", !string.IsNullOrWhiteSpace(result));

      var candidate = response.Candidates.First();
      Assert("GroundingMetadata should not be null when GoogleSearch tool is used.",
          candidate.GroundingMetadata.HasValue);

      var groundingMetadata = candidate.GroundingMetadata.Value;

      Assert("WebSearchQueries should not be empty.",
          groundingMetadata.WebSearchQueries.Any());

      Assert("GroundingChunks should not be empty.",
          groundingMetadata.GroundingChunks.Any());

      Assert("GroundingSupports should not be empty.",
          groundingMetadata.GroundingSupports.Any());

      Assert("SearchEntryPoint should not be null.",
          groundingMetadata.SearchEntryPoint.HasValue);

      Assert("SearchEntryPoint.RenderedContent should not be empty.",
          !string.IsNullOrWhiteSpace(groundingMetadata.SearchEntryPoint?.RenderedContent));
    }

    // Test if when using Chat the model will get the previous messages.
    async Task TestChatBasicTextNoHistory(Backend backend)
    {
      var model = CreateGenerativeModel(backend);
      var chat = model.StartChat();

      string keyword = "Firebase";
      GenerateContentResponse response1 = await chat.SendMessageAsync(
          $"Hello, I am testing chat history, can you include the word '{keyword}' " +
          "in all future responses?");

      Assert("First response was empty.", !string.IsNullOrWhiteSpace(response1.Text));
      if (!response1.Text.Contains(keyword))
      {
        DebugLog($"WARNING: First response string was missing the expected keyword '{keyword}': " +
            $"\n{response1.Text}");
      }

      GenerateContentResponse response2 = await chat.SendMessageAsync(
          "Thanks. Can you response with another short sentence? Be sure to " +
          "include the special word I told you before in it.");

      Assert("Second response was empty.", !string.IsNullOrWhiteSpace(response2.Text));
      if (!response2.Text.Contains(keyword))
      {
        DebugLog($"WARNING: Second response string was missing the expected keyword '{keyword}': " +
            $"\n{response2.Text}");
      }

      AssertEq("Chat history length is wrong", chat.History.Count(), 4);
    }

    // Test if when using Chat the model gets the initial starting history.
    async Task TestChatBasicTextPriorHistory(Backend backend)
    {
      var model = CreateGenerativeModel(backend);
      string keyword = "Firebase";
      var chat = model.StartChat(
          ModelContent.Text($"Hello, please include '{keyword}' in all your reponses."),
          new ModelContent("model",
              new ModelContent.TextPart($"Golly gee whiz, I love {keyword}.")));

      GenerateContentResponse response = await chat.SendMessageAsync(
          "Hello, I am testing chat history, can you write a short sentence " +
          "with that special word?");

      Assert("Response was empty.", !string.IsNullOrWhiteSpace(response.Text));
      if (!response.Text.Contains(keyword))
      {
        DebugLog($"WARNING: Response string was missing the expected keyword '{keyword}': " +
            $"\n{response.Text}");
      }

      AssertEq("Chat history length is wrong", chat.History.Count(), 4);
    }

    // Test if when using Chat, the model handles Function Calling, and getting a response.
    async Task TestChatFunctionCalling(Backend backend)
    {
      var tool = new Tool(new FunctionDeclaration(
        "GetKeyword", "Call to retrieve a special keyword.",
        new Dictionary<string, Schema>() {
          { "input", Schema.String("Input string") },
        }));
      var model = GetFirebaseAI(backend).GetGenerativeModel(TestModelName,
        tools: new Tool[] { tool }
      );
      var chat = model.StartChat();

      string keyword = "Firebase";
      string expectedInput = "Banana";
      GenerateContentResponse response1 = await chat.SendMessageAsync(
          "Hello, I am testing function calling with Chat. Can you return a short " +
          $"sentence with the special keyword? Pass in '{expectedInput}' as the input.");

      // Validate the Function Call happened.
      Assert("First response missing candidates.", response1.Candidates.Any());
      var functionCalls = response1.FunctionCalls;
      AssertEq("Wrong number of Function Calls", functionCalls.Count(), 1);
      var functionCall = functionCalls.First();
      AssertEq("Wrong FunctionCall name", functionCall.Name, "GetKeyword");
      AssertEq("Wrong number of Args", functionCall.Args.Count, 1);
      Assert($"Missing parameter", functionCall.Args.ContainsKey("input"));
      AssertType("Input parameter", functionCall.Args["input"], out string inputParameter);
      if (inputParameter != expectedInput)
      {
        DebugLog($"WARNING: Input parameter: {inputParameter} != {expectedInput}");
      }

      // Respond with the requested FunctionCall with the keyword.
      var response2 = await chat.SendMessageAsync(ModelContent.FunctionResponse("GetKeyword",
          new Dictionary<string, object>() {
            { "result" , keyword }
      }));

      // Second response should hopefully have the keyword as part of it.
      Assert("Second response was empty.", !string.IsNullOrWhiteSpace(response2.Text));
      if (!response2.Text.Contains(keyword))
      {
        DebugLog($"WARNING: Response string was missing the expected keyword '{keyword}': " +
            $"\n{response2.Text}");
      }

      AssertEq("Chat history length is wrong", chat.History.Count(), 4);
    }

    // Test if Chat works with streaming a text result.
    async Task TestChatBasicTextStream(Backend backend)
    {
      var model = CreateGenerativeModel(backend);

      string keyword = "Firebase";
      var chat = model.StartChat(
          ModelContent.Text($"Hello, please include '{keyword}' in all your reponses."),
          new ModelContent("model",
              new ModelContent.TextPart($"Golly gee whiz, I love {keyword}.")));

      var responseStream = chat.SendMessageStreamAsync(
          "Hello, I am testing streaming. Can you respond with a short sentence, " +
          "and be sure to include the word I gave you before.");

      // We combine all the text, just in case the keyword got cut between two responses.
      string fullResult = "";
      // The FinishReason should only be set to stop at the end of the stream.
      bool finishReasonStop = false;
      int responseCount = 0;
      await foreach (GenerateContentResponse response in responseStream)
      {
        // Should only be receiving non-empty text responses, but only assert for null.
        string text = response.Text;
        Assert("Received null text from the stream.", text != null);
        if (string.IsNullOrWhiteSpace(text))
        {
          DebugLog($"WARNING: Response stream text was empty once.");
        }

        Assert("Previous FinishReason was stop, but received more", !finishReasonStop);
        if (response.Candidates.First().FinishReason == FinishReason.Stop)
        {
          finishReasonStop = true;
        }

        fullResult += text;
        responseCount++;
      }

      Assert("Finished without seeing FinishReason.Stop", finishReasonStop);

      // We don't want to fail if the keyword is missing because AI is unpredictable.
      if (!fullResult.Contains(keyword))
      {
        DebugLog($"WARNING: Streaming response was missing the expected keyword '{keyword}': " +
            $"\n{fullResult}");
      }

      // The chat history should be:
      //    The 2 original messages that were given as history.
      //    The 1 from the request.
      //    However many streaming responses were given back (stored in responseCount).
      AssertEq("Chat history length is wrong", chat.History.Count(), 3 + responseCount);
    }

    // Test if calling CountTokensAsync works as expected.
    async Task TestCountTokens(Backend backend)
    {
      // Include some additional settings, since they are used in the call.
      var model = GetFirebaseAI(backend).GetGenerativeModel(TestModelName,
        generationConfig: new GenerationConfig(temperature: 0.8f),
        systemInstruction: ModelContent.Text("This is a test SystemInstruction")
      );

      CountTokensResponse response = await model.CountTokensAsync("Hello, I am testing CountTokens!");

      Assert($"CountTokens TotalTokens {response.TotalTokens}", response.TotalTokens > 0);

      AssertEq("CountTokens PromptTokenDetails", response.PromptTokensDetails.Count(), 1);
      var details = response.PromptTokensDetails.First();
      AssertEq("CountToken Detail Modality", details.Modality, ContentModality.Text);
      Assert($"CountToken Detail TokenCount {details.TokenCount}", details.TokenCount > 0);
    }

    // Test being able to provide a Youtube link to the model.
    async Task TestYoutubeLink(Backend backend)
    {
      var model = CreateGenerativeModel(backend);

      GenerateContentResponse response = await model.GenerateContentAsync(new ModelContent[] {
        ModelContent.Text("I am testing Youtube input. Can you give a short description of the video that I've linked you to?"),
        ModelContent.FileData("video/mp4", new Uri($"https://www.youtube.com/watch?v=cEr8XCnoSVY"))
      });

      Assert("Response missing candidates.", response.Candidates.Any());

      Assert($"Response should have included Firebase: {response.Text}",
          response.Text.Contains("Firebase", StringComparison.OrdinalIgnoreCase));
    }

    // Test being able to generate an image with GenerateContent.
    async Task TestGenerateImage(Backend backend)
    {
      var model = GetFirebaseAI(backend).GetGenerativeModel("gemini-2.5-flash-image",
        generationConfig: new GenerationConfig(
          responseModalities: new[] { ResponseModality.Text, ResponseModality.Image })
      );

      GenerateContentResponse response = await model.GenerateContentAsync(
        ModelContent.Text("Generate a picture of a cartoon dog, and a couple of sentences about him?")
      );

      Assert("Response missing candidates.", response.Candidates.Any());

      // We don't care much about the response, just that there is an image, and text.
      bool foundText = false;
      bool foundImage = false;
      var candidate = response.Candidates.First();
      foreach (var part in candidate.Content.Parts)
      {
        if (part is ModelContent.TextPart)
        {
          foundText = true;
        }
        else if (part is ModelContent.InlineDataPart dataPart)
        {
          if (dataPart.MimeType.Contains("image"))
          {
            foundImage = true;
          }
        }
      }
      Assert($"Missing expected modalities. Text: {foundText}, Image: {foundImage}", foundText && foundImage);
    }

    // Test generating an image via Imagen.
    async Task TestImagenGenerateImage(Backend backend)
    {
      var model = GetFirebaseAI(backend).GetImagenModel("imagen-4.0-generate-001");

      var response = await model.GenerateImagesAsync(
          "Generate an image of a cartoon dog.");

      // We can't easily test if the image is correct, but can check other random data.
      AssertEq("FilteredReason", response.FilteredReason, null);
      AssertEq("Image Count", response.Images.Count, 1);

      AssertEq($"Image MimeType", response.Images[0].MimeType, "image/png");

      var texture = response.Images[0].AsTexture2D();
      Assert($"Image as Texture2D", texture != null);
      // By default the image should be Square 1x1, so check for that.
      Assert($"Image Height > 0", texture.height > 0);
      AssertEq($"Image Height = Width", texture.height, texture.width);
    }

    // Test generating an image via Imagen with various options.
    async Task TestImagenGenerateImageOptions(Backend backend)
    {
      var model = GetFirebaseAI(backend).GetImagenModel(
          modelName: "imagen-4.0-generate-001",
          generationConfig: new ImagenGenerationConfig(
            // negativePrompt and addWatermark are not supported on this version of the model.
            numberOfImages: 2,
            aspectRatio: ImagenAspectRatio.Landscape4x3,
            imageFormat: ImagenImageFormat.Jpeg(50)
          ),
          safetySettings: new ImagenSafetySettings(
            safetyFilterLevel: ImagenSafetySettings.SafetyFilterLevel.BlockLowAndAbove,
            personFilterLevel: ImagenSafetySettings.PersonFilterLevel.BlockAll),
          requestOptions: new RequestOptions(timeout: TimeSpan.FromMinutes(1)));

      var response = await model.GenerateImagesAsync(
          "Generate an image of a cartoon dog.");

      // We can't easily test if the image is correct, but can check other random data.
      AssertEq("FilteredReason", response.FilteredReason, null);
      AssertEq("Image Count", response.Images.Count, 2);

      for (int i = 0; i < 2; i++)
      {
        AssertEq($"Image {i} MimeType", response.Images[i].MimeType, "image/jpeg");

        var texture = response.Images[i].AsTexture2D();
        Assert($"Image {i} as Texture2D", texture != null);
        // By default the image should be Landscape 4x3, so check for that.
        Assert($"Image {i} Height > 0", texture.height > 0);
        Assert($"Image {i} Height < Width {texture.height} < {texture.width}",
            texture.height < texture.width);
      }
    }

    // Test defining a thinking budget, and getting back thought tokens.
    async Task TestThinkingBudget(Backend backend)
    {
      // Thinking Budget requires at least the 2.5 model.
      var model = GetFirebaseAI(backend).GetGenerativeModel(
        modelName: TestModelName,
        generationConfig: new GenerationConfig(
          thinkingConfig: new ThinkingConfig(
            thinkingBudget: 1024
          )
        )
      );

      GenerateContentResponse response = await model.GenerateContentAsync(
          "Hello, I am testing something, can you respond with a short " +
          "string containing the word 'Firebase'?");

      string result = response.Text;
      Assert("Response text was missing", !string.IsNullOrWhiteSpace(result));

      // ThoughtSummary should be null since we didn't set includeThoughts.
      Assert("ThoughtSummary wasn't null", string.IsNullOrWhiteSpace(response.ThoughtSummary));

      Assert("UsageMetadata was missing", response.UsageMetadata != null);
      Assert("UsageMetadata.ThoughtsTokenCount was missing",
        response.UsageMetadata?.ThoughtsTokenCount > 0);
    }

    // Test requesting thought summaries.
    async Task TestIncludeThoughts(Backend backend)
    {
      var model = GetFirebaseAI(backend, "global").GetGenerativeModel(
        modelName: "gemini-3-flash-preview",
        generationConfig: new GenerationConfig(
          thinkingConfig: new ThinkingConfig(
            thinkingLevel: ThinkingConfig.ThinkingLevel.Low,
            includeThoughts: true
          )
        )
      );

      GenerateContentResponse response = await model.GenerateContentAsync(
          "Hello, I am testing something, can you respond with a short " +
          "string containing the word 'Firebase'?");

      string result = response.Text;
      Assert("Response text was missing", !string.IsNullOrWhiteSpace(result));

      Assert("ThoughtSummary was missing", !string.IsNullOrWhiteSpace(response.ThoughtSummary));
    }

    async Task TestCodeExecution(Backend backend)
    {
      var model = GetFirebaseAI(backend).GetGenerativeModel(
        modelName: TestModelName,
        tools: new Tool[] { new Tool(new CodeExecution()) }
      );

      var prompt = "What is the sum of the first 50 prime numbers? Generate and run code for the calculation.";
      var chat = model.StartChat();
      var response = await chat.SendMessageAsync(prompt);

      string result = response.Text;
      Assert("Response text was missing", !string.IsNullOrWhiteSpace(result));

      var executableCodeParts = response.Candidates.First().Content.Parts.OfType<ModelContent.ExecutableCodePart>();
      Assert("Missing ExecutableCodeParts", executableCodeParts.Any());

      var codeExecutionResultParts = response.Candidates.First().Content.Parts.OfType<ModelContent.CodeExecutionResultPart>();
      Assert("Missing CodeExecutionResultParts", codeExecutionResultParts.Any());
    }

    async Task TestUrlContext(Backend backend)
    {
      if (backend == Backend.GoogleAI)
      {
        // TODO: Remove when the backend is more reliable
        // The Developer backend keeps raising issues with URL context, so disable for now
        return;
      }

      var model = GetFirebaseAI(backend).GetGenerativeModel(
        // Url Context requires 2.5 or newer
        modelName: TestModelName,
        tools: new Tool[] { new Tool(new UrlContext()) }
      );

      var prompt = "Can you summarize https://firebase.google.com/games";
      var response = await model.GenerateContentAsync(prompt);

      string result = response.Text;
      Assert("Response text was missing", !string.IsNullOrWhiteSpace(result));

      // We don't want to check on anything specific, but it should ideally have the metadata.
      if (!response.Candidates.First().UrlContextMetadata.HasValue)
      {
        DebugLog("WARNING: Response did not have expected Url Context Metadata.");
      }
    }

    async Task TestTemplateGenerateContent(Backend backend)
    {
      var model = GetFirebaseAI(backend, "global").GetTemplateGenerativeModel();

      var inputs = new Dictionary<string, object>()
      {
        ["customerName"] = "Jane"
      };
      var response = await model.GenerateContentAsync("input-system-instructions", inputs);

      string result = response.Text;
      Assert("Response text was missing", !string.IsNullOrWhiteSpace(result));
    }

    async Task TestTemplateGenerateContentStream(Backend backend)
    {
      var model = GetFirebaseAI(backend, "global").GetTemplateGenerativeModel();

      var inputs = new Dictionary<string, object>()
      {
        ["customerName"] = "Jane"
      };
      var responseStream = model.GenerateContentStreamAsync("input-system-instructions", inputs);

      // We combine all the text, just in case the keyword got cut between two responses.
      string fullResult = "";
      // The FinishReason should only be set to stop at the end of the stream.
      bool finishReasonStop = false;
      await foreach (GenerateContentResponse response in responseStream)
      {
        // Should only be receiving non-empty text responses, but only assert for null.
        string text = response.Text;
        Assert("Received null text from the stream.", text != null);
        if (string.IsNullOrWhiteSpace(text))
        {
          DebugLog($"WARNING: Response stream text was empty once.");
        }

        Assert("Previous FinishReason was stop, but received more", !finishReasonStop);
        if (response.Candidates.First().FinishReason == FinishReason.Stop)
        {
          finishReasonStop = true;
        }

        fullResult += text;
      }

      Assert("Response text was missing", !string.IsNullOrWhiteSpace(fullResult));
      Assert("Finished without seeing FinishReason.Stop", finishReasonStop);
    }

    async Task TestTemplateImagenGenerateImage(Backend backend)
    {
      var model = GetFirebaseAI(backend).GetTemplateImagenModel();

      var inputs = new Dictionary<string, object>()
      {
        ["prompt"] = "flowers",
      };
      var response = await model.GenerateImagesAsync(
          "imagen-generation-basic", inputs);

      // We can't easily test if the image is correct, but can check other random data.
      AssertEq("FilteredReason", response.FilteredReason, null);
      AssertEq("Image Count", response.Images.Count, 1);

      AssertEq($"Image MimeType", response.Images[0].MimeType, "image/png");

      var texture = response.Images[0].AsTexture2D();
      Assert($"Image as Texture2D", texture != null);
      // By default the image should be Square 1x1, so check for that.
      Assert($"Image Height > 0", texture.height > 0);
      AssertEq($"Image Height = Width", texture.height, texture.width);
    }

    // Test providing a file from a GCS bucket (Firebase Storage) to the model.
    async Task TestReadFile()
    {
      // GCS is currently only supported with VertexAI.
      var model = CreateGenerativeModel(Backend.VertexAI);

      GenerateContentResponse response = await model.GenerateContentAsync(new ModelContent[] {
        ModelContent.Text("I am testing File input. Can you describe the content in the attached file?"),
        ModelContent.FileData("text/plain", new Uri($"gs://{FirebaseApp.DefaultInstance.Options.StorageBucket}/HelloWorld.txt"))
      });

      Assert("Response missing candidates.", response.Candidates.Any());

      Assert($"Response should have included 'Hello World': {response.Text}",
          response.Text.Contains("Hello World", StringComparison.OrdinalIgnoreCase));
    }

    // Test providing a file requiring authentication from a GCS bucket (Firebase Storage) to the model.
    // Should pass if Auth is included or not. To turn Auth on, define INCLUDE_FIREBASE_AUTH at the top of the file.
    async Task TestReadSecureFile()
    {
      // GCS is currently only supported with VertexAI.
      var model = CreateGenerativeModel(Backend.VertexAI);

#if INCLUDE_FIREBASE_AUTH
      var authResult = await FirebaseAuth.DefaultInstance.SignInAnonymouslyAsync();
#endif

      try
      {
        GenerateContentResponse response = await model.GenerateContentAsync(new ModelContent[] {
          ModelContent.Text("I am testing File input. Can you describe the image in the attached file?"),
          ModelContent.FileData("image/png", new Uri($"gs://{FirebaseApp.DefaultInstance.Options.StorageBucket}/FCMImages/mushroom.png"))
        });

        // Without Auth, the previous call should throw an exception.
        // With Auth, we should be able to describe the image in the file.
        Assert("Response missing candidates.", response.Candidates.Any());

        Assert($"Response should have included mushroom: {response.Text}",
            response.Text.Contains("mushroom", StringComparison.OrdinalIgnoreCase));
      }
#if !INCLUDE_FIREBASE_AUTH
      catch (HttpRequestException ex)
      {
        Assert("Missing Http Status Code 403", ex.Message.Contains("403"));
      }
#endif
      finally
      {
#if INCLUDE_FIREBASE_AUTH
        // Clean up the created user.
        await authResult.User.DeleteAsync();
#endif
      }
    }

    // The url prefix to use when fetching test data to use from the separate GitHub repo.
    readonly string testDataUrl =
        "https://raw.githubusercontent.com/FirebaseExtended/vertexai-sdk-test-data/refs/heads/main/mock-responses/";
    readonly HttpClient httpClient = new();

    private Task<string> LoadStreamingAsset(string fullPath)
    {
      TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();
      UnityWebRequest request = UnityWebRequest.Get(fullPath);
      request.SendWebRequest().completed += (_) =>
      {
        if (request.result == UnityWebRequest.Result.Success)
        {
          tcs.SetResult(request.downloadHandler.text);
        }
        else
        {
          tcs.SetResult(null);
        }
      };
      return tcs.Task;
    }

    // Gets the Json test data from the given filename, potentially downloading from a GitHub repo.
    private async Task<Dictionary<string, object>> GetJsonTestData(string filename)
    {
      string jsonString = null;
      // First, try to load the file from StreamingAssets
      string localPath = Path.Combine(Application.streamingAssetsPath, "TestData", filename);
      if (localPath.StartsWith("jar") || localPath.StartsWith("http"))
      {
        // Special case to access StreamingAsset content on Android
        jsonString = await LoadStreamingAsset(localPath);
      }
      else if (File.Exists(localPath))
      {
        jsonString = File.ReadAllText(localPath);
      }

      if (string.IsNullOrEmpty(jsonString))
      {
        var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, testDataUrl + filename));
        response.EnsureSuccessStatusCode();

        jsonString = await response.Content.ReadAsStringAsync();
      }

      return Json.Deserialize(jsonString) as Dictionary<string, object>;
    }

    private Task<Dictionary<string, object>> GetVertexJsonTestData(string filename)
    {
      return GetJsonTestData($"vertexai/{filename}");
    }

    private Task<Dictionary<string, object>> GetGoogleAIJsonTestData(string filename)
    {
      return GetJsonTestData($"googleai/{filename}");
    }

    // Helper function to validate that the response has a TextPart as expected.
    private void ValidateTextPart(GenerateContentResponse response, string expectedText)
    {
      int candidateCount = response.Candidates.Count();
      AssertEq("Candidate count", candidateCount, 1);

      Candidate candidate = response.Candidates.First();
      AssertEq("Content had the wrong role", candidate.Content.Role, "model");
      var parts = candidate.Content.Parts;
      int partsCount = parts.Count();
      AssertEq("Parts count", partsCount, 1);
      var part = parts.First();
      AssertType("TextPart", part, out ModelContent.TextPart _);
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
        bool blocked = default)
    {
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
    private void ValidateUsageMetadata(UsageMetadata? usageMetadata, int promptTokenCount,
        int candidatesTokenCount, int thoughtsTokenCount, int toolUsePromptTokenCount, int totalTokenCount)
    {
      Assert("UsageMetadata", usageMetadata.HasValue);
      AssertEq("Wrong PromptTokenCount",
          usageMetadata?.PromptTokenCount, promptTokenCount);
      AssertEq("Wrong CandidatesTokenCount",
          usageMetadata?.CandidatesTokenCount, candidatesTokenCount);
      AssertEq("Wrong ThoughtsTokenCount",
          usageMetadata?.ThoughtsTokenCount, thoughtsTokenCount);
      AssertEq("Wrong ToolUsePromptTokenCount",
          usageMetadata?.ToolUsePromptTokenCount, toolUsePromptTokenCount);
      AssertEq("Wrong TotalTokenCount",
          usageMetadata?.TotalTokenCount, totalTokenCount);
    }

    // Helper function to validate Citations.
    private void ValidateCitation(Citation citation,
        int startIndex = default,
        int endIndex = default,
        Uri uri = default,
        string title = default,
        string license = default,
        DateTime? publicationDate = default)
    {
      AssertEq("Citation.StartIndex", citation.StartIndex, startIndex);
      AssertEq("Citation.EndIndex", citation.EndIndex, endIndex);
      AssertEq("Citation.Uri", citation.Uri, uri);
      AssertEq("Citation.Title", citation.Title, title);
      AssertEq("Citation.License", citation.License, license);
      AssertEq("Citation.PublicationDate", citation.PublicationDate, publicationDate);
    }

    // Test that parsing a basic short reply works as expected.
    // https://github.com/FirebaseExtended/vertexai-sdk-test-data/blob/main/mock-responses/unary-success-basic-reply-short.json
    async Task InternalTestBasicReplyShort()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-success-basic-reply-short.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.VertexAI);

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

      ValidateUsageMetadata(response.UsageMetadata, 6, 7, 0, 0, 13);
    }

    // Test that parsing a response including Citations works.
    // https://github.com/FirebaseExtended/vertexai-sdk-test-data/blob/main/mock-responses/unary-success-citations.json
    async Task InternalTestCitations()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-success-citations.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.VertexAI);

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
    async Task InternalTestBlockedSafetyWithMessage()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-failure-prompt-blocked-safety-with-message.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.VertexAI);

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
    async Task InternalTestFinishReasonSafetyNoContent()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-failure-finish-reason-safety-no-content.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.VertexAI);

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

      ValidateUsageMetadata(response.UsageMetadata, 8, 0, 0, 0, 8);
    }

    // Test that parsing a response with unknown safety enums works.
    // https://github.com/FirebaseExtended/vertexai-sdk-test-data/blob/main/mock-responses/unary-success-unknown-enum-safety-ratings.json
    async Task InternalTestUnknownEnumSafetyRatings()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-success-unknown-enum-safety-ratings.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.VertexAI);

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

    // Test that parsing a response with a FunctionCall part works.
    async Task InternalTestFunctionCallWithArguments()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-success-function-call-with-arguments.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.VertexAI);

      AssertEq("Candidate count", response.Candidates.Count(), 1);
      var candidate = response.Candidates.First();

      AssertEq("Role", candidate.Content.Role, "model");
      AssertEq("Candidate.Parts count", candidate.Content.Parts.Count(), 1);
      AssertType("FunctionCallPart", candidate.Content.Parts.First(),
          out ModelContent.FunctionCallPart fcPart);
      AssertEq("FunctionCall name", fcPart.Name, "sum");
      AssertEq("FunctionCall args wrong length", fcPart.Args.Count, 2);
      // The Args are passed along as longs.
      AssertEq("FunctionCall args[y] wrong value", fcPart.Args["y"], 5L);
      AssertEq("FunctionCall args[x] wrong value", fcPart.Args["x"], 4L);
    }

    // Test that parsing a Vertex AI response with GroundingMetadata works.
    // https://github.com/FirebaseExtended/vertexai-sdk-test-data/blob/main/mock-responses/vertexai/unary-success-google-search-grounding.json
    async Task InternalTestVertexAIGrounding()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-success-google-search-grounding.json");

      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.VertexAI);

      Assert("Response missing candidates.", response.Candidates.Any());
      var candidate = response.Candidates.First();
      Assert("Candidate should have GroundingMetadata", candidate.GroundingMetadata.HasValue);

      var grounding = candidate.GroundingMetadata.Value;

      Assert("WebSearchQueries should not be empty", grounding.WebSearchQueries.Any());
      Assert("SearchEntryPoint should not be null", grounding.SearchEntryPoint.HasValue);
      Assert("GroundingChunks should not be empty", grounding.GroundingChunks.Any());
      var chunk = grounding.GroundingChunks.First();
      Assert("GroundingChunk.Web should not be null", chunk.Web.HasValue);
      Assert("GroundingSupports should not be empty", grounding.GroundingSupports.Any());
      var support = grounding.GroundingSupports.First();
      Assert("GroundingChunkIndices should not be empty", support.GroundingChunkIndices.Any());
    }

    // Test that parsing a Google AI response with GroundingMetadata works.
    // https://github.com/FirebaseExtended/vertexai-sdk-test-data/blob/main/mock-responses/googleai/unary-success-google-search-grounding.json
    async Task InternalTestGoogleAIGrounding()
    {
      Dictionary<string, object> json = await GetGoogleAIJsonTestData("unary-success-google-search-grounding.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.GoogleAI);

      Assert("Response missing candidates.", response.Candidates.Any());
      var candidate = response.Candidates.First();
      Assert("Candidate should have GroundingMetadata", candidate.GroundingMetadata.HasValue);

      var grounding = candidate.GroundingMetadata.Value;

      AssertEq("WebSearchQueries count", grounding.WebSearchQueries.Count(), 1);
      AssertEq("WebSearchQueries content", grounding.WebSearchQueries.First(),
          "current weather in London");

      Assert("SearchEntryPoint should not be null", grounding.SearchEntryPoint.HasValue);
      Assert("SearchEntryPoint content should not be empty", !string.IsNullOrEmpty(grounding.SearchEntryPoint.Value.RenderedContent));

      AssertEq("GroundingChunks count", grounding.GroundingChunks.Count(), 2);
      var firstChunk = grounding.GroundingChunks.First();
      Assert("GroundingChunk.Web should not be null", firstChunk.Web.HasValue);
      var webChunk = firstChunk.Web.Value;
      AssertEq("WebGroundingChunk.Title", webChunk.Title, "accuweather.com");
      Assert("WebGroundingChunk.Uri should not be null", webChunk.Uri != null);
      Assert("WebGroundingChunk.Domain should be null or empty", string.IsNullOrEmpty(webChunk.Domain));

      AssertEq("GroundingSupports count", grounding.GroundingSupports.Count(), 3);
      var firstSupport = grounding.GroundingSupports.First();
      var segment = firstSupport.Segment;
      AssertEq("Segment.Text", segment.Text, "The current weather in London, United Kingdom is cloudy.");
      AssertEq("Segment.StartIndex", segment.StartIndex, 0);
      AssertEq("Segment.PartIndex", segment.PartIndex, 0);
      AssertEq("Segment.EndIndex", segment.EndIndex, 56);
      AssertEq("GroundingChunkIndices count", firstSupport.GroundingChunkIndices.Count(), 1);
      AssertEq("GroundingChunkIndices content", firstSupport.GroundingChunkIndices.First(), 0);
    }

    // Test that parsing a Google AI response with empty GroundingChunks works.
    // https://github.com/FirebaseExtended/vertexai-sdk-test-data/blob/main/mock-responses/googleai/unary-success-google-search-grounding-empty-grounding-chunks.json
    async Task InternalTestGoogleAIGroundingEmptyChunks()
    {
      Dictionary<string, object> json = await GetGoogleAIJsonTestData("unary-success-google-search-grounding-empty-grounding-chunks.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.GoogleAI);

      Assert("Response missing candidates.", response.Candidates.Any());
      var candidate = response.Candidates.First();
      Assert("Candidate should have GroundingMetadata", candidate.GroundingMetadata.HasValue);

      var grounding = candidate.GroundingMetadata.Value;
      AssertEq("WebSearchQueries count", grounding.WebSearchQueries.Count(), 1);
      AssertEq("GroundingChunks count", grounding.GroundingChunks.Count(), 2);
      Assert("First GroundingChunk.Web should be null", !grounding.GroundingChunks.ElementAt(0).Web.HasValue);
      Assert("Second GroundingChunk.Web should be null", !grounding.GroundingChunks.ElementAt(1).Web.HasValue);

      AssertEq("GroundingSupports count", grounding.GroundingSupports.Count(), 1);
      var support = grounding.GroundingSupports.First();
      AssertEq(
          "Segment.Text",
          support.Segment.Text,
          "There is a 0% chance of rain and the humidity is around 41%.");
    }

    // Test parsing an empty GroundingMetadata object.
    Task InternalTestGroundingMetadata_Empty()
    {
      var json = new Dictionary<string, object>();
      var grounding = GroundingMetadata.FromJson(json);

      Assert("WebSearchQueries should be empty", !grounding.WebSearchQueries.Any());
      Assert("GroundingChunks should be empty", !grounding.GroundingChunks.Any());
      Assert("GroundingSupports should be empty", !grounding.GroundingSupports.Any());
      Assert("SearchEntryPoint should be null", !grounding.SearchEntryPoint.HasValue);

      return Task.CompletedTask;
    }

    // Test parsing an empty Segment object.
    Task InternalTestSegment_Empty()
    {
      var json = new Dictionary<string, object>();
      var segment = Segment.FromJson(json);

      AssertEq("PartIndex should default to 0", segment.PartIndex, 0);
      AssertEq("StartIndex should default to 0", segment.StartIndex, 0);
      AssertEq("EndIndex should default to 0", segment.EndIndex, 0);
      Assert("Text should be empty", string.IsNullOrEmpty(segment.Text));

      return Task.CompletedTask;
    }

    // Test that parsing a count token response works.
    async Task InternalTestCountTokenResponse()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-success-detailed-token-response.json");
      CountTokensResponse response = CountTokensResponse.FromJson(json);

      AssertEq("TotalTokens", response.TotalTokens, 1837);
#pragma warning disable CS0618
      AssertEq("TotalBillableCharacters", response.TotalBillableCharacters, 117);
#pragma warning restore CS0618
      List<ModalityTokenCount> details = response.PromptTokensDetails.ToList();
      AssertEq("PromptTokensDetails.Count", details.Count, 2);
      AssertEq("PromptTokensDetails[0].Modality", details[0].Modality, ContentModality.Image);
      AssertEq("PromptTokensDetails[0].TokenCount", details[0].TokenCount, 1806);
      AssertEq("PromptTokensDetails[1].Modality", details[1].Modality, ContentModality.Text);
      AssertEq("PromptTokensDetails[1].TokenCount", details[1].TokenCount, 31);
    }

    // Test that the UsageMetadata is getting parsed correctly.
    async Task InternalTestBasicResponseLongUsageMetadata()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-success-basic-response-long-usage-metadata.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.VertexAI);

      AssertEq("Response Text", response.Text, "Here is a description of the image:\\n\\n");

      AssertEq("PromptTokenCount", response.UsageMetadata?.PromptTokenCount, 1837);
      AssertEq("CandidatesTokenCount", response.UsageMetadata?.CandidatesTokenCount, 76);
      AssertEq("TotalTokenCount", response.UsageMetadata?.TotalTokenCount, 1913);

      var promptDetails = response.UsageMetadata?.PromptTokensDetails.ToList();
      AssertEq("PromptTokensDetails.Count", promptDetails.Count, 2);
      AssertEq("PromptTokensDetails[0].Modality", promptDetails[0].Modality, ContentModality.Image);
      AssertEq("PromptTokensDetails[0].TokenCount", promptDetails[0].TokenCount, 1806);
      AssertEq("PromptTokensDetails[1].Modality", promptDetails[1].Modality, ContentModality.Text);
      AssertEq("PromptTokensDetails[1].TokenCount", promptDetails[1].TokenCount, 76);

      var candidatesDetails = response.UsageMetadata?.CandidatesTokensDetails.ToList();
      AssertEq("CandidatesTokensDetails.Count", candidatesDetails.Count, 1);
      AssertEq("CandidatesTokensDetails[0].Modality", candidatesDetails[0].Modality, ContentModality.Text);
      AssertEq("CandidatesTokensDetails[0].TokenCount", candidatesDetails[0].TokenCount, 76);
    }

    // Test that the UsageMetadata with caching information is getting parsed correctly.
    async Task InternalTestUsageMetadataWithCaching()
    {
       Dictionary<string, object> json = await GetVertexJsonTestData("unary-success-cached-content-usage-metadata.json");
       GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.VertexAI);

       AssertEq("PromptTokenCount", response.UsageMetadata?.PromptTokenCount, 200);
       AssertEq("CandidatesTokenCount", response.UsageMetadata?.CandidatesTokenCount, 50);
       AssertEq("TotalTokenCount", response.UsageMetadata?.TotalTokenCount, 250);
       AssertEq("CachedContentTokenCount", response.UsageMetadata?.CachedContentTokenCount, 150);

       var cacheTokensDetails = response.UsageMetadata?.CacheTokensDetails;
       AssertEq("CacheTokensDetails.Count", cacheTokensDetails.Count, 1);
       AssertEq("CacheTokensDetails[0].Modality", cacheTokensDetails[0].Modality, ContentModality.Text);
       AssertEq("CacheTokensDetails[0].TokenCount", cacheTokensDetails[0].TokenCount, 150);
    }

    // Test that parsing a basic short reply from Google AI endpoint works as expected.
    // https://github.com/FirebaseExtended/vertexai-sdk-test-data/blob/main/mock-responses/googleai/unary-success-basic-reply-short.txt
    async Task InternalTestGoogleAIBasicReplyShort()
    {
      Dictionary<string, object> json = await GetGoogleAIJsonTestData("unary-success-basic-reply-short.json"); //
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.GoogleAI);

      ValidateTextPart(response, "Google's headquarters, also known as the Googleplex, is located in **Mountain View, California**.\n");

      Candidate candidate = response.Candidates.First();
      AssertEq($"FinishReason", candidate.FinishReason, FinishReason.Stop);

      var safetyRatings = candidate.SafetyRatings.ToList();
      AssertEq("SafetyRatings count", safetyRatings.Count, 4);
      ValidateSafetyRating(safetyRatings[0], HarmCategory.HateSpeech, SafetyRating.HarmProbability.Negligible);
      ValidateSafetyRating(safetyRatings[1], HarmCategory.DangerousContent, SafetyRating.HarmProbability.Negligible);
      ValidateSafetyRating(safetyRatings[2], HarmCategory.Harassment, SafetyRating.HarmProbability.Negligible);
      ValidateSafetyRating(safetyRatings[3], HarmCategory.SexuallyExplicit, SafetyRating.HarmProbability.Negligible);

      // No citations in this response
      AssertEq("CitationMetadata", candidate.CitationMetadata, null);

      ValidateUsageMetadata(response.UsageMetadata, 7, 22, 0, 0, 29);
      // No prompt feedback in this response
      AssertEq("PromptFeedback", response.PromptFeedback, null);
    }

    // Test parsing a Google AI format response with citations.
    // Based on: https://github.com/FirebaseExtended/vertexai-sdk-test-data/blob/main/mock-responses/googleai/unary-success-citations.txt
    async Task InternalTestGoogleAICitations()
    {
      Dictionary<string, object> json = await GetGoogleAIJsonTestData("unary-success-citations.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.GoogleAI);

      // Validate Text Part (check start and end)
      string expectedStart = "Okay, let's break down quantum mechanics.";
      string expectedEnd = "It's a challenging but fascinating area of physics!";
      Assert("Candidate count", response.Candidates.Count() == 1);
      Candidate candidate = response.Candidates.First();
      AssertEq("Content role", candidate.Content.Role, "model");
      Assert("Parts count", candidate.Content.Parts.Count() == 1);
      var part = candidate.Content.Parts.First();
      AssertType("TextPart", part, out ModelContent.TextPart textPart);
      Assert("Text part is null/empty", !string.IsNullOrEmpty(textPart.Text));
      Assert($"Text part start mismatch", textPart.Text.StartsWith(expectedStart));
      Assert($"Text part end mismatch", textPart.Text.EndsWith(expectedEnd));
      Assert($"Shorthand text start mismatch", response.Text.StartsWith(expectedStart));
      Assert($"Shorthand text end mismatch", response.Text.EndsWith(expectedEnd));

      // Validate FinishReason
      AssertEq($"FinishReason", candidate.FinishReason, FinishReason.Stop);

      // Validate SafetyRatings (Note: Format differs from vertexai tests)
      // The current parser converts these strings to HarmCategory/Probability enums.
      var safetyRatings = candidate.SafetyRatings.ToList();
      AssertEq("SafetyRatings count", safetyRatings.Count, 4);
      // Just check one for brevity, assuming parser maps correctly.
      // Relies on `FromJson` correctly mapping HARM_CATEGORY_HATE_SPEECH string -> HarmCategory.HateSpeech enum
      // and "NEGLIGIBLE" string -> SafetyRating.HarmProbability.Negligible enum.
      // This might require updates to `GenerateContentResponse.FromJson` or `EnumConverters` if not handled.
      var hateSpeechRating = safetyRatings.First(r => r.Category == HarmCategory.HateSpeech);
      AssertEq("Hate speech probability", hateSpeechRating.Probability, SafetyRating.HarmProbability.Negligible);
      // The googleai format doesn't include scores or blocked status in this example.

      // Validate Citations (Note: Format differs slightly from vertexai tests)
      CitationMetadata? metadata = candidate.CitationMetadata;
      Assert("CitationMetadata", metadata.HasValue);
      var citations = metadata?.Citations.ToList();
      AssertEq("Citation count", citations.Count, 4);

      // Use ValidateCitation helper, adapting for missing fields in this format
      ValidateCitation(citations[0],
          startIndex: 548,
          endIndex: 690,
          uri: new Uri("https://www.example.com/some-citation-1"),
          license: "mit"); // title and publicationDate are null/default

      ValidateCitation(citations[1],
          startIndex: 1240,
          endIndex: 1407,
          uri: new Uri("https://www.example.com/some-citation-1")); // license, title, publicationDate are null/default

      ValidateCitation(citations[2],
          startIndex: 1942,
          endIndex: 2149); // uri, license, title, publicationDate are null/default

      ValidateCitation(citations[3],
          startIndex: 2036,
          endIndex: 2175); // uri, license, title, publicationDate are null/default


      // Validate UsageMetadata
      ValidateUsageMetadata(response.UsageMetadata,
        promptTokenCount: 15,
        candidatesTokenCount: 1667,
        thoughtsTokenCount: 0,
        toolUsePromptTokenCount: 0,
        totalTokenCount: 1682);

      // Validate UsageMetadata Details if needed
      var promptDetails = response.UsageMetadata?.PromptTokensDetails.ToList();
      AssertEq("PromptTokensDetails count", promptDetails.Count, 1);
      AssertEq("PromptTokensDetails[0].Modality", promptDetails[0].Modality, ContentModality.Text);
      AssertEq("PromptTokensDetails[0].TokenCount", promptDetails[0].TokenCount, 15);

      var candidatesDetails = response.UsageMetadata?.CandidatesTokensDetails.ToList();
      AssertEq("CandidatesTokensDetails count", candidatesDetails.Count, 1);
      AssertEq("CandidatesTokensDetails[0].Modality", candidatesDetails[0].Modality, ContentModality.Text);
      AssertEq("CandidatesTokensDetails[0].TokenCount", candidatesDetails[0].TokenCount, 1667);
    }

    async Task InternalTestGenerateImagesBase64()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-success-generate-images-base64.json");
      var response = ImagenGenerationResponse<ImagenInlineImage>.FromJson(json);

      AssertEq("FilteredReason", response.FilteredReason, null);
      AssertEq("Image Count", response.Images.Count, 4);

      for (int i = 0; i < response.Images.Count; i++)
      {
        var image = response.Images[i];
        AssertEq($"Image {i} MimeType", image.MimeType, "image/png");
        Assert($"Image {i} Length: {image.Data.Length}", image.Data.Length > 0);

        var texture = image.AsTexture2D();
        Assert($"Failed to convert Image {i}", texture != null);
      }
    }

    async Task InternalTestGenerateImagesAllFiltered()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-failure-generate-images-all-filtered.json");
      var response = ImagenGenerationResponse<ImagenInlineImage>.FromJson(json);

      AssertEq("FilteredReason", response.FilteredReason,
        "Unable to show generated images. All images were filtered out because " +
        "they violated Vertex AI's usage guidelines. You will not be charged for " +
        "blocked images. Try rephrasing the prompt. If you think this was an error, " +
        "send feedback. Support codes: 39322892, 29310472");
      AssertEq("Image Count", response.Images.Count, 0);
    }

    async Task InternalTestGenerateImagesBase64SomeFiltered()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-failure-generate-images-base64-some-filtered.json");
      var response = ImagenGenerationResponse<ImagenInlineImage>.FromJson(json);

      AssertEq("FilteredReason", response.FilteredReason,
        "Your current safety filter threshold filtered out 2 generated images. " +
        "You will not be charged for blocked images. Try rephrasing the prompt. " +
        "If you think this was an error, send feedback.");
      AssertEq("Image Count", response.Images.Count, 2);

      for (int i = 0; i < response.Images.Count; i++)
      {
        var image = response.Images[i];
        AssertEq($"Image {i} MimeType", image.MimeType, "image/png");
        Assert($"Image {i} Length: {image.Data.Length}", image.Data.Length > 0);

        var texture = image.AsTexture2D();
        Assert($"Failed to convert Image {i}", texture != null);
      }
    }

    async Task InternalTestThoughtSummary()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-success-thinking-reply-thought-summary.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.VertexAI);

      AssertEq("Response text", response.Text, "Mountain View");

      AssertEq("Thought summary", response.ThoughtSummary,
          "Right, someone needs the city where Google's headquarters are. " +
          "I already know this. No need to overthink it, it's a straightforward request. " +
          "Let me just pull up the city name from memory... " +
          "Mountain View. That's it. Just the city, nothing else. Got it.\n");

      ValidateUsageMetadata(response.UsageMetadata, 13, 2, 39, 0, 54);
    }

    async Task InternalTestCodeExecution()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-success-code-execution.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.VertexAI);

      AssertEq("Candidate count", response.Candidates.Count(), 1);
      var candidate = response.Candidates.First();

      var executableCodeParts = candidate.Content.Parts.OfType<ModelContent.ExecutableCodePart>().ToList();
      AssertEq("ExecutableCodePart count", executableCodeParts.Count(), 1);
      AssertEq("ExecutableCodePart language", executableCodeParts[0].Language, ModelContent.ExecutableCodePart.CodeLanguage.Python);
      AssertEq("ExecutableCodePart code", executableCodeParts[0].Code, "prime_numbers = [2, 3, 5, 7, 11]\nsum_of_primes = sum(prime_numbers)\nprint(f'The sum of the first 5 prime numbers is: {sum_of_primes}')\n");

      var codeExecutionResultParts = candidate.Content.Parts.OfType<ModelContent.CodeExecutionResultPart>().ToList();
      AssertEq("CodeExecutionResultPart count", codeExecutionResultParts.Count(), 1);
      AssertEq("CodeExecutionResultPart outcome", codeExecutionResultParts[0].Outcome, ModelContent.CodeExecutionResultPart.ExecutionOutcome.Ok);
      AssertEq("CodeExecutionResultPart output", codeExecutionResultParts[0].Output, "The sum of the first 5 prime numbers is: 28\n");

      ValidateUsageMetadata(response.UsageMetadata, 20, 192, 192, 371, 775);

      var details = response.UsageMetadata?.ToolUsePromptTokensDetails;
      AssertEq("ToolUsePromptTokensDetails.Count", details.Count, 1);
      AssertEq("ToolUsePromptTokensDetails[0].Modality", details[0].Modality, ContentModality.Text);
      AssertEq("ToolUsePromptTokensDetails[0].TokenCount", details[0].TokenCount, 181);
    }

    async Task InternalTestUrlContextMixedValidity()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-success-url-context-mixed-validity.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.VertexAI);

      Assert("Candidate should have UrlContextMetadata", response.Candidates.First().UrlContextMetadata.HasValue);
      var urlContextMetadata = response.Candidates.First().UrlContextMetadata.Value;
      AssertEq("UrlMetadata count", urlContextMetadata.UrlMetadata.Count(), 3);

      var urlMetadata = urlContextMetadata.UrlMetadata.ToList();
      AssertEq("UrlMetadata[0] status", urlMetadata[0].RetrievalStatus, UrlMetadata.UrlRetrievalStatus.Error);
      AssertEq("UrlMetadata[1] url", urlMetadata[1].Url, new Uri("https://ai.google.dev"));
      AssertEq("UrlMetadata[1] status", urlMetadata[1].RetrievalStatus, UrlMetadata.UrlRetrievalStatus.Success);
      AssertEq("UrlMetadata[2] url", urlMetadata[2].Url, new Uri("https://a-completely-non-existent-url-for-testing.org"));
      AssertEq("UrlMetadata[2] status", urlMetadata[2].RetrievalStatus, UrlMetadata.UrlRetrievalStatus.Error);

      ValidateUsageMetadata(response.UsageMetadata, 116, 446, 179, 177, 918);
    }
  }
}
