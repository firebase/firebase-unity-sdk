namespace Firebase.Sample.Messaging {
  using Firebase.Extensions;
  using Firebase.Messaging;
  using System;
  using System.Collections;
  using System.Text.RegularExpressions;
  using System.Threading.Tasks;
  using UnityEngine;
  using UnityEngine.Networking;

  public class UIHandlerAutomated : UIHandler {
    private Firebase.Sample.AutomatedTestRunner testRunner;

    private const string TestTopic = "TestTopic";
    private const string ServerKey = "REPLACE_WITH_YOUR_SERVER_KEY";
    private const string FirebaseBackendUrl = "REPLACE_WITH_YOUR_BACKEND_URL";

    private const string MessageFoo = "This is a test message";
    private const string MessageBar = "It contains some data";
    private const string MessageSpam = "This is a another test message";
    private const string MessageEggs = "It also contains some data";

    private const string MessageNotificationTitle = "JSON message!";
    private const string MessageNotificationBody = "This notification has a body!";
    private const string JsonMessageNotification = "\"notification\":{\"title\":\"" +
      MessageNotificationTitle + "\",\"body\":\"" + MessageNotificationBody + "\"}";

    private const string PlaintextMessage = "data.foo=" + MessageFoo + "&data.bar=" + MessageBar;
    private const string JsonMessageA = "{\"data\":{\"spam\":\"" + MessageSpam + "\", " +
        "\"eggs\":\"" + MessageEggs + "\"}," + JsonMessageNotification + "}";
    private const string JsonMessageB = "{\"data\":{\"foo\":\"" + MessageFoo + "\", " +
        "\"bar\":\"" + MessageBar + "\"}," + JsonMessageNotification + "}";

    private string registrationToken;
    private FirebaseMessage lastReceivedMessage;

    protected override void Start() {
      Func<Task>[] tests = {
        // Disable these tests on desktop, as desktop never receives a token, and so WaitForToken
        // (called by all of these tests) stalls forever.
#if (UNITY_IOS || UNITY_ANDROID)
        MakeTest(TestWaitForToken),
#if !UNITY_IOS
        // TODO(b/130674454) This test times out on iOS, disabling until fixed.
        MakeTest(TestSendPlaintextMessageToDevice),
#endif // !UNITY_IOS
        MakeTest(TestSendJsonMessageToDevice),
        MakeTest(TestSendJsonMessageToSubscribedTopic),
#else  // (UNITY_IOS || UNITY_ANDROID)
        // Run a vacuous test. Should be removed if/when desktop platforms get a real test.
        MakeTest(TestDummy),
#endif // (UNITY_IOS || UNITY_ANDROID)
        // TODO(varconst): a more involved test to check that resubscribing works
        MakeTest(TestGetTokenAsync),
        MakeTest(TestDeleteTokenAsync),
      };
      testRunner = AutomatedTestRunner.CreateTestRunner(
        testsToRun: tests,
        logFunc: DebugLog
      );

      base.Start();
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

    // Waits until the app is given a registration token, expected shortly after startup.
    IEnumerator TestWaitForToken(TaskCompletionSource<string> tcs) {
      yield return StartCoroutine(WaitForToken());
      tcs.SetResult(registrationToken);
    }

    // Blocks until registrationToken is non-empty. Trying to send a message without a registration
    // token will fail.
    IEnumerator WaitForToken() {
      while (String.IsNullOrEmpty(registrationToken)) {
        yield return new WaitForSeconds(0.5f);
      }
    }

    // Sends a plaintext message to the server, setting this device as the addressee, waits until the
    // app receives the message and verifies the contents are the same as were sent.
    IEnumerator TestSendPlaintextMessageToDevice(TaskCompletionSource<string> tcs) {
      yield return StartCoroutine(WaitForToken());
      SendPlaintextMessageToDeviceAsync(PlaintextMessage, registrationToken);
      // TODO(b/65218400): check message id.
      while (lastReceivedMessage == null) {
        yield return new WaitForSeconds(0.5f);
      }
      ValidatePlaintextMessage(tcs, lastReceivedMessage);
      lastReceivedMessage = null;
    }

    // Sends a JSON message to the server, setting this device as the addressee, waits until the app
    // receives the message and verifies the contents are the same as were sent.
    IEnumerator TestSendJsonMessageToDevice(TaskCompletionSource<string> tcs) {
      yield return StartCoroutine(WaitForToken());
      SendJsonMessageToDeviceAsync(JsonMessageA, registrationToken);
      // TODO(b/65218400): check message id.
      while (lastReceivedMessage == null) {
        yield return new WaitForSeconds(0.5f);
      }
      ValidateJsonMessageA(tcs, lastReceivedMessage);
      lastReceivedMessage = null;
    }

    // Sends a JSON message to the server, specifying a topic to which this device is subscribed,
    // waits until the app receives the message and verifies the contents are the same as were sent.
    IEnumerator TestSendJsonMessageToSubscribedTopic(TaskCompletionSource<string> tcs) {
      yield return StartCoroutine(WaitForToken());
      SendJsonMessageToTopicAsync(JsonMessageB, TestTopic);
      // TODO(b/65218400): check message id.
      while (lastReceivedMessage == null) {
        yield return new WaitForSeconds(0.5f);
      }
      ValidateJsonMessageB(tcs, lastReceivedMessage);
      lastReceivedMessage = null;
    }

    // Fake test (always passes immediately). Can be used on platforms with no other tests.
    IEnumerator TestDummy(TaskCompletionSource<string> tcs) {
      tcs.SetResult("Dummy test completed.");
      yield break;
    }

    // Test GetTokenAsync
    IEnumerator TestGetTokenAsync(TaskCompletionSource<string> tcs) {
      yield return StartCoroutine(WaitForToken());
      FirebaseMessaging.GetTokenAsync().ContinueWithOnMainThread(task => {
        tcs.SetResult(task.Result);
      });

    }
    // Test DeleteTokenAsync
    IEnumerator TestDeleteTokenAsync(TaskCompletionSource<string> tcs) {
      yield return StartCoroutine(WaitForToken());
      FirebaseMessaging.DeleteTokenAsync().ContinueWithOnMainThread(task => {
        tcs.SetResult("DeleteTokenAsync completed");
      });
    }

    // Sends the given message to targetDevice in plaintext format and gives back the message id iff
    // the message was sent successfully.
    Task<string> SendPlaintextMessageToDeviceAsync(string message, string targetDevice) {
      var payload = "registration_id=" + targetDevice + "&" + message;
      var request = CreateSendMessageRequest(payload);
      // Though Firebase docs state that if content type is not specified, it defaults to plaintext,
      // server actually returns an error without the following line. This likely has something to do
      // with the way Unity formats the request.
      request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded;charset=UTF-8");
      return DeliverMessageAsync(request);
    }

    // Sends the given message to targetDevice in JSON format and gives back the message id iff the
    // message was sent successfully.
    Task<string> SendJsonMessageToDeviceAsync(string message, string targetDevice) {
      var payload = AddTargetToJsonMessage(message, targetDevice);
      var request = CreateSendMessageRequest(payload);
      request.SetRequestHeader("Content-Type", "application/json");
      return DeliverMessageAsync(request);
    }

    // Sends the given message to the topic in JSON format and gives back the message id iff the
    // message was sent successfully.
    Task<string> SendJsonMessageToTopicAsync(string message, string topic) {
      var payload = AddTargetToJsonMessage(message, "/topics/" + topic);
      var request = CreateSendMessageRequest(payload);
      request.SetRequestHeader("Content-Type", "application/json");
      return DeliverMessageAsync(request);
    }

    // Inserts "to" field into the given JSON string.
    string AddTargetToJsonMessage(string message, string target) {
      return message.Insert(message.IndexOf('{') + 1, "\"to\":\"" + target + "\", ");
    }

    // Creates a POST request to FCM server with proper authentication.
    UnityWebRequest CreateSendMessageRequest(string message) {
      // UnityWebRequest.Post unavoidably applies URL encoding to the payload, which leads to Firebase
      // server rejecting the resulting garbled JSON. Unfortunately, there is no way to turn it off.
      // The workaround is instead to create a PUT request instead (which is not encoded) and then
      // change method to POST.
      // See this discussion for reference:
      // https://forum.unity3d.com/threads/unitywebrequest-post-url-jsondata-sending-broken-json.414708/#post-2719900
      var request = UnityWebRequest.Put(FirebaseBackendUrl, message);
      request.method = "POST";

      request.SetRequestHeader("Authorization", String.Format("key={0}", ServerKey));

      return request;
    }

    Task<string> DeliverMessageAsync(UnityWebRequest request) {
      var tcs = new TaskCompletionSource<string>();
      StartCoroutine(DeliverMessageCoroutine(request, tcs));
      return tcs.Task;
    }

    // Sends the given POST request and gives back the message id iff the message was sent
    // successfully.
    IEnumerator DeliverMessageCoroutine(UnityWebRequest request, TaskCompletionSource<string> tcs) {
      yield return request.Send();

#if UNITY_5
    if (request.isError) {
#else
      // After Unity 2017, the UnityWebRequest API changed isError property to isNetworkError for
      // system errors, while isHttpError and responseCode is used for server return code such as
      // 404/Not Found and 500/Internal Server Error.
      if (request.isNetworkError) {
#endif
        DebugLog("The server responded with an error: " + request.error);
        tcs.TrySetException(new Exception(request.error));
      }

      DebugLog("Server response code: " + request.responseCode.ToString());
      DebugLog("Server response contents: " + request.downloadHandler.text);

      // Extract message ID from server response. Unfortunately, there are 3 possible response
      // formats.
      var messageIdCaptureGroup = "([0-9a-f:%]+)";
      // JSON format
      var messageIdMatch = Regex.Match(request.downloadHandler.text, "\"message_id\":\"" +
          messageIdCaptureGroup + "\"");
      // When sending to a topic, a different response format is used, try that.
      if (!messageIdMatch.Success) {
        messageIdMatch = Regex.Match(request.downloadHandler.text, "\"message_id\":" +
          messageIdCaptureGroup);
      }
      if (!messageIdMatch.Success) {
        // Try plaintext format
        messageIdMatch = Regex.Match(request.downloadHandler.text, "id=" + messageIdCaptureGroup);
      }
      if (messageIdMatch.Success) {
        tcs.TrySetResult(messageIdMatch.Groups[1].Value);
      } else {
        tcs.TrySetException(new Exception("Server response doesn't contain message id: " +
              request.downloadHandler.text));
      }
    }

    void ValidatePlaintextMessage(TaskCompletionSource<string> tcs, FirebaseMessage message) {
      try {
        ValidateMessageData(message, "foo", MessageFoo);
        ValidateMessageData(message, "bar", MessageBar);
        tcs.SetResult(message.MessageId);
      } catch (Exception e) {
        tcs.SetException(e);
      }
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
