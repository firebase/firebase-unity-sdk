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

namespace Firebase.Sample.FirebaseAI {
  using Firebase;
  using Firebase.AI;
  using Firebase.Extensions;
  using Firebase.AppCheck;
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Text;
  using UnityEngine;

  // Handler for UI buttons on the scene.  Also performs some
  // necessary setup (initializing the firebase app, etc) on
  // startup.
  public class UIHandler : MonoBehaviour {
    private const int kMaxLogSize = 16382;

    public GUISkin fb_GUISkin;
    private Vector2 controlsScrollViewVector = Vector2.zero;
    private string logText = "";
    private Vector2 scrollViewVector = Vector2.zero;
    protected bool UIEnabled = false;
    private float textAreaLineHeight;

    private DependencyStatus dependencyStatus = DependencyStatus.UnavailableOther;
    protected bool isFirebaseInitializationComplete = false;

    private string appCheckDebugToken = "REPLACE_WITH_APP_CHECK_TOKEN";

    protected virtual void InitializeAppCheck() {
      DebugLog("Initializing App Check directly in manual UIHandler");
      DebugAppCheckProviderFactory.Instance.SetDebugToken(appCheckDebugToken);
      FirebaseAppCheck.SetAppCheckProviderFactory(DebugAppCheckProviderFactory.Instance);
    }

    protected virtual void Start() {
      InitializeAppCheck();
      InitializeFirebase();
    }

    protected void InitializeFirebase() {
      FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
      {
        dependencyStatus = task.Result;
        if (dependencyStatus == DependencyStatus.Available)
        {
          DebugLog("Firebase Ready: " + FirebaseApp.DefaultInstance);
          UIEnabled = true;
        }
        else
        {
          Debug.LogError(
            "Could not resolve all Firebase dependencies: " + dependencyStatus);
        }
        isFirebaseInitializationComplete = true;
      });
    }

    public string ModelName = "gemini-3.1-flash-lite";
    public string LiveModelName = "gemini-3.8-live";
    public string LiveAgentPlatformLocation = "us-central1";

    private int backendSelection = 0;
    private string[] backendChoices = new string[] { "Google AI Backend", "Agent Platform Backend" };

    private LiveSession liveSession = null;
    private CancellationTokenSource liveSessionCts = null;
    private bool isConnectingLiveSession = false;
    private bool isSimulatingSpeech = false;
    private string voiceActivityStatus = "Idle";
    private int activityStartCount = 0;
    private int activityEndCount = 0;

    private const int kLiveMicSampleRate = 16000;
    private const float kMicPollIntervalSeconds = 0.1f;
    private bool isStreamingMic = false;
    private AudioClip micClip = null;
#if !UNITY_TVOS
    private string activeMicDevice = null;
    private int lastMicSamplePosition = 0;
#endif
    private float nextMicPollTime = 0.0f;

    private GenerativeModel GetModel() {
      var backend = backendSelection == 0
          ? FirebaseAI.Backend.GoogleAI()
          : FirebaseAI.Backend.AgentPlatform();

      return FirebaseAI.GetInstance(backend, useLimitedUseAppCheckTokens: true).GetGenerativeModel(ModelName);
    }

    private FirebaseAI GetLiveFirebaseAI() {
      var backend = backendSelection == 0
          ? FirebaseAI.Backend.GoogleAI()
          : FirebaseAI.Backend.AgentPlatform(
              string.IsNullOrWhiteSpace(LiveAgentPlatformLocation) ? "us-central1" : LiveAgentPlatformLocation.Trim());

      return FirebaseAI.GetInstance(backend, useLimitedUseAppCheckTokens: true);
    }

    private LiveGenerativeModel GetLiveModel() {
      return GetLiveFirebaseAI().GetLiveModel(
          LiveModelName,
          liveGenerationConfig: new LiveGenerationConfig(
              responseModalities: new[] { ResponseModality.Audio },
              inputAudioTranscription: new AudioTranscriptionConfig(),
              outputAudioTranscription: new AudioTranscriptionConfig()));
    }

