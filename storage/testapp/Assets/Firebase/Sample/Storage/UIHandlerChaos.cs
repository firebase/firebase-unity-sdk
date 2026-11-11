// Copyright 2026 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Firebase.Sample.Storage {
  using Firebase;
  using Firebase.Storage;
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Net;
  using System.Net.Http;
  using System.Security.Cryptography;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using UnityEngine;

  // Network-chaos scenarios for the Storage engine. Enabled only when the FIREBASE_STORAGE_CHAOS
  // environment variable is set (a comma separated list of scenario ids, or "all"); they replace
  // the regular test list. The player is expected to be behind a chaos HTTP CONNECT proxy whose
  // control endpoint is FIREBASE_STORAGE_CHAOS_CONTROL (default http://127.0.0.1:8889); the
  // scenarios drive that proxy themselves (black-hole, reset, throttle...) at exact transfer
  // offsets, so they are repeatable regardless of network speed.
  //
  // Environment:
  //   FIREBASE_STORAGE_CHAOS          scenario ids ("S1,S2") or "all"
  //   FIREBASE_STORAGE_PROXY          e.g. http://127.0.0.1:8888 (installed as WebRequest.DefaultWebProxy)
  //   FIREBASE_STORAGE_CHAOS_CONTROL  proxy control URL (default http://127.0.0.1:8889)
  //   FIREBASE_STORAGE_EMULATOR       host:port of a Storage emulator to target instead of prod
  //   FIREBASE_STORAGE_CHAOS_MB       size of the "large" payload in MiB (default 64)
  //   FIREBASE_STORAGE_CHAOS_SOAK     iterations for S20 (default 100)
  //
  // Every measurement is logged as a single line "CHAOS|<scenario>|key=value|key=value..." so a
  // report can be assembled from the player log.
  public partial class UIHandlerAutomated {
    private static readonly HttpClient ControlClient =
        new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
    private string chaosControlUrl = "http://127.0.0.1:8889";
    private int chaosLargeMb = 64;
    private int chaosSoakIterations = 100;
    private float chaosMaxFrameGap;
    private int sdkRetryLogLines;
    private int sdkResyncLogLines;
    private int sdkBudgetExhaustedLines;
    private readonly List<string> sdkWarningLines = new List<string>();

    private static string Env(string name) {
      return Environment.GetEnvironmentVariable(name);
    }

    private bool ChaosEnabled {
      get { return !string.IsNullOrEmpty(Env("FIREBASE_STORAGE_CHAOS")); }
    }

    // Called from Start() before Firebase is initialized.
    private void ConfigureChaosEnvironment() {
      string proxy = Env("FIREBASE_STORAGE_PROXY");
      if (!string.IsNullOrEmpty(proxy)) {
        WebRequest.DefaultWebProxy = new WebProxy(new Uri(proxy));
        Debug.Log("CHAOS|env|proxy=" + proxy);
      }
      string control = Env("FIREBASE_STORAGE_CHAOS_CONTROL");
      if (!string.IsNullOrEmpty(control)) {
        chaosControlUrl = control;
      }
      int.TryParse(Env("FIREBASE_STORAGE_CHAOS_MB") ?? "", out int mb);
      if (mb > 0) chaosLargeMb = mb;
      int.TryParse(Env("FIREBASE_STORAGE_CHAOS_SOAK") ?? "", out int soak);
      if (soak > 0) chaosSoakIterations = soak;
      Application.logMessageReceivedThreaded += OnLogLine;
    }

    // Called once FirebaseStorage.DefaultInstance exists.
    private void ConfigureChaosStorage(FirebaseStorage storage) {
      string emulator = Env("FIREBASE_STORAGE_EMULATOR");
      if (!string.IsNullOrEmpty(emulator)) {
        string[] parts = emulator.Split(':');
        storage.UseEmulator(parts[0], int.Parse(parts[1]));
        Debug.Log("CHAOS|env|emulator=" + emulator);
      }
      // Debug output from the SDK is part of the evidence (retry / resync lines).
      storage.LogLevel = LogLevel.Debug;
    }

    private void OnLogLine(string condition, string stackTrace, LogType type) {
      if (condition == null) return;
      if (condition.Contains("retrying in")) Interlocked.Increment(ref sdkRetryLogLines);
      if (condition.Contains("resynchronizing")) Interlocked.Increment(ref sdkResyncLogLines);
      if (condition.Contains("retry budget")) Interlocked.Increment(ref sdkBudgetExhaustedLines);
      if (type == LogType.Warning && condition.Contains("Cloud Storage")) {
        lock (sdkWarningLines) {
          if (sdkWarningLines.Count < 200) sdkWarningLines.Add(condition);
        }
      }
    }

    private void ResetSdkCounters() {
      sdkRetryLogLines = 0;
      sdkResyncLogLines = 0;
      sdkBudgetExhaustedLines = 0;
      lock (sdkWarningLines) sdkWarningLines.Clear();
      chaosMaxFrameGap = 0;
    }

    private void TrackFrameGap() {
      if (Time.unscaledDeltaTime > chaosMaxFrameGap) chaosMaxFrameGap = Time.unscaledDeltaTime;
    }

    private Func<Task>[] ChaosTests() {
      var all = new Dictionary<string, Func<Task>> {
        { "S0", ChaosS0RawHttpBaseline },
        { "S1", ChaosS1ThroughputClean },
        { "S2", ChaosS2UploadBlackout },
        { "S3", ChaosS3UploadResets },
        { "S4", ChaosS4UploadThrottle },
        { "S5", ChaosS5LatencyJitterReorder },
        { "S6", ChaosS6Fragmentation },
        { "S7", ChaosS7BudgetExhaustionThenResume },
#if !FIREBASE_STORAGE_LEGACY
        { "S8", ChaosS8PauseDuringBlackout },
        { "S9", ChaosS9PauseResumeChurn },
        { "S10", ChaosS10CancelDuringBlackout },
        { "S11", ChaosS11AppDisposeMidUpload },
#endif
        { "S12", ChaosS12DownloadBlackout },
#if !FIREBASE_STORAGE_LEGACY
        { "S13", ChaosS13RawStreamResets },
        { "S14", ChaosS14RawStreamDisposeDuringBlackhole },
        { "S15", ChaosS15MainThreadSyncRead },
#endif
        { "S16", ChaosS16ObjectReplacedMidDownload },
        { "S17", ChaosS17MaxDownloadSize },
        { "S18", ChaosS18WriteOnlyRules },
        { "S19", ChaosS19ErrorMapping },
        { "S20", ChaosS20Soak },
      };
      string spec = Env("FIREBASE_STORAGE_CHAOS");
      var selected = new List<Func<Task>>();
      var names = new List<string>();
      foreach (var id in (spec == "all" ? string.Join(",", all.Keys) : spec).Split(',')) {
        string key = id.Trim();
        if (all.TryGetValue(key, out Func<Task> test)) {
          selected.Add(test);
          names.Add(key);
        }
      }
      Debug.Log("CHAOS|env|scenarios=" + string.Join(",", names) + "|large_mb=" + chaosLargeMb);
      return selected.ToArray();
    }

    // ---------------------------------------------------------------- proxy control

    private async Task<string> ControlAsync(string path, string json = "{}") {
      try {
        using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
        using (HttpResponseMessage response = await ControlClient.PostAsync(chaosControlUrl + path, content)) {
          return await response.Content.ReadAsStringAsync();
        }
      } catch (Exception e) {
        Debug.LogWarning("CHAOS|control|path=" + path + "|error=" + e.Message);
        return null;
      }
    }

    private async Task<string> ControlStateAsync() {
      try {
        return await ControlClient.GetStringAsync(chaosControlUrl + "/state");
      } catch (Exception e) {
        return "{\"error\":\"" + e.Message + "\"}";
      }
    }

    private Task SetWeatherAsync(int latencyMs = 0, int jitterMs = 0, long bandwidthBps = 0,
                                 int fragmentBytes = 0, int fragmentGapMs = 0, double reorderProb = 0) {
      return ControlAsync("/set", "{\"latency_ms\":" + latencyMs + ",\"jitter_ms\":" + jitterMs +
          ",\"bandwidth_bps\":" + bandwidthBps + ",\"fragment_bytes\":" + fragmentBytes +
          ",\"fragment_gap_ms\":" + fragmentGapMs + ",\"reorder_prob\":" +
          reorderProb.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
    }

    private Task ClearWeatherAsync() { return SetWeatherAsync(); }
    private Task BlackholeAsync(int seconds) { return ControlAsync("/blackhole", "{\"seconds\":" + seconds + "}"); }
    private Task OfflineAsync(int seconds) { return ControlAsync("/offline", "{\"seconds\":" + seconds + "}"); }
    private Task ResetAllAsync() { return ControlAsync("/reset_all"); }
    private Task ResetCountersAsync() { return ControlAsync("/counters/reset"); }

    private async Task<long> ProxyCounterAsync(string name) {
      string state = await ControlStateAsync();
      int i = state.IndexOf("\"" + name + "\":", StringComparison.Ordinal);
      if (i < 0) return -1;
      i += name.Length + 3;
      while (i < state.Length && state[i] == ' ') i++;
      int j = i;
      while (j < state.Length && (char.IsDigit(state[j]) || state[j] == '-')) j++;
      long.TryParse(state.Substring(i, j - i), out long value);
      return value;
    }

    // ---------------------------------------------------------------- helpers

    private byte[] MakePayload(int seed, long bytes) {
      var payload = new byte[bytes];
      new System.Random(seed).NextBytes(payload);
      return payload;
    }

    private static string Sha256(byte[] data) {
      using (var sha = SHA256.Create()) {
        return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
      }
    }

    private static string Sha256(Stream stream) {
      using (var sha = SHA256.Create()) {
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
      }
    }

    private StorageReference ChaosRef(string name) {
      return FirebaseStorage.DefaultInstance.GetReference("chaos/" + name);
    }

    private void Metric(string scenario, params string[] kv) {
      Debug.Log("CHAOS|" + scenario + "|" + string.Join("|", kv) + "|retries=" + sdkRetryLogLines +
                "|resyncs=" + sdkResyncLogLines + "|budget_exhausted=" + sdkBudgetExhaustedLines +
                "|max_frame_gap_ms=" + (int)(chaosMaxFrameGap * 1000) +
                "|managed_mb=" + (GC.GetTotalMemory(false) >> 20) + "|gc0=" + GC.CollectionCount(0) +
                "|threads=" + System.Diagnostics.Process.GetCurrentProcess().Threads.Count);
    }

    // Tracks progress callbacks: count, monotonicity, thread affinity and last value.
    private sealed class ProgressProbe<T> : IProgress<T> {
      private readonly Func<T, long> bytes;
      private readonly Action<long> onReport;
      private readonly int mainThreadId;
      public long Last = -1;
      public int Count;
      public int Regressions;
      public int OffMainThread;
      public long FirstAtTicks;
      public ProgressProbe(Func<T, long> bytes, int mainThreadId, Action<long> onReport = null) {
        this.bytes = bytes;
        this.mainThreadId = mainThreadId;
        this.onReport = onReport;
      }
      public void Report(T value) {
        long b = bytes(value);
        if (Count == 0) FirstAtTicks = DateTime.UtcNow.Ticks;
        Count++;
        if (b < Last) Regressions++;
        Last = b;
        if (Thread.CurrentThread.ManagedThreadId != mainThreadId) OffMainThread++;
        onReport?.Invoke(b);
      }
    }

    private int MainThreadId => mainThreadDispatcherThreadId;
    private int mainThreadDispatcherThreadId;

    private async Task<string> DownloadAndHashAsync(StorageReference reference, long expectedBytes,
                                                     string scenario) {
      var sw = System.Diagnostics.Stopwatch.StartNew();
      byte[] data = await reference.GetBytesAsync(0);
      sw.Stop();
      AssertEq(scenario + " downloaded length", (long)data.Length, expectedBytes);
      Metric(scenario, "phase=verify_download", "seconds=" + sw.Elapsed.TotalSeconds.ToString("F1"),
             "mbps=" + (data.Length / 1048576.0 / Math.Max(0.001, sw.Elapsed.TotalSeconds)).ToString("F1"));
      return Sha256(data);
    }

    private Task UploadLargeOnceAsync(string name, byte[] payload) {
      return ChaosRef(name).PutBytesAsync(payload);
    }

    // ---------------------------------------------------------------- scenarios

    // S0: what the runtime's own HttpClient manages on this link, through the same proxy, with no
    // Storage engine involved: the ceiling against which S1 should be read.
    async Task ChaosS0RawHttpBaseline() {
      await ClearWeatherAsync();
      long size = chaosLargeMb * 1024L * 1024L;
      byte[] payload = MakePayload(1, size);
      string bucket = FirebaseStorage.DefaultInstance.App.Options.StorageBucket;
      string url = "https://firebasestorage.googleapis.com/v0/b/" + bucket + "/o";
      using (var client = new HttpClient()) {
        client.Timeout = TimeSpan.FromMinutes(10);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using (var content = new ByteArrayContent(payload))
        using (HttpResponseMessage response = await client.PostAsync(url + "?name=chaos%2Fs0.bin&uploadType=media", content)) {
          Assert("S0 raw upload status " + response.StatusCode, response.IsSuccessStatusCode);
        }
        sw.Stop();
        Metric("S0", "phase=raw_upload", "seconds=" + sw.Elapsed.TotalSeconds.ToString("F1"),
               "mbps=" + (size / 1048576.0 / sw.Elapsed.TotalSeconds).ToString("F1"));
        sw.Restart();
        byte[] data = await client.GetByteArrayAsync(url + "/chaos%2Fs0.bin?alt=media");
        sw.Stop();
        AssertEq("S0 raw download length", (long)data.Length, size);
        Metric("S0", "phase=raw_download", "seconds=" + sw.Elapsed.TotalSeconds.ToString("F1"),
               "mbps=" + (size / 1048576.0 / sw.Elapsed.TotalSeconds).ToString("F1"));
        sw.Restart();
        using (HttpResponseMessage response = await client.GetAsync(url + "/chaos%2Fs0.bin?alt=media", HttpCompletionOption.ResponseHeadersRead))
        using (Stream stream = await response.Content.ReadAsStreamAsync()) {
          var buffer = new byte[256 * 1024];
          long total = 0;
          int read;
          while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0) total += read;
          AssertEq("S0 streamed length", total, size);
        }
        sw.Stop();
        Metric("S0", "phase=raw_stream_256k_reads", "seconds=" + sw.Elapsed.TotalSeconds.ToString("F1"),
               "mbps=" + (size / 1048576.0 / sw.Elapsed.TotalSeconds).ToString("F1"));
      }
    }

    // S1: clean 64 MiB round trip with every metric we care about.
    async Task ChaosS1ThroughputClean() {
      ResetSdkCounters();
      await ClearWeatherAsync();
      long size = chaosLargeMb * 1024L * 1024L;
      byte[] payload = MakePayload(1, size);
      string hash = Sha256(payload);
      var probe = new ProgressProbe<UploadState>(s => s.BytesTransferred, MainThreadId);
      long memBefore = GC.GetTotalMemory(true);
      int gcBefore = GC.CollectionCount(0);
      long startTicks = DateTime.UtcNow.Ticks;
      chaosMaxFrameGap = 0;  // measure the transfer, not the payload generation above
      var sw = System.Diagnostics.Stopwatch.StartNew();
      StorageMetadata metadata = await ChaosRef("s1.bin").PutBytesAsync(payload, null, probe);
      sw.Stop();
      long memAfter = GC.GetTotalMemory(false);
      AssertEq("S1 metadata.SizeBytes", metadata.SizeBytes, size);
      AssertEq("S1 final progress", probe.Last, size);
      AssertEq("S1 progress regressions", probe.Regressions, 0);
      Metric("S1", "phase=upload", "callbacks_off_main_thread=" + probe.OffMainThread, "seconds=" + sw.Elapsed.TotalSeconds.ToString("F1"),
             "mbps=" + (size / 1048576.0 / sw.Elapsed.TotalSeconds).ToString("F1"),
             "callbacks=" + probe.Count,
             "ttfp_ms=" + ((probe.FirstAtTicks - startTicks) / 10000),
             "managed_delta_mb=" + ((memAfter - memBefore) >> 20), "gc0_delta=" + (GC.CollectionCount(0) - gcBefore));
      string downloadedHash = await DownloadAndHashAsync(ChaosRef("s1.bin"), size, "S1");
      AssertEq("S1 hash", downloadedHash, hash);
    }

    // S2: 90 s black-hole in the middle of a large upload.
    async Task ChaosS2UploadBlackout() {
      await RunUploadWithEventAsync("S2", 12L * 1024 * 1024, async () => { await BlackholeAsync(90); }, 2);
    }

    // S3: connection reset every 4 s while the upload is moving. The link is capped at 2 MiB/s so
    // a 64 MiB upload spans ~30 s and takes at least six resets.
    async Task ChaosS3UploadResets() {
      await SetWeatherAsync(bandwidthBps: 2 * 1024 * 1024);
      try {
        await RunUploadWithEventAsync("S3", 1024 * 1024, async () => {
          for (int i = 0; i < 8; i++) {
            await Task.Delay(4000);
            await ResetAllAsync();
          }
        }, 3, keepWeather: true);
      } finally {
        await ClearWeatherAsync();
      }
    }

    // S4: 48 KB/s link; the watchdog must never fire while bytes trickle.
    async Task ChaosS4UploadThrottle() {
      ResetSdkCounters();
      await SetWeatherAsync(bandwidthBps: 48 * 1024);
      try {
        await RunUploadAndVerifyAsync("S4", 4L * 1024 * 1024, 4);
        AssertEq("S4 no retries under throttle", sdkRetryLogLines, 0);
      } finally {
        await ClearWeatherAsync();
      }
    }

    // S5: high latency with heavy jitter (which is what reordered packets look like above TCP:
    // bursts, stalls and out-of-order *responses* across connections).
    async Task ChaosS5LatencyJitterReorder() {
      ResetSdkCounters();
      await SetWeatherAsync(latencyMs: 900, jitterMs: 600);
      try {
        await RunUploadAndVerifyAsync("S5", 16L * 1024 * 1024, 5);
      } finally {
        await ClearWeatherAsync();
      }
    }

    // S6: every write fragmented into 512-byte pieces with 2 ms gaps.
    async Task ChaosS6Fragmentation() {
      ResetSdkCounters();
      await SetWeatherAsync(fragmentBytes: 512, fragmentGapMs: 2);
      try {
        await RunUploadAndVerifyAsync("S6", 2L * 1024 * 1024, 6);
      } finally {
        await ClearWeatherAsync();
      }
    }

    // S7: black-hole longer than MaxUploadRetryTime -> clean failure with session URI, then resume
    // from the published session without re-sending committed bytes.
    async Task ChaosS7BudgetExhaustionThenResume() {
      ResetSdkCounters();
      await ClearWeatherAsync();
      var storage = FirebaseStorage.DefaultInstance;
      TimeSpan originalBudget = storage.MaxUploadRetryTime;
      storage.MaxUploadRetryTime = TimeSpan.FromSeconds(20);
      long size = 32L * 1024 * 1024;
      byte[] payload = MakePayload(7, size);
      Uri sessionUri = null;
      long committedAtFailure = 0;
      bool fired = false;
      var probe = new ProgressProbe<UploadState>(s => s.BytesTransferred, MainThreadId);
      var sessionProbe = new StorageProgress<UploadState>(s => {
        probe.Report(s);
        if (s.UploadSessionUri != null) sessionUri = s.UploadSessionUri;
        if (!fired && s.BytesTransferred >= 4L * 1024 * 1024) {
          fired = true;
          committedAtFailure = s.BytesTransferred;
          _ = BlackholeAsync(120);
        }
      });
      var sw = System.Diagnostics.Stopwatch.StartNew();
      Exception failure = null;
      try {
        await ChaosRef("s7.bin").PutBytesAsync(payload, null, sessionProbe);
        Assert("S7 upload should have faulted", false);
      } catch (StorageException e) {
        failure = e;
        Metric("S7", "phase=fault", "seconds=" + sw.Elapsed.TotalSeconds.ToString("F1"),
               "error_code=" + e.ErrorCode, "http=" + e.HttpResultCode, "recoverable=" + e.IsRecoverableException,
               "inner=" + (e.InnerException?.GetType().Name ?? "none"), "message=" + e.Message.Replace('|', '/'));
      } finally {
        storage.MaxUploadRetryTime = originalBudget;
      }
      Assert("S7 faulted with StorageException", failure != null);
      Assert("S7 IsRecoverableException", ((StorageException)failure).IsRecoverableException);
      Assert("S7 session uri published", sessionUri != null);
      Assert("S7 watchdog fired before budget ran out (60s stall + 20s budget < 120s)", sw.Elapsed.TotalSeconds < 115);
      // Wait for the black-hole to lift, then resume from the published session.
      while (sw.Elapsed.TotalSeconds < 125) await Task.Delay(1000);
      await ResetCountersAsync();
      ResetSdkCounters();
      var resumeProbe = new ProgressProbe<UploadState>(s => s.BytesTransferred, MainThreadId);
      var sw2 = System.Diagnostics.Stopwatch.StartNew();
      StorageMetadata metadata = await ChaosRef("s7.bin").PutBytesAsync(payload, null, resumeProbe, default, sessionUri);
      sw2.Stop();
      long bytesUpAfterResume = await ProxyCounterAsync("bytes_up");
      AssertEq("S7 resumed size", metadata.SizeBytes, size);
      Metric("S7", "phase=resume", "seconds=" + sw2.Elapsed.TotalSeconds.ToString("F1"),
             "committed_at_failure=" + committedAtFailure, "proxy_bytes_up_on_resume=" + bytesUpAfterResume,
             "first_resume_progress=" + resumeProbe.Last);
      Assert("S7 resume did not re-send committed bytes",
             bytesUpAfterResume < size - committedAtFailure + 2L * 1024 * 1024);
      string hash = await DownloadAndHashAsync(ChaosRef("s7.bin"), size, "S7");
      AssertEq("S7 hash", hash, Sha256(payload));
    }

#if !FIREBASE_STORAGE_LEGACY
    // S8: Pause() while the link is black-holed; nothing may be sent while paused; no fault.
    async Task ChaosS8PauseDuringBlackout() {
      ResetSdkCounters();
      await ClearWeatherAsync();
      long size = 32L * 1024 * 1024;
      byte[] payload = MakePayload(8, size);
      var controller = new StorageController();
      bool fired = false;
      var probe = new ProgressProbe<UploadState>(s => s.BytesTransferred, MainThreadId, b => {
        if (!fired && b >= 4L * 1024 * 1024) {
          fired = true;
          _ = BlackholeAsync(40);
        }
      });
      var task = ChaosRef("s8.bin").PutBytesAsync(payload, null, probe, default, null, controller);
      while (!fired && !task.IsCompleted) await Task.Delay(100);
      await Task.Delay(2000);
      Assert("S8 Pause() during blackout", controller.Pause());
      long connectsAtPause = await ProxyCounterAsync("connects");
      long progressAtPause = probe.Last;
      // Black-hole lifts at ~40 s; stay paused through 55 s.
      await Task.Delay(53000);
      long connectsWhilePaused = await ProxyCounterAsync("connects") - connectsAtPause;
      Assert("S8 still pending while paused", !task.IsCompleted);
      Assert("S8 IsPaused", controller.IsPaused);
      Metric("S8", "phase=paused", "connects_while_paused=" + connectsWhilePaused, "progress_at_pause=" + progressAtPause);
      Assert("S8 no connections while paused (allow the aborted one)", connectsWhilePaused <= 1);
      Assert("S8 Resume()", controller.Resume());
      StorageMetadata metadata = await task;
      AssertEq("S8 size", metadata.SizeBytes, size);
      AssertEq("S8 hash", await DownloadAndHashAsync(ChaosRef("s8.bin"), size, "S8"), Sha256(payload));
    }

    // S9: pause/resume every 2 s, 20 times, under jitter.
    async Task ChaosS9PauseResumeChurn() {
      ResetSdkCounters();
      await SetWeatherAsync(latencyMs: 200, jitterMs: 200);
      try {
        long size = 16L * 1024 * 1024;
        byte[] payload = MakePayload(9, size);
        var controller = new StorageController();
        var probe = new ProgressProbe<UploadState>(s => s.BytesTransferred, MainThreadId);
        var task = ChaosRef("s9.bin").PutBytesAsync(payload, null, probe, default, null, controller);
        int pauses = 0, resumes = 0, inconsistent = 0;
        for (int i = 0; i < 20 && !task.IsCompleted; i++) {
          if (controller.Pause()) pauses++;
          if (!controller.IsPaused) inconsistent++;
          await Task.Delay(1000);
          if (controller.Resume()) resumes++;
          if (controller.IsPaused) inconsistent++;
          await Task.Delay(1000);
        }
        StorageMetadata metadata = await task;
        AssertEq("S9 size", metadata.SizeBytes, size);
        AssertEq("S9 regressions", probe.Regressions, 0);
        Metric("S9", "pauses=" + pauses, "resumes=" + resumes, "inconsistent=" + inconsistent);
        AssertEq("S9 state consistent", inconsistent, 0);
        AssertEq("S9 hash", await DownloadAndHashAsync(ChaosRef("s9.bin"), size, "S9"), Sha256(payload));
      } finally {
        await ClearWeatherAsync();
      }
    }

    // S10: Cancel() while black-holed must complete the task promptly as Canceled.
    async Task ChaosS10CancelDuringBlackout() {
      ResetSdkCounters();
      await ClearWeatherAsync();
      long size = 32L * 1024 * 1024;
      byte[] payload = MakePayload(10, size);
      var controller = new StorageController();
      bool fired = false;
      var task = ChaosRef("s10.bin").PutBytesAsync(payload, null,
          new StorageProgress<UploadState>(s => {
            if (!fired && s.BytesTransferred >= 4L * 1024 * 1024) {
              fired = true;
              _ = BlackholeAsync(30);
            }
          }), default, null, controller);
      while (!fired && !task.IsCompleted) await Task.Delay(100);
      await Task.Delay(2000);
      var sw = System.Diagnostics.Stopwatch.StartNew();
      Assert("S10 Cancel()", controller.Cancel());
      bool canceled = false;
      try { await task; } catch (OperationCanceledException) { canceled = true; }
      sw.Stop();
      Metric("S10", "cancel_latency_ms=" + sw.ElapsedMilliseconds, "is_canceled=" + task.IsCanceled);
      Assert("S10 canceled", canceled && task.IsCanceled && !task.IsFaulted);
      Assert("S10 cancel latency < 3 s", sw.Elapsed.TotalSeconds < 3);
      await Task.Delay(30000);  // let the black-hole lift before the next scenario
    }

    // S11: disposing the FirebaseApp mid-upload cancels without a session-cancel request.
    async Task ChaosS11AppDisposeMidUpload() {
      ResetSdkCounters();
      await ClearWeatherAsync();
      var options = FirebaseApp.DefaultInstance.Options;
      var app = FirebaseApp.Create(options, "chaos-dispose-" + Guid.NewGuid().ToString("N"));
      var storage = FirebaseStorage.GetInstance(app);
      long size = 32L * 1024 * 1024;
      byte[] payload = MakePayload(11, size);
      bool fired = false;
      var task = storage.GetReference("chaos/s11.bin").PutBytesAsync(payload, null,
          new StorageProgress<UploadState>(s => {
            if (!fired && s.BytesTransferred >= 2L * 1024 * 1024) fired = true;
          }));
      while (!fired && !task.IsCompleted) await Task.Delay(100);
      long connectsBefore = await ProxyCounterAsync("connects");
      var sw = System.Diagnostics.Stopwatch.StartNew();
      app.Dispose();
      bool canceled = false;
      try { await task; } catch (OperationCanceledException) { canceled = true; }
      sw.Stop();
      await Task.Delay(5000);
      long connectsAfter = await ProxyCounterAsync("connects") - connectsBefore;
      Metric("S11", "cancel_latency_ms=" + sw.ElapsedMilliseconds, "connects_after_dispose=" + connectsAfter,
             "faulted=" + task.IsFaulted);
      Assert("S11 canceled", canceled && task.IsCanceled);
      Assert("S11 no session-cancel request after app dispose", connectsAfter == 0);
    }
#endif

    // S12: 90 s black-hole in the middle of a large download (Range resume).
    async Task ChaosS12DownloadBlackout() {
      ResetSdkCounters();
      await ClearWeatherAsync();
      long size = chaosLargeMb * 1024L * 1024L;
      byte[] payload = MakePayload(12, size);
      await UploadLargeOnceAsync("s12.bin", payload);
      bool fired = false;
      var probe = new ProgressProbe<DownloadState>(s => s.BytesTransferred, MainThreadId, b => {
        if (!fired && b >= size * 3 / 10) {
          fired = true;
          _ = BlackholeAsync(90);
        }
      });
      await ResetCountersAsync();
      var sw = System.Diagnostics.Stopwatch.StartNew();
      byte[] data = await ChaosRef("s12.bin").GetBytesAsync(0, probe);
      sw.Stop();
      long bytesDown = await ProxyCounterAsync("bytes_down");
      AssertEq("S12 length", (long)data.Length, size);
      AssertEq("S12 regressions", probe.Regressions, 0);
      Metric("S12", "seconds=" + sw.Elapsed.TotalSeconds.ToString("F1"), "proxy_bytes_down=" + bytesDown,
             "overhead_mb=" + ((bytesDown - size) >> 20), "callbacks=" + probe.Count);
      AssertEq("S12 hash", Sha256(data), Sha256(payload));
      Assert("S12 did not re-download the object", bytesDown < size + 8L * 1024 * 1024);
    }

#if !FIREBASE_STORAGE_LEGACY
    // S13: raw stream survives three resets; data continuous.
    async Task ChaosS13RawStreamResets() {
      ResetSdkCounters();
      await ClearWeatherAsync();
      long size = 32L * 1024 * 1024;
      byte[] payload = MakePayload(13, size);
      await UploadLargeOnceAsync("s13.bin", payload);
      string hash;
      int resets = 0;
      var sw = System.Diagnostics.Stopwatch.StartNew();
      using (Stream stream = await ChaosRef("s13.bin").GetStreamAsync())
      using (var sha = SHA256.Create()) {
        Assert("S13 CanSeek == false", !stream.CanSeek);
        var buffer = new byte[256 * 1024];
        long total = 0;
        long nextReset = 4L * 1024 * 1024;
        await Task.Run(async () => {
          while (true) {
            int read = await stream.ReadAsync(buffer, 0, buffer.Length);
            if (read <= 0) break;
            sha.TransformBlock(buffer, 0, read, null, 0);
            total += read;
            AssertEq("S13 Position", stream.Position, total);
            if (resets < 3 && total >= nextReset) {
              resets++;
              nextReset += 8L * 1024 * 1024;
              await ResetAllAsync();
            }
          }
        });
        sha.TransformFinalBlock(buffer, 0, 0);
        hash = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
        AssertEq("S13 total", total, size);
      }
      Metric("S13", "seconds=" + sw.Elapsed.TotalSeconds.ToString("F1"), "resets=" + resets);
      AssertEq("S13 hash", hash, Sha256(payload));
    }

    // S14: Dispose() the raw stream from another thread while a read is black-holed.
    async Task ChaosS14RawStreamDisposeDuringBlackhole() {
      ResetSdkCounters();
      await ClearWeatherAsync();
      long size = 32L * 1024 * 1024;
      await UploadLargeOnceAsync("s14.bin", MakePayload(14, size));
      Stream stream = await ChaosRef("s14.bin").GetStreamAsync();
      var buffer = new byte[256 * 1024];
      await stream.ReadAsync(buffer, 0, buffer.Length);
      await BlackholeAsync(90);
      // Drain whatever the socket / TLS layers had already buffered until a read really blocks.
      Task<int> blocked = null;
      long drained = 0;
      for (int i = 0; i < 2000; i++) {
        blocked = Task.Run(() => stream.ReadAsync(buffer, 0, buffer.Length));
        if (await Task.WhenAny(blocked, Task.Delay(1500)) != blocked) break;
        drained += await blocked;
        blocked = null;
      }
      Assert("S14 read is blocked (drained " + drained + " buffered bytes first)", blocked != null && !blocked.IsCompleted);
      var sw = System.Diagnostics.Stopwatch.StartNew();
      stream.Dispose();
      Exception failure = null;
      try { await blocked; } catch (Exception e) { failure = e; }
      sw.Stop();
      Metric("S14", "unblock_ms=" + sw.ElapsedMilliseconds, "drained_before_block=" + drained,
             "exception=" + (failure?.GetType().Name ?? "none"));
      Assert("S14 read failed after dispose", failure != null);
      Assert("S14 failed fast (< 5 s, not the 10 min retry budget)", sw.Elapsed.TotalSeconds < 5);
      Assert("S14 ObjectDisposedException", failure is ObjectDisposedException);
      await Task.Delay(90000);  // let the black-hole lift before the next scenario
    }

    // S15: synchronous Read() on the Unity main thread must not deadlock when the connection
    // drops; it fails with a StorageException and the frame loop keeps running.
    async Task ChaosS15MainThreadSyncRead() {
      ResetSdkCounters();
      await ClearWeatherAsync();
      long size = 8L * 1024 * 1024;
      await UploadLargeOnceAsync("s15.bin", MakePayload(15, size));
      Stream stream = await ChaosRef("s15.bin").GetStreamAsync();
      var buffer = new byte[64 * 1024];
      long total = 0;
      for (int i = 0; i < 16; i++) {
        total += stream.Read(buffer, 0, buffer.Length);  // happy path on the main thread
      }
      await ResetAllAsync();
      await Task.Delay(500);
      var sw = System.Diagnostics.Stopwatch.StartNew();
      Exception failure = null;
      try {
        // Drain whatever was buffered, then hit the dropped connection.
        for (int i = 0; i < 4096; i++) {
          int read = stream.Read(buffer, 0, buffer.Length);
          if (read <= 0) break;
          total += read;
        }
      } catch (Exception e) {
        failure = e;
      }
      sw.Stop();
      Metric("S15", "blocked_ms=" + sw.ElapsedMilliseconds, "bytes_before_failure=" + total,
             "exception=" + (failure?.GetType().Name ?? "none"));
      Assert("S15 main-thread Read() failed fast instead of deadlocking", failure is StorageException);
      Assert("S15 blocked < 10 s", sw.Elapsed.TotalSeconds < 10);
      // The same stream recovers when read from a background thread.
      int recovered = await Task.Run(() => stream.ReadAsync(buffer, 0, buffer.Length));
      Assert("S15 background read reconnects", recovered > 0);
      stream.Dispose();
    }
#endif

    // S16: the object is replaced while a download is in flight; the engine must restart once
    // and deliver the new object, never a splice of both.
    async Task ChaosS16ObjectReplacedMidDownload() {
      ResetSdkCounters();
      await ClearWeatherAsync();
      long size = 16L * 1024 * 1024;
      byte[] a = MakePayload(160, size);
      byte[] b = MakePayload(161, size);
      await UploadLargeOnceAsync("s16.bin", a);
      await SetWeatherAsync(bandwidthBps: 2 * 1024 * 1024);
      bool fired = false;
      var probe = new ProgressProbe<DownloadState>(s => s.BytesTransferred, MainThreadId, bytes => {
        if (!fired && bytes >= 2L * 1024 * 1024) {
          fired = true;
          _ = Task.Run(async () => {
            await ClearWeatherAsync();
            await ChaosRef("s16.bin").PutBytesAsync(b);
            await ResetAllAsync();  // force the download to reconnect and see the new ETag
          });
        }
      });
      byte[] data = await ChaosRef("s16.bin").GetBytesAsync(0, probe);
      await ClearWeatherAsync();
      string got = Sha256(data);
      string which = got == Sha256(b) ? "replacement" : got == Sha256(a) ? "original" : "SPLICED/CORRUPT";
      Metric("S16", "regressions=" + probe.Regressions, "callbacks=" + probe.Count, "delivered=" + which);
      Assert("S16 delivered a consistent object (" + which + ")", which != "SPLICED/CORRUPT");
      Assert("S16 restart observed (progress went back to 0)", probe.Regressions >= 1);
      AssertEq("S16 delivered the replacement object", got, Sha256(b));
    }

    // S17: maxDownloadSizeBytes smaller than the object.
    async Task ChaosS17MaxDownloadSize() {
      long size = 4L * 1024 * 1024;
      await UploadLargeOnceAsync("s17.bin", MakePayload(17, size));
      Exception failure = null;
      try { await ChaosRef("s17.bin").GetBytesAsync(size / 2); } catch (Exception e) { failure = e; }
      Metric("S17", "exception=" + (failure?.GetType().Name ?? "none"));
      Assert("S17 IndexOutOfRangeException", failure is IndexOutOfRangeException);
    }

    // S18: write-only rules: the upload must complete from the finalize response alone.
    async Task ChaosS18WriteOnlyRules() {
      long size = 1024 * 1024;
      byte[] payload = MakePayload(18, size);
      var reference = ChaosRef("writeonly/s18.bin");
      StorageMetadata metadata = await reference.PutBytesAsync(payload);
      Metric("S18", "size=" + metadata.SizeBytes, "md5=" + metadata.Md5Hash);
      AssertEq("S18 size", metadata.SizeBytes, size);
      Exception failure = null;
      try { await reference.GetMetadataAsync(); } catch (StorageException e) { failure = e; }
      Assert("S18 read is denied", failure is StorageException se && se.HttpResultCode == 403 &&
             se.ErrorCode == StorageException.ErrorNotAuthorized);
    }

    // S19: error mapping table.
    async Task ChaosS19ErrorMapping() {
      var results = new List<string>();
      async Task Probe(string label, Func<Task> action, int expectedCode, int expectedHttp) {
        try {
          await action();
          results.Add(label + "=no_error");
          Assert("S19 " + label + " expected an error", false);
        } catch (StorageException e) {
          results.Add(label + "=" + e.ErrorCode + "/" + e.HttpResultCode);
          Assert("S19 " + label + " code " + e.ErrorCode + " http " + e.HttpResultCode,
                 e.ErrorCode == expectedCode && e.HttpResultCode == expectedHttp);
        }
      }
      await Probe("denied_upload", () => ChaosRef("denied/x.bin").PutBytesAsync(new byte[16]),
                  StorageException.ErrorNotAuthorized, 403);
      await Probe("missing_download", () => ChaosRef("does-not-exist-" + Guid.NewGuid().ToString("N")).GetBytesAsync(0),
                  StorageException.ErrorObjectNotFound, 404);
      await Probe("missing_metadata", () => ChaosRef("does-not-exist-" + Guid.NewGuid().ToString("N")).GetMetadataAsync(),
                  StorageException.ErrorObjectNotFound, 404);
      string bucket = FirebaseStorage.DefaultInstance.App.Options.StorageBucket;
      string projectId = bucket.Substring(0, bucket.IndexOf('.'));
      var alt = FirebaseStorage.GetInstance("gs://" + projectId + "-pr1555-alt");
      byte[] payload = MakePayload(19, 64 * 1024);
      StorageMetadata altMeta = await alt.GetReference("chaos/s19.bin").PutBytesAsync(payload);
      AssertEq("S19 alt bucket upload", altMeta.SizeBytes, 64L * 1024);
      AssertEq("S19 alt bucket round trip", Sha256(await alt.GetReference("chaos/s19.bin").GetBytesAsync(0)), Sha256(payload));
      bool threw = false;
      try {
        FirebaseStorage.DefaultInstance.GetReferenceFromUrl("gs://" + projectId + "-pr1555-alt/chaos/s19.bin");
      } catch (ArgumentException) { threw = true; }
      Assert("S19 cross-bucket GetReferenceFromUrl throws ArgumentException", threw);
      var bogus = FirebaseStorage.GetInstance("gs://" + projectId + "-this-bucket-does-not-exist");
      try {
        await bogus.GetReference("chaos/x.bin").PutBytesAsync(new byte[16]);
        results.Add("bogus_bucket=no_error");
      } catch (StorageException e) {
        results.Add("bogus_bucket=" + e.ErrorCode + "/" + e.HttpResultCode);
      }
      Metric("S19", results.ToArray());
    }

    // S20: soak with random weather.
    async Task ChaosS20Soak() {
      ResetSdkCounters();
      await ClearWeatherAsync();
      int iterations = chaosSoakIterations;
      long size = 1024 * 1024;
      long memStart = GC.GetTotalMemory(true);
      int threadsStart = System.Diagnostics.Process.GetCurrentProcess().Threads.Count;
      var rng = new System.Random(20);
      var stop = new CancellationTokenSource();
      Task weather = Task.Run(async () => {
        while (!stop.IsCancellationRequested) {
          await Task.Delay(3000);
          switch (rng.Next(4)) {
            case 0: await ResetAllAsync(); break;
            case 1: await BlackholeAsync(4); break;
            case 2: await SetWeatherAsync(latencyMs: 300, jitterMs: 300); break;
            default: await ClearWeatherAsync(); break;
          }
        }
      });
      int failures = 0;
      var sw = System.Diagnostics.Stopwatch.StartNew();
      try {
        for (int batch = 0; batch < iterations; batch += 3) {
          var tasks = new List<Task>();
          for (int k = 0; k < 3 && batch + k < iterations; k++) {
            int i = batch + k;
            tasks.Add(Task.Run(async () => {
              byte[] payload = MakePayload(2000 + i, size);
              var reference = ChaosRef("soak/" + i + ".bin");
              try {
                await reference.PutBytesAsync(payload);
                byte[] back = await reference.GetBytesAsync(0);
                if (Sha256(back) != Sha256(payload)) Interlocked.Increment(ref failures);
                await reference.DeleteAsync();
              } catch (Exception e) {
                Interlocked.Increment(ref failures);
                Debug.LogWarning("CHAOS|S20|iteration=" + i + "|error=" + e.GetType().Name + ": " + e.Message.Replace('|', '/'));
              }
            }));
          }
          await Task.WhenAll(tasks);
          if (batch % 30 == 0) {
            Metric("S20", "phase=progress", "done=" + (batch + 3), "failures=" + failures,
                   "elapsed_s=" + (int)sw.Elapsed.TotalSeconds);
          }
        }
      } finally {
        stop.Cancel();
        await ClearWeatherAsync();
      }
      long memEnd = GC.GetTotalMemory(true);
      int threadsEnd = System.Diagnostics.Process.GetCurrentProcess().Threads.Count;
      Metric("S20", "phase=done", "iterations=" + iterations, "failures=" + failures,
             "seconds=" + (int)sw.Elapsed.TotalSeconds, "managed_start_mb=" + (memStart >> 20),
             "managed_end_mb=" + (memEnd >> 20), "threads_start=" + threadsStart, "threads_end=" + threadsEnd);
      AssertEq("S20 failures", failures, 0);
      Assert("S20 managed heap did not grow by > 64 MB", memEnd - memStart < 64L * 1024 * 1024);
      Assert("S20 thread count stable (+/- 10)", Math.Abs(threadsEnd - threadsStart) <= 10);
    }

    // ---------------------------------------------------------------- shared upload drivers

    // Uploads a large payload, fires `atOffset` -> `eventAsync` once, verifies the round trip.
    private async Task RunUploadWithEventAsync(string scenario, long atOffset, Func<Task> eventAsync, int seed,
                                               bool keepWeather = false) {
      ResetSdkCounters();
      if (!keepWeather) await ClearWeatherAsync();
      await ResetCountersAsync();
      long size = chaosLargeMb * 1024L * 1024L;
      byte[] payload = MakePayload(seed, size);
      bool fired = false;
      long eventAtTicks = 0;
      long firstProgressAfterEventTicks = 0;
      Task eventTask = Task.CompletedTask;
      var probe = new ProgressProbe<UploadState>(s => s.BytesTransferred, MainThreadId, b => {
        if (!fired && b >= atOffset) {
          fired = true;
          eventAtTicks = DateTime.UtcNow.Ticks;
          eventTask = eventAsync();
        } else if (fired && firstProgressAfterEventTicks == 0 && b > atOffset) {
          firstProgressAfterEventTicks = DateTime.UtcNow.Ticks;
        }
      });
      var sw = System.Diagnostics.Stopwatch.StartNew();
      StorageMetadata metadata;
      try {
        metadata = await ChaosRef(scenario.ToLowerInvariant() + ".bin").PutBytesAsync(payload, null, probe);
      } finally {
        // Never let a chaos timeline leak into the next scenario, even when the transfer failed.
        try { await eventTask; } catch (Exception e) { Debug.LogWarning("CHAOS|" + scenario + "|event_error=" + e.Message); }
      }
      sw.Stop();
      long bytesUp = await ProxyCounterAsync("bytes_up");
      AssertEq(scenario + " size", metadata.SizeBytes, size);
      AssertEq(scenario + " progress regressions", probe.Regressions, 0);
      Assert(scenario + " event fired", fired);
      Metric(scenario, "phase=upload", "seconds=" + sw.Elapsed.TotalSeconds.ToString("F1"),
             "recover_s=" + ((firstProgressAfterEventTicks - eventAtTicks) / 10000000.0).ToString("F1"),
             "proxy_bytes_up=" + bytesUp, "resent_mb=" + Math.Max(0, (bytesUp - size) >> 20), "callbacks=" + probe.Count);
      string hash = await DownloadAndHashAsync(ChaosRef(scenario.ToLowerInvariant() + ".bin"), size, scenario);
      AssertEq(scenario + " hash", hash, Sha256(payload));
      Assert(scenario + " re-sent at most 40 MiB after failure", bytesUp - size < 40L * 1024 * 1024);
    }

    private async Task RunUploadAndVerifyAsync(string scenario, long size, int seed) {
      await ResetCountersAsync();
      byte[] payload = MakePayload(seed, size);
      var probe = new ProgressProbe<UploadState>(s => s.BytesTransferred, MainThreadId);
      var sw = System.Diagnostics.Stopwatch.StartNew();
      StorageMetadata metadata = await ChaosRef(scenario.ToLowerInvariant() + ".bin").PutBytesAsync(payload, null, probe);
      sw.Stop();
      AssertEq(scenario + " size", metadata.SizeBytes, size);
      AssertEq(scenario + " progress regressions", probe.Regressions, 0);
      Metric(scenario, "phase=upload", "seconds=" + sw.Elapsed.TotalSeconds.ToString("F1"),
             "proxy_bytes_up=" + await ProxyCounterAsync("bytes_up"), "callbacks=" + probe.Count);
      string hash = await DownloadAndHashAsync(ChaosRef(scenario.ToLowerInvariant() + ".bin"), size, scenario);
      AssertEq(scenario + " hash", hash, Sha256(payload));
    }
  }
}
