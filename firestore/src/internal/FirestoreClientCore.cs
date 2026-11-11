// Copyright 2026 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Internal;

namespace Firebase.Firestore.Internal {
  internal sealed class PendingMutationBatch {
    internal int BatchId { get; }
    internal Timestamp LocalWriteTime { get; }
    internal IReadOnlyList<MutationSpec> Mutations { get; }
    internal List<TaskCompletionSource<bool>> Waiters { get; } =
        new List<TaskCompletionSource<bool>>(1);

    internal PendingMutationBatch(
        int batchId, Timestamp localWriteTime, IReadOnlyList<MutationSpec> mutations) {
      BatchId = batchId;
      LocalWriteTime = localWriteTime;
      Mutations = mutations;
    }
  }

  internal sealed class ActiveDocListener {
    internal int ListenerId { get; }
    internal int WatchTargetId { get; }
    internal ResourcePath Path { get; }
    internal MetadataChanges MetadataChanges { get; }
    internal Action<DocumentSnapshot> Callback { get; }
    internal SynchronizationContext CapturedContext { get; }
    internal TaskCompletionSource<bool> ListenerTcs { get; } =
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    internal volatile bool Stopped;
    internal bool HasEmitted;
    internal bool LastExists;
    internal Dictionary<string, FirestoreValue> LastFields;
    internal SnapshotMetadata LastMetadata;
    internal bool TargetIsSynced;

    internal ActiveDocListener(
        int listenerId,
        int watchTargetId,
        ResourcePath path,
        MetadataChanges metadataChanges,
        Action<DocumentSnapshot> callback,
        SynchronizationContext capturedContext) {
      ListenerId = listenerId;
      WatchTargetId = watchTargetId;
      Path = path;
      MetadataChanges = metadataChanges;
      Callback = callback;
      CapturedContext = capturedContext;
    }
  }

  internal sealed class ActiveQueryListener {
    internal int ListenerId { get; }
    internal int WatchTargetId { get; }
    internal Query QueryObj { get; }
    internal QuerySpec Spec { get; }
    internal MetadataChanges MetadataChanges { get; }
    internal Action<QuerySnapshot> Callback { get; }
    internal SynchronizationContext CapturedContext { get; }
    internal TaskCompletionSource<bool> ListenerTcs { get; } =
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    internal volatile bool Stopped;
    internal bool HasEmitted;
    internal List<DocumentSnapshot> LastDocs;
    internal SnapshotMetadata LastMetadata;
    internal bool LastHadCommittedMutations;
    internal bool TargetIsSynced;
    internal HashSet<ResourcePath> RemoteMatchedKeys { get; } = new HashSet<ResourcePath>();

    internal ActiveQueryListener(
        int listenerId,
        int watchTargetId,
        Query queryObj,
        QuerySpec spec,
        MetadataChanges metadataChanges,
        Action<QuerySnapshot> callback,
        SynchronizationContext capturedContext) {
      ListenerId = listenerId;
      WatchTargetId = watchTargetId;
      QueryObj = queryObj;
      Spec = spec;
      MetadataChanges = metadataChanges;
      Callback = callback;
      CapturedContext = capturedContext;
    }
  }

  internal sealed class ActiveSyncListener {
    internal int ListenerId { get; }
    internal Action Callback { get; }
    internal SynchronizationContext CapturedContext { get; }
    internal TaskCompletionSource<bool> ListenerTcs { get; } =
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    internal volatile bool Stopped;

    internal ActiveSyncListener(int listenerId, Action callback, SynchronizationContext ctx) {
      ListenerId = listenerId;
      Callback = callback;
      CapturedContext = ctx;
    }
  }

  /// <summary>
  /// Pure-C# Firestore client engine owning the remote cache, optimistic local mutation queue,
  /// optional <see cref="AtomicFileStore"/> persistence, multiplexed WebChannel watch listeners,
  /// transactions, and bundle loading for a single <c>(FirebaseApp, databaseId)</c> instance.
  /// </summary>
  internal sealed class FirestoreClientCore : IDisposable {
    private readonly FirebaseFirestore _owner;
    private readonly FirebaseApp _app;
    private readonly string _appName;
    private readonly string _projectId;
    private readonly string _databaseId;
    private readonly string _persistenceKey;

    private readonly object _stateLock = new object();
    private readonly Dictionary<ResourcePath, MutableDocument> _remoteCache =
        new Dictionary<ResourcePath, MutableDocument>();
    private readonly List<PendingMutationBatch> _mutationQueue = new List<PendingMutationBatch>();
    private readonly List<TaskCompletionSource<bool>> _allWritesWaiters =
        new List<TaskCompletionSource<bool>>();
    private readonly Dictionary<string, Timestamp> _loadedBundles =
        new Dictionary<string, Timestamp>(StringComparer.Ordinal);
    private readonly Dictionary<string, QuerySpec> _namedQueries =
        new Dictionary<string, QuerySpec>(StringComparer.Ordinal);

    private readonly Dictionary<int, ActiveDocListener> _docListeners =
        new Dictionary<int, ActiveDocListener>();
    private readonly Dictionary<int, ActiveQueryListener> _queryListeners =
        new Dictionary<int, ActiveQueryListener>();
    private readonly Dictionary<int, ActiveSyncListener> _syncListeners =
        new Dictionary<int, ActiveSyncListener>();
    private readonly HashSet<ResourcePath> _pendingWatchPaths = new HashSet<ResourcePath>();
    private bool _pendingWatchSyncEvent;
    private readonly Queue<Action> _callbackQueue = new Queue<Action>();
    private bool _callbackPumpScheduled;
    private SynchronizationContext _lastCapturedContext;

    private FirestoreRpcClient _rpc;
    private FirestoreWatchStream _watchStream;
    private bool _started;
    private bool _persistenceEnabled = true;
    private bool _persistenceLoaded;
    private bool _persistenceDirty;
    private int _flushScheduled;
    private bool _networkEnabled = true;
    private bool _terminated;
    private bool _disposed;
    private int _nextBatchId = 1;
    private int _nextListenerId = 1;
    private int _nextTargetId = 2; // Even targetIds like JS SDK.
    private bool _mutationDrainRunning;
    private readonly CancellationTokenSource _lifetimeCts = new CancellationTokenSource();

    internal string ProjectId => _projectId;
    internal string DatabaseId => _databaseId;
    internal bool IsStarted {
      get { lock (_stateLock) return _started; }
    }
    internal bool IsTerminated {
      get { lock (_stateLock) return _terminated; }
    }
    internal bool IsDisposed {
      get { lock (_stateLock) return _disposed; }
    }

    internal FirestoreClientCore(
        FirebaseFirestore owner, FirebaseApp app, string databaseId) {
      _owner = owner;
      _app = app;
      _appName = app?.Name ?? "__DEFAULT";
      _projectId = app?.Options?.ProjectId ?? string.Empty;
      _databaseId = string.IsNullOrEmpty(databaseId) ? "(default)" : databaseId;
      _persistenceKey = BuildPersistenceKey(_appName, _projectId, _databaseId);
    }

    private static string BuildPersistenceKey(string appName, string projectId, string dbId) {
      return $"firestore/{SanitizeFileToken(appName)}__{SanitizeFileToken(projectId)}__{SanitizeFileToken(dbId)}.json";
    }

    private static string SanitizeFileToken(string raw) {
      if (string.IsNullOrEmpty(raw)) return "_";
      var sb = new StringBuilder(raw.Length);
      for (int i = 0; i < raw.Length; i++) {
        char c = raw[i];
        if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
            c == '-' || c == '_') {
          sb.Append(c);
        } else {
          sb.Append('_');
        }
      }
      return sb.ToString();
    }

    internal FirestoreRpcClient EnsureStarted() {
      lock (_stateLock) {
        if (_disposed) {
          throw new InvalidOperationException("This FirebaseFirestore instance has been disposed.");
        }
        if (_rpc != null) return _rpc;

        FirebaseFirestoreSettings settings = _owner.Settings;
        settings.ValidateAndFreeze();
        _persistenceEnabled = settings.PersistenceEnabled;
        _started = true;
        _rpc = new FirestoreRpcClient(
            _app, _projectId, _databaseId, settings.Host, settings.SslEnabled);
        _watchStream = new FirestoreWatchStream(
            _rpc,
            OnWatchDocChange,
            OnWatchDocRemoveOrDelete,
            OnWatchTargetCurrent,
            OnWatchTargetReset,
            OnWatchExistenceFilter,
            OnWatchTargetError,
            OnWatchBatchFlush);
        _watchStream.SetNetworkEnabled(_networkEnabled);

        if (_persistenceEnabled && !_persistenceLoaded) {
          _persistenceLoaded = true;
          LoadPersistenceIntoLockedState();
          StartMutationDrainIfNeededLocked();
        }
        return _rpc;
      }
    }

    #region Persistence (AtomicFileStore)

    private void LoadPersistenceIntoLockedState() {
      string json = null;
      try {
        json = AtomicFileStore.Default.ReadAllText(_persistenceKey);
      } catch (Exception) {
        // Unreadable cache file; start clean.
      }
      if (string.IsNullOrEmpty(json)) return;

      if (JsonHelper.ParseObject(json) is not Dictionary<string, object> root) return;
      if (JsonHelper.GetList(root, "docs") is List<object> docsList) {
        for (int i = 0; i < docsList.Count; i++) {
          if (docsList[i] is Dictionary<string, object> entry) {
            string pathStr = JsonHelper.GetString(entry, "path");
            if (string.IsNullOrEmpty(pathStr)) continue;
            ResourcePath rp = ResourcePath.ParseUntrusted(pathStr);
            bool exists = JsonHelper.GetBool(entry, "exists", true);
            long verSec = JsonHelper.GetLong(entry, "verSec", 0);
            int verNano = (int)JsonHelper.GetLong(entry, "verNano", 0);
            bool committed = JsonHelper.GetBool(entry, "committed", false);
            var fields = new Dictionary<string, FirestoreValue>(StringComparer.Ordinal);
            if (exists && JsonHelper.GetMap(entry, "fields") is Dictionary<string, object> rawFields) {
              foreach (KeyValuePair<string, object> kv in rawFields) {
                fields[kv.Key] = FirestoreJson.ParseMiniJsonFirestoreValue(kv.Value);
              }
            }
            _remoteCache[rp] = new MutableDocument(
                rp,
                exists,
                fields,
                new Timestamp(verSec, verNano),
                hasLocalMutations: false,
                hasCommittedMutations: committed);
          }
        }
      }

      if (JsonHelper.GetList(root, "mutations") is List<object> mutBatches) {
        for (int b = 0; b < mutBatches.Count; b++) {
          if (mutBatches[b] is not Dictionary<string, object> batchMap) continue;
          long wSec = JsonHelper.GetLong(batchMap, "sec", 0);
          int wNano = (int)JsonHelper.GetLong(batchMap, "nano", 0);
          if (JsonHelper.GetList(batchMap, "ops") is not List<object> opsList || opsList.Count == 0) {
            continue;
          }
          var specs = new List<MutationSpec>(opsList.Count);
          for (int m = 0; m < opsList.Count; m++) {
            if (opsList[m] is Dictionary<string, object> opMap &&
                TryParsePersistedMutation(opMap, out MutationSpec spec)) {
              specs.Add(spec);
            }
          }
          if (specs.Count > 0) {
            _mutationQueue.Add(new PendingMutationBatch(
                _nextBatchId++, new Timestamp(wSec, wNano), specs));
          }
        }
      }

      if (JsonHelper.GetMap(root, "bundles") is Dictionary<string, object> bundlesMap) {
        foreach (KeyValuePair<string, object> kv in bundlesMap) {
          if (kv.Value is string tsStr) {
            _loadedBundles[kv.Key] = FirestoreJson.ParseRfc3339Timestamp(tsStr);
          }
        }
      }
    }