    // Send a single message to the Generative Model, without any history.
    async Task SendSingleMessage(string message) {
      try
      {
        DebugLog("Sending message to model: " + message);
        var response = await GetModel().GenerateContentAsync(message);
        DebugLog("Response: " + response.Text);
        LogUsageMetadata(response.UsageMetadata);
      }
      catch (Exception e)
      {
        DebugLog("Error while generating content:\n" + e);
      }
    }

    private Chat chatSession = null;
    void StartChatSession() {
      chatSession = GetModel().StartChat();
    }

    void CloseChatSession() {
      chatSession = null;
    }

    // Send a message to the ongoing Chat with the Generative Model, which
    // will preserve the history.
    async Task SendChatMessage(string message) {
      if (chatSession == null) {
        DebugLog("Missing Chat Session");
        return;
      }

      try
      {
        DebugLog("Sending chat message: " + message);
        var response = await chatSession.SendMessageAsync(message);
        DebugLog("Chat response: " + response.Text);
        LogUsageMetadata(response.UsageMetadata);
      }
      catch (Exception e)
      {
        DebugLog("Error while generating content:\n" + e);
      }
    }

    private void LogUsageMetadata(UsageMetadata? metadata) {
      if (metadata == null) return;

      var m = metadata.Value;
      DebugLog($"UsageMetadata: Prompt: {m.PromptTokenCount}, Candidates: {m.CandidatesTokenCount}, Total: {m.TotalTokenCount}, Cached: {m.CachedContentTokenCount}");
      foreach (var detail in m.CacheTokensDetails) {
          DebugLog($"    {detail.Modality}: {detail.TokenCount}");
      }
    }

    async Task StartLiveSessionAsync() {
      if (liveSession != null || isConnectingLiveSession) {
        return;
      }

      isConnectingLiveSession = true;
      voiceActivityStatus = "Connecting...";
      activityStartCount = 0;
      activityEndCount = 0;
      liveSessionCts = new CancellationTokenSource();

      try {
        DebugLog($"Connecting LiveSession to '{LiveModelName}'...");
        liveSession = await GetLiveModel().ConnectAsync(cancellationToken: liveSessionCts.Token);
        voiceActivityStatus = "Idle (listening for voice activity)";
        DebugLog("LiveSession connected. Start mic streaming or click 'Simulate Speech Turn' to test VoiceActivity.");
        _ = ReceiveLiveResponsesLoopAsync(liveSession, liveSessionCts.Token);
      } catch (Exception e) {
        DebugLog("Error connecting LiveSession:\n" + e);
        await CloseLiveSessionAsync();
      } finally {
        isConnectingLiveSession = false;
      }
    }

    async Task ReceiveLiveResponsesLoopAsync(LiveSession session, CancellationToken cancellationToken) {
      try {
        while (!cancellationToken.IsCancellationRequested && liveSession == session) {
          bool receivedAnyInTurn = false;
          await foreach (var response in session.ReceiveAsync(cancellationToken)) {
            receivedAnyInTurn = true;
            HandleLiveSessionResponse(response);
          }

          if (!receivedAnyInTurn) {
            DebugLog("LiveSession stream closed by server.");
            break;
          }
        }
      } catch (OperationCanceledException) {
        // Expected on manual close.
      } catch (Exception e) {
        if (!cancellationToken.IsCancellationRequested) {
          DebugLog("LiveSession receive loop error:\n" + e);
        }
      } finally {
        if (liveSession == session) {
          await CloseLiveSessionAsync();
        }
      }
    }

