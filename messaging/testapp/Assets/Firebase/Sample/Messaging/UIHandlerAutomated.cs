namespace Firebase.Sample.Messaging {
  using Firebase.Extensions;
  using Firebase.Functions;
  using Firebase.Messaging;
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.Text.RegularExpressions;
  using System.Threading.Tasks;
  using UnityEngine;
  using UnityEngine.Networking;

  public class UIHandlerAutomated : UIHandler {
    private Firebase.Sample.AutomatedTestRunner testRunner;

    private const string TestTopic = "TestTopic";

    private const string MessageFoo = "This is a test message";
    private const string MessageBar = "It contains some data";
    private const string MessageSpam = "This is a another test message";
    private const string MessageEggs = "It also contains some data";

    private const string MessageNotificationTitle = "JSON message!";
    private const string MessageNotificationBody = "This notification has a body!";

    private static readonly Dictionary<string, object> TokenMessageFields = new Dictionary<string, object> {
      { "spam", MessageSpam },
      { "eggs", MessageEggs }
    };
    private static readonly Dictionary<string, object> TopicMessageFields = new Dictionary<string, object> {
      { "foo", MessageFoo },
      { "bar", MessageBar }
    };

    private string registrationToken;
    private FirebaseMessage lastReceivedMessage;

    // Don't subscribe to a topic, since it might confuse the tests.
    protected override bool SubscribeToTopicOnStart {
      get {
        return false;
      }
    }

    protected override void Start() {
#if FIREBASE_RUNNING_FROM_CI && (UNITY_IOS || UNITY_TVOS)
      // Messaging on iOS requires user interaction to give permissions
      // So if running on CI, just run a dummy test instead.
      Func<Task>[] tests = {
        MakeTest(TestDummy)
      };
      string[] customTests = {
        "TestDummy"
      };
      testRunner = AutomatedTestRunner.CreateTestRunner(
        testsToRun: tests,
        logFunc: DebugLog,
        testNames: customTests
      );
      // Don't use base.Start(), since that will trigger the permission request.
      DebugLog("Skipping usual Messaging tests on CI + iOS");
      isFirebaseInitialized = true;

#else // FIREBASE_RUNNING_FROM_CI && (UNITY_IOS || UNITY_TVOS)

      Func<Task>[] tests = {
        // Disable these tests on desktop, as desktop uses a stub implementation.
#if (UNITY_IOS || UNITY_TVOS || UNITY_ANDROID)
        TestGetRegistrationToken,
        MakeTest(TestSendJsonMessageToDevice),
        MakeTest(TestSendJsonMessageToSubscribedTopic),
#else  // (UNITY_IOS || UNITY_TVOS || UNITY_ANDROID)
        // Run a vacuous test. Should be removed if/when desktop platforms get a real test.
        MakeTest(TestDummy),
#endif // (UNITY_IOS || UNITY_TVOS || UNITY_ANDROID)
        // TODO(varconst): a more involved test to check that resubscribing works
        MakeTest(TestGetTokenAsync),
        MakeTest(TestDeleteTokenAsync),
      };

      string[] customTests = {
        // Disable these tests on desktop, as desktop uses a stub implementation.
#if (UNITY_IOS || UNITY_TVOS || UNITY_ANDROID)
        "TestGetRegistrationToken",
        "TestSendJsonMessageToDevice",
        "TestSendJsonMessageToSubscribedTopic",
#else  // #if (UNITY_IOS || UNITY_TVOS || UNITY_ANDROID)
        // Run a vacuous test. Should be removed if/when desktop platforms get a real test.
        "TestDummy",
#endif // (UNITY_IOS || UNITY_TVOS || UNITY_ANDROID)
        // TODO(varconst): a more involved test to check that resubscribing works
        "TestGetTokenAsync",
        "TestDeleteTokenAsync",
      };

      testRunner = AutomatedTestRunner.CreateTestRunner(
        testsToRun: tests,
        logFunc: DebugLog,
        testNames: customTests
      );

      base.Start();
#endif // FIREBASE_RUNNING_FROM_CI
    }

    protected override void Update() {
      base.Update();
      if (testRunner != null && isFirebaseInitialized) {
        testRunner.Update();
      }
    }

    public override void OnMessageReceived(object sender, MessageReceivedEventArgs e) {
      base.OnMessageReceived(sender, e);
      lastReceivedMessage = e.Message;
    }

    public override void OnTokenReceived(object sender, TokenReceivedEventArgs token) {
      base.OnTokenReceived(sender, token);
      registrationToken = token.Token;
    }

    // Starts the given coroutine and returns a task which reflects the ultimate result of the
    // coroutine execution. The provided coroutine is given a TaskCompletionSource and is responsible
    // for completing or faulting it sensibly.
    Func<Task> MakeTest(Func<TaskCompletionSource<string>, IEnumerator> coroutine) {
      return () => {
        var tcs = new TaskCompletionSource<string>();
        StartCoroutine(coroutine(tcs));
        return tcs.Task;
      };
    }

    // Guarantee that the registration token is set, before running other tests.
    Task TestGetRegistrationToken() {
      // The registration token might already be set, if gotten via OnTokenReceived
      if (!string.IsNullOrEmpty(registrationToken)) {
        DebugLog("Already have a registration token, skipping GetTokenAsync call");
        return Task.CompletedTask;
      }

      // Otherwise, call GetTokenAsync, to fetch one. This can happen if the app
      // already had a token from a previous run, and thus didn't need a new token.
      return Firebase.Messaging.FirebaseMessaging.GetTokenAsync().ContinueWithOnMainThread(t => {
        if (t.IsFaulted) {
          throw t.Exception;
        }

        registrationToken = t.Result;
      });
    }

    // If the registration token is missing, throw an exception.
    // Use for tests that require a registration token to function properly.
    void ThrowIfMissingRegistrationToken() {
      if (string.IsNullOrEmpty(registrationToken)) {
        throw new InvalidOperationException("Registration Token is missing.");
      }
    }

    // Sends a JSON message to the server, setting this device as the addressee, waits until the app
    // receives the message and verifies the contents are the same as were sent.
    IEnumerator TestSendJsonMessageToDevice(TaskCompletionSource<string> tcs) {
      ThrowIfMissingRegistrationToken();
      bool failedToSend = false;
      SendMessageToDeviceAsync(registrationToken).ContinueWithOnMainThread(t => {
        if (t.IsFaulted) {
          tcs.TrySetException(t.Exception);
          failedToSend = true;
        }
      });
      // TODO(b/65218400): check message id.
      while (lastReceivedMessage == null && !failedToSend) {
        yield return new WaitForSeconds(0.5f);
      }
      if (lastReceivedMessage != null) {
        ValidateJsonMessageA(tcs, lastReceivedMessage);
        lastReceivedMessage = null;
      }
    }

    // Sends a JSON message to the server, specifying a topic to which this device is subscribed,
    // waits until the app receives the message and verifies the contents are the same as were sent.
    IEnumerator TestSendJsonMessageToSubscribedTopic(TaskCompletionSource<string> tcs) {
      ThrowIfMissingRegistrationToken();
      bool failedToSend = false;
      // Note: Ideally this would use a more unique topic, but topic creation and subscription
      // takes additional time, so instead this only subscribes during this one test, and doesn't
      // fully test unsubscribing.
      Firebase.Messaging.FirebaseMessaging.SubscribeAsync(TestTopic).ContinueWithOnMainThread(t => {
        SendMessageToTopicAsync(TestTopic).ContinueWithOnMainThread(t2 => {
          if (t2.IsFaulted) {
            tcs.TrySetException(t2.Exception);
            failedToSend = true;
          }
        });
      });
      // TODO(b/65218400): check message id.
      while (lastReceivedMessage == null && !failedToSend) {
        yield return new WaitForSeconds(0.5f);
      }
      if (lastReceivedMessage != null) {
        // Unsubscribe from the test topic, to make sure that other messages aren't received.
        Firebase.Messaging.FirebaseMessaging.UnsubscribeAsync(TestTopic).ContinueWithOnMainThread(t => {
          ValidateJsonMessageB(tcs, lastReceivedMessage);
          lastReceivedMessage = null;
        });
      }
    }

    // Fake test (always passes immediately). Can be used on platforms with no other tests.
    IEnumerator TestDummy(TaskCompletionSource<string> tcs) {
      tcs.SetResult("Dummy test completed.");
      yield break;
    }

    // Test GetTokenAsync
    IEnumerator TestGetTokenAsync(TaskCompletionSource<string> tcs) {
      FirebaseMessaging.GetTokenAsync().ContinueWithOnMainThread(task => {
        tcs.SetResult(task.Result);
        DebugLog("GetToken:"+task.Result);
      });
      yield break;
    }
    // Test DeleteTokenAsync
    IEnumerator TestDeleteTokenAsync(TaskCompletionSource<string> tcs) {
      FirebaseMessaging.DeleteTokenAsync().ContinueWithOnMainThread(task => {
        tcs.SetResult("DeleteTokenAsync completed");
      });
      yield break;
    }

    // Sends a message to the specified target device, using Cloud Functions.
    // This relies on the sendMessage function that is defined in the C++ repo.
    Task<HttpsCallableResult> SendMessageToDeviceAsync(string targetDevice) {
      Dictionary<string, object> data = new Dictionary<string, object>();
      data["sendTo"] = targetDevice;
      data["isToken"] = true;
      data["notificationTitle"] = MessageNotificationTitle;
      data["notificationBody"] = MessageNotificationBody;
      data["messageFields"] = TokenMessageFields;

      var callable = FirebaseFunctions.DefaultInstance.GetHttpsCallable("sendMessage");
      DebugLog("Calling the Cloud Function to send a targeted message");
      return callable.CallAsync(data);
    }

    Task<HttpsCallableResult> SendMessageToTopicAsync(string topic) {
      Dictionary<string, object> data = new Dictionary<string, object>();
      data["sendTo"] = topic;
      data["isToken"] = false;
      data["notificationTitle"] = MessageNotificationTitle;
      data["notificationBody"] = MessageNotificationBody;
      data["messageFields"] = TopicMessageFields;

      var callable = FirebaseFunctions.DefaultInstance.GetHttpsCallable("sendMessage");
      DebugLog("Calling the Cloud Function to send a topic message");
      return callable.CallAsync(data);
    }

    void ValidateJsonMessageA(TaskCompletionSource<string> tcs, FirebaseMessage message) {
      try {
        ValidateMessageData(message, "spam", MessageSpam);
        ValidateMessageData(message, "eggs", MessageEggs);
        ValidateMessageNotification(message, MessageNotificationTitle, MessageNotificationBody);
        tcs.SetResult(message.MessageId);
      } catch (Exception e) {
        tcs.SetException(e);
      }
    }

    void ValidateJsonMessageB(TaskCompletionSource<string> tcs, FirebaseMessage message) {
      try {
        ValidateMessageData(message, "foo", MessageFoo);
        ValidateMessageData(message, "bar", MessageBar);
        ValidateMessageNotification(message, MessageNotificationTitle, MessageNotificationBody);
        tcs.SetResult(message.MessageId);
      } catch (Exception e) {
        tcs.SetException(e);
      }
    }

    void ValidateMessageData(FirebaseMessage message, string key, string expectedValue) {
      if (message.Data == null || !message.Data.ContainsKey(key) ||
          message.Data[key] != expectedValue) {
        throw new Exception("Received message doesn't contain expected data");
      }
    }

    void ValidateMessageNotification(FirebaseMessage message, string expectedTitle,
        string expectedBody) {
      if (message.Notification == null || message.Notification.Title != expectedTitle ||
          MessageNotificationBody != expectedBody) {
        throw new Exception("Received message doesn't contain expected notification");
      }
    }
  }
}