    private static bool TryParsePersistedMutation(
        Dictionary<string, object> opMap, out MutationSpec spec) {
      spec = null;
      string pathStr = JsonHelper.GetString(opMap, "path");
      if (string.IsNullOrEmpty(pathStr)) return false;
      ResourcePath rp = ResourcePath.ParseUntrusted(pathStr);
      MutationKind kind = (MutationKind)(byte)JsonHelper.GetLong(opMap, "kind", 0);
      if (kind == MutationKind.Delete) {
        spec = MutationSpec.CreateDelete(rp);
        return true;
      }

      var data = new Dictionary<string, FirestoreValue>(StringComparer.Ordinal);
      if (JsonHelper.GetMap(opMap, "data") is Dictionary<string, object> rawData) {
        foreach (KeyValuePair<string, object> kv in rawData) {
          data[kv.Key] = FirestoreJson.ParseMiniJsonFirestoreValue(kv.Value);
        }
      }

      List<FieldPath> mask = null;
      if (JsonHelper.GetList(opMap, "mask") is List<object> rawMask) {
        mask = new List<FieldPath>(rawMask.Count);
        for (int i = 0; i < rawMask.Count; i++) {
          if (rawMask[i] is List<object> segs && segs.Count > 0) {
            var segArr = new string[segs.Count];
            for (int s = 0; s < segs.Count; s++) segArr[s] = segs[s]?.ToString() ?? string.Empty;
            mask.Add(new FieldPath(segArr));
          }
        }
      }

      var transforms = new List<FieldTransformSpec>();
      if (JsonHelper.GetList(opMap, "transforms") is List<object> rawTr) {
        for (int i = 0; i < rawTr.Count; i++) {
          if (rawTr[i] is not Dictionary<string, object> trMap) continue;
          if (JsonHelper.GetList(trMap, "field") is not List<object> fSegs || fSegs.Count == 0) continue;
          var segArr = new string[fSegs.Count];
          for (int s = 0; s < fSegs.Count; s++) segArr[s] = fSegs[s]?.ToString() ?? string.Empty;
          FieldTransformKind tk = (FieldTransformKind)(byte)JsonHelper.GetLong(trMap, "kind", 0);
          FirestoreValue operand = trMap.TryGetValue("op", out object opRaw) && opRaw != null
              ? FirestoreJson.ParseMiniJsonFirestoreValue(opRaw)
              : FirestoreValue.Null;
          transforms.Add(new FieldTransformSpec(new FieldPath(segArr), tk, operand));
        }
      }

      spec = new MutationSpec(rp, kind, data, mask, transforms);
      return true;
    }