    private void HandleLiveSessionResponse(LiveSessionResponse response) {
      if (response.Message is LiveSessionVoiceActivity voiceActivity) {
        string offsetText = voiceActivity.AudioOffset.HasValue
            ? $"{voiceActivity.AudioOffset.Value.TotalSeconds:F3}s ({voiceActivity.AudioOffset.Value})"
            : "null";

        switch (voiceActivity.Type) {
          case LiveSessionVoiceActivity.VoiceActivityType.ActivityStart:
            activityStartCount++;
            voiceActivityStatus = $"SPEAKING (ActivityStart #{activityStartCount} @ {offsetText})";
            DebugLog($"[VoiceActivity] User started speaking (ActivityStart) at AudioOffset={offsetText}");
            break;
          case LiveSessionVoiceActivity.VoiceActivityType.ActivityEnd:
            activityEndCount++;
            voiceActivityStatus = $"SILENT (ActivityEnd #{activityEndCount} @ {offsetText})";
            DebugLog($"[VoiceActivity] User stopped speaking (ActivityEnd) at AudioOffset={offsetText}");
            break;
          default:
            voiceActivityStatus = $"UNSPECIFIED ({voiceActivity.Type} @ {offsetText})";
            DebugLog($"[VoiceActivity] Unspecified signal ({voiceActivity.Type}) at AudioOffset={offsetText}");
            break;
        }
      } else if (response.Message is LiveSessionContent content) {
        if (content.InputTranscription.HasValue &&
            !string.IsNullOrEmpty(content.InputTranscription.Value.Text)) {
          DebugLog($"[Live InputTranscription] {content.InputTranscription.Value.Text}");
        }
        if (content.OutputTranscription.HasValue &&
            !string.IsNullOrEmpty(content.OutputTranscription.Value.Text)) {
          DebugLog($"[Live OutputTranscription] {content.OutputTranscription.Value.Text}");
        }
        if (!string.IsNullOrEmpty(response.Text)) {
          DebugLog($"[Live Text] {response.Text}");
        }
        if (content.Interrupted) {
          DebugLog("[Live Content] Model generation interrupted (barge-in).");
        }
        if (content.TurnComplete) {
          DebugLog("[Live Content] TurnComplete.");
        }
      } else if (response.Message is LiveSessionGoingAway goingAway) {
        DebugLog($"[Live GoingAway] Server disconnecting in {goingAway.TimeLeft}.");
      }
    }

    async Task SendLiveTextMessageAsync(string message) {
      if (liveSession == null || string.IsNullOrWhiteSpace(message)) {
        return;
      }

      try {
        DebugLog($"Sending Live text message: {message}");
        var token = liveSessionCts != null ? liveSessionCts.Token : CancellationToken.None;
        await liveSession.SendAsync(ModelContent.Text(message), turnComplete: true, cancellationToken: token);
      } catch (Exception e) {
        DebugLog("Error sending Live text message:\n" + e);
      }
    }

    void ToggleMicStreaming() {
      if (isStreamingMic) {
        StopMicStreaming();
      } else {
        StartMicStreaming();
      }
    }

    void StartMicStreaming() {
      if (liveSession == null || isStreamingMic) {
        return;
      }
#if !UNITY_TVOS
      if (Microphone.devices == null || Microphone.devices.Length == 0) {
        DebugLog("No microphone devices detected. Use 'Simulate Speech Turn' to test VoiceActivity without a mic.");
        return;
      }

      activeMicDevice = Microphone.devices[0];
      micClip = Microphone.Start(activeMicDevice, true, 10, kLiveMicSampleRate);
      if (micClip == null) {
        DebugLog($"Failed to start microphone '{activeMicDevice}'.");
        return;
      }

      lastMicSamplePosition = 0;
      nextMicPollTime = Time.unscaledTime + kMicPollIntervalSeconds;
      isStreamingMic = true;
      DebugLog($"Started 16kHz microphone streaming on '{activeMicDevice}'. Speak to trigger ActivityStart / ActivityEnd.");
#else
      DebugLog("Microphone capture is not supported on this platform. Use 'Simulate Speech Turn' instead.");
#endif
    }

