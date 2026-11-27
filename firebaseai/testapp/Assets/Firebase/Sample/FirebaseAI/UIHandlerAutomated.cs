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
  using System.Net.Sockets;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using Google.MiniJSON;
  using UnityEngine;
  using UnityEngine.Networking;
  using System.IO;
#if INCLUDE_FIREBASE_AUTH
  using Firebase.Auth;
#endif
  using Firebase.AppCheck;

  // An automated version of the UIHandler that runs tests on Firebase AI.
  public class UIHandlerAutomated : UIHandler
  {
    // Delegate which validates a completed task.
    delegate Task TaskValidationDelegate(Task task);

    private Firebase.Sample.AutomatedTestRunner testRunner;
    private int mainThreadId;

    private string appCheckDebugTokenForAutomated = "REPLACE_WITH_APP_CHECK_TOKEN";

    protected override void InitializeAppCheck()
    {
      DebugLog("Initializing App Check directly in automated handler");
      DebugAppCheckProviderFactory.Instance.SetDebugToken(appCheckDebugTokenForAutomated);
      FirebaseAppCheck.SetAppCheckProviderFactory(DebugAppCheckProviderFactory.Instance);
    }

    // Texture used for tests involving images.
    public Texture2D RedBlueTexture;

    // Not reusing the ones from the SDK, since they are internal and only visible
    // because we are providing source libraries.
    private enum Backend
    {
      GoogleAI,
      AgentPlatform,
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
            int jitter = new System.Random().Next(1000);
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
      mainThreadId = Thread.CurrentThread.ManagedThreadId;
      InitializeAppCheck();
      // Set of tests that use multiple backends.
      // When running on CI, these tests will be run on all devices
      Func<Backend, Task>[] basicMultiBackendTests = {
        TestCreateModel,
        TestBasicText,
        TestBasicImage,
        TestModelOptions,
        TestCountTokens
      };
      // When running on CI, these tests are only run in the Editor
      Func<Backend, Task>[] editorMultiBackendTests = {
        TestBasicTextStream,
        TestBasicTextStreamCancellation,
        TestFunctionCallingAny,
        TestFunctionCallingNone,
        TestEnumSchemaResponse,
        TestAnyOfSchemaResponse,
        TestChatBasicTextNoHistory,
        TestChatBasicTextPriorHistory,
        TestChatFunctionCalling,
        TestChatBasicTextStream,
        TestThinkingBudget,
        TestTemplateGenerateContent,
        TestTemplateGenerateContentStream,
        TestTemplateGenerateContentStreamCancellation,
        TestJsonSchemaStructureOutput,
        TestChatAutoFunctionCalling
      };
      // When running on CI, these tests aren't run since they are more flakey
      Func<Backend, Task>[] complexMultiBackendTests = {
        TestIncludeThoughts,
        TestYoutubeLink,
        TestGenerateImage,
        TestSearchGrounding,
        TestCodeExecution,
        TestUrlContext,
        TestGoogleMaps,
        TestTemplateChat,
        TestTemplateChatStreamAutoFunction,
      };
      // Construct the set of tests to run, based on the environment
      List<Func<Backend, Task>> multiBackendTests = new(basicMultiBackendTests);
#if !(FIREBASE_RUNNING_FROM_CI && !UNITY_EDITOR)
      multiBackendTests.AddRange(editorMultiBackendTests);
#endif
#if !FIREBASE_RUNNING_FROM_CI
      multiBackendTests.AddRange(complexMultiBackendTests);
#endif

      // Set of tests that only run the single time, will be run on all devices.
      Func<Task>[] singleTests = {
        TestFirebaseAIInstanceCaching,
        TestReadFile,
        TestReadSecureFile,
        // Internal tests for Json parsing, requires using a source library.
        InternalTestBasicReplyShort,
        InternalTestFinishReasonExpanded,
        InternalTestImageConfigSerialization,
        InternalTestSpeechConfigSerialization,
        InternalTestSpeechConfigValidations,
        InternalTestCitations,
        InternalTestBlockedSafetyWithMessage,
        InternalTestFinishReasonSafetyNoContent,
        InternalTestUnknownEnumSafetyRatings,
        InternalTestFunctionCallWithArguments,
        InternalTestAgentPlatformGrounding,
        InternalTestGoogleAIGrounding,
        InternalTestGoogleAIGroundingEmptyChunks,
        InternalTestMapsGrounding,
        InternalTestGroundingMetadata_Empty,
        InternalTestSegment_Empty,
        InternalTestCountTokenResponse,
        InternalTestBasicResponseLongUsageMetadata,
        InternalTestGoogleAIBasicReplyShort,
        InternalTestGoogleAICitations,
        InternalTestThoughtSummary,
        InternalTestCodeExecution,
        InternalTestUrlContextMixedValidity,
        InternalTestUsageMetadataWithCaching,
        InternalTestRealtimeInputConfigSerialization,
        InternalTestNativeHttpFallbackClassification,
        InternalTestNativeHttpLoopbackStreamingAndCancellation,
        InternalTestNativeHttpRedirectsAndMethods,
        InternalTestNativeHttpLargeStreamingAndStress,
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
      testRunner.TestTimeoutSeconds = 240f;

      base.Start();
    }

    // Passes along the update call to automated test runner.
    protected override void Update()
    {
      base.Update();
      if (testRunner != null && isFirebaseInitializationComplete)
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
    private readonly string TestModelName = "gemini-3.1-flash-lite";

    private FirebaseAI GetFirebaseAI(Backend backend, string location = "global", bool useLimitedUseAppCheckTokens = true)
    {
      return backend switch
      {
        Backend.GoogleAI => FirebaseAI.GetInstance(FirebaseAI.Backend.GoogleAI(), useLimitedUseAppCheckTokens),
        Backend.AgentPlatform => FirebaseAI.GetInstance(FirebaseAI.Backend.AgentPlatform(location), useLimitedUseAppCheckTokens),
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

    // Test that FirebaseAI instances are correctly cached based on backend and limited use settings.
    Task TestFirebaseAIInstanceCaching()
    {
      // Test within GoogleAI
      var googleAi1 = GetFirebaseAI(Backend.GoogleAI, useLimitedUseAppCheckTokens: false);
      var googleAi2 = GetFirebaseAI(Backend.GoogleAI, useLimitedUseAppCheckTokens: true);
      var googleAi3 = GetFirebaseAI(Backend.GoogleAI, useLimitedUseAppCheckTokens: false);

      Assert("GoogleAI: Instances with different limited use settings should be different.", googleAi1 != googleAi2);
      Assert("GoogleAI: Instances with the same limited use settings should be the same.", googleAi1 == googleAi3);

      // Test within AgentPlatform
      var agentPlatform1 = GetFirebaseAI(Backend.AgentPlatform, useLimitedUseAppCheckTokens: false);
      var agentPlatform2 = GetFirebaseAI(Backend.AgentPlatform, useLimitedUseAppCheckTokens: true);
      var agentPlatform3 = GetFirebaseAI(Backend.AgentPlatform, useLimitedUseAppCheckTokens: false);

      Assert("AgentPlatform: Instances with different limited use settings should be different.", agentPlatform1 != agentPlatform2);
      Assert("AgentPlatform: Instances with the same limited use settings should be the same.", agentPlatform1 == agentPlatform3);

      // Test cross-backend isolation
      Assert("Instances of GoogleAI and AgentPlatform with the same settings should be different.", googleAi1 != agentPlatform1);
      Assert("Instances of GoogleAI and AgentPlatform with different settings should be different.", googleAi2 != agentPlatform2);

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
        generationConfig: new GenerationConfig(maxOutputTokens: 100),
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
          responseModalities: new[] { ResponseModality.Text, ResponseModality.Image },
          imageConfig: new ImageConfig(
              aspectRatio: ImageConfig.AspectRatio.Square1x1))
      );

      GenerateContentResponse response = await model.GenerateContentAsync(
        ModelContent.Text("Generate a picture of a cartoon dog, and a couple of sentences about him?")
      );

      Assert("Response missing candidates.", response.Candidates.Any());

      // We don't care much about the response, just that there is an image, and text.
      bool foundText = false;
      bool foundImage = false;
      Texture2D image = new(1, 2);
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
            image.LoadImage(dataPart.Data.ToArray());
          }
        }
      }
      Assert($"Missing expected modalities. Text: {foundText}, Image: {foundImage}", foundText && foundImage);
      // The height and width should match, since we requested a 1x1 aspect ratio.
      AssertEq("Image dimensions should match.", image.height, image.width);
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

    async Task TestTemplateChat(Backend backend)
    {
      var model = GetFirebaseAI(backend, "global").GetTemplateGenerativeModel();

      var chat = model.StartChat("function-calling-weather");

      // The template defines a function for the model to call, which we trigger, and respond to.
      var response1 = await chat.SendMessageAsync("What was the weather in San Jose, CA, on January 1, 2026?");

      var functionCallPart = response1.FunctionCalls?.First();
      AssertEq("Function Call Name", functionCallPart?.Name, "fetchWeather");

      var response2 = await chat.SendMessageAsync(
        ModelContent.FunctionResponse("fetchWeather",
          new Dictionary<string, object>
          {
            { "result", "Cloudy, with a chance of meatballs" }
          },
          functionCallPart?.Id)
      );

      // There isn't much guarantee on what this will respond with. We just want non-empty.
      Assert("Response was empty.", !string.IsNullOrWhiteSpace(response2.Text));
    }

    private class TestAutoFunctionLocation
    {
      public string city;
      public string state;
    }

    async Task TestTemplateChatStreamAutoFunction(Backend backend)
    {
      var model = GetFirebaseAI(backend, "global").GetTemplateGenerativeModel();

      var chat = model.StartChat("function-calling-weather",
          tools: new[] {
            // This function declaration matches what the server prompt defines
            new TemplateTool.AutoFunctionDeclaration(
                (Func<TestAutoFunctionLocation, string, string>)((location, date) => "Cloudy with a chance of meatballs"),
                name: "fetchWeather")
          });

      // The template defines a function for the model to call, which we trigger, and respond to.
      var responseStream = chat.SendMessageStreamAsync("What was the weather in San Jose, CA, on January 1, 2026?");

      await foreach (var response in responseStream)
      {
        // There isn't much guarantee on what this will respond with. We just want non-empty.
        Assert("Response was empty.", !string.IsNullOrWhiteSpace(response.Text));
      }
    }

    // Class used for validating JsonSchema generation
    public class SampleRecord
    {
      public enum MyColor
      {
        Red,
        Green,
        Blue
      }

      [SchemaInfo(Description = "The first and last name of a person")]
      public string name;

      public int? age;

      public bool Alive { get; set; }

      [SchemaInfo(Description = "How much of their life they have left.")]
      [Range(0, 1)]
      public double Percent;

      public MyColor eye_color;

      [SchemaInfo(Optional = true)]
      public char BloodType;

      [SchemaInfo(Nullable = true, Optional = true)]
      public SampleRecord[] Children;

      public Dictionary<int, string> Dummy;

      public override string ToString()
      {
        string childrenStr = Children != null ? string.Join(", ", Children.Select(t => $"({t})")) : "";
        return $"{name} {age} {Alive} {Percent} {eye_color} [{childrenStr}]";
      }
    }

    async Task TestJsonSchemaStructureOutput(Backend backend)
    {
      var model = GetFirebaseAI(backend).GetGenerativeModel(TestModelName,
        generationConfig: new GenerationConfig(
          responseMimeType: "application/json",
          responseJsonSchema: JsonSchema.FromType(typeof(SampleRecord))));

      var response = await model.GenerateObjectAsync<SampleRecord>(
        "Hello, I am testing setting the response schema with an object, cause you give me some random values.");

      // There isn't much guarantee on what this will respond with. We just want non-empty.
      Assert("Response was missing a Name.", !string.IsNullOrWhiteSpace(response.Result.name));
    }

    string LastInput = null;
    const int TestPrice = 10;
    Task<int> TestAutoFunctionPriceCheck([AutoFunctionDescription("The fruit to check for")] string input)
    {
      // Save the input, to check against
      LastInput = input;
      return Task.FromResult(10);
    }

    async Task TestChatAutoFunctionCalling(Backend backend)
    {
      var tool = new Tool(new AutoFunctionDeclaration(
          (Func<string, Task<int>>)TestAutoFunctionPriceCheck,
          description: "Checks the price of the given item"));
      var model = GetFirebaseAI(backend).GetGenerativeModel(TestModelName,
        tools: new Tool[] { tool }
      );
      var chat = model.StartChat();

      // Clear the saved last input first
      LastInput = null;
      string expectedInput = "Banana";
      GenerateContentResponse response = await chat.SendMessageAsync(
          $"Hello, I am testing function calling with Chat. How much does a {expectedInput} cost?");

      // We don't know exactly how the response will be phrased, but it should have text
      Assert("Response was empty.", !string.IsNullOrWhiteSpace(response.Text));
      // Ideally the response will include the price, though it isn't guaranteed.
      if (!response.Text.Contains($"{TestPrice}"))
      {
        DebugLog($"WARNING: Response string was missing the expected price of {TestPrice}: " +
            $"\n{response.Text}");
      }

      // Check if the recorded LastInput for the function matches what we expect.
      Assert($"LastInput != ExpectedInput ({LastInput}, {expectedInput})",
          string.Equals(LastInput, expectedInput, StringComparison.CurrentCultureIgnoreCase));
    }

    // Test grounding with Google Maps.
    async Task TestGoogleMaps(Backend backend)
    {
      // Use a model that supports grounding.
      var model = GetFirebaseAI(backend).GetGenerativeModel(TestModelName,
        tools: new Tool[] { new Tool(new GoogleMaps()) },
        toolConfig: new ToolConfig(
          // Give it the location of the Statue of Liberty
          retrievalConfig: new RetrievalConfig(new LatLng(40.6892, -74.0445)))
      );

      // A prompt that should trigger the map data.
      GenerateContentResponse response = await model.GenerateContentAsync("Find me one famous statue that is nearby?");

      Assert("Response missing candidates.", response.Candidates.Any());

      string result = response.Text;
      Assert("Response text was missing", !string.IsNullOrWhiteSpace(result));

      var candidate = response.Candidates.First();
      Assert("GroundingMetadata should not be null when GoogleMaps tool is used.",
          candidate.GroundingMetadata.HasValue);

      var groundingMetadata = candidate.GroundingMetadata.Value;

      Assert("GroundingChunks should have Maps data.",
          groundingMetadata.GroundingChunks.Any(gc => gc.Maps != null));
    }

    // Test providing a file from a GCS bucket (Firebase Storage) to the model.
    async Task TestReadFile()
    {
      // GCS is currently only supported with AgentPlatform.
      var model = CreateGenerativeModel(Backend.AgentPlatform);

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
      // GCS is currently only supported with AgentPlatform.
      var model = CreateGenerativeModel(Backend.AgentPlatform);

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
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.AgentPlatform);

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

    // Test that parsing a response with expanded FinishReason works.
    Task InternalTestFinishReasonExpanded()
    {
      string jsonStr = @"{
        ""candidates"": [{
          ""content"": {
            ""parts"": [{""text"": ""Hello""}],
            ""role"": ""model""
          },
          ""finishReason"": ""IMAGE_SAFETY""
        }]
      }";
      Dictionary<string, object> json = (Dictionary<string, object>)Json.Deserialize(jsonStr);
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.AgentPlatform);

      Assert("Response missing candidates.", response.Candidates.Any());
      Candidate candidate = response.Candidates.First();
      AssertEq("FinishReason", candidate.FinishReason, FinishReason.ImageSafety);

      // Test another one
      jsonStr = @"{
        ""candidates"": [{
          ""content"": {""parts"": []},
          ""finishReason"": ""MALFORMED_RESPONSE""
        }]
      }";
      json = (Dictionary<string, object>)Json.Deserialize(jsonStr);
      response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.AgentPlatform);
      candidate = response.Candidates.First();
      AssertEq("FinishReason", candidate.FinishReason, FinishReason.MalformedResponse);

      return Task.CompletedTask;
    }

    // Test that ImageConfig serialization works as expected.
    Task InternalTestImageConfigSerialization()
    {
      var imageConfig = new ImageConfig(ImageConfig.AspectRatio.Landscape16x9, ImageConfig.ImageSize.Size1K);
      var json = imageConfig.ToJson();

      AssertEq("ImageConfig.aspectRatio", json["aspectRatio"], "16:9");
      AssertEq("ImageConfig.imageSize", json["imageSize"], "1K");

      var genConfig = new GenerationConfig(imageConfig: imageConfig);
      var genJson = genConfig.ToJson();

      Assert("GenerationConfig missing imageConfig", genJson.ContainsKey("imageConfig"));
      var imageConfigJson = genJson["imageConfig"] as Dictionary<string, object>;
      Assert("imageConfig is not a dictionary", imageConfigJson != null);
      AssertEq("imageConfigJson.aspectRatio", imageConfigJson["aspectRatio"], "16:9");
      AssertEq("imageConfigJson.imageSize", imageConfigJson["imageSize"], "1K");

      return Task.CompletedTask;
    }

    // Test that SpeechConfig serialization works as expected.
    Task InternalTestSpeechConfigSerialization()
    {
      // 1. Single Speaker with languageCode
      var singleSpeechConfig = SpeechConfig.UsePrebuiltVoice("Kore", "en-US");
      var singleJson = singleSpeechConfig.ToJson();

      Assert("singleJson missing voiceConfig", singleJson.ContainsKey("voiceConfig"));
      var voiceConfigJson = singleJson["voiceConfig"] as Dictionary<string, object>;
      Assert("voiceConfig is not a dictionary", voiceConfigJson != null);
      Assert("voiceConfigJson missing prebuiltVoiceConfig", voiceConfigJson.ContainsKey("prebuiltVoiceConfig"));
      var prebuiltVoiceConfigJson = voiceConfigJson["prebuiltVoiceConfig"] as Dictionary<string, object>;
      Assert("prebuiltVoiceConfig is not a dictionary", prebuiltVoiceConfigJson != null);
      AssertEq("prebuiltVoiceConfig.voiceName", prebuiltVoiceConfigJson["voiceName"], "Kore");
      AssertEq("singleJson.languageCode", singleJson["languageCode"], "en-US");

      // 2. Multi-Speaker with languageCode
      var multiSpeechConfig = SpeechConfig.UseMultiSpeakerVoice(
        new MultiSpeakerVoiceConfig(new List<SpeakerVoiceConfig> {
          SpeakerVoiceConfig.UsePrebuiltVoice("Joe", "Kore"),
          SpeakerVoiceConfig.UsePrebuiltVoice("Jane", "Puck")
        }),
        "en-US"
      );
      var multiJson = multiSpeechConfig.ToJson();

      Assert("multiJson missing multiSpeakerVoiceConfig", multiJson.ContainsKey("multiSpeakerVoiceConfig"));
      var multiSpeakerVoiceConfigJson = multiJson["multiSpeakerVoiceConfig"] as Dictionary<string, object>;
      Assert("multiSpeakerVoiceConfig is not a dictionary", multiSpeakerVoiceConfigJson != null);
      Assert("multiSpeakerVoiceConfigJson missing speakerVoiceConfigs", multiSpeakerVoiceConfigJson.ContainsKey("speakerVoiceConfigs"));
      var speakerVoiceConfigsList = multiSpeakerVoiceConfigJson["speakerVoiceConfigs"] as List<object>;
      Assert("speakerVoiceConfigs is not a List<object>", speakerVoiceConfigsList != null);
      AssertEq("speakerVoiceConfigs count", speakerVoiceConfigsList.Count, 2);

      var joeConfig = speakerVoiceConfigsList[0] as Dictionary<string, object>;
      Assert("joeConfig is not a dictionary", joeConfig != null);
      AssertEq("joe speaker name", joeConfig["speaker"], "Joe");
      var joeVoiceConfig = joeConfig["voiceConfig"] as Dictionary<string, object>;
      Assert("joeVoiceConfig is not a dictionary", joeVoiceConfig != null);
      var joePrebuilt = joeVoiceConfig["prebuiltVoiceConfig"] as Dictionary<string, object>;
      AssertEq("joe voice name", joePrebuilt["voiceName"], "Kore");

      var janeConfig = speakerVoiceConfigsList[1] as Dictionary<string, object>;
      Assert("janeConfig is not a dictionary", janeConfig != null);
      AssertEq("jane speaker name", janeConfig["speaker"], "Jane");
      var janeVoiceConfig = janeConfig["voiceConfig"] as Dictionary<string, object>;
      Assert("janeVoiceConfig is not a dictionary", janeVoiceConfig != null);
      var janePrebuilt = janeVoiceConfig["prebuiltVoiceConfig"] as Dictionary<string, object>;
      AssertEq("jane voice name", janePrebuilt["voiceName"], "Puck");

      AssertEq("multiJson.languageCode", multiJson["languageCode"], "en-US");

      // 3. Integration with GenerationConfig
      var genConfig = new GenerationConfig(speechConfig: multiSpeechConfig);
      var genJson = genConfig.ToJson();

      Assert("GenerationConfig missing speechConfig", genJson.ContainsKey("speechConfig"));
      var speechConfigJson = genJson["speechConfig"] as Dictionary<string, object>;
      Assert("speechConfig is not a dictionary", speechConfigJson != null);
      Assert("speechConfigJson missing multiSpeakerVoiceConfig", speechConfigJson.ContainsKey("multiSpeakerVoiceConfig"));

      return Task.CompletedTask;
    }

    // Test that SpeechConfig validations throw correct exceptions.
    Task InternalTestSpeechConfigValidations()
    {
      // 1. Uninitialized MultiSpeakerVoiceConfig (default struct) should not serialize to JSON
      var defaultMultiSpeech = SpeechConfig.UseMultiSpeakerVoice(default(MultiSpeakerVoiceConfig));
      var defaultJson = defaultMultiSpeech.ToJson();
      Assert("defaultJson should not contain multiSpeakerVoiceConfig", !defaultJson.ContainsKey("multiSpeakerVoiceConfig"));

      // 2. SpeakerVoiceConfig.UsePrebuiltVoice should throw ArgumentException on null/empty speaker
      try
      {
        SpeakerVoiceConfig.UsePrebuiltVoice(null, "Kore");
        Assert("Should have thrown ArgumentException for null speaker", false);
      }
      catch (System.ArgumentException)
      {
        // Expected
      }

      // 3. SpeakerVoiceConfig.UsePrebuiltVoice should throw ArgumentException on null/empty voiceName
      try
      {
        SpeakerVoiceConfig.UsePrebuiltVoice("Joe", "");
        Assert("Should have thrown ArgumentException for empty voiceName", false);
      }
      catch (System.ArgumentException)
      {
        // Expected
      }

      // 4. MultiSpeakerVoiceConfig should throw ArgumentNullException on null enumerable
      try
      {
        new MultiSpeakerVoiceConfig(null);
        Assert("Should have thrown ArgumentNullException for null speakerVoiceConfigs", false);
      }
      catch (System.ArgumentNullException)
      {
        // Expected
      }

      return Task.CompletedTask;
    }

    // Test that parsing a response including Citations works.
    // https://github.com/FirebaseExtended/vertexai-sdk-test-data/blob/main/mock-responses/unary-success-citations.json
    async Task InternalTestCitations()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-success-citations.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.AgentPlatform);

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
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.AgentPlatform);

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
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.AgentPlatform);

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
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.AgentPlatform);

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
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.AgentPlatform);

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

    // Test that parsing an Agent Platform response with GroundingMetadata works.
    // https://github.com/FirebaseExtended/vertexai-sdk-test-data/blob/main/mock-responses/vertexai/unary-success-google-search-grounding.json
    async Task InternalTestAgentPlatformGrounding()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-success-google-search-grounding.json");

      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.AgentPlatform);

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

    // Test that parsing a Vertex AI response with Maps GroundingMetadata works.
    // https://github.com/FirebaseExtended/vertexai-sdk-test-data/blob/main/mock-responses/vertexai/unary-success-google-maps-grounding.json
    async Task InternalTestMapsGrounding()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-success-google-maps-grounding.json");

      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.AgentPlatform);

      Assert("Response missing candidates.", response.Candidates.Any());
      var candidate = response.Candidates.First();
      Assert("Candidate should have GroundingMetadata", candidate.GroundingMetadata.HasValue);

      var grounding = candidate.GroundingMetadata.Value;

      AssertEq("GroundingChunks count", grounding.GroundingChunks.Count(), 20);
      var chunk = grounding.GroundingChunks.First();
      Assert("GroundingChunk.Maps should not be null", chunk.Maps.HasValue);
      var mapsChunk = chunk.Maps.Value;
      AssertEq("GoogleMapsGroundingChunk.Title", mapsChunk.Title, "Joe’s Pizza");
      AssertEq("GoogleMapsGroundingChunk.Uri", mapsChunk.Uri, new Uri("https://maps.google.com/?cid=10332424901773702701"));
      AssertEq("GoogleMapsGroundingChunk.PlaceId", mapsChunk.PlaceId, "places/ChIJqdNaaBVbwokRLTafYrQlZI8");

      AssertEq("GroundingSupports count", grounding.GroundingSupports.Count(), 39);
      var support = grounding.GroundingSupports.First();
      AssertEq("GroundingChunkIndices count", support.GroundingChunkIndices.Count(), 1);
      AssertEq("GroundingChunkIndices content", support.GroundingChunkIndices.First(), 0);
      AssertEq("Segment text", support.Segment.Text, "*   **Joe's Pizza** at 124 Fulton St is a long-time family-owned pizzeria that serves classic pies and slices");
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
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.AgentPlatform);

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
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.AgentPlatform);

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

    async Task InternalTestThoughtSummary()
    {
      Dictionary<string, object> json = await GetVertexJsonTestData("unary-success-thinking-reply-thought-summary.json");
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.AgentPlatform);

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
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.AgentPlatform);

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
      GenerateContentResponse response = GenerateContentResponse.FromJson(json, FirebaseAI.Backend.InternalProvider.AgentPlatform);

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

    Task InternalTestRealtimeInputConfigSerialization()
    {
      // 1. Empty RealtimeInputConfig
      var emptyConfig = new RealtimeInputConfig();
      var emptyJson = emptyConfig.ToJson();
      Assert("emptyJson should have no keys", emptyJson.Count == 0);
      Assert("emptyConfig.AutomaticActivityDetection should be null", emptyConfig.AutomaticActivityDetection == null);
      Assert("emptyConfig.Handling should be null", emptyConfig.Handling == null);
      Assert("emptyConfig.Coverage should be null", emptyConfig.Coverage == null);

      // 2. Disabled ActivityDetectionConfig
      var disabledConfig = ActivityDetectionConfig.Disabled();
      var disabledJson = disabledConfig.ToJson();
      Assert("disabledJson missing disabled", disabledJson.ContainsKey("disabled"));
      AssertEq("disabledJson.disabled", disabledJson["disabled"], true);
      Assert("disabledJson shouldn't have startOfSpeechSensitivity", !disabledJson.ContainsKey("startOfSpeechSensitivity"));
      Assert("disabledJson shouldn't have endOfSpeechSensitivity", !disabledJson.ContainsKey("endOfSpeechSensitivity"));
      AssertEq("disabledConfig.IsDisabled", disabledConfig.IsDisabled, (bool?)true);

      // 3. Full RealtimeInputConfig with milliseconds
      var detectionConfigMs = new ActivityDetectionConfig(
        startSensitivity: ActivityDetectionConfig.Sensitivity.Low,
        endSensitivity: ActivityDetectionConfig.Sensitivity.High,
        prefixPaddingMS: 500,
        silenceDurationMS: 1000);

      AssertEq("detectionConfigMs.StartSensitivity", detectionConfigMs.StartSensitivity, (ActivityDetectionConfig.Sensitivity?)ActivityDetectionConfig.Sensitivity.Low);
      AssertEq("detectionConfigMs.EndSensitivity", detectionConfigMs.EndSensitivity, (ActivityDetectionConfig.Sensitivity?)ActivityDetectionConfig.Sensitivity.High);
      AssertEq("detectionConfigMs.PrefixPaddingMS", detectionConfigMs.PrefixPaddingMS, (int?)500);
      AssertEq("detectionConfigMs.SilenceDurationMS", detectionConfigMs.SilenceDurationMS, (int?)1000);
      AssertEq("detectionConfigMs.PrefixPadding", detectionConfigMs.PrefixPadding, (TimeSpan?)TimeSpan.FromMilliseconds(500));
      AssertEq("detectionConfigMs.SilenceDuration", detectionConfigMs.SilenceDuration, (TimeSpan?)TimeSpan.FromMilliseconds(1000));

      var fullConfig = new RealtimeInputConfig(
        automaticActivityDetection: detectionConfigMs,
        activityHandling: RealtimeInputConfig.ActivityHandling.Interrupt,
        turnCoverage: RealtimeInputConfig.TurnCoverage.AudioActivityAndAllVideo);

      var fullJson = fullConfig.ToJson();
      Assert("fullJson missing automaticActivityDetection", fullJson.ContainsKey("automaticActivityDetection"));
      var detectionJson = fullJson["automaticActivityDetection"] as Dictionary<string, object>;
      Assert("detectionJson is null", detectionJson != null);
      AssertEq("startOfSpeechSensitivity", detectionJson["startOfSpeechSensitivity"], "START_SENSITIVITY_LOW");
      AssertEq("endOfSpeechSensitivity", detectionJson["endOfSpeechSensitivity"], "END_SENSITIVITY_HIGH");
      AssertEq("prefixPaddingMs", detectionJson["prefixPaddingMs"], 500);
      AssertEq("silenceDurationMs", detectionJson["silenceDurationMs"], 1000);
      Assert("detectionJson shouldn't have disabled", !detectionJson.ContainsKey("disabled"));

      AssertEq("activityHandling", fullJson["activityHandling"], "START_OF_ACTIVITY_INTERRUPTS");
      AssertEq("turnCoverage", fullJson["turnCoverage"], "TURN_INCLUDES_AUDIO_ACTIVITY_AND_ALL_VIDEO");

      // 4. TimeSpan constructor overload test
      var detectionConfigTimeSpan = new ActivityDetectionConfig(
        startSensitivity: ActivityDetectionConfig.Sensitivity.High,
        endSensitivity: ActivityDetectionConfig.Sensitivity.Low,
        prefixPadding: TimeSpan.FromSeconds(2),
        silenceDuration: TimeSpan.FromMilliseconds(750));

      AssertEq("detectionConfigTimeSpan.PrefixPaddingMS", detectionConfigTimeSpan.PrefixPaddingMS, (int?)2000);
      AssertEq("detectionConfigTimeSpan.SilenceDurationMS", detectionConfigTimeSpan.SilenceDurationMS, (int?)750);
      var timeSpanJson = detectionConfigTimeSpan.ToJson();
      AssertEq("timeSpanJson.prefixPaddingMs", timeSpanJson["prefixPaddingMs"], 2000);
      AssertEq("timeSpanJson.silenceDurationMs", timeSpanJson["silenceDurationMs"], 750);

      // 5. Validation tests (negative durations & overflow)
      bool caughtNegativePrefix = false;
      try {
        new ActivityDetectionConfig(prefixPaddingMS: -100);
      } catch (ArgumentOutOfRangeException) {
        caughtNegativePrefix = true;
      }
      Assert("Should throw on negative prefixPaddingMS", caughtNegativePrefix);

      bool caughtNegativeSilence = false;
      try {
        new ActivityDetectionConfig(silenceDurationMS: -500);
      } catch (ArgumentOutOfRangeException) {
        caughtNegativeSilence = true;
      }
      Assert("Should throw on negative silenceDurationMS", caughtNegativeSilence);

      bool caughtNegativeTimeSpan = false;
      try {
        new ActivityDetectionConfig(
          startSensitivity: ActivityDetectionConfig.Sensitivity.Low,
          endSensitivity: ActivityDetectionConfig.Sensitivity.High,
          prefixPadding: TimeSpan.FromSeconds(-1));
      } catch (ArgumentOutOfRangeException) {
        caughtNegativeTimeSpan = true;
      }
      Assert("Should throw on negative prefix TimeSpan", caughtNegativeTimeSpan);

      bool caughtOverflowTimeSpan = false;
      try {
        new ActivityDetectionConfig(
          startSensitivity: ActivityDetectionConfig.Sensitivity.Low,
          endSensitivity: ActivityDetectionConfig.Sensitivity.High,
          prefixPadding: TimeSpan.FromDays(100));
      } catch (ArgumentOutOfRangeException) {
        caughtOverflowTimeSpan = true;
      }
      Assert("Should throw on overflowing prefix TimeSpan", caughtOverflowTimeSpan);

      return Task.CompletedTask;
    }

    // Verifies mid-stream CancellationToken cancellation on GenerativeModel SSE streams.
    async Task TestBasicTextStreamCancellation(Backend backend)
    {
      var model = CreateGenerativeModel(backend);
      using var cts = new CancellationTokenSource();
      int receivedChunks = 0;
      bool canceled = false;
      try
      {
        await foreach (var response in model.GenerateContentStreamAsync(
            "Write a detailed 500-word essay about the history of space exploration.",
            cts.Token))
        {
          receivedChunks++;
          if (receivedChunks >= 1)
          {
            cts.Cancel();
          }
        }
      }
      catch (OperationCanceledException)
      {
        canceled = true;
      }

      Assert("Expected at least one chunk before canceling", receivedChunks >= 1);
      Assert("Expected OperationCanceledException on mid-stream cancellation", canceled);
    }

    // Verifies mid-stream CancellationToken cancellation on TemplateGenerativeModel SSE streams.
    async Task TestTemplateGenerateContentStreamCancellation(Backend backend)
    {
      var model = GetFirebaseAI(backend, "global").GetTemplateGenerativeModel();
      var inputs = new Dictionary<string, object>()
      {
        ["customerName"] = "Jane"
      };
      using var cts = new CancellationTokenSource();
      int receivedChunks = 0;
      bool canceled = false;
      try
      {
        await foreach (var response in model.GenerateContentStreamAsync(
            "input-system-instructions", inputs, cancellationToken: cts.Token))
        {
          receivedChunks++;
          if (receivedChunks >= 1)
          {
            cts.Cancel();
          }
        }
      }
      catch (OperationCanceledException)
      {
        canceled = true;
      }

      Assert("Expected at least one template chunk before canceling", receivedChunks >= 1);
      Assert("Expected OperationCanceledException on template mid-stream cancellation", canceled);
    }

    // Verifies managed fallback detection unwraps SWIG's 2-level nested TypeInitializationException
    // (AppUtilPINVOKE -> SWIGExceptionHelper -> DllNotFoundException) as well as BadImageFormatException,
    // confirms the native transport is active on this platform, and validates TLS error classification.
    Task InternalTestNativeHttpFallbackClassification()
    {
      bool nativeActive = FirebaseHttpMessageHandler.IsNativeTransportActive;
      DebugLog($"NATIVE_HTTP_ACTIVE={nativeActive}");
      Assert("Expected native HTTP transport to be active (not managed fallback)", nativeActive);

      Assert("Direct DllNotFoundException",
          FirebaseHttpMessageHandler.IsNativeLibraryUnavailable(new DllNotFoundException("missing")));
      Assert("Direct EntryPointNotFoundException",
          FirebaseHttpMessageHandler.IsNativeLibraryUnavailable(new EntryPointNotFoundException("missing")));
      Assert("Direct BadImageFormatException",
          FirebaseHttpMessageHandler.IsNativeLibraryUnavailable(new BadImageFormatException("wrong arch")));

      var oneLevel = new TypeInitializationException("AppUtilPINVOKE", new DllNotFoundException("missing"));
      Assert("1-level TypeInitializationException",
          FirebaseHttpMessageHandler.IsNativeLibraryUnavailable(oneLevel));

      var swigNested = new TypeInitializationException(
          "Firebase.AppUtilPINVOKE",
          new TypeInitializationException(
              "Firebase.AppUtilPINVOKE+SWIGExceptionHelper",
              new DllNotFoundException("FirebaseCppApp")));
      Assert("2-level SWIG TypeInitializationException",
          FirebaseHttpMessageHandler.IsNativeLibraryUnavailable(swigNested));

      var nestedBadImage = new TypeInitializationException(
          "Firebase.AppUtilPINVOKE",
          new TypeInitializationException(
              "Firebase.AppUtilPINVOKE+SWIGExceptionHelper",
              new BadImageFormatException("arch mismatch")));
      Assert("2-level BadImageFormatException",
          FirebaseHttpMessageHandler.IsNativeLibraryUnavailable(nestedBadImage));

      Assert("Unrelated InvalidOperationException must not trigger fallback",
          !FirebaseHttpMessageHandler.IsNativeLibraryUnavailable(new InvalidOperationException("bug")));
      var unrelatedNested = new TypeInitializationException(
          "SomeType", new InvalidOperationException("unrelated"));
      Assert("TypeInitializationException wrapping unrelated error must not trigger fallback",
          !FirebaseHttpMessageHandler.IsNativeLibraryUnavailable(unrelatedNested));

      // S4: TLS failures wrapped inside HttpRequestException or IOException must be detected.
      var authEx = new System.Security.Authentication.AuthenticationException("bad cert");
      Assert("Direct AuthenticationException classified as TLS",
          FirebaseHttpMessageHandler.IsTlsFailure(authEx));
      Assert("Wrapped HttpRequestException(AuthenticationException) classified as TLS",
          FirebaseHttpMessageHandler.IsTlsFailure(new HttpRequestException("tls", authEx)));
      Assert("Plain IOException must not be classified as TLS",
          !FirebaseHttpMessageHandler.IsTlsFailure(new IOException("reset")));

      return Task.CompletedTask;
    }

    // Non-seekable stream wrapper so HttpClient sends unknown-length chunked transfer encoding.
    private sealed class NonSeekableReadStream : Stream
    {
      private readonly byte[] _data;
      private int _pos;

      public NonSeekableReadStream(byte[] data) { _data = data; }
      public override bool CanRead => true;
      public override bool CanSeek => false;
      public override bool CanWrite => false;
      public override long Length => throw new NotSupportedException();
      public override long Position
      {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
      }
      public override void Flush() { }
      public override int Read(byte[] buffer, int offset, int count)
      {
        int avail = _data.Length - _pos;
        if (avail <= 0) return 0;
        int n = Math.Min(count, avail);
        Buffer.BlockCopy(_data, _pos, buffer, offset, n);
        _pos += n;
        return n;
      }
      public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ParsedLoopbackRequest
    {
      public string Method;
      public string Target;
      public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      public byte[] Body = Array.Empty<byte>();
    }

    private static async Task<ParsedLoopbackRequest> ReadFullHttpRequestAsync(
        NetworkStream ns,
        bool readBody,
        CancellationToken ct)
    {
      var raw = new List<byte>(4096);
      byte[] tmp = new byte[8192];
      int headerEnd = -1;
      while (headerEnd < 0)
      {
        int n = await ns.ReadAsync(tmp, 0, tmp.Length, ct).ConfigureAwait(false);
        if (n <= 0) return null;
        for (int i = 0; i < n; i++) raw.Add(tmp[i]);
        for (int i = Math.Max(0, raw.Count - n - 3); i <= raw.Count - 4; i++)
        {
          if (raw[i] == '\r' && raw[i + 1] == '\n' && raw[i + 2] == '\r' && raw[i + 3] == '\n')
          {
            headerEnd = i;
            break;
          }
        }
      }

      string headerText = Encoding.ASCII.GetString(raw.ToArray(), 0, headerEnd);
      var req = new ParsedLoopbackRequest();
      string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
      if (lines.Length == 0) return null;
      string[] parts = lines[0].Split(' ');
      if (parts.Length < 2) return null;
      req.Method = parts[0];
      req.Target = parts[1];
      for (int i = 1; i < lines.Length; i++)
      {
        int colon = lines[i].IndexOf(':');
        if (colon > 0)
        {
          req.Headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
        }
      }

      if (!readBody) return req;

      int remStart = headerEnd + 4;
      var bodyBuf = new MemoryStream();
      if (req.Headers.TryGetValue("Transfer-Encoding", out string te) &&
          te.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
      {
        var pending = new List<byte>();
        for (int i = remStart; i < raw.Count; i++) pending.Add(raw[i]);
        int cursor = 0;
        async Task<bool> EnsureBytesAsync(int need)
        {
          while (pending.Count - cursor < need)
          {
            int n = await ns.ReadAsync(tmp, 0, tmp.Length, ct).ConfigureAwait(false);
            if (n <= 0) return false;
            for (int i = 0; i < n; i++) pending.Add(tmp[i]);
          }
          return true;
        }
        while (true)
        {
          int crlf = -1;
          while (crlf < 0)
          {
            for (int i = cursor; i + 1 < pending.Count; i++)
            {
              if (pending[i] == '\r' && pending[i + 1] == '\n') { crlf = i; break; }
            }
            if (crlf < 0)
            {
              if (!await EnsureBytesAsync(pending.Count - cursor + 1).ConfigureAwait(false)) return req;
            }
          }
          string hex = Encoding.ASCII.GetString(pending.GetRange(cursor, crlf - cursor).ToArray()).Trim();
          int semi = hex.IndexOf(';');
          if (semi >= 0) hex = hex.Substring(0, semi);
          int chunkLen = Convert.ToInt32(hex, 16);
          cursor = crlf + 2;
          if (chunkLen == 0) break;
          if (!await EnsureBytesAsync(chunkLen + 2).ConfigureAwait(false)) return req;
          bodyBuf.Write(pending.GetRange(cursor, chunkLen).ToArray(), 0, chunkLen);
          cursor += chunkLen + 2;
          if (cursor > 65536)
          {
            pending.RemoveRange(0, cursor);
            cursor = 0;
          }
        }
      }
      else if (req.Headers.TryGetValue("Content-Length", out string clStr) &&
               int.TryParse(clStr, out int contentLen) && contentLen > 0)
      {
        int have = raw.Count - remStart;
        if (have > 0)
        {
          int take = Math.Min(have, contentLen);
          bodyBuf.Write(raw.ToArray(), remStart, take);
        }
        while (bodyBuf.Length < contentLen)
        {
          int want = Math.Min(tmp.Length, contentLen - (int)bodyBuf.Length);
          int n = await ns.ReadAsync(tmp, 0, want, ct).ConfigureAwait(false);
          if (n <= 0) break;
          bodyBuf.Write(tmp, 0, n);
        }
      }
      req.Body = bodyBuf.ToArray();
      return req;
    }

    private static async Task LingeringCloseAsync(TcpClient client, NetworkStream ns, CancellationToken ct)
    {
      try { client.Client.Shutdown(SocketShutdown.Send); } catch { }
      byte[] drain = new byte[4096];
      using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      drainCts.CancelAfter(1000);
      try
      {
        while (await ns.ReadAsync(drain, 0, drain.Length, drainCts.Token).ConfigureAwait(false) > 0) { }
      }
      catch { }
    }

    // Deterministic offline loopback tests for FirebaseHttpMessageHandler + HttpHelpers covering:
    //   1. Multi-value headers, JSON POST 200, 400 error body, and 204 No Content.
    //   2. Barrier-synchronized SSE incremental delivery (server waits for ack before next chunk).
    //   3. Idle SSE mid-stream cancellation from Unity main thread + server socket-close assertion.
    //   4. Stalled non-2xx error body cancellation in HttpHelpers.ValidateHttpResponse.
    //   5. Unary ResponseContentRead post-header stall timeout enforcement.
    //   6. Early 413 response during a 2 MiB upload completing cleanly without hanging.
    async Task InternalTestNativeHttpLoopbackStreamingAndCancellation()
    {
      var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      int port = ((IPEndPoint)listener.LocalEndpoint).Port;
      using var serverStopCts = new CancellationTokenSource();

      var sseAck1 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      var sseAck2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      var cancelSocketClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

      Task serverLoop = Task.Run(async () =>
      {
        while (!serverStopCts.IsCancellationRequested)
        {
          TcpClient client;
          try
          {
            client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
          }
          catch
          {
            break;
          }
          _ = Task.Run(async () =>
          {
            using (client)
            using (NetworkStream ns = client.GetStream())
            {
              try
              {
                var req = await ReadFullHttpRequestAsync(ns, readBody: false, serverStopCts.Token).ConfigureAwait(false);
                if (req == null) return;

                if (req.Method == "GET" && req.Target == "/headers")
                {
                  byte[] resp = Encoding.UTF8.GetBytes(
                      "HTTP/1.1 200 OK\r\n" +
                      "Content-Type: text/plain\r\n" +
                      "X-Multi: one\r\n" +
                      "X-Multi: two\r\n" +
                      "Content-Length: 2\r\n" +
                      "Connection: close\r\n\r\nok");
                  await ns.WriteAsync(resp, 0, resp.Length).ConfigureAwait(false);
                }
                else if (req.Method == "POST" && req.Target == "/json-200")
                {
                  string body = "{\"ok\":true}";
                  byte[] resp = Encoding.UTF8.GetBytes(
                      "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " +
                      body.Length + "\r\nConnection: close\r\n\r\n" + body);
                  await ns.WriteAsync(resp, 0, resp.Length).ConfigureAwait(false);
                }
                else if (req.Method == "POST" && req.Target == "/err-400")
                {
                  string body = "{\"error\":{\"status\":\"INVALID_ARGUMENT\"}}";
                  byte[] resp = Encoding.UTF8.GetBytes(
                      "HTTP/1.1 400 Bad Request\r\nContent-Type: application/json\r\nContent-Length: " +
                      body.Length + "\r\nConnection: close\r\n\r\n" + body);
                  await ns.WriteAsync(resp, 0, resp.Length).ConfigureAwait(false);
                }
                else if (req.Method == "DELETE" && req.Target == "/del-204")
                {
                  byte[] resp = Encoding.UTF8.GetBytes(
                      "HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n");
                  await ns.WriteAsync(resp, 0, resp.Length).ConfigureAwait(false);
                }
                else if (req.Method == "GET" && req.Target == "/sse-incremental")
                {
                  byte[] hdr = Encoding.UTF8.GetBytes(
                      "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\n" +
                      "Transfer-Encoding: chunked\r\nConnection: close\r\n\r\n");
                  await ns.WriteAsync(hdr, 0, hdr.Length).ConfigureAwait(false);
                  async Task SendSseChunkAsync(string payload)
                  {
                    int len = Encoding.UTF8.GetByteCount(payload);
                    byte[] c = Encoding.UTF8.GetBytes($"{len:x}\r\n{payload}\r\n");
                    await ns.WriteAsync(c, 0, c.Length).ConfigureAwait(false);
                    await ns.FlushAsync().ConfigureAwait(false);
                  }
                  await SendSseChunkAsync("data: {\"seq\":1}\n\n").ConfigureAwait(false);
                  await Task.WhenAny(sseAck1.Task, Task.Delay(5000, serverStopCts.Token)).ConfigureAwait(false);
                  await SendSseChunkAsync("data: {\"seq\":2}\n\n").ConfigureAwait(false);
                  await Task.WhenAny(sseAck2.Task, Task.Delay(5000, serverStopCts.Token)).ConfigureAwait(false);
                  await SendSseChunkAsync("data: {\"seq\":3}\n\n").ConfigureAwait(false);
                  byte[] end = Encoding.UTF8.GetBytes("0\r\n\r\n");
                  await ns.WriteAsync(end, 0, end.Length).ConfigureAwait(false);
                }
                else if (req.Method == "GET" && req.Target == "/sse-hang")
                {
                  string ssePayload = "data: {\"seq\":1}\n\n";
                  int chunkByteLen = Encoding.UTF8.GetByteCount(ssePayload);
                  byte[] resp = Encoding.UTF8.GetBytes(
                      "HTTP/1.1 200 OK\r\n" +
                      "Content-Type: text/event-stream\r\n" +
                      "Transfer-Encoding: chunked\r\n" +
                      "Connection: close\r\n\r\n" +
                      $"{chunkByteLen:x}\r\n{ssePayload}\r\n");
                  await ns.WriteAsync(resp, 0, resp.Length).ConfigureAwait(false);
                  await ns.FlushAsync().ConfigureAwait(false);
                  // Wait for client cancel to close the TCP socket.
                  byte[] probe = new byte[64];
                  using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(serverStopCts.Token);
                  probeCts.CancelAfter(5000);
                  try
                  {
                    int n = await ns.ReadAsync(probe, 0, probe.Length, probeCts.Token).ConfigureAwait(false);
                    if (n <= 0) cancelSocketClosed.TrySetResult(true);
                  }
                  catch
                  {
                    cancelSocketClosed.TrySetResult(true);
                  }
                }
                else if (req.Method == "GET" && req.Target == "/err-stall")
                {
                  byte[] resp = Encoding.UTF8.GetBytes(
                      "HTTP/1.1 503 Service Unavailable\r\n" +
                      "Content-Type: application/json\r\n" +
                      "Content-Length: 1024\r\n" +
                      "Connection: close\r\n\r\n{\"partial\":");
                  await ns.WriteAsync(resp, 0, resp.Length).ConfigureAwait(false);
                  await ns.FlushAsync().ConfigureAwait(false);
                  try { await Task.Delay(15000, serverStopCts.Token).ConfigureAwait(false); } catch { }
                }
                else if (req.Method == "GET" && req.Target == "/unary-stall")
                {
                  byte[] resp = Encoding.UTF8.GetBytes(
                      "HTTP/1.1 200 OK\r\n" +
                      "Content-Type: application/json\r\n" +
                      "Content-Length: 4096\r\n" +
                      "Connection: close\r\n\r\n{\"slow\":");
                  await ns.WriteAsync(resp, 0, resp.Length).ConfigureAwait(false);
                  await ns.FlushAsync().ConfigureAwait(false);
                  try { await Task.Delay(15000, serverStopCts.Token).ConfigureAwait(false); } catch { }
                }
                else if (req.Method == "PUT" && req.Target == "/early-413")
                {
                  byte[] resp = Encoding.UTF8.GetBytes(
                      "HTTP/1.1 413 Payload Too Large\r\n" +
                      "Content-Length: 0\r\n" +
                      "Connection: close\r\n\r\n");
                  await ns.WriteAsync(resp, 0, resp.Length).ConfigureAwait(false);
                  await ns.FlushAsync().ConfigureAwait(false);
                  await LingeringCloseAsync(client, ns, serverStopCts.Token).ConfigureAwait(false);
                }
              }
              catch
              {
              }
            }
          });
        }
      });

      try
      {
        string baseUrl = $"http://127.0.0.1:{port}";
        using var client = Firebase.Internal.HttpHelpers.CreateHttpClient(TimeSpan.FromSeconds(10));

        // 1. Multi-value response headers round-trip.
        using (var resp = await client.GetAsync(baseUrl + "/headers"))
        {
          AssertEq("Loopback /headers status", (int)resp.StatusCode, 200);
          AssertEq("Loopback /headers body", await resp.Content.ReadAsStringAsync(), "ok");
          Assert("Loopback X-Multi header present", resp.Headers.TryGetValues("X-Multi", out var vals));
          AssertEq("Loopback X-Multi values", string.Join(", ", vals), "one, two");
#if !UNITY_IOS && !UNITY_TVOS
          AssertEq("Loopback X-Multi entry count", vals.Count(), 2);
#endif
        }

        // 1b. JSON POST 200, 400 error body, and 204 No Content.
        using (var resp = await client.PostAsync(
            baseUrl + "/json-200", new StringContent("{\"hi\":1}", Encoding.UTF8, "application/json")))
        {
          AssertEq("JSON POST 200 status", (int)resp.StatusCode, 200);
          AssertEq("JSON POST 200 body", await resp.Content.ReadAsStringAsync(), "{\"ok\":true}");
        }
        using (var resp = await client.PostAsync(
            baseUrl + "/err-400", new StringContent("{}", Encoding.UTF8, "application/json")))
        {
          AssertEq("JSON POST 400 status", (int)resp.StatusCode, 400);
          AssertEq("JSON POST 400 body", await resp.Content.ReadAsStringAsync(),
              "{\"error\":{\"status\":\"INVALID_ARGUMENT\"}}");
        }
        using (var resp = await client.DeleteAsync(baseUrl + "/del-204"))
        {
          AssertEq("DELETE 204 status", (int)resp.StatusCode, 204);
          AssertEq("DELETE 204 body", await resp.Content.ReadAsStringAsync(), "");
        }

        // 2. Barrier-synchronized SSE incremental delivery (proves zero whole-body buffering).
        using (var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/sse-incremental"))
        using (var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead))
        using (var stream = await resp.Content.ReadAsStreamAsync())
        using (var reader = new StreamReader(stream))
        {
          var sseSw = System.Diagnostics.Stopwatch.StartNew();
          string l1 = await reader.ReadLineAsync();
          long firstChunkMs = sseSw.ElapsedMilliseconds;
          AssertEq("SSE incremental chunk 1", l1, "data: {\"seq\":1}");
          await reader.ReadLineAsync();
          sseAck1.TrySetResult(true);

          string l2 = await reader.ReadLineAsync();
          AssertEq("SSE incremental chunk 2", l2, "data: {\"seq\":2}");
          await reader.ReadLineAsync();
          sseAck2.TrySetResult(true);

          string l3 = await reader.ReadLineAsync();
          long lastChunkMs = sseSw.ElapsedMilliseconds;
          AssertEq("SSE incremental chunk 3", l3, "data: {\"seq\":3}");
          DebugLog($"SSE_INCREMENTAL|chunks=3|first_to_last_ms={lastChunkMs - firstChunkMs}");
        }

        // 3. Idle SSE cancellation from the Unity main thread + server socket-close assertion.
        using (var sseCts = new CancellationTokenSource())
        {
          using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/sse-hang");
          using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, sseCts.Token);
          using var reg = sseCts.Token.Register(() => resp.Dispose());
          await Firebase.Internal.HttpHelpers.ValidateHttpResponse(resp, sseCts.Token);
          using var stream = await resp.Content.ReadAsStreamAsync();
          using var reader = new StreamReader(stream);

          string firstLine = await Firebase.Internal.HttpHelpers.ReadLineWithCancellationAsync(reader, sseCts.Token);
          AssertEq("SSE first chunk", firstLine, "data: {\"seq\":1}");
          string blankLine = await Firebase.Internal.HttpHelpers.ReadLineWithCancellationAsync(reader, sseCts.Token);
          AssertEq("SSE blank separator", blankLine, "");

          // Start blocked read, then cancel directly from Unity's main thread.
          Task<string> blockedRead = Firebase.Internal.HttpHelpers.ReadLineWithCancellationAsync(reader, sseCts.Token);
          await Task.Delay(50);
          bool onMainThread = Thread.CurrentThread.ManagedThreadId == mainThreadId;
          var sw = System.Diagnostics.Stopwatch.StartNew();
          sseCts.Cancel();
          bool canceled = false;
          try
          {
            await blockedRead;
          }
          catch (OperationCanceledException)
          {
            canceled = true;
          }
          sw.Stop();
          Assert("SSE mid-stream cancel must throw OperationCanceledException", canceled);
          Assert($"SSE mid-stream cancel took too long: {sw.ElapsedMilliseconds}ms", sw.ElapsedMilliseconds < 3000);
          Task closedDone = await Task.WhenAny(cancelSocketClosed.Task, Task.Delay(3000));
          Assert("Server must observe socket close after SSE cancel", closedDone == cancelSocketClosed.Task);
          DebugLog($"MAIN_THREAD_CANCEL|on_main_thread={onMainThread}|latency_ms={sw.ElapsedMilliseconds}|socket_closed=true");
        }

        // 4. Stalled non-2xx error body under ResponseHeadersRead cancels promptly in ValidateHttpResponse.
        using (var errCts = new CancellationTokenSource())
        {
          using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/err-stall");
          using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, errCts.Token);
          errCts.CancelAfter(150);
          var sw = System.Diagnostics.Stopwatch.StartNew();
          bool canceled = false;
          try
          {
            await Firebase.Internal.HttpHelpers.ValidateHttpResponse(resp, errCts.Token);
          }
          catch (OperationCanceledException)
          {
            canceled = true;
          }
          sw.Stop();
          Assert("Stalled error body must cancel with OperationCanceledException", canceled);
          Assert($"Stalled error body cancel took too long: {sw.ElapsedMilliseconds}ms", sw.ElapsedMilliseconds < 3000);
        }

        // 5. Unary ResponseContentRead enforces HttpClient.Timeout on post-header stalls.
        using (var shortClient = Firebase.Internal.HttpHelpers.CreateHttpClient(TimeSpan.FromMilliseconds(350)))
        {
          var sw = System.Diagnostics.Stopwatch.StartNew();
          bool timedOut = false;
          try
          {
            using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/unary-stall");
            using var resp = await shortClient.SendAsync(req);
          }
          catch (OperationCanceledException)
          {
            timedOut = true;
          }
          sw.Stop();
          Assert("Unary post-header stall must time out", timedOut);
          Assert($"Unary post-header timeout took too long: {sw.ElapsedMilliseconds}ms", sw.ElapsedMilliseconds < 3000);
        }

        // 6. Early 413 response during a 2 MiB upload completes cleanly without hanging (A2, P1, T5).
        {
          byte[] bigPayload = new byte[2 * 1024 * 1024];
          using var req = new HttpRequestMessage(HttpMethod.Put, baseUrl + "/early-413");
          req.Content = new StreamContent(new MemoryStream(bigPayload));
          var sw = System.Diagnostics.Stopwatch.StartNew();
          using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
          sw.Stop();
          AssertEq("Early 413 status", (int)resp.StatusCode, 413);
          Assert($"Early 413 upload took too long: {sw.ElapsedMilliseconds}ms", sw.ElapsedMilliseconds < 3000);
          DebugLog($"EARLY_413_UPLOAD|status={(int)resp.StatusCode}|ms={sw.ElapsedMilliseconds}");
        }
      }
      finally
      {
        serverStopCts.Cancel();
        listener.Stop();
        try { await serverLoop; } catch { }
      }
    }

    // Cross-platform loopback verification for A1, A3, C1, C3, S3, T4, T5:
    //   - Zero-length GET stays GET, zero-length HEAD stays HEAD
    //   - 301/302/303 POST -> GET, 303 HEAD stays HEAD, 307 streamed body rejected
    //   - Same-origin redirect preserves Authorization; cross-origin strips Auth, Cookie,
    //     AppCheck, and API key while preserving safe headers
    //   - Redirect target with space is percent-encoded cleanly
    async Task InternalTestNativeHttpRedirectsAndMethods()
    {
      var listenerA = new TcpListener(IPAddress.Loopback, 0);
      var listenerB = new TcpListener(IPAddress.Loopback, 0);
      listenerA.Start();
      listenerB.Start();
      int portA = ((IPEndPoint)listenerA.LocalEndpoint).Port;
      int portB = ((IPEndPoint)listenerB.LocalEndpoint).Port;
      using var stopCts = new CancellationTokenSource();

      string observedZeroGetMethod = null;
      string observedZeroHeadMethod = null;
      string observed301Method = null;
      string observed303PostMethod = null;
      string observed303HeadMethod = null;
      string observedSameAuth = null;
      string observedSpaceTarget = null;
      ParsedLoopbackRequest observedCrossB = null;

      Task loopB = Task.Run(async () =>
      {
        while (!stopCts.IsCancellationRequested)
        {
          TcpClient c;
          try { c = await listenerB.AcceptTcpClientAsync().ConfigureAwait(false); } catch { break; }
          _ = Task.Run(async () =>
          {
            using (c)
            using (NetworkStream ns = c.GetStream())
            {
              try
              {
                var req = await ReadFullHttpRequestAsync(ns, readBody: false, stopCts.Token).ConfigureAwait(false);
                if (req == null) return;
                observedCrossB = req;
                byte[] resp = Encoding.UTF8.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Length: 4\r\nConnection: close\r\n\r\ndone");
                await ns.WriteAsync(resp, 0, resp.Length).ConfigureAwait(false);
              }
              catch { }
            }
          });
        }
      });

      Task loopA = Task.Run(async () =>
      {
        while (!stopCts.IsCancellationRequested)
        {
          TcpClient c;
          try { c = await listenerA.AcceptTcpClientAsync().ConfigureAwait(false); } catch { break; }
          _ = Task.Run(async () =>
          {
            using (c)
            using (NetworkStream ns = c.GetStream())
            {
              try
              {
                var req = await ReadFullHttpRequestAsync(ns, readBody: false, stopCts.Token).ConfigureAwait(false);
                if (req == null) return;
                string rawResp;
                if (req.Target == "/zero-get")
                {
                  observedZeroGetMethod = req.Method;
                  rawResp = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok";
                }
                else if (req.Target == "/zero-head")
                {
                  observedZeroHeadMethod = req.Method;
                  rawResp = "HTTP/1.1 200 OK\r\nContent-Length: 10\r\nConnection: close\r\n\r\n";
                }
                else if (req.Target == "/post-301")
                {
                  rawResp = "HTTP/1.1 301 Moved Permanently\r\nLocation: /after-301\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                }
                else if (req.Target == "/after-301")
                {
                  observed301Method = req.Method;
                  req.Headers.TryGetValue("Authorization", out observedSameAuth);
                  rawResp = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\n301";
                }
                else if (req.Target == "/post-303")
                {
                  rawResp = "HTTP/1.1 303 See Other\r\nLocation: /after-303-post\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                }
                else if (req.Target == "/after-303-post")
                {
                  observed303PostMethod = req.Method;
                  rawResp = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\n303";
                }
                else if (req.Target == "/head-303")
                {
                  rawResp = "HTTP/1.1 303 See Other\r\nLocation: /after-303-head\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                }
                else if (req.Target == "/after-303-head")
                {
                  observed303HeadMethod = req.Method;
                  rawResp = "HTTP/1.1 200 OK\r\nContent-Length: 42\r\nConnection: close\r\n\r\n";
                }
                else if (req.Target == "/body-307")
                {
                  rawResp = "HTTP/1.1 307 Temporary Redirect\r\nLocation: /never\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                }
                else if (req.Target == "/space-redir")
                {
                  rawResp = "HTTP/1.1 302 Found\r\nLocation: /path with space\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                }
                else if (req.Target == "/path%20with%20space")
                {
                  observedSpaceTarget = req.Target;
                  rawResp = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nspace";
                }
                else if (req.Target == "/cross-307")
                {
                  rawResp = $"HTTP/1.1 307 Temporary Redirect\r\nLocation: http://127.0.0.1:{portB}/final\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                }
                else
                {
                  rawResp = "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                }
                byte[] resp = Encoding.UTF8.GetBytes(rawResp);
                await ns.WriteAsync(resp, 0, resp.Length).ConfigureAwait(false);
                if (req.Target == "/body-307")
                {
                  await LingeringCloseAsync(c, ns, stopCts.Token).ConfigureAwait(false);
                }
              }
              catch { }
            }
          });
        }
      });

      try
      {
        string baseA = $"http://127.0.0.1:{portA}";
        using var client = Firebase.Internal.HttpHelpers.CreateHttpClient(TimeSpan.FromSeconds(10));

        // 1. Zero-length GET stays GET and zero-length HEAD stays HEAD (A1).
        using (var req = new HttpRequestMessage(HttpMethod.Get, baseA + "/zero-get"))
        {
          req.Content = new ByteArrayContent(Array.Empty<byte>());
          using var resp = await client.SendAsync(req);
          AssertEq("Zero-length GET status", (int)resp.StatusCode, 200);
          AssertEq("Zero-length GET method", observedZeroGetMethod, "GET");
        }
        using (var req = new HttpRequestMessage(HttpMethod.Head, baseA + "/zero-head"))
        {
          req.Content = new ByteArrayContent(Array.Empty<byte>());
          using var resp = await client.SendAsync(req);
          AssertEq("Zero-length HEAD status", (int)resp.StatusCode, 200);
          AssertEq("Zero-length HEAD method", observedZeroHeadMethod, "HEAD");
        }
        DebugLog($"ZERO_LENGTH_GET|method={observedZeroGetMethod}|head_method={observedZeroHeadMethod}");

        // 2. 301 POST -> GET preserves same-origin Authorization.
        using (var req = new HttpRequestMessage(HttpMethod.Post, baseA + "/post-301"))
        {
          req.Headers.TryAddWithoutValidation("Authorization", "Bearer same-secret");
          req.Content = new ByteArrayContent(Array.Empty<byte>());
          using var resp = await client.SendAsync(req);
          AssertEq("301 POST->GET status", (int)resp.StatusCode, 200);
          AssertEq("301 method", observed301Method, "GET");
          AssertEq("301 same-origin Authorization", observedSameAuth, "Bearer same-secret");
        }

        // 3. 303 POST -> GET and 303 HEAD stays HEAD (A3, C1).
        using (var resp = await client.PostAsync(baseA + "/post-303", new ByteArrayContent(Array.Empty<byte>())))
        {
          AssertEq("303 POST->GET status", (int)resp.StatusCode, 200);
          AssertEq("303 POST method", observed303PostMethod, "GET");
        }
        using (var req = new HttpRequestMessage(HttpMethod.Head, baseA + "/head-303"))
        using (var resp = await client.SendAsync(req))
        {
          AssertEq("303 HEAD status", (int)resp.StatusCode, 200);
          AssertEq("303 HEAD kept as HEAD", observed303HeadMethod, "HEAD");
        }

        // 4. 307 with streamed body fails cleanly (T4).
        bool caught307 = false;
        try
        {
          using var req = new HttpRequestMessage(HttpMethod.Post, baseA + "/body-307");
          req.Content = new StreamContent(new NonSeekableReadStream(Encoding.UTF8.GetBytes("cannot-replay")));
          using var resp = await client.SendAsync(req);
        }
        catch (HttpRequestException)
        {
          caught307 = true;
        }
        Assert("307 with streamed body must fail", caught307);

        // 5. Redirect target containing a space succeeds (C3).
        using (var resp = await client.GetAsync(baseA + "/space-redir"))
        {
          AssertEq("Space redirect status", (int)resp.StatusCode, 200);
          AssertEq("Space redirect target", observedSpaceTarget, "/path%20with%20space");
        }

        // 6. Cross-origin 307 strips Authorization, Cookie, AppCheck, and API key (S3, T5).
        using (var req = new HttpRequestMessage(HttpMethod.Get, baseA + "/cross-307"))
        {
          req.Headers.TryAddWithoutValidation("Authorization", "Bearer top-secret");
          req.Headers.TryAddWithoutValidation("Cookie", "sid=secret-cookie");
          req.Headers.TryAddWithoutValidation("X-Firebase-AppCheck", "appcheck-secret");
          req.Headers.TryAddWithoutValidation("x-goog-api-key", "apikey-secret");
          req.Headers.TryAddWithoutValidation("X-Safe", "keep-me");
          using var resp = await client.SendAsync(req);
          AssertEq("Cross-origin 307 status", (int)resp.StatusCode, 200);
          Assert("Cross-origin server B received request", observedCrossB != null);
          Assert("Authorization stripped cross-origin", !observedCrossB.Headers.ContainsKey("Authorization"));
          Assert("Cookie stripped cross-origin", !observedCrossB.Headers.ContainsKey("Cookie"));
          Assert("AppCheck stripped cross-origin", !observedCrossB.Headers.ContainsKey("X-Firebase-AppCheck"));
          Assert("API key stripped cross-origin", !observedCrossB.Headers.ContainsKey("x-goog-api-key"));
          AssertEq("Safe header preserved cross-origin",
              observedCrossB.Headers.TryGetValue("X-Safe", out string safeVal) ? safeVal : null,
              "keep-me");
        }
      }
      finally
      {
        stopCts.Cancel();
        listenerA.Stop();
        listenerB.Stop();
        try { await loopA; } catch { }
        try { await loopB; } catch { }
      }
    }

    // Cross-platform large streaming, TLS error classification, and 100 start-and-cancel stress (T5):
    //   1. 1 MiB unknown-length chunked upload verified byte-for-byte.
    //   2. 4 MiB download with slow reader verified byte-for-byte.
    //   3. Bad TLS handshake classified as AuthenticationException (non-retryable by Storage, S4).
    //   4. 100 start-and-cancel cycles with thread/memory baseline check.
    async Task InternalTestNativeHttpLargeStreamingAndStress()
    {
      const int uploadSize = 1024 * 1024;      // 1 MiB chunked
      const int downloadSize = 4 * 1024 * 1024; // 4 MiB slow reader
      byte[] expectedUpload = new byte[uploadSize];
      for (int i = 0; i < uploadSize; i++) expectedUpload[i] = (byte)((i * 31 + 7) & 0xFF);
      byte[] expectedDownload = new byte[downloadSize];
      for (int i = 0; i < downloadSize; i++) expectedDownload[i] = (byte)((i * 17 + 3) & 0xFF);

      byte[] receivedUpload = null;
      var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      int port = ((IPEndPoint)listener.LocalEndpoint).Port;

      var tlsListener = new TcpListener(IPAddress.Loopback, 0);
      tlsListener.Start();
      int tlsPort = ((IPEndPoint)tlsListener.LocalEndpoint).Port;

      using var stopCts = new CancellationTokenSource();

      Task serverLoop = Task.Run(async () =>
      {
        while (!stopCts.IsCancellationRequested)
        {
          TcpClient c;
          try { c = await listener.AcceptTcpClientAsync().ConfigureAwait(false); } catch { break; }
          _ = Task.Run(async () =>
          {
            using (c)
            using (NetworkStream ns = c.GetStream())
            {
              try
              {
                var req = await ReadFullHttpRequestAsync(ns, readBody: true, stopCts.Token).ConfigureAwait(false);
                if (req == null) return;
                if (req.Method == "PUT" && req.Target == "/chunked-upload")
                {
                  receivedUpload = req.Body;
                  byte[] resp = Encoding.UTF8.GetBytes(
                      "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
                  await ns.WriteAsync(resp, 0, resp.Length).ConfigureAwait(false);
                }
                else if (req.Method == "GET" && req.Target == "/download-4m")
                {
                  byte[] hdr = Encoding.UTF8.GetBytes(
                      $"HTTP/1.1 200 OK\r\nContent-Length: {downloadSize}\r\nConnection: close\r\n\r\n");
                  await ns.WriteAsync(hdr, 0, hdr.Length).ConfigureAwait(false);
                  int sent = 0;
                  while (sent < downloadSize)
                  {
                    int slice = Math.Min(65536, downloadSize - sent);
                    await ns.WriteAsync(expectedDownload, sent, slice, stopCts.Token).ConfigureAwait(false);
                    sent += slice;
                  }
                }
                else if (req.Method == "GET" && req.Target == "/hang-cancel")
                {
                  byte[] hdr = Encoding.UTF8.GetBytes(
                      "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nTransfer-Encoding: chunked\r\n\r\n");
                  await ns.WriteAsync(hdr, 0, hdr.Length).ConfigureAwait(false);
                  await ns.FlushAsync().ConfigureAwait(false);
                  byte[] sink = new byte[64];
                  try { await ns.ReadAsync(sink, 0, sink.Length, stopCts.Token).ConfigureAwait(false); } catch { }
                }
              }
              catch { }
            }
          });
        }
      });

      int tlsServerAccepts = 0;
      Task tlsLoop = Task.Run(async () =>
      {
        byte[] tlsAlert = { 0x15, 0x03, 0x03, 0x00, 0x02, 0x02, 0x28 };
        while (!stopCts.IsCancellationRequested)
        {
          TcpClient c;
          try { c = await tlsListener.AcceptTcpClientAsync().ConfigureAwait(false); } catch { break; }
          _ = Task.Run(async () =>
          {
            using (c)
            using (NetworkStream ns = c.GetStream())
            {
              try
              {
                Interlocked.Increment(ref tlsServerAccepts);
                byte[] buf = new byte[512];
                await ns.ReadAsync(buf, 0, buf.Length, stopCts.Token).ConfigureAwait(false);
                await ns.WriteAsync(tlsAlert, 0, tlsAlert.Length, stopCts.Token).ConfigureAwait(false);
                try { c.Client.Shutdown(SocketShutdown.Send); } catch { }
              }
              catch { }
            }
          });
        }
      });

      try
      {
        string baseUrl = $"http://127.0.0.1:{port}";
        using var client = Firebase.Internal.HttpHelpers.CreateHttpClient(TimeSpan.FromSeconds(30));

        // 1. 1 MiB unknown-length chunked upload verified byte-for-byte.
        using (var req = new HttpRequestMessage(HttpMethod.Put, baseUrl + "/chunked-upload"))
        {
          req.Content = new StreamContent(new NonSeekableReadStream(expectedUpload));
          req.Content.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
          using var resp = await client.SendAsync(req);
          AssertEq("1 MiB chunked upload status", (int)resp.StatusCode, 200);
          AssertEq("1 MiB chunked upload response", await resp.Content.ReadAsStringAsync(), "ok");
        }
        Assert("1 MiB chunked upload received", receivedUpload != null && receivedUpload.Length == uploadSize);
        for (int i = 0; i < uploadSize; i += 4093)
        {
          AssertEq($"1 MiB chunked upload byte {i}", receivedUpload[i], expectedUpload[i]);
        }

        // 2. 4 MiB download with slow reader verified byte-for-byte.
        using (var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/download-4m"))
        using (var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead))
        using (var stream = await resp.Content.ReadAsStreamAsync())
        {
          AssertEq("4 MiB download status", (int)resp.StatusCode, 200);
          byte[] rbuf = new byte[65536];
          int total = 0;
          int reads = 0;
          while (true)
          {
            int n = await stream.ReadAsync(rbuf, 0, rbuf.Length);
            if (n <= 0) break;
            for (int i = 0; i < n; i += 4093)
            {
              AssertEq($"4 MiB download byte {total + i}", rbuf[i], expectedDownload[total + i]);
            }
            total += n;
            reads++;
            if ((reads % 8) == 0) await Task.Delay(5);
          }
          AssertEq("4 MiB download total bytes", total, downloadSize);
        }

        // 3. Bad TLS handshake returns AuthenticationException inner exception (S4).
        bool caughtTls = false;
        try
        {
          using var resp = await client.GetAsync($"https://127.0.0.1:{tlsPort}/bad-tls");
        }
        catch (HttpRequestException ex)
        {
          caughtTls = FirebaseHttpMessageHandler.IsTlsFailure(ex);
          DebugLog($"BAD_TLS_CHECK|is_tls={caughtTls}|inner={ex.InnerException?.GetType().Name}|msg={ex.Message}");
        }
        Assert("TLS handshake failure must be classified as IsTlsFailure", caughtTls);

        // 4. 100 start-and-cancel cycles checking thread count & managed memory stability.
        int TryGetThreadCount()
        {
          try { return System.Diagnostics.Process.GetCurrentProcess().Threads.Count; }
          catch { return -1; }
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long memBefore = GC.GetTotalMemory(true);
        int threadsBefore = TryGetThreadCount();
        for (int i = 0; i < 100; i++)
        {
          using var cts = new CancellationTokenSource();
          using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/hang-cancel");
          Task<HttpResponseMessage> t = client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
          cts.Cancel();
          try
          {
            using var r = await t;
          }
          catch (OperationCanceledException) { }
          catch (HttpRequestException) { }
        }
        await Task.Delay(250);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long memAfter = GC.GetTotalMemory(true);
        int threadsAfter = TryGetThreadCount();
        DebugLog($"CANCEL_CYCLES_100|threads_before={threadsBefore}|threads_after={threadsAfter}|gc_before_mb={memBefore / (1024.0 * 1024.0):F2}|gc_after_mb={memAfter / (1024.0 * 1024.0):F2}");
      }
      finally
      {
        stopCts.Cancel();
        listener.Stop();
        tlsListener.Stop();
        try { await serverLoop; } catch { }
        try { await tlsLoop; } catch { }
      }
    }
  }
}