    private void MarkPersistenceDirtyLocked() {
      if (!_persistenceEnabled) return;
      _persistenceDirty = true;
      if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) == 0) {
        _ = Task.Run(async () => {
          try {
            await Task.Delay(15, _lifetimeCts.Token).ConfigureAwait(false);
          } catch (OperationCanceledException) {
            // Flushing on teardown or cancelled.
          } finally {
            Interlocked.Exchange(ref _flushScheduled, 0);
            FlushPersistenceIfDirty();
          }
        });
      }
    }

    internal void FlushPersistenceIfDirty() {
      string serialized = null;
      lock (_stateLock) {
        if (!_persistenceEnabled || !_persistenceDirty) return;
        _persistenceDirty = false;
        serialized = BuildPersistenceJsonLocked();
      }
      if (serialized != null) {
        try {
          AtomicFileStore.Default.WriteAllText(_persistenceKey, serialized);
        } catch (Exception) {
          // Best-effort local disk cache write.
        }
      }
    }

    private string BuildPersistenceJsonLocked() {
      using var w = new PooledUtf8JsonWriter();
      w.WriteAscii("{\"docs\":[");
      bool first = true;
      foreach (MutableDocument doc in _remoteCache.Values) {
        if (!first) w.WriteByte((byte)',');
        first = false;
        w.WriteAscii("{\"path\":");
        w.WriteString(doc.Path.CanonicalString);
        w.WriteAscii(",\"exists\":");
        w.WriteBool(doc.Exists);
        w.WriteAscii(",\"verSec\":");
        w.WriteInt64(doc.Version.Seconds);
        w.WriteAscii(",\"verNano\":");
        w.WriteInt64(doc.Version.Nanoseconds);
        if (doc.HasCommittedMutations) {
          w.WriteAscii(",\"committed\":true");
        }
        if (doc.Exists && doc.Fields.Count > 0) {
          w.WriteAscii(",\"fields\":");
          FirestoreJson.WriteFieldsMap(w, doc.Fields);
        }
        w.WriteByte((byte)'}');
      }
      w.WriteAscii("],\"mutations\":[");
      for (int b = 0; b < _mutationQueue.Count; b++) {
        if (b > 0) w.WriteByte((byte)',');
        PendingMutationBatch batch = _mutationQueue[b];
        w.WriteAscii("{\"sec\":");
        w.WriteInt64(batch.LocalWriteTime.Seconds);
        w.WriteAscii(",\"nano\":");
        w.WriteInt64(batch.LocalWriteTime.Nanoseconds);
        w.WriteAscii(",\"ops\":[");
        for (int m = 0; m < batch.Mutations.Count; m++) {
          if (m > 0) w.WriteByte((byte)',');
          MutationSpec spec = batch.Mutations[m];
          w.WriteAscii("{\"path\":");
          w.WriteString(spec.Path.CanonicalString);
          w.WriteAscii(",\"kind\":");
          w.WriteInt64((int)spec.Kind);
          if (spec.Data != null && spec.Data.Count > 0) {
            w.WriteAscii(",\"data\":");
            FirestoreJson.WriteFieldsMap(w, spec.Data);
          }
          if (spec.Mask != null) {
            w.WriteAscii(",\"mask\":[");
            for (int k = 0; k < spec.Mask.Count; k++) {
              if (k > 0) w.WriteByte((byte)',');
              WriteSegmentsJsonArray(w, spec.Mask[k].Segments);
            }
            w.WriteByte((byte)']');
          }
          if (spec.Transforms != null && spec.Transforms.Count > 0) {
            w.WriteAscii(",\"transforms\":[");
            for (int t = 0; t < spec.Transforms.Count; t++) {
              if (t > 0) w.WriteByte((byte)',');
              FieldTransformSpec tr = spec.Transforms[t];
              w.WriteAscii("{\"field\":");
              WriteSegmentsJsonArray(w, tr.Field.Segments);
              w.WriteAscii(",\"kind\":");
              w.WriteInt64((int)tr.Kind);
              if (tr.Kind != FieldTransformKind.ServerTimestamp) {
                w.WriteAscii(",\"op\":");
                FirestoreJson.WriteFirestoreValue(w, tr.Operand);
              }
              w.WriteByte((byte)'}');
            }
            w.WriteByte((byte)']');
          }
          w.WriteByte((byte)'}');
        }
        w.WriteAscii("]}");
      }
      w.WriteAscii("],\"bundles\":{");
      bool firstB = true;
      foreach (KeyValuePair<string, Timestamp> kv in _loadedBundles) {
        if (!firstB) w.WriteByte((byte)',');
        firstB = false;
        w.WriteString(kv.Key);
        w.WriteByte((byte)':');
        w.WriteTimestampString(kv.Value);
      }
      w.WriteAscii("}}");
      return w.ToUtf8String();
    }

    private static void WriteSegmentsJsonArray(PooledUtf8JsonWriter w, IReadOnlyList<string> segs) {
      w.WriteByte((byte)'[');
      for (int i = 0; i < segs.Count; i++) {
        if (i > 0) w.WriteByte((byte)',');
        w.WriteString(segs[i]);
      }
      w.WriteByte((byte)']');
    }

    internal Task ClearPersistenceAsync() {
      lock (_stateLock) {
        if (_disposed) {
          throw new InvalidOperationException("This FirebaseFirestore instance has been disposed.");
        }
        if (_started && !_terminated) {
          return Util.TaskFromException<bool>(new FirestoreException(
              FirestoreError.FailedPrecondition,
              "Persistence cannot be cleared while the Firestore instance is running."));
        }
        _remoteCache.Clear();
        _mutationQueue.Clear();
        _loadedBundles.Clear();
        _namedQueries.Clear();
        _persistenceDirty = false;
      }
      try {
        AtomicFileStore.Default.Delete(_persistenceKey);
      } catch (Exception) {
        // Ignore if file already absent.
      }
      return Task.CompletedTask;
    }

    #endregion

    #region Effective Local Cache & Mutation Queue

    private MutableDocument ComputeEffectiveDocumentLocked(ResourcePath path, out bool inCache) {
      inCache = _remoteCache.TryGetValue(path, out MutableDocument baseDoc);
      MutableDocument current = baseDoc?.Clone();
      for (int i = 0; i < _mutationQueue.Count; i++) {
        PendingMutationBatch batch = _mutationQueue[i];
        for (int j = 0; j < batch.Mutations.Count; j++) {
          MutationSpec m = batch.Mutations[j];
          if (m.Path.Equals(path)) {
            inCache = true;
            current = m.ApplyToLocal(current, batch.LocalWriteTime);
          }
        }
      }
      return current;
    }

    private Dictionary<ResourcePath, MutableDocument> ComputeAllEffectiveDocumentsLocked() {
      var map = new Dictionary<ResourcePath, MutableDocument>(_remoteCache.Count + 8);
      foreach (KeyValuePair<ResourcePath, MutableDocument> kv in _remoteCache) {
        map[kv.Key] = kv.Value.Clone();
      }
      for (int i = 0; i < _mutationQueue.Count; i++) {
        PendingMutationBatch batch = _mutationQueue[i];
        for (int j = 0; j < batch.Mutations.Count; j++) {
          MutationSpec m = batch.Mutations[j];
          map.TryGetValue(m.Path, out MutableDocument cur);
          map[m.Path] = m.ApplyToLocal(cur, batch.LocalWriteTime);
        }
      }
      return map;
    }

    internal Task EnqueueMutationsAsync(IReadOnlyList<MutationSpec> mutations) {
      lock (_stateLock) {
        if (_disposed) {
          return Util.TaskFromException<bool>(new FirestoreException(
              FirestoreError.FailedPrecondition, "This FirebaseFirestore instance has been disposed."));
        }
        if (_terminated) {
          return Util.TaskFromException<bool>(
              new InvalidOperationException("This FirebaseFirestore instance has been terminated."));
        }
      }

      if (mutations == null || mutations.Count == 0) {
        EnsureStarted();
        return Task.CompletedTask;
      }

      for (int i = 0; i < mutations.Count; i++) {
        FirestoreException pathErr = mutations[i].Path.ValidateForBackend();
        if (pathErr != null) {
          return Util.TaskFromException<bool>(pathErr);
        }
      }

      EnsureStarted();
      var waiterTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

      lock (_stateLock) {
        var batch = new PendingMutationBatch(
            _nextBatchId++, Timestamp.GetCurrentTimestamp(), mutations);
        batch.Waiters.Add(waiterTcs);
        _mutationQueue.Add(batch);
        MarkPersistenceDirtyLocked();
        EnqueueListenerCallbacksLocked(
            changedPaths: mutations.Select(m => m.Path).ToList(),
            checkSyncEvent: false,
            allowUnsyncedLocal: true);
        StartMutationDrainIfNeededLocked();
      }

      return waiterTcs.Task;
    }

    private void StartMutationDrainIfNeededLocked() {
      if (_mutationDrainRunning || !_networkEnabled || _terminated || _disposed ||
          _mutationQueue.Count == 0) {
        return;
      }
      _mutationDrainRunning = true;
      CancellationToken ct = _lifetimeCts.Token;
      _ = Task.Run(() => DrainMutationQueueLoopAsync(ct));
    }

    private async Task DrainMutationQueueLoopAsync(CancellationToken ct) {
      while (!ct.IsCancellationRequested) {
        PendingMutationBatch head;
        FirestoreRpcClient rpc;
        lock (_stateLock) {
          if (!_networkEnabled || _terminated || _disposed || _mutationQueue.Count == 0) {
            _mutationDrainRunning = false;
            return;
          }
          head = _mutationQueue[0];
          rpc = _rpc;
        }

        CommitBatchResponse commitResp = null;
        Exception commitErr = null;
        try {
          commitResp = await rpc.CommitAsync(head.Mutations, transactionId: null, ct)
              .ConfigureAwait(false);
        } catch (FirestoreException fex) when (
            fex.ErrorCode == FirestoreError.Unavailable ||
            fex.ErrorCode == FirestoreError.DeadlineExceeded ||
            fex.ErrorCode == FirestoreError.ResourceExhausted) {
          // Transient offline/unavailable: keep queued and pause briefly if still online.
          try {
            await Task.Delay(300, ct).ConfigureAwait(false);
          } catch (Exception) {
            // Cancelled during client shutdown.
            break;
          }
          continue;
        } catch (Exception ex) {
          commitErr = ex;
        }

        List<TaskCompletionSource<bool>> batchWaiters;
        List<TaskCompletionSource<bool>> allWaiters = null;

        lock (_stateLock) {
          if (_mutationQueue.Count > 0 && ReferenceEquals(_mutationQueue[0], head)) {
            _mutationQueue.RemoveAt(0);
          }
          batchWaiters = new List<TaskCompletionSource<bool>>(head.Waiters);

          if (commitErr == null && commitResp != null) {
            ApplyCommittedBatchToRemoteCacheLocked(head, commitResp);
            MarkPersistenceDirtyLocked();
          }

          if (_mutationQueue.Count == 0 && _allWritesWaiters.Count > 0) {
            allWaiters = new List<TaskCompletionSource<bool>>(_allWritesWaiters);
            _allWritesWaiters.Clear();
          }

          EnqueueListenerCallbacksLocked(
              changedPaths: head.Mutations.Select(m => m.Path).ToList(),
              checkSyncEvent: _mutationQueue.Count == 0,
              allowUnsyncedLocal: true);

          if (_mutationQueue.Count == 0 || !_networkEnabled || _terminated || _disposed) {
            _mutationDrainRunning = false;
          }
        }

        if (commitErr != null) {
          for (int i = 0; i < batchWaiters.Count; i++) {
            batchWaiters[i].TrySetException(commitErr);
          }
        } else {
          for (int i = 0; i < batchWaiters.Count; i++) {
            batchWaiters[i].TrySetResult(true);
          }
        }
        if (allWaiters != null) {
          for (int i = 0; i < allWaiters.Count; i++) {
            allWaiters[i].TrySetResult(true);
          }
        }

        lock (_stateLock) {
          if (!_mutationDrainRunning) return;
        }
      }
    }

    private void ApplyCommittedBatchToRemoteCacheLocked(
        PendingMutationBatch batch, CommitBatchResponse resp) {
      for (int i = 0; i < batch.Mutations.Count; i++) {
        MutationSpec m = batch.Mutations[i];
        WriteCommitResult wr = i < resp.WriteResults.Count ? resp.WriteResults[i] : null;
        Timestamp version = wr != null && !wr.UpdateTime.Equals(default(Timestamp))
            ? wr.UpdateTime
            : resp.CommitTime;

        _remoteCache.TryGetValue(m.Path, out MutableDocument prev);
        MutableDocument applied = m.ApplyToLocal(prev, batch.LocalWriteTime);
        applied.HasLocalMutations = false;
        applied.HasCommittedMutations = true;
        if (prev != null && prev.Version.CompareTo(version) > 0) {
          version = prev.Version;
        }
        applied.Version = version;

        if (applied.Exists && wr != null && wr.TransformResults.Count > 0 && m.Transforms.Count > 0) {
          int count = Math.Min(m.Transforms.Count, wr.TransformResults.Count);
          for (int t = 0; t < count; t++) {
            FirestoreValue.SetFieldByPath(applied.Fields, m.Transforms[t].Field, wr.TransformResults[t]);
          }
        }
        _remoteCache[m.Path] = applied;
      }
    }

    internal Task WaitForPendingWritesAsync() {
      lock (_stateLock) {
        if (_disposed) {
          throw new InvalidOperationException("This FirebaseFirestore instance has been disposed.");
        }
        if (_terminated) {
          return Util.TaskFromException<bool>(
              new InvalidOperationException("This FirebaseFirestore instance has been terminated."));
        }
      }
      EnsureStarted();
      lock (_stateLock) {
        if (_mutationQueue.Count == 0) {
          return Task.CompletedTask;
        }
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _allWritesWaiters.Add(tcs);
        return tcs.Task;
      }
    }

    internal Task DisableNetworkAsync() {
      lock (_stateLock) {
        if (_disposed) {
          throw new InvalidOperationException("This FirebaseFirestore instance has been disposed.");
        }
        if (_terminated) {
          return Util.TaskFromException<bool>(
              new InvalidOperationException("This FirebaseFirestore instance has been terminated."));
        }
      }
      EnsureStarted();
      lock (_stateLock) {
        _networkEnabled = false;
        _watchStream?.SetNetworkEnabled(false);
        foreach (ActiveDocListener dl in _docListeners.Values) dl.TargetIsSynced = false;
        foreach (ActiveQueryListener ql in _queryListeners.Values) ql.TargetIsSynced = false;
        EnqueueListenerCallbacksLocked(changedPaths: null, checkSyncEvent: false);
      }
      return Task.CompletedTask;
    }

    internal Task EnableNetworkAsync() {
      lock (_stateLock) {
        if (_disposed) {
          throw new InvalidOperationException("This FirebaseFirestore instance has been disposed.");
        }
        if (_terminated) {
          return Util.TaskFromException<bool>(
              new InvalidOperationException("This FirebaseFirestore instance has been terminated."));
        }
      }
      EnsureStarted();
      lock (_stateLock) {
        _networkEnabled = true;
        _watchStream?.SetNetworkEnabled(true);
        StartMutationDrainIfNeededLocked();
      }
      return Task.CompletedTask;
    }

    #endregion

    #region Unary Document, Query & Aggregate Reads

    internal Task<DocumentSnapshot> GetDocumentSnapshotAsync(
        DocumentReference docRef, Source source) {
      lock (_stateLock) {
        if (_disposed) {
          return Util.TaskFromException<DocumentSnapshot>(new FirestoreException(
              FirestoreError.FailedPrecondition, "This FirebaseFirestore instance has been disposed."));
        }
        if (_terminated) {
          throw new InvalidOperationException("This FirebaseFirestore instance has been terminated.");
        }
      }

      FirestoreException invalidPath = docRef.ResourcePath.ValidateForBackend();
      if (invalidPath != null) {
        return Util.TaskFromException<DocumentSnapshot>(invalidPath);
      }

      FirestoreRpcClient rpc = EnsureStarted();
      if (source == Source.Cache) {
        try {
          return Task.FromResult(ReadDocumentFromCacheOrThrow(docRef));
        } catch (Exception ex) {
          return Util.TaskFromException<DocumentSnapshot>(ex);
        }
      }

      bool online;
      lock (_stateLock) online = _networkEnabled;
      if (!online) {
        if (source == Source.Server) {
          return Util.TaskFromException<DocumentSnapshot>(new FirestoreException(
              FirestoreError.Unavailable,
              "Failed to get document because the client is offline."));
        }
        try {
          return Task.FromResult(ReadDocumentFromCacheOrThrow(docRef));
        } catch (Exception ex) {
          return Util.TaskFromException<DocumentSnapshot>(ex);
        }
      }

      return FetchDocumentRemoteWithFallbackAsync(rpc, docRef, source);
    }

    private async Task<DocumentSnapshot> FetchDocumentRemoteWithFallbackAsync(
        FirestoreRpcClient rpc, DocumentReference docRef, Source source) {
      try {
        Dictionary<ResourcePath, MutableDocument> batch =
            await rpc.BatchGetAsync(new[] { docRef.ResourcePath }, null, _lifetimeCts.Token)
                .ConfigureAwait(false);
        MutableDocument effective;
        lock (_stateLock) {
          if (batch.TryGetValue(docRef.ResourcePath, out MutableDocument remoteDoc)) {
            remoteDoc.HasCommittedMutations = false;
            remoteDoc.HasLocalMutations = false;
            _remoteCache[docRef.ResourcePath] = remoteDoc;
            MarkPersistenceDirtyLocked();
          }
          effective = ComputeEffectiveDocumentLocked(docRef.ResourcePath, out _);
          EnqueueListenerCallbacksLocked(new[] { docRef.ResourcePath }, checkSyncEvent: false);
        }
        return BuildDocumentSnapshot(docRef, effective, isFromCache: false);
      } catch (FirestoreException fex) when (
          source == Source.Default && fex.ErrorCode == FirestoreError.Unavailable) {
        return ReadDocumentFromCacheOrThrow(docRef);
      }
    }

    private DocumentSnapshot ReadDocumentFromCacheOrThrow(DocumentReference docRef) {
      lock (_stateLock) {
        MutableDocument doc = ComputeEffectiveDocumentLocked(docRef.ResourcePath, out bool inCache);
        if (!inCache) {
          throw new FirestoreException(
              FirestoreError.Unavailable,
              $"Failed to get document '{docRef.ResourcePath}' from cache.");
        }
        return BuildDocumentSnapshot(docRef, doc, isFromCache: true);
      }
    }

    internal Task<QuerySnapshot> GetQuerySnapshotAsync(Query queryObj, Source source) {
      lock (_stateLock) {
        if (_disposed) {
          return Util.TaskFromException<QuerySnapshot>(new FirestoreException(
              FirestoreError.FailedPrecondition, "This FirebaseFirestore instance has been disposed."));
        }
        if (_terminated) {
          throw new InvalidOperationException("This FirebaseFirestore instance has been terminated.");
        }
      }

      QuerySpec spec = queryObj.Spec;
      if (!spec.IsCollectionGroup) {
        FirestoreException invalidPath = spec.Path.ValidateForBackend();
        if (invalidPath != null) {
          return Util.TaskFromException<QuerySnapshot>(invalidPath);
        }
      }
      try {
        spec.ValidateForExecution();
      } catch (Exception ex) {
        return Util.TaskFromException<QuerySnapshot>(ex);
      }

      FirestoreRpcClient rpc = EnsureStarted();
      if (source == Source.Cache) {
        return Task.FromResult(EvaluateQueryFromCache(queryObj, isFromCache: true));
      }

      bool online;
      lock (_stateLock) online = _networkEnabled;
      if (!online) {
        if (source == Source.Server) {
          return Util.TaskFromException<QuerySnapshot>(new FirestoreException(
              FirestoreError.Unavailable, "Failed to get documents because the client is offline."));
        }
        return Task.FromResult(EvaluateQueryFromCache(queryObj, isFromCache: true));
      }

      return FetchQueryRemoteWithFallbackAsync(rpc, queryObj, source);
    }

    private async Task<QuerySnapshot> FetchQueryRemoteWithFallbackAsync(
        FirestoreRpcClient rpc, Query queryObj, Source source) {
      QuerySpec spec = queryObj.Spec;
      try {
        List<MutableDocument> remoteDocs =
            await rpc.RunQueryAsync(spec, null, _lifetimeCts.Token).ConfigureAwait(false);
        QuerySnapshot snap;
        lock (_stateLock) {
          bool hadCommittedBeforeSync = false;
          var returnedSet = new HashSet<ResourcePath>();
          for (int i = 0; i < remoteDocs.Count; i++) {
            MutableDocument rd = remoteDocs[i];
            if (_remoteCache.TryGetValue(rd.Path, out MutableDocument existing) &&
                existing.HasCommittedMutations) {
              hadCommittedBeforeSync = true;
            }
            rd.HasCommittedMutations = false;
            rd.HasLocalMutations = false;
            _remoteCache[rd.Path] = rd;
            returnedSet.Add(rd.Path);
          }

          // Reconcile cached docs matching the query scope that no longer match on server.
          var staleKeys = new List<ResourcePath>();
          foreach (KeyValuePair<ResourcePath, MutableDocument> kv in _remoteCache) {
            if (!kv.Value.Exists || returnedSet.Contains(kv.Key)) continue;
            if (spec.IsCollectionGroup) {
              if (kv.Key.IsDocument &&
                  string.Equals(kv.Key.Parent.Id, spec.CollectionGroup, StringComparison.Ordinal)) {
                if (spec.EvaluateLocally(new[] { kv.Value }, _projectId, _databaseId).Count > 0) {
                  if (kv.Value.HasCommittedMutations) hadCommittedBeforeSync = true;
                  staleKeys.Add(kv.Key);
                }
              }
            } else if (spec.Path.IsImmediateParentOf(kv.Key)) {
              if (spec.EvaluateLocally(new[] { kv.Value }, _projectId, _databaseId).Count > 0) {
                if (kv.Value.HasCommittedMutations) hadCommittedBeforeSync = true;
                staleKeys.Add(kv.Key);
              }
            }
          }
          for (int i = 0; i < staleKeys.Count; i++) {
            _remoteCache[staleKeys[i]] =
                new MutableDocument(staleKeys[i], exists: false, fields: null, version: default);
          }
          MarkPersistenceDirtyLocked();

          // Re-apply any still-uncommitted local mutations on top of fresh server results.
          Dictionary<ResourcePath, MutableDocument> effectiveAll = ComputeAllEffectiveDocumentsLocked();
          List<MutableDocument> finalMatched =
              spec.EvaluateLocally(effectiveAll.Values, _projectId, _databaseId);
          for (int i = 0; i < finalMatched.Count; i++) {
            if (finalMatched[i].HasCommittedMutations) {
              hadCommittedBeforeSync = true;
              break;
            }
          }
          snap = BuildInitialQuerySnapshot(
              queryObj, finalMatched, isFromCache: false, hadCommittedMutations: hadCommittedBeforeSync);
          EnqueueListenerCallbacksLocked(changedPaths: null, checkSyncEvent: false);
        }
        return snap;
      } catch (FirestoreException fex) when (
          source == Source.Default && fex.ErrorCode == FirestoreError.Unavailable) {
        return EvaluateQueryFromCache(queryObj, isFromCache: true);
      }
    }

    private QuerySnapshot EvaluateQueryFromCache(Query queryObj, bool isFromCache) {
      lock (_stateLock) {
        Dictionary<ResourcePath, MutableDocument> effective = ComputeAllEffectiveDocumentsLocked();
        List<MutableDocument> matched =
            queryObj.Spec.EvaluateLocally(effective.Values, _projectId, _databaseId);
        bool hadCommitted = false;
        for (int i = 0; i < matched.Count; i++) {
          if (matched[i].HasCommittedMutations) {
            hadCommitted = true;
            break;
          }
        }
        return BuildInitialQuerySnapshot(
            queryObj, matched, isFromCache, hadCommittedMutations: hadCommitted);
      }
    }

    internal async Task<AggregateQuerySnapshot> GetAggregateCountSnapshotAsync(
        AggregateQuery aggQuery) {
      lock (_stateLock) {
        if (_disposed) {
          throw new FirestoreException(
              FirestoreError.FailedPrecondition, "This FirebaseFirestore instance has been disposed.");
        }
        if (_terminated) {
          throw new InvalidOperationException("This FirebaseFirestore instance has been terminated.");
        }
      }
      FirestoreRpcClient rpc = EnsureStarted();
      bool online;
      lock (_stateLock) online = _networkEnabled;
      if (!online) {
        throw new FirestoreException(
            FirestoreError.Unavailable, "Cannot execute AggregateQuery while offline.");
      }
      long count = await rpc.RunCountAggregationAsync(aggQuery.Query.Spec, _lifetimeCts.Token)
          .ConfigureAwait(false);
      return new AggregateQuerySnapshot(aggQuery, count);
    }

    #endregion

    #region Realtime Listeners (WebChannel + Optimistic Local View + Main-Thread Dispatch)

    internal ListenerRegistration ListenDocument(
        DocumentReference docRef,
        MetadataChanges metadataChanges,
        Action<DocumentSnapshot> callback) {
      Preconditions.CheckNotNull(callback, nameof(callback));
      lock (_stateLock) {
        if (_disposed) {
          // Per TestFirestoreDispose lines 4436-4437: Listen returns an inert registration.
          return ListenerRegistration.CreateInert();
        }
        if (_terminated) {
          throw new InvalidOperationException("This FirebaseFirestore instance has been terminated.");
        }
      }

      FirestoreException invalidPath = docRef.ResourcePath.ValidateForBackend();
      if (invalidPath != null) {
        return ListenerRegistration.CreateFaulted(invalidPath);
      }

      EnsureStarted();
      SynchronizationContext ctx = SynchronizationContext.Current;
      ActiveDocListener listener;

      lock (_stateLock) {
        if (ctx != null) _lastCapturedContext = ctx;
        int lid = _nextListenerId++;
        int tid = _nextTargetId;
        _nextTargetId += 2;
        listener = new ActiveDocListener(
            lid, tid, docRef.ResourcePath, metadataChanges, callback, ctx);
        _docListeners[lid] = listener;

        // If we already have a cached entry (or offline), emit immediately from cache!
        MutableDocument cached = ComputeEffectiveDocumentLocked(docRef.ResourcePath, out bool inCache);
        if (inCache || !_networkEnabled) {
          Action act = TryBuildDocListenerActionLocked(listener, cached);
          if (act != null) {
            EnqueueSingleCallbackLocked(act);
            EnqueueSyncListenersLocked();
          }
        }

        _watchStream.AddTarget(
            new FirestoreWatchStream.WatchTargetSpec(tid, docRef.ResourcePath));
      }

      return new ListenerRegistration(
          listener.ListenerTcs.Task,
          () => StopDocListener(listener));
    }

    private void StopDocListener(ActiveDocListener listener) {
      listener.Stopped = true;
      lock (_stateLock) {
        _docListeners.Remove(listener.ListenerId);
        _watchStream?.RemoveTarget(listener.WatchTargetId);
      }
      listener.ListenerTcs.TrySetResult(true);
    }

    internal ListenerRegistration ListenQuery(
        Query queryObj,
        MetadataChanges metadataChanges,
        Action<QuerySnapshot> callback) {
      Preconditions.CheckNotNull(callback, nameof(callback));
      lock (_stateLock) {
        if (_disposed) {
          return ListenerRegistration.CreateInert();
        }
        if (_terminated) {
          throw new InvalidOperationException("This FirebaseFirestore instance has been terminated.");
        }
      }

      QuerySpec spec = queryObj.Spec;
      if (!spec.IsCollectionGroup) {
        FirestoreException invalidPath = spec.Path.ValidateForBackend();
        if (invalidPath != null) {
          return ListenerRegistration.CreateFaulted(invalidPath);
        }
      }
      spec.ValidateForExecution();

      EnsureStarted();
      SynchronizationContext ctx = SynchronizationContext.Current;
      ActiveQueryListener listener;

      lock (_stateLock) {
        if (ctx != null) _lastCapturedContext = ctx;
        int lid = _nextListenerId++;
        int tid = _nextTargetId;
        _nextTargetId += 2;
        listener = new ActiveQueryListener(
            lid, tid, queryObj, spec, metadataChanges, callback, ctx);
        _queryListeners[lid] = listener;

        // Emit initial cached snapshot immediately if offline OR if local cache already has matching docs!
        Dictionary<ResourcePath, MutableDocument> effective = ComputeAllEffectiveDocumentsLocked();
        List<MutableDocument> matched =
            spec.EvaluateLocally(effective.Values, _projectId, _databaseId);
        if (!_networkEnabled || matched.Count > 0) {
          Action act = TryBuildQueryListenerActionLocked(listener, matched);
          if (act != null) {
            EnqueueSingleCallbackLocked(act);
            EnqueueSyncListenersLocked();
          }
        }

        _watchStream.AddTarget(new FirestoreWatchStream.WatchTargetSpec(tid, spec));
      }

      return new ListenerRegistration(
          listener.ListenerTcs.Task,
          () => StopQueryListener(listener));
    }

    private void StopQueryListener(ActiveQueryListener listener) {
      listener.Stopped = true;
      lock (_stateLock) {
        _queryListeners.Remove(listener.ListenerId);
        _watchStream?.RemoveTarget(listener.WatchTargetId);
      }
      listener.ListenerTcs.TrySetResult(true);
    }

    internal ListenerRegistration ListenSnapshotsInSync(Action callback) {
      Preconditions.CheckNotNull(callback, nameof(callback));
      lock (_stateLock) {
        if (_disposed) {
          throw new InvalidOperationException("This FirebaseFirestore instance has been disposed.");
        }
        if (_terminated) {
#if UNITY_ANDROID
          throw new InvalidOperationException("This FirebaseFirestore instance has been terminated.");
#else
          return ListenerRegistration.CreateInert();
#endif
        }
      }

      EnsureStarted();
      SynchronizationContext ctx = SynchronizationContext.Current;
      ActiveSyncListener syncListener;

      lock (_stateLock) {
        if (ctx != null) _lastCapturedContext = ctx;
        int lid = _nextListenerId++;
        syncListener = new ActiveSyncListener(lid, callback, ctx);
        _syncListeners[lid] = syncListener;
        EnqueueSingleCallbackLocked(() => {
          if (!syncListener.Stopped) syncListener.Callback();
        });
      }

      return new ListenerRegistration(
          syncListener.ListenerTcs.Task,
          () => {
            syncListener.Stopped = true;
            lock (_stateLock) _syncListeners.Remove(syncListener.ListenerId);
            syncListener.ListenerTcs.TrySetResult(true);
          });
    }

    private void EnqueueSyncListenersLocked() {
      if (_syncListeners.Count == 0) return;
      bool added = false;
      foreach (ActiveSyncListener sl in _syncListeners.Values) {
        if (sl.Stopped) continue;
        ActiveSyncListener captured = sl;
        _callbackQueue.Enqueue(() => {
          if (!captured.Stopped) captured.Callback();
        });
        added = true;
      }
      if (added) {
        ScheduleCallbackPumpLocked();
      }
    }

    private void OnWatchDocChange(
        MutableDocument doc, IReadOnlyList<int> targetIds, IReadOnlyList<int> removedTargetIds) {
      lock (_stateLock) {
        if (_terminated || _disposed) return;
        doc.HasCommittedMutations = false;
        doc.HasLocalMutations = false;
        _remoteCache[doc.Path] = doc;
        MarkPersistenceDirtyLocked();

        foreach (ActiveQueryListener ql in _queryListeners.Values) {
          if (targetIds.Contains(ql.WatchTargetId)) {
            ql.RemoteMatchedKeys.Add(doc.Path);
          }
          if (removedTargetIds.Contains(ql.WatchTargetId)) {
            ql.RemoteMatchedKeys.Remove(doc.Path);
          }
        }
        _pendingWatchPaths.Add(doc.Path);
      }
    }

    private void OnWatchDocRemoveOrDelete(ResourcePath path, IReadOnlyList<int> removedTargetIds) {
      lock (_stateLock) {
        if (_terminated || _disposed) return;
        // If removed from all targets or deleted, mark deleted in remote cache unless still matched elsewhere.
        bool anyTargetStillHas = false;
        foreach (ActiveQueryListener ql in _queryListeners.Values) {
          if (removedTargetIds.Count == 0 || removedTargetIds.Contains(ql.WatchTargetId)) {
            ql.RemoteMatchedKeys.Remove(path);
          } else if (ql.RemoteMatchedKeys.Contains(path)) {
            anyTargetStillHas = true;
          }
        }
        if (!anyTargetStillHas) {
          _remoteCache[path] = new MutableDocument(path, exists: false, fields: null, version: default);
          MarkPersistenceDirtyLocked();
        }
        _pendingWatchPaths.Add(path);
      }
    }

    private void OnWatchTargetCurrent(IReadOnlyList<int> targetIds, string resumeToken) {
      lock (_stateLock) {
        if (_terminated || _disposed) return;
        bool allTargets = targetIds == null || targetIds.Count == 0;
        foreach (ActiveDocListener dl in _docListeners.Values) {
          if (allTargets || targetIds.Contains(dl.WatchTargetId)) {
            dl.TargetIsSynced = true;
            if (!_remoteCache.ContainsKey(dl.Path)) {
              _remoteCache[dl.Path] =
                  new MutableDocument(dl.Path, exists: false, fields: null, version: default);
            } else {
              _remoteCache[dl.Path].HasCommittedMutations = false;
            }
          }
        }
        foreach (ActiveQueryListener ql in _queryListeners.Values) {
          if (allTargets || targetIds.Contains(ql.WatchTargetId)) {
            ql.TargetIsSynced = true;
            ReconcileRemoteQueryTargetCacheLocked(ql);
          }
        }
        _pendingWatchSyncEvent = true;
      }
    }

    private void OnWatchBatchFlush() {
      lock (_stateLock) {
        if (_terminated || _disposed) return;
        if (_pendingWatchPaths.Count == 0 && !_pendingWatchSyncEvent) return;
        bool syncEvent = _pendingWatchSyncEvent;
        IReadOnlyList<ResourcePath> paths = syncEvent ? null : _pendingWatchPaths.ToList();
        _pendingWatchPaths.Clear();
        _pendingWatchSyncEvent = false;
        EnqueueListenerCallbacksLocked(
            changedPaths: paths, checkSyncEvent: syncEvent, allowUnsyncedLocal: false);
      }
    }

    private void ReconcileRemoteQueryTargetCacheLocked(ActiveQueryListener ql) {
      var toMarkMissing = new List<ResourcePath>();
      foreach (KeyValuePair<ResourcePath, MutableDocument> kv in _remoteCache) {
        if (!kv.Value.Exists) continue;
        kv.Value.HasCommittedMutations = false;
        if (ql.RemoteMatchedKeys.Contains(kv.Key)) continue;
        bool inScope = ql.Spec.IsCollectionGroup
            ? (kv.Key.IsDocument &&
               string.Equals(kv.Key.Parent.Id, ql.Spec.CollectionGroup, StringComparison.Ordinal))
            : ql.Spec.Path.IsImmediateParentOf(kv.Key);
        if (inScope &&
            ql.Spec.EvaluateLocally(new[] { kv.Value }, _projectId, _databaseId).Count > 0) {
          toMarkMissing.Add(kv.Key);
        }
      }
      for (int i = 0; i < toMarkMissing.Count; i++) {
        _remoteCache[toMarkMissing[i]] =
            new MutableDocument(toMarkMissing[i], exists: false, fields: null, version: default);
      }
    }

    private void OnWatchTargetReset(IReadOnlyList<int> targetIds) {
      lock (_stateLock) {
        bool all = targetIds == null || targetIds.Count == 0;
        foreach (ActiveQueryListener ql in _queryListeners.Values) {
          if (all || targetIds.Contains(ql.WatchTargetId)) {
            ql.RemoteMatchedKeys.Clear();
            ql.TargetIsSynced = false;
          }
        }
      }
    }

    private void OnWatchExistenceFilter(int targetId, int expectedCount) {
      bool mismatch = false;
      QuerySpec mismatchQuery = null;
      ActiveQueryListener targetListener = null;
      lock (_stateLock) {
        foreach (ActiveQueryListener ql in _queryListeners.Values) {
          if (ql.WatchTargetId == targetId) {
            if (ql.TargetIsSynced && ql.RemoteMatchedKeys.Count != expectedCount) {
              mismatch = true;
              mismatchQuery = ql.Spec;
              targetListener = ql;
            }
            break;
          }
        }
      }
      if (mismatch && mismatchQuery != null && targetListener != null) {
        // Re-sync target via unary RunQuery when existence filter count diverges.
        _ = Task.Run(async () => {
          try {
            await FetchQueryRemoteWithFallbackAsync(_rpc, targetListener.QueryObj, Source.Server)
                .ConfigureAwait(false);
          } catch (Exception) {
            // Will retry on next watch cycle.
          }
        });
      }
    }

    private void OnWatchTargetError(IReadOnlyList<int> targetIds, FirestoreException error) {
      var docToFail = new List<ActiveDocListener>();
      var queryToFail = new List<ActiveQueryListener>();
      lock (_stateLock) {
        foreach (int tid in targetIds) {
          foreach (ActiveDocListener dl in _docListeners.Values.ToList()) {
            if (dl.WatchTargetId == tid) {
              dl.Stopped = true;
              _docListeners.Remove(dl.ListenerId);
              docToFail.Add(dl);
            }
          }
          foreach (ActiveQueryListener ql in _queryListeners.Values.ToList()) {
            if (ql.WatchTargetId == tid) {
              ql.Stopped = true;
              _queryListeners.Remove(ql.ListenerId);
              queryToFail.Add(ql);
            }
          }
        }
      }
      for (int i = 0; i < docToFail.Count; i++) {
        docToFail[i].ListenerTcs.TrySetException(error);
      }
      for (int i = 0; i < queryToFail.Count; i++) {
        queryToFail[i].ListenerTcs.TrySetException(error);
      }
    }

    private void EnqueueListenerCallbacksLocked(
        IReadOnlyList<ResourcePath> changedPaths,
        bool checkSyncEvent,
        bool allowUnsyncedLocal = false) {
      if (_docListeners.Count == 0 && _queryListeners.Count == 0 && _syncListeners.Count == 0) {
        return;
      }

      int snapshotEventsAdded = 0;
      Dictionary<ResourcePath, MutableDocument> allEffective = null;

      foreach (ActiveDocListener dl in _docListeners.Values) {
        if (dl.Stopped) continue;
        if (_networkEnabled && !dl.TargetIsSynced && !allowUnsyncedLocal) {
          continue;
        }
        MutableDocument doc = ComputeEffectiveDocumentLocked(dl.Path, out bool inCache);
        if (!inCache && _networkEnabled && !dl.TargetIsSynced) continue;
        Action act = TryBuildDocListenerActionLocked(dl, doc);
        if (act != null) {
          _callbackQueue.Enqueue(act);
          snapshotEventsAdded++;
        }
      }

      if (_queryListeners.Count > 0) {
        allEffective = ComputeAllEffectiveDocumentsLocked();
        foreach (ActiveQueryListener ql in _queryListeners.Values) {
          if (ql.Stopped) continue;
          if (_networkEnabled && !ql.TargetIsSynced && !allowUnsyncedLocal) {
            continue;
          }
          List<MutableDocument> matched =
              ql.Spec.EvaluateLocally(allEffective.Values, _projectId, _databaseId);
          Action act = TryBuildQueryListenerActionLocked(ql, matched);
          if (act != null) {
            _callbackQueue.Enqueue(act);
            snapshotEventsAdded++;
          }
        }
      }

      if (snapshotEventsAdded > 0) {
        ScheduleCallbackPumpLocked();
        EnqueueSyncListenersLocked();
      }
    }

    private void EnqueueSingleCallbackLocked(Action act) {
      if (act == null) return;
      _callbackQueue.Enqueue(act);
      ScheduleCallbackPumpLocked();
    }

    private Action TryBuildDocListenerActionLocked(ActiveDocListener dl, MutableDocument doc) {
      bool isFromCache = !_networkEnabled || !dl.TargetIsSynced;
      bool exists = doc != null && doc.Exists;
      bool hasPending = doc != null && doc.HasPendingWrites;
      var metadata = new SnapshotMetadata(hasPending, isFromCache);
      Dictionary<string, FirestoreValue> fieldsClone = exists
          ? FirestoreValue.CloneMapDeep(doc.Fields)
          : new Dictionary<string, FirestoreValue>(StringComparer.Ordinal);

      if (dl.HasEmitted) {
        bool dataSame = dl.LastExists == exists &&
                        (!exists || MapsEqualExact(dl.LastFields, fieldsClone));
        bool metaSame = dl.LastMetadata.Equals(metadata);
        if (dataSame && metaSame) return null;
        if (dataSame && dl.MetadataChanges == MetadataChanges.Exclude) {
          dl.LastMetadata = metadata;
          return null;
        }
      }

      dl.HasEmitted = true;
      dl.LastExists = exists;
      dl.LastFields = fieldsClone;
      dl.LastMetadata = metadata;

      var docRef = new DocumentReference(dl.Path, _owner);
      var snapshot = new DocumentSnapshot(docRef, exists ? fieldsClone : null, exists, metadata);
      return () => {
        if (!dl.Stopped) dl.Callback(snapshot);
      };
    }

    private Action TryBuildQueryListenerActionLocked(
        ActiveQueryListener ql, List<MutableDocument> matched) {
      bool isFromCache = !_networkEnabled || !ql.TargetIsSynced;
      bool queryHasPendingWrites = false;
      bool queryHasCommittedMutations = false;
      var currDocSnaps = new List<DocumentSnapshot>(matched.Count);
      for (int i = 0; i < matched.Count; i++) {
        MutableDocument md = matched[i];
        if (md.HasPendingWrites) queryHasPendingWrites = true;
        if (md.HasCommittedMutations) queryHasCommittedMutations = true;
        var docMeta = new SnapshotMetadata(md.HasPendingWrites, isFromCache);
        currDocSnaps.Add(new DocumentSnapshot(
            new DocumentReference(md.Path, _owner),
            FirestoreValue.CloneMapDeep(md.Fields),
            exists: true,
            metadata: docMeta));
      }

      var queryMeta = new SnapshotMetadata(queryHasPendingWrites, isFromCache);
      List<DocumentChange> excludeChanges;
      List<DocumentChange> includeChanges;

      if (!ql.HasEmitted) {
        excludeChanges = BuildAllAddedChanges(currDocSnaps);
        includeChanges = excludeChanges;
      } else {
        ComputeQueryDiffChanges(
            ql.LastDocs, currDocSnaps, out excludeChanges, out includeChanges);
        bool metaChanged = !ql.LastMetadata.Equals(queryMeta);
        if (excludeChanges.Count == 0 && includeChanges.Count == 0 && !metaChanged) {
          return null;
        }
        if (excludeChanges.Count == 0 && ql.MetadataChanges == MetadataChanges.Exclude) {
          ql.LastDocs = currDocSnaps;
          ql.LastMetadata = queryMeta;
          ql.LastHadCommittedMutations = queryHasCommittedMutations;
          return null;
        }
      }

      ql.HasEmitted = true;
      ql.LastDocs = currDocSnaps;
      ql.LastMetadata = queryMeta;
      ql.LastHadCommittedMutations = queryHasCommittedMutations;

      var qSnap = new QuerySnapshot(
          ql.QueryObj,
          currDocSnaps,
          excludeChanges,
          includeChanges,
          queryMeta,
          hadCommittedMutations: queryHasCommittedMutations);
      return () => {
        if (!ql.Stopped) ql.Callback(qSnap);
      };
    }

    internal static void ComputeQueryDiffChanges(
        IReadOnlyList<DocumentSnapshot> prevDocs,
        IReadOnlyList<DocumentSnapshot> currDocs,
        out List<DocumentChange> excludeChanges,
        out List<DocumentChange> includeChanges) {
      excludeChanges = ComputeQueryDiffSingleMode(prevDocs, currDocs, includeMetadataOnly: false);
      includeChanges = ComputeQueryDiffSingleMode(prevDocs, currDocs, includeMetadataOnly: true);
    }

    private static List<DocumentChange> ComputeQueryDiffSingleMode(
        IReadOnlyList<DocumentSnapshot> prevDocs,
        IReadOnlyList<DocumentSnapshot> currDocs,
        bool includeMetadataOnly) {
      var changes = new List<DocumentChange>();
      var prevByPath = new Dictionary<ResourcePath, DocumentSnapshot>(prevDocs.Count);
      var working = new List<ResourcePath>(prevDocs.Count);
      for (int i = 0; i < prevDocs.Count; i++) {
        ResourcePath p = prevDocs[i].Reference.ResourcePath;
        prevByPath[p] = prevDocs[i];
        working.Add(p);
      }

      var currSet = new HashSet<ResourcePath>();
      for (int i = 0; i < currDocs.Count; i++) {
        currSet.Add(currDocs[i].Reference.ResourcePath);
      }

      // 1. Removals in old order.
      for (int i = 0; i < prevDocs.Count; i++) {
        ResourcePath p = prevDocs[i].Reference.ResourcePath;
        if (!currSet.Contains(p)) {
          int oldIdx = working.IndexOf(p);
          working.RemoveAt(oldIdx);
          changes.Add(new DocumentChange(
              prevDocs[i], DocumentChange.Type.Removed, oldIndex: oldIdx, newIndex: -1));
        }
      }

      // 2. Additions and Modifications in new target sort order.
      for (int newIdx = 0; newIdx < currDocs.Count; newIdx++) {
        DocumentSnapshot curr = currDocs[newIdx];
        ResourcePath p = curr.Reference.ResourcePath;
        if (!prevByPath.TryGetValue(p, out DocumentSnapshot prev)) {
          working.Insert(newIdx, p);
          changes.Add(new DocumentChange(
              curr, DocumentChange.Type.Added, oldIndex: -1, newIndex: newIdx));
        } else {
          int oldIdx = working.IndexOf(p);
          bool dataChanged = !MapsEqualExact(prev.InternalFields, curr.InternalFields);
          bool moved = oldIdx != newIdx;
          // Note: per TestDocumentChangeEqualsAndGetHashCode lines 4290-4306, a metadata-only
          // DocumentChange is emitted ONLY when the document's own HasPendingWrites flag changed
          // (NOT when query-level IsFromCache flipped true -> false with HasPendingWrites == false)!
          bool docPendingChanged =
              includeMetadataOnly &&
              prev.Metadata.HasPendingWrites != curr.Metadata.HasPendingWrites;

          if (moved) {
            working.RemoveAt(oldIdx);
            working.Insert(newIdx, p);
          }
          if (dataChanged || moved || docPendingChanged) {
            changes.Add(new DocumentChange(
                curr, DocumentChange.Type.Modified, oldIndex: oldIdx, newIndex: newIdx));
          }
        }
      }

      return changes;
    }

    private static List<DocumentChange> BuildAllAddedChanges(IReadOnlyList<DocumentSnapshot> docs) {
      var list = new List<DocumentChange>(docs.Count);
      for (int i = 0; i < docs.Count; i++) {
        list.Add(new DocumentChange(docs[i], DocumentChange.Type.Added, oldIndex: -1, newIndex: i));
      }
      return list;
    }

    private QuerySnapshot BuildInitialQuerySnapshot(
        Query queryObj, List<MutableDocument> docs, bool isFromCache, bool hadCommittedMutations = false) {
      bool anyPending = false;
      var snaps = new List<DocumentSnapshot>(docs.Count);
      for (int i = 0; i < docs.Count; i++) {
        MutableDocument md = docs[i];
        if (md.HasPendingWrites) anyPending = true;
        var docMeta = new SnapshotMetadata(md.HasPendingWrites, isFromCache);
        snaps.Add(new DocumentSnapshot(
            new DocumentReference(md.Path, _owner),
            FirestoreValue.CloneMapDeep(md.Fields),
            exists: true,
            metadata: docMeta));
      }
      List<DocumentChange> added = BuildAllAddedChanges(snaps);
      return new QuerySnapshot(
          queryObj, snaps, added, added, new SnapshotMetadata(anyPending, isFromCache), hadCommittedMutations);
    }

    private DocumentSnapshot BuildDocumentSnapshot(
        DocumentReference docRef, MutableDocument doc, bool isFromCache) {
      bool exists = doc != null && doc.Exists;
      bool pending = doc != null && doc.HasPendingWrites;
      return new DocumentSnapshot(
          docRef,
          exists ? FirestoreValue.CloneMapDeep(doc.Fields) : null,
          exists,
          new SnapshotMetadata(pending, isFromCache));
    }

    internal static bool MapsEqualExact(
        IDictionary<string, FirestoreValue> a, IDictionary<string, FirestoreValue> b) {
      if (ReferenceEquals(a, b)) return true;
      if (a == null || b == null || a.Count != b.Count) return false;
      foreach (KeyValuePair<string, FirestoreValue> kv in a) {
        if (!b.TryGetValue(kv.Key, out FirestoreValue bv) || !kv.Value.Equals(bv)) {
          return false;
        }
      }
      return true;
    }

    private void ScheduleCallbackPumpLocked() {
      if (_callbackPumpScheduled || _callbackQueue.Count == 0) return;
      _callbackPumpScheduled = true;
      SynchronizationContext fallbackCtx = _lastCapturedContext;
      if (Firebase.Platform.FirebaseHandler.DefaultInstance != null) {
        if (Firebase.Platform.FirebaseHandler.DefaultInstance.IsMainThread()) {
          ThreadPool.QueueUserWorkItem(_ => {
            _ = Firebase.Platform.FirebaseHandler.RunOnMainThreadAsync(DrainCallbackQueueOnMainThread);
          });
        } else {
          _ = Firebase.Platform.FirebaseHandler.RunOnMainThreadAsync(DrainCallbackQueueOnMainThread);
        }
        return;
      }
      if (fallbackCtx != null) {
        fallbackCtx.Post(_ => DrainCallbackQueueOnMainThread(), null);
        return;
      }
      Task.Run(() => DrainCallbackQueueOnMainThread());
    }

    private bool DrainCallbackQueueOnMainThread() {
      try {
        while (true) {
          Action next;
          lock (_stateLock) {
            if (_callbackQueue.Count == 0) {
              _callbackPumpScheduled = false;
              return true;
            }
            next = _callbackQueue.Dequeue();
          }
          next();
        }
      } catch {
        lock (_stateLock) {
          _callbackPumpScheduled = false;
          if (_callbackQueue.Count > 0) {
            ScheduleCallbackPumpLocked();
          }
        }
        throw;
      }
    }

    private static Task<T> RunFuncOnMainThreadAsync<T>(
        SynchronizationContext capturedCtx, Func<Task<T>> callback) {
      if (Firebase.Platform.FirebaseHandler.DefaultInstance != null) {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Firebase.Platform.FirebaseHandler.RunOnMainThreadAsync(() => {
          try {
            Task<T> inner = callback();
            if (inner == null) {
              tcs.TrySetException(
                  new InvalidOperationException("Transaction callback returned a null Task."));
            } else {
              inner.ContinueWith(t => {
                if (t.IsFaulted && t.Exception != null) {
                  tcs.TrySetException(Util.FlattenException(t.Exception));
                } else if (t.IsCanceled) {
                  tcs.TrySetCanceled();
                } else {
                  tcs.TrySetResult(t.Result);
                }
              }, TaskScheduler.Default);
            }
          } catch (Exception ex) {
            tcs.TrySetException(ex);
          }
          return true;
        });
        return tcs.Task;
      }

      if (capturedCtx != null && SynchronizationContext.Current != capturedCtx) {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        capturedCtx.Post(_ => {
          try {
            Task<T> inner = callback();
            if (inner == null) {
              tcs.TrySetException(
                  new InvalidOperationException("Transaction callback returned a null Task."));
            } else {
              inner.ContinueWith(t => {
                if (t.IsFaulted && t.Exception != null) {
                  tcs.TrySetException(Util.FlattenException(t.Exception));
                } else if (t.IsCanceled) {
                  tcs.TrySetCanceled();
                } else {
                  tcs.TrySetResult(t.Result);
                }
              }, TaskScheduler.Default);
            }
          } catch (Exception ex) {
            tcs.TrySetException(ex);
          }
        }, null);
        return tcs.Task;
      }

      Task<T> direct = callback();
      if (direct == null) {
        return Util.TaskFromException<T>(
            new InvalidOperationException("Transaction callback returned a null Task."));
      }
      return direct;
    }

    #endregion

    #region Transactions

    internal Task<T> RunTransactionAsync<T>(
        TransactionOptions options, Func<Transaction, Task<T>> callback) {
      Preconditions.CheckNotNull(options, nameof(options));
      Preconditions.CheckNotNull(callback, nameof(callback));
      lock (_stateLock) {
        if (_disposed) {
          throw new InvalidOperationException("This FirebaseFirestore instance has been disposed.");
        }
        if (_terminated) {
          return Util.TaskFromException<T>(
              new InvalidOperationException("This FirebaseFirestore instance has been terminated."));
        }
      }

      FirestoreRpcClient rpc = EnsureStarted();
      SynchronizationContext capturedCtx = SynchronizationContext.Current;
      int maxAttempts = Math.Max(1, options.MaxAttempts);
      return ExecuteTransactionLoopAsync(rpc, maxAttempts, capturedCtx, callback);
    }

    private async Task<T> ExecuteTransactionLoopAsync<T>(
        FirestoreRpcClient rpc,
        int maxAttempts,
        SynchronizationContext capturedCtx,
        Func<Transaction, Task<T>> callback) {
      int backoffMs = 100;
      Exception lastError = null;

      for (int attempt = 1; attempt <= maxAttempts; attempt++) {
        bool online;
        lock (_stateLock) online = _networkEnabled && !_terminated && !_disposed;
        if (!online) {
          throw new FirestoreException(
              FirestoreError.Unavailable, "Transactions cannot be executed while offline.");
        }

        if (attempt > 1) {
          int jitter = (Math.Abs(Environment.TickCount ^ Thread.CurrentThread.ManagedThreadId ^ attempt) % Math.Max(20, backoffMs));
          await Task.Delay((backoffMs / 2) + jitter, _lifetimeCts.Token).ConfigureAwait(false);
          backoffMs = Math.Min(backoffMs * 2, 1500);
        }

        string txId;
        try {
          txId = await rpc.BeginTransactionAsync(_lifetimeCts.Token).ConfigureAwait(false);
        } catch (FirestoreException fex) when (IsRetryableTransactionError(fex.ErrorCode) && attempt < maxAttempts) {
          lastError = fex;
          continue;
        }

        var tx = new Transaction(_owner, rpc, txId, _lifetimeCts.Token);
        T userResult;
        try {
          userResult = await RunFuncOnMainThreadAsync(capturedCtx, () => callback(tx))
              .ConfigureAwait(false);
        } catch (Exception userEx) {
          await rpc.RollbackBestEffortAsync(txId, _lifetimeCts.Token).ConfigureAwait(false);
          if (userEx is FirestoreException ufex) {
            if (IsRetryableTransactionError(ufex.ErrorCode) && attempt < maxAttempts) {
              lastError = ufex;
              continue;
            }
            if (ufex.ErrorCode == FirestoreError.Aborted) {
              throw new FirestoreException(
                  FirestoreError.FailedPrecondition,
                  "Transaction failed all retries: " + ufex.Message);
            }
          }
          throw;
        } finally {
          tx.MarkDisposed();
        }

        bool stillActive;
        lock (_stateLock) stillActive = !_terminated && !_disposed;
        if (!stillActive) {
          await rpc.RollbackBestEffortAsync(txId, _lifetimeCts.Token).ConfigureAwait(false);
          throw new FirestoreException(
              FirestoreError.Cancelled, "This FirebaseFirestore instance has been terminated or disposed.");
        }

        try {
          CommitBatchResponse commitResp =
              await rpc.CommitAsync(tx.PendingMutations, txId, _lifetimeCts.Token)
                  .ConfigureAwait(false);

          lock (_stateLock) {
            var pseudoBatch = new PendingMutationBatch(
                0, commitResp.CommitTime, tx.PendingMutations);
            ApplyCommittedBatchToRemoteCacheLocked(pseudoBatch, commitResp);
            MarkPersistenceDirtyLocked();
            EnqueueListenerCallbacksLocked(
                tx.PendingMutations.Select(m => m.Path).ToList(), checkSyncEvent: false);
          }
          return userResult;
        } catch (FirestoreException commitEx) {
          if (commitEx.ErrorCode != FirestoreError.Aborted) {
            await rpc.RollbackBestEffortAsync(txId, _lifetimeCts.Token).ConfigureAwait(false);
          }
          if (IsRetryableTransactionError(commitEx.ErrorCode) && attempt < maxAttempts) {
            lastError = commitEx;
            continue;
          }
          if (commitEx.ErrorCode == FirestoreError.Aborted) {
            throw new FirestoreException(
                FirestoreError.FailedPrecondition,
                "Transaction failed all retries: " + commitEx.Message);
          }
          throw;
        }
      }

      if (lastError is FirestoreException lfex && lfex.ErrorCode == FirestoreError.Aborted) {
        throw new FirestoreException(
            FirestoreError.FailedPrecondition,
            "Transaction failed all retries: " + lfex.Message);
      }
      throw lastError ?? new FirestoreException(
          FirestoreError.FailedPrecondition, "Transaction failed after maximum retry attempts.");
    }

    private static bool IsRetryableTransactionError(FirestoreError code) =>
        code == FirestoreError.Aborted ||
        code == FirestoreError.Unavailable ||
        code == FirestoreError.DeadlineExceeded ||
        code == FirestoreError.ResourceExhausted ||
        code == FirestoreError.Internal ||
        code == FirestoreError.Unknown;

    #endregion

    #region Bundles (LoadBundleAsync & GetNamedQueryAsync)

    internal Task<LoadBundleTaskProgress> LoadBundleAsync(
        byte[] bundleData, EventHandler<LoadBundleTaskProgress> progressHandler) {
      Preconditions.CheckNotNull(bundleData, nameof(bundleData));
      lock (_stateLock) {
        if (_disposed || _terminated) {
          throw new InvalidOperationException(
              "This FirebaseFirestore instance has been terminated or disposed.");
        }
      }
      EnsureStarted();
      SynchronizationContext ctx = SynchronizationContext.Current;
      if (Firebase.Platform.FirebaseHandler.DefaultInstance != null &&
          Firebase.Platform.FirebaseHandler.DefaultInstance.IsMainThread()) {
        try {
          return Task.FromResult(ExecuteLoadBundleAsync(bundleData, progressHandler, ctx));
        } catch (Exception ex) {
          return Util.TaskFromException<LoadBundleTaskProgress>(ex);
        }
      }
      return Task.Run(() => ExecuteLoadBundleAsync(bundleData, progressHandler, ctx));
    }

    private LoadBundleTaskProgress ExecuteLoadBundleAsync(
        byte[] bundleData,
        EventHandler<LoadBundleTaskProgress> progressHandler,
        SynchronizationContext ctx) {
      int offset = 0;
      if (!TryReadLengthPrefixedElement(bundleData, ref offset, out string metaJson)) {
        EmitBundleProgress(
            progressHandler, ctx,
            new LoadBundleTaskProgress(0, 0, 0, 0, LoadBundleTaskProgress.LoadBundleTaskState.Error));
        throw new FirestoreException(FirestoreError.InvalidArgument, "Invalid bundle: missing metadata.");
      }

      Dictionary<string, object> metaWrapper = JsonHelper.ParseObject(metaJson);
      Dictionary<string, object> metaObj = JsonHelper.GetMap(metaWrapper, "metadata");
      if (metaObj == null) {
        EmitBundleProgress(
            progressHandler, ctx,
            new LoadBundleTaskProgress(0, 0, 0, 0, LoadBundleTaskProgress.LoadBundleTaskState.Error));
        throw new FirestoreException(FirestoreError.InvalidArgument, "Invalid bundle metadata element.");
      }

      string bundleId = JsonHelper.GetString(metaObj, "id", string.Empty);
      Timestamp createTime = ParseBundleTimestamp(metaObj, "createTime");
      int totalDocs = (int)JsonHelper.GetLong(metaObj, "totalDocuments", 0);
      long totalBytes = JsonHelper.GetLong(metaObj, "totalBytes", 0);
      int contentStartOffset = offset;

      lock (_stateLock) {
        if (_loadedBundles.TryGetValue(bundleId, out Timestamp prevTime) &&
            prevTime.CompareTo(createTime) >= 0) {
          var skippedProgress = new LoadBundleTaskProgress(
              totalDocs, totalDocs, totalBytes, totalBytes,
              LoadBundleTaskProgress.LoadBundleTaskState.Success);
          EmitBundleProgress(progressHandler, ctx, skippedProgress);
          return skippedProgress;
        }
      }

      // Emit initial InProgress (0 documents loaded).
      EmitBundleProgress(
          progressHandler, ctx,
          new LoadBundleTaskProgress(
              0, totalDocs, 0, totalBytes, LoadBundleTaskProgress.LoadBundleTaskState.InProgress));

      int docsLoaded = 0;
      ResourcePath pendingDocPath = null;
      bool pendingDocExists = false;
      Timestamp pendingReadTime = default;
      var parsedNamedQueries = new List<(string Name, QuerySpec Spec)>();
      var parsedDocs = new List<MutableDocument>();

      try {
        while (offset < bundleData.Length) {
          if (!TryReadLengthPrefixedElement(bundleData, ref offset, out string elemJson)) {
            throw new FirestoreException(
                FirestoreError.InvalidArgument, "Truncated bundle element.");
          }
          long bytesLoaded = offset - contentStartOffset;
          Dictionary<string, object> elem = JsonHelper.ParseObject(elemJson);
          if (elem == null) {
            throw new FirestoreException(FirestoreError.InvalidArgument, "Malformed bundle JSON.");
          }

          if (elem.TryGetValue("namedQuery", out object nqRaw) &&
              nqRaw is Dictionary<string, object> nqMap) {
            string qName = JsonHelper.GetString(nqMap, "name", string.Empty);
            Dictionary<string, object> bq = JsonHelper.GetMap(nqMap, "bundledQuery");
            QuerySpec qSpec = ParseBundledQuery(bq);
            parsedNamedQueries.Add((qName, qSpec));
          } else if (elem.TryGetValue("documentMetadata", out object dmRaw) &&
                     dmRaw is Dictionary<string, object> dmMap) {
            string docResName = JsonHelper.GetString(dmMap, "name");
            ValidateBundleResourceDatabase(docResName, out pendingDocPath);
            pendingDocExists = JsonHelper.GetBool(dmMap, "exists", false);
            pendingReadTime = ParseBundleTimestamp(dmMap, "readTime");
            if (!pendingDocExists) {
              parsedDocs.Add(new MutableDocument(
                  pendingDocPath, exists: false, fields: null, version: pendingReadTime));
              docsLoaded++;
              EmitBundleProgress(
                  progressHandler, ctx,
                  new LoadBundleTaskProgress(
                      docsLoaded, totalDocs, bytesLoaded, totalBytes,
                      LoadBundleTaskProgress.LoadBundleTaskState.InProgress));
              pendingDocPath = null;
            }
          } else if (elem.TryGetValue("document", out object docRaw) &&
                     docRaw is Dictionary<string, object> docMap) {
            string docResName = JsonHelper.GetString(docMap, "name");
            ValidateBundleResourceDatabase(docResName, out _);
            MutableDocument mdoc = FirestoreJson.ParseMiniJsonDocument(docMap);
            if (mdoc == null) {
              throw new FirestoreException(FirestoreError.InvalidArgument, "Invalid bundled document.");
            }
            parsedDocs.Add(mdoc);
            docsLoaded++;
            EmitBundleProgress(
                progressHandler, ctx,
                new LoadBundleTaskProgress(
                    docsLoaded, totalDocs, bytesLoaded, totalBytes,
                    LoadBundleTaskProgress.LoadBundleTaskState.InProgress));
            pendingDocPath = null;
          }
        }
      } catch (Exception ex) {
        long errBytes = Math.Max(0, offset - contentStartOffset);
        EmitBundleProgress(
            progressHandler, ctx,
            new LoadBundleTaskProgress(
                docsLoaded, totalDocs, errBytes, totalBytes,
                LoadBundleTaskProgress.LoadBundleTaskState.Error));
        if (ex is FirestoreException) throw;
        throw new FirestoreException(FirestoreError.InvalidArgument, ex.Message);
      }

      lock (_stateLock) {
        for (int i = 0; i < parsedDocs.Count; i++) {
          MutableDocument bd = parsedDocs[i];
          if (_remoteCache.TryGetValue(bd.Path, out MutableDocument existing) &&
              existing.Version.CompareTo(bd.Version) >= 0) {
            // Do not overwrite newer backend documents already in cache!
            continue;
          }
          _remoteCache[bd.Path] = bd;
        }
        for (int i = 0; i < parsedNamedQueries.Count; i++) {
          _namedQueries[parsedNamedQueries[i].Name] = parsedNamedQueries[i].Spec;
        }
        _loadedBundles[bundleId] = createTime;
        MarkPersistenceDirtyLocked();
        EnqueueListenerCallbacksLocked(changedPaths: null, checkSyncEvent: false);
      }

      var finalProgress = new LoadBundleTaskProgress(
          docsLoaded, totalDocs, totalBytes, totalBytes,
          LoadBundleTaskProgress.LoadBundleTaskState.Success);
      EmitBundleProgress(progressHandler, ctx, finalProgress);
      return finalProgress;
    }

    private void ValidateBundleResourceDatabase(string fullName, out ResourcePath path) {
      if (!ResourcePath.TryParseResourceName(
              fullName, out string proj, out string db, out path)) {
        throw new FirestoreException(
            FirestoreError.InvalidArgument, $"Invalid bundle resource name: '{fullName}'.");
      }
      if (!string.Equals(proj, _projectId, StringComparison.Ordinal) ||
          !string.Equals(db, _databaseId, StringComparison.Ordinal)) {
        throw new FirestoreException(
            FirestoreError.InvalidArgument,
            $"Resource '{fullName}' belongs to {proj}/{db}, expected {_projectId}/{_databaseId}.");
      }
    }

    private QuerySpec ParseBundledQuery(Dictionary<string, object> bq) {
      if (bq == null) {
        throw new FirestoreException(FirestoreError.InvalidArgument, "Missing bundledQuery.");
      }
      string parent = JsonHelper.GetString(bq, "parent");
      ValidateBundleResourceDatabase(parent, out ResourcePath parentPath);

      Dictionary<string, object> sq = JsonHelper.GetMap(bq, "structuredQuery")
          ?? throw new FirestoreException(FirestoreError.InvalidArgument, "Missing structuredQuery.");
      List<object> fromList = JsonHelper.GetList(sq, "from");
      string collId = string.Empty;
      bool allDescendants = false;
      if (fromList != null && fromList.Count > 0 &&
          fromList[0] is Dictionary<string, object> fromItem) {
        collId = JsonHelper.GetString(fromItem, "collectionId", string.Empty);
        allDescendants = JsonHelper.GetBool(fromItem, "allDescendants", false);
      }

      ResourcePath qPath = allDescendants ? ResourcePath.Empty : parentPath.Child(collId);
      string qGroup = allDescendants ? collId : null;

      var filters = new List<FilterSpec>();
      if (JsonHelper.GetMap(sq, "where") is Dictionary<string, object> whereObj) {
        FilterSpec parsedFilter = ParseBundledFilter(whereObj);
        if (parsedFilter != null) filters.Add(parsedFilter);
      }

      var orderBys = new List<OrderBySpec>();
      if (JsonHelper.GetList(sq, "orderBy") is List<object> obList) {
        for (int i = 0; i < obList.Count; i++) {
          if (obList[i] is Dictionary<string, object> obItem) {
            Dictionary<string, object> fieldMap = JsonHelper.GetMap(obItem, "field");
            string fpStr = JsonHelper.GetString(fieldMap, "fieldPath", string.Empty);
            string dirStr = JsonHelper.GetString(obItem, "direction", "ASCENDING");
            bool desc = string.Equals(dirStr, "DESCENDING", StringComparison.OrdinalIgnoreCase);
            FieldPath fp = FieldPath.ParseCanonical(fpStr);
            // Skip trailing implicit __name__ if direction already matches last explicit orderBy.
            orderBys.Add(new OrderBySpec(fp, desc));
          }
        }
      }

      BoundSpec startBound = null;
      if (JsonHelper.GetMap(sq, "startAt") is Dictionary<string, object> startMap) {
        startBound = ParseBundledBound(startMap, isStart: true);
      }
      BoundSpec endBound = null;
      if (JsonHelper.GetMap(sq, "endAt") is Dictionary<string, object> endMap) {
        endBound = ParseBundledBound(endMap, isStart: false);
      }

      int? limit = null;
      LimitKind limitKind = LimitKind.None;
      if (sq.TryGetValue("limit", out object limRaw) && limRaw != null) {
        if (limRaw is Dictionary<string, object> limMap) {
          limit = (int)JsonHelper.GetLong(limMap, "value", 0);
        } else {
          limit = (int)JsonHelper.GetLong(sq, "limit", 0);
        }
        string limitTypeStr = JsonHelper.GetString(bq, "limitType", "FIRST");
        limitKind = string.Equals(limitTypeStr, "LAST", StringComparison.OrdinalIgnoreCase)
            ? LimitKind.Last
            : LimitKind.First;
      }

      return new QuerySpec(
          qPath,
          qGroup,
          filters.ToArray(),
          orderBys.ToArray(),
          startBound,
          endBound,
          limit,
          limitKind);
    }

    private static FilterSpec ParseBundledFilter(Dictionary<string, object> filterMap) {
      if (JsonHelper.GetMap(filterMap, "compositeFilter") is Dictionary<string, object> compMap) {
        string opStr = JsonHelper.GetString(compMap, "op", "AND");
        CompositeFilterOp cop = string.Equals(opStr, "OR", StringComparison.OrdinalIgnoreCase)
            ? CompositeFilterOp.Or
            : CompositeFilterOp.And;
        List<object> rawChildren = JsonHelper.GetList(compMap, "filters");
        var children = new List<FilterSpec>();
        if (rawChildren != null) {
          for (int i = 0; i < rawChildren.Count; i++) {
            if (rawChildren[i] is Dictionary<string, object> ch) {
              FilterSpec cf = ParseBundledFilter(ch);
              if (cf != null) children.Add(cf);
            }
          }
        }
        return FilterSpec.ForComposite(cop, children.ToArray());
      }
      if (JsonHelper.GetMap(filterMap, "unaryFilter") is Dictionary<string, object> unaryMap) {
        string fpStr = JsonHelper.GetString(JsonHelper.GetMap(unaryMap, "field"), "fieldPath", string.Empty);
        FieldPath fp = FieldPath.ParseCanonical(fpStr);
        string opStr = JsonHelper.GetString(unaryMap, "op", "IS_NULL");
        return opStr switch {
          "IS_NULL" => FilterSpec.ForField(fp, FieldFilterOp.Equal, FirestoreValue.Null),
          "IS_NOT_NULL" => FilterSpec.ForField(fp, FieldFilterOp.NotEqual, FirestoreValue.Null),
          "IS_NAN" => FilterSpec.ForField(fp, FieldFilterOp.Equal, FirestoreValue.FromDouble(double.NaN)),
          "IS_NOT_NAN" => FilterSpec.ForField(fp, FieldFilterOp.NotEqual, FirestoreValue.FromDouble(double.NaN)),
          _ => null,
        };
      }
      if (JsonHelper.GetMap(filterMap, "fieldFilter") is Dictionary<string, object> ffMap) {
        string fpStr = JsonHelper.GetString(JsonHelper.GetMap(ffMap, "field"), "fieldPath", string.Empty);
        FieldPath fp = FieldPath.ParseCanonical(fpStr);
        string opStr = JsonHelper.GetString(ffMap, "op", "EQUAL");
        FieldFilterOp fop = opStr switch {
          "LESS_THAN" => FieldFilterOp.LessThan,
          "LESS_THAN_OR_EQUAL" => FieldFilterOp.LessThanOrEqual,
          "GREATER_THAN" => FieldFilterOp.GreaterThan,
          "GREATER_THAN_OR_EQUAL" => FieldFilterOp.GreaterThanOrEqual,
          "NOT_EQUAL" => FieldFilterOp.NotEqual,
          "ARRAY_CONTAINS" => FieldFilterOp.ArrayContains,
          "IN" => FieldFilterOp.In,
          "ARRAY_CONTAINS_ANY" => FieldFilterOp.ArrayContainsAny,
          "NOT_IN" => FieldFilterOp.NotIn,
          _ => FieldFilterOp.Equal,
        };
        FirestoreValue val = ffMap.TryGetValue("value", out object vObj)
            ? FirestoreJson.ParseMiniJsonFirestoreValue(vObj)
            : FirestoreValue.Null;
        return FilterSpec.ForField(fp, fop, val);
      }
      return null;
    }

    private static BoundSpec ParseBundledBound(Dictionary<string, object> boundMap, bool isStart) {
      List<object> valsRaw = JsonHelper.GetList(boundMap, "values");
      var pos = valsRaw == null ? Array.Empty<FirestoreValue>() : new FirestoreValue[valsRaw.Count];
      if (valsRaw != null) {
        for (int i = 0; i < valsRaw.Count; i++) {
          pos[i] = FirestoreJson.ParseMiniJsonFirestoreValue(valsRaw[i]);
        }
      }
      bool before = JsonHelper.GetBool(boundMap, "before", false);
      bool inclusive = isStart ? before : !before;
      return new BoundSpec(pos, inclusive);
    }

    private static Timestamp ParseBundleTimestamp(Dictionary<string, object> map, string key) {
      if (map == null || !map.TryGetValue(key, out object raw) || raw == null) return default;
      if (raw is string s) return FirestoreJson.ParseRfc3339Timestamp(s);
      if (raw is Dictionary<string, object> tsObj) {
        long sec = JsonHelper.GetLong(tsObj, "seconds", 0);
        int nanos = (int)JsonHelper.GetLong(tsObj, "nanos", 0);
        return new Timestamp(sec, nanos);
      }
      return default;
    }

    private static bool TryReadLengthPrefixedElement(
        byte[] bytes, ref int offset, out string elementJson) {
      elementJson = null;
      while (offset < bytes.Length &&
             (bytes[offset] == ' ' || bytes[offset] == '\n' || bytes[offset] == '\r' || bytes[offset] == '\t')) {
        offset++;
      }
      if (offset >= bytes.Length) return false;
      int len = 0;
      int digits = 0;
      while (offset < bytes.Length && bytes[offset] >= '0' && bytes[offset] <= '9') {
        len = len * 10 + (bytes[offset] - '0');
        offset++;
        digits++;
      }
      if (digits == 0 || len <= 0 || offset + len > bytes.Length) return false;
      elementJson = Encoding.UTF8.GetString(bytes, offset, len);
      offset += len;
      return true;
    }

    private void EmitBundleProgress(
        EventHandler<LoadBundleTaskProgress> handler,
        SynchronizationContext ctx,
        LoadBundleTaskProgress progress) {
      if (handler == null) return;
      if (Firebase.Platform.FirebaseHandler.DefaultInstance != null) {
        if (Firebase.Platform.FirebaseHandler.DefaultInstance.IsMainThread()) {
          handler(_owner, progress);
        } else {
          Firebase.Platform.FirebaseHandler.RunOnMainThread(() => {
            handler(_owner, progress);
            return true;
          });
        }
        return;
      }
      if (ctx != null && SynchronizationContext.Current != ctx) {
        using var done = new ManualResetEventSlim(false);
        ctx.Post(_ => {
          try {
            handler(_owner, progress);
          } finally {
            done.Set();
          }
        }, null);
        done.Wait();
        return;
      }
      handler(_owner, progress);
    }

    internal Task<Query> GetNamedQueryAsync(string queryName) {
      Preconditions.CheckNotNull(queryName, nameof(queryName));
      lock (_stateLock) {
        if (_disposed || _terminated) {
          throw new InvalidOperationException(
              "This FirebaseFirestore instance has been terminated or disposed.");
        }
      }
      EnsureStarted();
      lock (_stateLock) {
        if (_namedQueries.TryGetValue(queryName, out QuerySpec spec)) {
          return Task.FromResult(new Query(spec, _owner));
        }
        return Task.FromResult<Query>(null);
      }
    }

    #endregion

    #region Termination & Dispose

    internal Task TerminateAsync() {
      FirestoreWatchStream ws;
      FirestoreRpcClient rpc;
      List<ActiveDocListener> docLs;
      List<ActiveQueryListener> queryLs;
      List<ActiveSyncListener> syncLs;

      lock (_stateLock) {
        if (_disposed) {
          throw new InvalidOperationException("This FirebaseFirestore instance has been disposed.");
        }
        if (_terminated) {
          return Task.CompletedTask;
        }
        _terminated = true;
        ws = _watchStream;
        rpc = _rpc;
        _watchStream = null;
        docLs = _docListeners.Values.ToList();
        queryLs = _queryListeners.Values.ToList();
        syncLs = _syncListeners.Values.ToList();
        _docListeners.Clear();
        _queryListeners.Clear();
        _syncListeners.Clear();
      }

      FlushPersistenceIfDirty();
      StopAllListenersInternal(docLs, queryLs, syncLs);
      try {
        _lifetimeCts.Cancel();
      } catch (Exception) {
        // Ignore teardown race.
      }
      ws?.Dispose();
      rpc?.Dispose();
      return Task.CompletedTask;
    }

    public void Dispose() {
      FirestoreWatchStream ws;
      FirestoreRpcClient rpc;
      List<ActiveDocListener> docLs;
      List<ActiveQueryListener> queryLs;
      List<ActiveSyncListener> syncLs;

      lock (_stateLock) {
        if (_disposed) return;
        _disposed = true;
        _terminated = true;
        ws = _watchStream;
        rpc = _rpc;
        _watchStream = null;
        docLs = _docListeners.Values.ToList();
        queryLs = _queryListeners.Values.ToList();
        syncLs = _syncListeners.Values.ToList();
        _docListeners.Clear();
        _queryListeners.Clear();
        _syncListeners.Clear();
      }

      FlushPersistenceIfDirty();
      StopAllListenersInternal(docLs, queryLs, syncLs);
      try {
        _lifetimeCts.Cancel();
      } catch (Exception) {
        // Ignore teardown race.
      }
      ws?.Dispose();
      rpc?.Dispose();
    }

    private static void StopAllListenersInternal(
        List<ActiveDocListener> docLs,
        List<ActiveQueryListener> queryLs,
        List<ActiveSyncListener> syncLs) {
      for (int i = 0; i < docLs.Count; i++) {
        docLs[i].Stopped = true;
        docLs[i].ListenerTcs.TrySetResult(true);
      }
      for (int i = 0; i < queryLs.Count; i++) {
        queryLs[i].Stopped = true;
        queryLs[i].ListenerTcs.TrySetResult(true);
      }
      for (int i = 0; i < syncLs.Count; i++) {
        syncLs[i].Stopped = true;
        syncLs[i].ListenerTcs.TrySetResult(true);
      }
    }

    #endregion
  }
}