    void StopMicStreaming() {
      if (!isStreamingMic) {
        return;
      }
#if !UNITY_TVOS
      if (!string.IsNullOrEmpty(activeMicDevice) && Microphone.IsRecording(activeMicDevice)) {
        Microphone.End(activeMicDevice);
      }
      activeMicDevice = null;
#endif
      isStreamingMic = false;
      micClip = null;
      DebugLog("Stopped microphone streaming.");
    }

    private void PollAndSendMicAudio() {
      if (!isStreamingMic || liveSession == null || micClip == null || Time.unscaledTime < nextMicPollTime) {
        return;
      }
      nextMicPollTime = Time.unscaledTime + kMicPollIntervalSeconds;

#if !UNITY_TVOS
      int currentPos = Microphone.GetPosition(activeMicDevice);
      if (currentPos < 0 || currentPos == lastMicSamplePosition) {
        return;
      }

      int totalSamples = micClip.samples;
      int sampleCount = currentPos >= lastMicSamplePosition
          ? currentPos - lastMicSamplePosition
          : (totalSamples - lastMicSamplePosition) + currentPos;

      if (sampleCount <= 0) {
        return;
      }

      float[] samples = new float[sampleCount * micClip.channels];
      micClip.GetData(samples, lastMicSamplePosition);
      lastMicSamplePosition = currentPos;

      float[] monoSamples = samples;
      if (micClip.channels > 1) {
        monoSamples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++) {
          float sum = 0f;
          for (int ch = 0; ch < micClip.channels; ch++) {
            sum += samples[i * micClip.channels + ch];
          }
          monoSamples[i] = sum / micClip.channels;
        }
      }

      var token = liveSessionCts != null ? liveSessionCts.Token : CancellationToken.None;
      _ = SendAudioChunkSafeAsync(monoSamples, token);
#endif
    }

    private async Task SendAudioChunkSafeAsync(float[] monoSamples, CancellationToken cancellationToken) {
      try {
        if (liveSession != null && !cancellationToken.IsCancellationRequested) {
          await liveSession.SendAudioAsync(monoSamples, cancellationToken);
        }
      } catch (OperationCanceledException) {
      } catch (Exception e) {
        if (!cancellationToken.IsCancellationRequested) {
          DebugLog("Error streaming audio chunk:\n" + e);
          StopMicStreaming();
        }
      }
    }

    private static float[] ResampleLinear(float[] input, int inRate, int outRate) {
      int outLen = (int)((long)input.Length * outRate / inRate);
      float[] output = new float[outLen];
      double ratio = (double)(input.Length - 1) / Math.Max(1, outLen - 1);
      for (int i = 0; i < outLen; i++) {
        double srcIdx = i * ratio;
        int idx0 = (int)srcIdx;
        int idx1 = Math.Min(idx0 + 1, input.Length - 1);
        float frac = (float)(srcIdx - idx0);
        output[i] = input[idx0] * (1f - frac) + input[idx1] * frac;
      }
      return output;
    }

    // Synthesizes a short spoken utterance via a helper LiveSession and streams it as 16kHz PCM
    // (padded with leading/trailing silence) into the active LiveSession so server VAD emits
    // ActivityStart and ActivityEnd even when testing without a physical microphone.
    async Task SimulateSpeechTurnAsync() {
      if (liveSession == null || isSimulatingSpeech) {
        return;
      }

      isSimulatingSpeech = true;
      var token = liveSessionCts != null ? liveSessionCts.Token : CancellationToken.None;
      try {
        DebugLog("Generating spoken sample audio to feed into LiveSession VAD...");
        List<float> samples24k = new List<float>();
        using (var synthSession = await GetLiveModel().ConnectAsync(cancellationToken: token)) {
          await synthSession.SendAsync(
              ModelContent.Text("Please say clearly in one short sentence: Hello Gemini, testing voice activity one two three."),
              turnComplete: true,
              cancellationToken: token);

          await foreach (var resp in synthSession.ReceiveAsync(token)) {
            if (resp.AudioAsFloat != null) {
              foreach (var chunk in resp.AudioAsFloat) {
                samples24k.AddRange(chunk);
              }
            }
          }
          await synthSession.CloseAsync(CancellationToken.None);
        }

        if (samples24k.Count == 0) {
          DebugLog("No audio returned when synthesizing sample utterance.");
          return;
        }

        float[] speech16k = ResampleLinear(samples24k.ToArray(), 24000, kLiveMicSampleRate);
        List<float> streamSamples = new List<float>(kLiveMicSampleRate * 4 + speech16k.Length);
        streamSamples.AddRange(new float[kLiveMicSampleRate]); // 1s leading silence
        streamSamples.AddRange(speech16k);
        streamSamples.AddRange(new float[kLiveMicSampleRate * 3]); // 3s trailing silence

        DebugLog($"Streaming {streamSamples.Count / (float)kLiveMicSampleRate:F1}s of 16kHz audio (1s silence + speech + 3s silence)...");
        const int chunkSize = 1600; // 100ms @ 16kHz
        for (int offset = 0; offset < streamSamples.Count && liveSession != null && !token.IsCancellationRequested; offset += chunkSize) {
          int count = Math.Min(chunkSize, streamSamples.Count - offset);
          float[] chunk = streamSamples.GetRange(offset, count).ToArray();
          await liveSession.SendAudioAsync(chunk, token);
          await Task.Delay(50, token);
        }
        DebugLog("Finished streaming simulated speech audio.");
      } catch (OperationCanceledException) {
      } catch (Exception e) {
        DebugLog("Error simulating speech turn:\n" + e);
      } finally {
        isSimulatingSpeech = false;
      }
    }

    async Task CloseLiveSessionAsync() {
      StopMicStreaming();

      var cts = liveSessionCts;
      liveSessionCts = null;
      if (cts != null) {
        cts.Cancel();
        cts.Dispose();
      }

      var session = liveSession;
      liveSession = null;
      voiceActivityStatus = "Idle";

      if (session != null) {
        try {
          await session.CloseAsync(CancellationToken.None);
        } catch (Exception) {
          // Ignore close errors during teardown.
        } finally {
          session.Dispose();
        }
        DebugLog("LiveSession closed.");
      }
    }

    protected virtual void OnDestroy() {
      StopMicStreaming();
      if (liveSessionCts != null) {
        liveSessionCts.Cancel();
        liveSessionCts.Dispose();
        liveSessionCts = null;
      }
      if (liveSession != null) {
        liveSession.Dispose();
        liveSession = null;
      }
    }

    // Exit if escape (or back, on mobile) is pressed.
    protected virtual void Update() {
      if (Input.GetKeyDown(KeyCode.Escape)) {
        Application.Quit();
      }
      PollAndSendMicAudio();
    }

    // Output text to the debug log text field, as well as the console.
    public void DebugLog(string s) {
      Debug.Log(s);
      logText += s + "\n";

      while (logText.Length > kMaxLogSize) {
        int index = logText.IndexOf("\n");
        logText = logText.Substring(index + 1);
      }

      scrollViewVector.y = int.MaxValue;
    }

    // Render the log output in a scroll view.
    void GUIDisplayLog() {
      scrollViewVector = GUILayout.BeginScrollView(scrollViewVector);
      GUILayout.Label(logText);
      GUILayout.EndScrollView();
    }

    private string textfieldString = "Hello";

    // Render the buttons and other controls.
    void GUIDisplayControls() {
      if (UIEnabled) {
        controlsScrollViewVector = GUILayout.BeginScrollView(controlsScrollViewVector);

        GUILayout.BeginVertical();

        if (chatSession == null && liveSession == null) {
          backendSelection = GUILayout.SelectionGrid(backendSelection, backendChoices, backendChoices.Length);

          textfieldString = GUILayout.TextField(textfieldString);

          if (GUILayout.Button("Send Single Message")) {
            _ = SendSingleMessage(textfieldString);
          }

          if (GUILayout.Button("Start Chat Session")) {
            StartChatSession();
          }

          GUILayout.Space(10);
          GUILayout.Label("Live API Model:");
          LiveModelName = GUILayout.TextField(LiveModelName);
          if (backendSelection == 1) {
            GUILayout.Label("Agent Platform Location (Live API):");
            LiveAgentPlatformLocation = GUILayout.TextField(LiveAgentPlatformLocation);
          }

          GUI.enabled = !isConnectingLiveSession;
          if (GUILayout.Button(isConnectingLiveSession ? "Connecting Live Session..." : "Start Live Session")) {
            _ = StartLiveSessionAsync();
          }
          GUI.enabled = true;
        } else if (chatSession != null) {
          textfieldString = GUILayout.TextField(textfieldString);

          if (GUILayout.Button("Send Chat Message")) {
            _ = SendChatMessage(textfieldString);
          }

          if (GUILayout.Button("Close Chat Session")) {
            CloseChatSession();
          }
        } else if (liveSession != null) {
          GUILayout.Label($"Live Model: {LiveModelName}");
          GUILayout.Label($"Voice Activity: {voiceActivityStatus}");
          GUILayout.Label($"Counts: Start={activityStartCount}, End={activityEndCount}");

          if (GUILayout.Button(isStreamingMic ? "Stop Mic Streaming" : "Start Mic Streaming (16kHz)")) {
            ToggleMicStreaming();
          }

          GUI.enabled = !isSimulatingSpeech;
          if (GUILayout.Button(isSimulatingSpeech ? "Simulating Speech Turn..." : "Simulate Speech Turn (No Mic Needed)")) {
            _ = SimulateSpeechTurnAsync();
          }
          GUI.enabled = true;

          textfieldString = GUILayout.TextField(textfieldString);
          if (GUILayout.Button("Send Live Text Message")) {
            _ = SendLiveTextMessageAsync(textfieldString);
          }

          if (GUILayout.Button("Close Live Session")) {
            _ = CloseLiveSessionAsync();
          }
        }

        GUILayout.EndVertical();
        GUILayout.EndScrollView();
      }
    }

    // Render the GUI:
    void OnGUI() {
      GUI.skin = fb_GUISkin;

      GUI.skin.textArea.fontSize = GUI.skin.textField.fontSize;
      // Reduce the text size on the desktop.
      if (UnityEngine.Application.platform != RuntimePlatform.Android &&
          UnityEngine.Application.platform != RuntimePlatform.IPhonePlayer) {
        var fontSize = GUI.skin.textArea.fontSize / 4;
        GUI.skin.textArea.fontSize = fontSize;
        GUI.skin.button.fontSize = fontSize;
        GUI.skin.label.fontSize = fontSize;
        GUI.skin.textField.fontSize = fontSize;
      }
      GUI.skin.textArea.stretchHeight = true;
      // Calculate the height of line of text in a text area.
      if (textAreaLineHeight == 0.0f) {
        textAreaLineHeight = GUI.skin.textArea.CalcSize(new GUIContent("Hello World")).y;
      }

      Rect logArea, controlArea;

      if (Screen.width < Screen.height) {
        // Portrait mode
        controlArea = new Rect(0.0f, 0.0f, Screen.width, Screen.height * 0.5f);
        logArea = new Rect(0.0f, Screen.height * 0.5f, Screen.width, Screen.height * 0.5f);
      } else {
        // Landscape mode
        controlArea = new Rect(0.0f, 0.0f, Screen.width * 0.5f, Screen.height);
        logArea = new Rect(Screen.width * 0.5f, 0.0f, Screen.width * 0.5f, Screen.height);
      }

      GUILayout.BeginArea(logArea);
      GUIDisplayLog();
      GUILayout.EndArea();

      GUILayout.BeginArea(controlArea);
      GUIDisplayControls();
      GUILayout.EndArea();
    }
  }
}
