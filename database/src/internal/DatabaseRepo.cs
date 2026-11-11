// Copyright 2026 Google LLC
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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Internal;
using Firebase.Platform;

namespace Firebase.Database.Internal {
  internal sealed class DatabaseRepo : IDisposable {
    private const int MaxTransactionAttempts = 25;

    private sealed class ActiveTransaction {
      internal RtdbPath Path { get; }
      internal volatile int AbortErrorCode; // 0 = none, else DatabaseError code
      internal TaskCompletionSource<bool> AbortSignal { get; } =
          new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

      internal ActiveTransaction(RtdbPath path) {
        Path = path;
      }

      internal void Abort(int code) {
        if (Interlocked.CompareExchange(ref AbortErrorCode, code, 0) == 0) {
          AbortSignal.TrySetResult(true);
        }
      }
    }

    private readonly FirebaseApp _app;
    private readonly FirebaseDatabase _database;
    private readonly ParsedDatabaseUrl _parsedUrl;
    private readonly object _repoLock = new object();
    private readonly WriteTree _writeTree = new WriteTree();
    private readonly SparseSnapshotTree _onDisconnectTree = new SparseSnapshotTree();
    private readonly Dictionary<QuerySpec, QueryView> _views = new Dictionary<QuerySpec, QueryView>();
    private readonly Dictionary<long, (QuerySpec Spec, RtdbNode Node, bool Complete)> _taggedServerCache =
        new Dictionary<long, (QuerySpec, RtdbNode, bool)>();
    private readonly Dictionary<RtdbPath, long> _serverCompletePaths = new Dictionary<RtdbPath, long>();
    private readonly Dictionary<RtdbPath, SemaphoreSlim> _txPathLocks = new Dictionary<RtdbPath, SemaphoreSlim>();
    private readonly List<ActiveTransaction> _activeTransactions = new List<ActiveTransaction>();
    private readonly ConcurrentQueue<DispatchedEvent> _eventQueue = new ConcurrentQueue<DispatchedEvent>();

    private RtdbConnection _connection;
    private RtdbPersistence _persistence;
    private RtdbNode _serverRoot = EmptyNode.Instance;
    private RtdbNode _infoRoot = EmptyNode.Instance;
    private bool _initialized;
    private bool _persistenceEnabled;
    private long _persistenceCacheSizeBytes = RtdbPersistence.DefaultCacheSizeBytes;
    private volatile LogLevel _logLevel = LogLevel.Info;
    private volatile bool _disposed;
    private long _serverTimeOffsetMs;
    private long _nextWriteId;
    private long _nextTagId;
    private long _lruClock;
    private long _persistSeq;
    private int _drainScheduled;

    internal DatabaseRepo(FirebaseApp app, FirebaseDatabase database, ParsedDatabaseUrl parsedUrl) {
      _app = app;
      _database = database;
      _parsedUrl = parsedUrl;

      // Initialize .info defaults
      _infoRoot = _infoRoot
          .UpdateImmediateChild("connected", new LeafNode(false))
          .UpdateImmediateChild("authenticated", new LeafNode(false))
          .UpdateImmediateChild("serverTimeOffset", new LeafNode(0L));
    }

    internal string BaseUrl => _parsedUrl.BaseUrl;
    internal ParsedDatabaseUrl ParsedUrl => _parsedUrl;
    internal bool IsDisposed => _disposed;

    internal LogLevel LogLevel {
      get => _logLevel;
      set => _logLevel = value;
    }

    internal long PersistenceCacheSizeBytes {
      get {
        lock (_repoLock) return _persistenceCacheSizeBytes;
      }
      set {
        if (value <= 0) {
          throw new DatabaseException("PersistenceCacheSizeBytes must be greater than zero.");
        }
        lock (_repoLock) {
          _persistenceCacheSizeBytes = value;
        }
      }
    }

    internal void SetPersistenceEnabled(bool enabled) {
      lock (_repoLock) {
        if (!_initialized) {
          _persistenceEnabled = enabled;
        }
      }
    }

    internal long EstimatedServerTimeMs =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Interlocked.Read(ref _serverTimeOffsetMs);

    internal void EnsureInitialized() {
      List<UserWriteRecord> restoredWrites = null;
      RtdbConnection connToStart = null;
      lock (_repoLock) {
        if (_disposed) return;
        if (_initialized) return;
        _initialized = true;

        _connection = new RtdbConnection(_app, _parsedUrl, this);
        connToStart = _connection;

        if (_persistenceEnabled) {
          _persistence = new RtdbPersistence(_app.Name, _parsedUrl.CacheKey);
          PersistedStateSnapshot loaded = _persistence.Load();
          loaded.CacheEntries.Sort((a, b) => {
            int cmp = a.Path.Count.CompareTo(b.Path.Count);
            return cmp != 0 ? cmp : a.LastAccessTick.CompareTo(b.LastAccessTick);
          });
          foreach (PersistedCacheEntry entry in loaded.CacheEntries) {
            _serverRoot = _serverRoot.UpdateChild(entry.Path, entry.Node);
            long tick = entry.LastAccessTick > 0 ? entry.LastAccessTick : ++_lruClock;
            if (tick > _lruClock) _lruClock = tick;
            _serverCompletePaths[entry.Path] = tick;
          }
          if (loaded.PendingWrites.Count > 0) {
            restoredWrites = loaded.PendingWrites;
            for (int i = 0; i < restoredWrites.Count; i++) {
              UserWriteRecord w = restoredWrites[i];
              if (w.WriteId >= _nextWriteId) {
                _nextWriteId = w.WriteId + 1;
              }
              if (!w.IsMerge) {
                RtdbNode existing = CalcLocalNodeUnlocked(w.Path, null, out _);
                RtdbNode resolved = ServerValues.ResolveNode(w.UnresolvedOverwrite, existing, _serverTimeOffsetMs);
                _writeTree.AddOverwrite(w.WriteId, w.Path, w.UnresolvedOverwrite, resolved, visible: true, persist: true);
              } else {
                var resolvedMerge = new List<KeyValuePair<RtdbPath, RtdbNode>>(w.UnresolvedMerge.Count);
                for (int m = 0; m < w.UnresolvedMerge.Count; m++) {
                  KeyValuePair<RtdbPath, RtdbNode> kv = w.UnresolvedMerge[m];
                  RtdbNode existing = CalcLocalNodeUnlocked(w.Path.Child(kv.Key), null, out _);
                  resolvedMerge.Add(new KeyValuePair<RtdbPath, RtdbNode>(
                      kv.Key,
                      ServerValues.ResolveNode(kv.Value, existing, _serverTimeOffsetMs)));
                }
                _writeTree.AddMerge(w.WriteId, w.Path, w.UnresolvedMerge, resolvedMerge, visible: true, persist: true);
              }
            }
          }
        }
      }

      if (restoredWrites != null) {
        for (int i = 0; i < restoredWrites.Count; i++) {
          UserWriteRecord w = restoredWrites[i];
          long writeId = w.WriteId;
          RtdbPath path = w.Path;
          if (!w.IsMerge) {
            connToStart.EnqueuePut(path, w.UnresolvedOverwrite.ToWireValue(), null, (s, d) => HandleWriteAck(writeId, path, s, d, null));
          } else {
            var wireDict = new Dictionary<string, object>(w.UnresolvedMerge.Count);
            for (int m = 0; m < w.UnresolvedMerge.Count; m++) {
              wireDict[w.UnresolvedMerge[m].Key.ToRelativeString()] = w.UnresolvedMerge[m].Value.ToWireValue();
            }
            connToStart.EnqueueMerge(path, wireDict, (s, d) => HandleWriteAck(writeId, path, s, d, null));
          }
        }
      }

      connToStart?.Start();
    }

    internal void GoOffline() {
      EnsureInitialized();
      _connection?.GoOffline();
    }

    internal void GoOnline() {
      EnsureInitialized();
      _connection?.GoOnline();
    }

    internal void PurgeOutstandingWrites() {
      EnsureInitialized();
      var events = new List<DispatchedEvent>();
      List<ActiveTransaction> abortedTx;
      long persistSeq = -1;
      RtdbPersistence persistence = null;

      lock (_repoLock) {
        if (_disposed) return;
        _writeTree.RemoveAllWrites();
        _onDisconnectTree.Clear();
        RecomputeViewsUnlocked(RtdbPath.Empty, events);

        abortedTx = new List<ActiveTransaction>(_activeTransactions);
        if (_persistence != null) {
          persistence = _persistence;
          persistSeq = ++_persistSeq;
        }
      }

      for (int i = 0; i < abortedTx.Count; i++) {
        abortedTx[i].Abort(DatabaseError.WriteCanceled);
      }

      _connection?.PurgeOutstandingWrites();

      if (persistence != null) {
        SchedulePersistenceSave(persistence, persistSeq);
      }

      EnqueueEvents(events);
    }

    internal void AddListener(QuerySpec spec, ListenerRegistration registration) {
      EnsureInitialized();
      var initialEvents = new List<DispatchedEvent>();
      bool startNetworkListen = false;
      long? tag = null;
      RtdbConnection conn;

      lock (_repoLock) {
        if (_disposed) return;
        conn = _connection;
        QueryView view = GetOrCreateViewUnlocked(spec, out bool created);
        bool neededBefore = view.NeedsNetworkListen;

        // Ensure view reflects current complete cache before adding registration
        RefreshSingleViewUnlocked(view, outEvents: null);
        view.AddRegistration(registration, initialEvents);

        bool needsAfter = view.NeedsNetworkListen;
        if (!neededBefore && needsAfter) {
          view.ReceivedServerDataPush = false;
          startNetworkListen = true;
          tag = view.Tag;
        } else if (created && !view.NeedsNetworkListen && !view.IsComplete) {
          _views.Remove(spec);
        }
      }

      EnqueueEvents(initialEvents);

      if (startNetworkListen && conn != null) {
        conn.Listen(spec, tag, (status, detail, noIndex) => HandleListenAck(spec, tag, status, detail, noIndex));
      }
    }

    internal void RemoveListener(QuerySpec spec, EventKind kind, Delegate handler) {
      if (handler == null) return;
      bool stopNetworkListen = false;
      RtdbConnection conn = null;

      lock (_repoLock) {
        if (!_views.TryGetValue(spec, out QueryView view)) {
          return;
        }
        bool neededBefore = view.NeedsNetworkListen;
        if (!view.RemoveRegistration(kind, handler)) {
          return;
        }
        bool needsAfter = view.NeedsNetworkListen;
        if (neededBefore && !needsAfter) {
          stopNetworkListen = true;
          conn = _connection;
          if (view.Tag.HasValue) {
            _taggedServerCache.Remove(view.Tag.Value);
          }
          _views.Remove(spec);
        }
      }

      if (stopNetworkListen && conn != null) {
        conn.Unlisten(spec);
      }
    }

    internal void SetKeepSynced(QuerySpec spec, bool keepSynced) {
      EnsureInitialized();
      bool startListen = false;
      bool stopListen = false;
      long? tag = null;
      RtdbConnection conn;

      lock (_repoLock) {
        if (_disposed) return;
        conn = _connection;
        if (keepSynced) {
          QueryView view = GetOrCreateViewUnlocked(spec, out _);
          bool neededBefore = view.NeedsNetworkListen;
          view.KeepSynced = true;
          RefreshSingleViewUnlocked(view, outEvents: null);
          if (!neededBefore && view.NeedsNetworkListen) {
            view.ReceivedServerDataPush = false;
            startListen = true;
            tag = view.Tag;
          }
        } else if (_views.TryGetValue(spec, out QueryView view)) {
          bool neededBefore = view.NeedsNetworkListen;
          view.KeepSynced = false;
          if (neededBefore && !view.NeedsNetworkListen) {
            stopListen = true;
            if (view.Tag.HasValue) {
              _taggedServerCache.Remove(view.Tag.Value);
            }
            _views.Remove(spec);
          }
        }
      }

      if (startListen && conn != null) {
        conn.Listen(spec, tag, (status, detail, noIndex) => HandleListenAck(spec, tag, status, detail, noIndex));
      } else if (stopListen && conn != null) {
        conn.Unlisten(spec);
      }
    }

    internal Task<DataSnapshot> GetValueAsync(QuerySpec spec) {
      EnsureInitialized();
      if (spec.Path.IsDotInfo) {
        RtdbNode infoNode;
        lock (_repoLock) {
          infoNode = _infoRoot.GetChild(spec.Path.PopFront());
        }
        return Task.FromResult(new DataSnapshot(_database, spec.Path, infoNode, spec.Params.Index));
      }

      bool online = _connection != null && _connection.IsDesiredOnline;

      lock (_repoLock) {
        if (_disposed) {
          return Task.FromException<DataSnapshot>(new DatabaseException("FirebaseDatabase has been disposed."));
        }

        // If actively listened/kept-synced with server-synced data, or if offline with complete cached/optimistic data,
        // return immediately without an extra network round-trip.
        if (_views.TryGetValue(spec, out QueryView activeView) &&
            activeView.NeedsNetworkListen &&
            activeView.IsComplete &&
            (!online || activeView.ServerSynced)) {
          TouchLruUnlocked(spec.Path);
          return Task.FromResult(new DataSnapshot(_database, spec.Path, activeView.CurrentNode, spec.Params.Index));
        }

        if (!online) {
          RtdbNode offlineNode = CalcQueryBaseNodeUnlocked(spec, activeView?.Tag, null, out bool offlineComplete);
          if (offlineComplete) {
            TouchLruUnlocked(spec.Path);
            RtdbNode filtered = spec.Params.ApplyFilter(offlineNode);
            return Task.FromResult(new DataSnapshot(_database, spec.Path, filtered, spec.Params.Index));
          }
        }
      }

      // Fetch fresh value from server via a temporary listen (merged with pending local WriteTree).
      var tcs = new TaskCompletionSource<DataSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
      EventHandler<ValueChangedEventArgs> oneShotHandler = null;
      oneShotHandler = (_, args) => {
        RemoveListener(spec, EventKind.ValueChanged, oneShotHandler);
        if (args.DatabaseError != null) {
          tcs.TrySetException(args.DatabaseError.ToException());
        } else {
          tcs.TrySetResult(args.Snapshot);
        }
      };

      var reg = new ListenerRegistration(spec, oneShotHandler, isOneShot: true);

      // If we are online and no continuous listener is active, clear stale serverComplete marker on unpinned path
      // so the one-shot listener waits for fresh server data rather than returning a stale previous snapshot!
      long? listenTag = null;
      bool startListen = false;
      RtdbConnection conn;
      var initialEvents = new List<DispatchedEvent>();

      lock (_repoLock) {
        if (_disposed) {
          return Task.FromException<DataSnapshot>(new DatabaseException("FirebaseDatabase has been disposed."));
        }
        conn = _connection;
        QueryView view = GetOrCreateViewUnlocked(spec, out _);
        bool hadContinuousListen = view.NeedsNetworkListen;
        if (!online) {
          RefreshSingleViewUnlocked(view, outEvents: null);
        } else if (hadContinuousListen && view.ServerSynced) {
          RefreshSingleViewUnlocked(view, outEvents: null);
        }
        view.AddRegistration(reg, initialEvents);
        if (!hadContinuousListen && view.NeedsNetworkListen) {
          view.ReceivedServerDataPush = false;
          startListen = true;
          listenTag = view.Tag;
        }
      }

      EnqueueEvents(initialEvents);

      if (startListen && conn != null) {
        conn.Listen(spec, listenTag, (status, detail, noIndex) => HandleListenAck(spec, listenTag, status, detail, noIndex));
      }

      return tcs.Task;
    }

    internal Task SetValueAsync(RtdbPath path, RtdbNode unresolvedNode) {
      EnsureInitialized();
      if (path.IsDotInfo) {
        throw new DatabaseException("Can't modify data under /.info.");
      }

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      var events = new List<DispatchedEvent>();
      List<ActiveTransaction> txToAbort = null;
      long writeId;
      long persistSeq = -1;
      RtdbPersistence persistence = null;
      RtdbConnection conn;

      lock (_repoLock) {
        if (_disposed) {
          return Task.FromException(new DatabaseException("FirebaseDatabase has been disposed."));
        }
        conn = _connection;
        writeId = ++_nextWriteId;
        RtdbNode existing = CalcLocalNodeUnlocked(path, null, out _);
        RtdbNode resolved = ServerValues.ResolveNode(unresolvedNode, existing, _serverTimeOffsetMs);

        _writeTree.AddOverwrite(writeId, path, unresolvedNode, resolved, visible: true, persist: true);
        RecomputeViewsUnlocked(path, events);
        txToAbort = CollectOverlappingTransactionsUnlocked(path);

        if (_persistence != null) {
          persistence = _persistence;
          persistSeq = ++_persistSeq;
        }
      }

      AbortTransactions(txToAbort, DatabaseError.OverriddenBySet);
      if (persistence != null) {
        SchedulePersistenceSave(persistence, persistSeq);
      }
      EnqueueEvents(events);

      conn.EnqueuePut(
          path,
          unresolvedNode.ToWireValue(),
          compareHash: null,
          onComplete: (status, detail) => HandleWriteAck(writeId, path, status, detail, tcs));
      return tcs.Task;
    }

    internal Task UpdateChildrenAsync(RtdbPath path, IDictionary<string, object> update) {
      if (update == null) {
        throw new ArgumentNullException(nameof(update));
      }
      EnsureInitialized();
      if (path.IsDotInfo) {
        throw new DatabaseException("Can't modify data under /.info.");
      }

      if (update.Count == 0) {
        return Task.CompletedTask;
      }

      var unresolvedList = new List<KeyValuePair<RtdbPath, RtdbNode>>(update.Count);
      var wireDict = new Dictionary<string, object>(update.Count);
      foreach (KeyValuePair<string, object> kv in update) {
        if (string.IsNullOrEmpty(kv.Key)) {
          throw new DatabaseException("UpdateChildrenAsync key cannot be null or empty.");
        }
        RtdbPath relPath;
        try {
          relPath = RtdbPath.ParseAndValidate(kv.Key, nameof(update), allowMetaKeys: true);
        } catch (ArgumentException ex) {
          throw new DatabaseException(ex.Message, ex);
        }
        RtdbNode childUnresolved = RtdbNodeFactory.NodeFromObject(kv.Value);
        unresolvedList.Add(new KeyValuePair<RtdbPath, RtdbNode>(relPath, childUnresolved));
        wireDict[relPath.ToRelativeString()] = childUnresolved.ToWireValue();
      }

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      var events = new List<DispatchedEvent>();
      var txToAbort = new List<ActiveTransaction>();
      long writeId;
      long persistSeq = -1;
      RtdbPersistence persistence = null;
      RtdbConnection conn;

      lock (_repoLock) {
        if (_disposed) {
          return Task.FromException(new DatabaseException("FirebaseDatabase has been disposed."));
        }
        conn = _connection;
        writeId = ++_nextWriteId;

        var resolvedList = new List<KeyValuePair<RtdbPath, RtdbNode>>(unresolvedList.Count);
        for (int i = 0; i < unresolvedList.Count; i++) {
          KeyValuePair<RtdbPath, RtdbNode> entry = unresolvedList[i];
          RtdbNode existing = CalcLocalNodeUnlocked(path.Child(entry.Key), null, out _);
          RtdbNode resolved = ServerValues.ResolveNode(entry.Value, existing, _serverTimeOffsetMs);
          resolvedList.Add(new KeyValuePair<RtdbPath, RtdbNode>(entry.Key, resolved));

          List<ActiveTransaction> hit = CollectOverlappingTransactionsUnlocked(path.Child(entry.Key));
          if (hit != null) txToAbort.AddRange(hit);
        }

        _writeTree.AddMerge(writeId, path, unresolvedList, resolvedList, visible: true, persist: true);
        RecomputeViewsUnlocked(path, events);

        if (_persistence != null) {
          persistence = _persistence;
          persistSeq = ++_persistSeq;
        }
      }

      AbortTransactions(txToAbort, DatabaseError.OverriddenBySet);
      if (persistence != null) {
        SchedulePersistenceSave(persistence, persistSeq);
      }
      EnqueueEvents(events);

      conn.EnqueueMerge(
          path,
          wireDict,
          onComplete: (status, detail) => HandleWriteAck(writeId, path, status, detail, tcs));
      return tcs.Task;
    }

    internal Task OnDisconnectSetValueAsync(RtdbPath path, RtdbNode unresolvedNode) {
      EnsureInitialized();
      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      _connection.EnqueueOnDisconnect(
          "o",
          path,
          unresolvedNode.ToWireValue(),
          (status, detail) => {
            if (string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase)) {
              lock (_repoLock) {
                _onDisconnectTree.Remember(path, unresolvedNode);
              }
              tcs.TrySetResult(true);
            } else {
              tcs.TrySetException(DatabaseError.FromStatus(status, detail).ToException());
            }
          });
      return tcs.Task;
    }

    internal Task OnDisconnectUpdateChildrenAsync(RtdbPath path, IDictionary<string, object> update) {
      if (update == null) {
        throw new ArgumentNullException(nameof(update));
      }
      EnsureInitialized();
      if (update.Count == 0) {
        return Task.CompletedTask;
      }

      var parsedEntries = new List<KeyValuePair<RtdbPath, RtdbNode>>(update.Count);
      var wireDict = new Dictionary<string, object>(update.Count);
      foreach (KeyValuePair<string, object> kv in update) {
        if (string.IsNullOrEmpty(kv.Key)) {
          throw new DatabaseException("OnDisconnect.UpdateChildren key cannot be null or empty.");
        }
        RtdbPath relPath;
        try {
          relPath = RtdbPath.ParseAndValidate(kv.Key, nameof(update), allowMetaKeys: true);
        } catch (ArgumentException ex) {
          throw new DatabaseException(ex.Message, ex);
        }
        RtdbNode node = RtdbNodeFactory.NodeFromObject(kv.Value);
        parsedEntries.Add(new KeyValuePair<RtdbPath, RtdbNode>(relPath, node));
        wireDict[relPath.ToRelativeString()] = node.ToWireValue();
      }

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      _connection.EnqueueOnDisconnect(
          "om",
          path,
          wireDict,
          (status, detail) => {
            if (string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase)) {
              lock (_repoLock) {
                for (int i = 0; i < parsedEntries.Count; i++) {
                  _onDisconnectTree.Remember(path.Child(parsedEntries[i].Key), parsedEntries[i].Value);
                }
              }
              tcs.TrySetResult(true);
            } else {
              tcs.TrySetException(DatabaseError.FromStatus(status, detail).ToException());
            }
          });
      return tcs.Task;
    }

    internal Task OnDisconnectCancelAsync(RtdbPath path) {
      EnsureInitialized();
      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      _connection.EnqueueOnDisconnect(
          "oc",
          path,
          wireData: null,
          (status, detail) => {
            if (string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase)) {
              lock (_repoLock) {
                _onDisconnectTree.Forget(path);
              }
              tcs.TrySetResult(true);
            } else {
              tcs.TrySetException(DatabaseError.FromStatus(status, detail).ToException());
            }
          });
      return tcs.Task;
    }

    internal async Task<DataSnapshot> RunTransactionAsync(
        RtdbPath path,
        Func<MutableData, TransactionResult> transaction,
        bool fireLocalEvents) {
      if (transaction == null) {
        throw new ArgumentNullException(nameof(transaction));
      }
      EnsureInitialized();
      if (path.IsDotInfo) {
        throw new DatabaseException("Can't run transaction under /.info.");
      }

      var activeTx = new ActiveTransaction(path);
      SemaphoreSlim pathGate;
      lock (_repoLock) {
        if (_disposed) {
          throw new DatabaseException("FirebaseDatabase has been disposed.");
        }
        _activeTransactions.Add(activeTx);
        if (!_txPathLocks.TryGetValue(path, out pathGate)) {
          pathGate = new SemaphoreSlim(1, 1);
          _txPathLocks[path] = pathGate;
        }
      }

      // Attach a continuous listener for the lifetime of this transaction so server updates stream in real-time.
      var watchSpec = new QuerySpec(path);
      EventHandler<ValueChangedEventArgs> watchHandler = (_, __) => { };
      var watchReg = new ListenerRegistration(watchSpec, watchHandler, isOneShot: false);
      AddListener(watchSpec, watchReg);

      long currentWriteId = -1;
      long currentPutSeqId = -1;
      bool gateAcquired = false;

      try {
        if (!pathGate.Wait(0)) {
          Task waitGateTask = pathGate.WaitAsync();
          Task gateCompleted = await Task.WhenAny(waitGateTask, activeTx.AbortSignal.Task).ConfigureAwait(false);
          if (gateCompleted != waitGateTask) {
            throw DatabaseError.FromCode(activeTx.AbortErrorCode).ToException();
          }
          await waitGateTask.ConfigureAwait(false);
        }
        gateAcquired = true;

        int semanticAttempts = 0;
        while (semanticAttempts < MaxTransactionAttempts) {
          if (_disposed) {
            throw new DatabaseException("FirebaseDatabase has been disposed.");
          }
          if (activeTx.AbortErrorCode != 0) {
            throw DatabaseError.FromCode(activeTx.AbortErrorCode).ToException();
          }

          RtdbNode baseState;
          lock (_repoLock) {
            HashSet<long> ignore = currentWriteId >= 0 ? new HashSet<long> { currentWriteId } : null;
            baseState = CalcLocalNodeUnlocked(path, ignore, out _);
          }

          string inputHash = baseState.GetHash();
          var holder = new MutableNodeHolder(baseState);
          var mutableData = new MutableData(holder, RtdbPath.Empty);

          TransactionResult userResult;
          try {
            userResult = await InvokeOnMainThreadAsync(
                () => transaction(mutableData),
                allowInlineIfAlreadyMainThread: true).ConfigureAwait(false);
          } catch (Exception userEx) {
            RevertTransactionWrite(ref currentWriteId, ref currentPutSeqId, path);
            throw DatabaseError.FromException(userEx).ToException();
          }

          if (activeTx.AbortErrorCode != 0) {
            RevertTransactionWrite(ref currentWriteId, ref currentPutSeqId, path);
            throw DatabaseError.FromCode(activeTx.AbortErrorCode).ToException();
          }

          if (userResult == null || !userResult.IsSuccess) {
            RevertTransactionWrite(ref currentWriteId, ref currentPutSeqId, path);
            throw DatabaseError.FromCode(DatabaseError.WriteCanceled).ToException();
          }

          RtdbNode outputUnresolved = holder.RootNode ?? EmptyNode.Instance;
          RtdbNode outputResolved;
          var localEvents = new List<DispatchedEvent>();

          lock (_repoLock) {
            if (currentWriteId >= 0) {
              _writeTree.RemoveWrite(currentWriteId, out _);
              currentWriteId = -1;
            }
            outputResolved = ServerValues.ResolveNode(outputUnresolved, baseState, _serverTimeOffsetMs);
            currentWriteId = ++_nextWriteId;
            _writeTree.AddOverwrite(
                currentWriteId,
                path,
                outputUnresolved,
                outputResolved,
                visible: fireLocalEvents,
                persist: false);
            if (fireLocalEvents) {
              RecomputeViewsUnlocked(path, localEvents);
            }
          }

          EnqueueEvents(localEvents);

          if (activeTx.AbortErrorCode != 0) {
            RevertTransactionWrite(ref currentWriteId, ref currentPutSeqId, path);
            throw DatabaseError.FromCode(activeTx.AbortErrorCode).ToException();
          }

          var putResultTcs = new TaskCompletionSource<(string Status, string Detail)>(
              TaskCreationOptions.RunContinuationsAsynchronously);
          currentPutSeqId = _connection.EnqueuePut(
              path,
              outputUnresolved.ToWireValue(),
              compareHash: inputHash,
              onComplete: (s, d) => putResultTcs.TrySetResult((s, d)));

          Task completedPut = await Task.WhenAny(putResultTcs.Task, activeTx.AbortSignal.Task).ConfigureAwait(false);
          if (completedPut != putResultTcs.Task) {
            RevertTransactionWrite(ref currentWriteId, ref currentPutSeqId, path);
            throw DatabaseError.FromCode(activeTx.AbortErrorCode).ToException();
          }

          currentPutSeqId = -1;
          (string status, string detail) = await putResultTcs.Task.ConfigureAwait(false);

          if (activeTx.AbortErrorCode != 0) {
            RevertTransactionWrite(ref currentWriteId, ref currentPutSeqId, path);
            throw DatabaseError.FromCode(activeTx.AbortErrorCode).ToException();
          }

          if (string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase)) {
            var commitEvents = new List<DispatchedEvent>();
            long persistSeq = -1;
            RtdbPersistence persistence = null;
            lock (_repoLock) {
              if (currentWriteId >= 0) {
                _writeTree.RemoveWrite(currentWriteId, out _);
                currentWriteId = -1;
              }
              _serverRoot = _serverRoot.UpdateChild(path, outputResolved);
              _serverCompletePaths[path] = ++_lruClock;
              AlsoUpdateOverlappingTaggedCachesUnlocked(path, outputResolved);
              RecomputeViewsUnlocked(path, commitEvents);
              if (_persistence != null) {
                persistence = _persistence;
                persistSeq = ++_persistSeq;
              }
            }
            if (persistence != null) {
              SchedulePersistenceSave(persistence, persistSeq);
            }
            EnqueueEvents(commitEvents);
            return new DataSnapshot(_database, path, outputResolved, RtdbIndex.Priority);
          }

          if (string.Equals(status, "disconnected", StringComparison.OrdinalIgnoreCase)) {
            // Transient WebSocket drop mid-transaction: wait briefly for reconnect without burning retry budget.
            RevertTransactionWrite(ref currentWriteId, ref currentPutSeqId, path);
            await Task.Delay(50).ConfigureAwait(false);
            continue;
          }

          if (string.Equals(status, "datastale", StringComparison.OrdinalIgnoreCase)) {
            semanticAttempts++;
            RevertTransactionWrite(ref currentWriteId, ref currentPutSeqId, path);
            Task waitServerTask = null;
            bool needRefreshListen = false;
            long? refreshTag = null;
            RtdbConnection conn = null;
            lock (_repoLock) {
              if (_views.TryGetValue(watchSpec, out QueryView watchView)) {
                if (!watchView.ServerSynced) {
                  waitServerTask = watchView.ServerSyncTcs.Task;
                } else {
                  RtdbNode currentBase = CalcLocalNodeUnlocked(path, null, out _);
                  if (string.Equals(currentBase.GetHash(), inputHash, StringComparison.Ordinal)) {
                    waitServerTask = watchView.WaitNextServerUpdateUnlocked();
                    needRefreshListen = true;
                    refreshTag = watchView.Tag;
                    conn = _connection;
                  }
                }
              }
            }
            if (needRefreshListen && conn != null) {
              conn.Listen(watchSpec, refreshTag, (s, d, noIndex) => HandleListenAck(watchSpec, refreshTag, s, d, noIndex));
            }
            if (waitServerTask != null && !waitServerTask.IsCompleted) {
              await Task.WhenAny(waitServerTask, activeTx.AbortSignal.Task, Task.Delay(2000)).ConfigureAwait(false);
            }
            continue;
          }

          // Non-retryable server error (e.g. permission_denied)
          RevertTransactionWrite(ref currentWriteId, ref currentPutSeqId, path);
          throw DatabaseError.FromStatus(status, detail).ToException();
        }

        RevertTransactionWrite(ref currentWriteId, ref currentPutSeqId, path);
        throw DatabaseError.FromCode(DatabaseError.MaxRetries).ToException();
      } finally {
        RevertTransactionWrite(ref currentWriteId, ref currentPutSeqId, path);
        lock (_repoLock) {
          _activeTransactions.Remove(activeTx);
        }
        if (gateAcquired) {
          pathGate.Release();
        }
        RemoveListener(watchSpec, EventKind.ValueChanged, watchHandler);
      }
    }

    private void RevertTransactionWrite(ref long writeId, ref long putSeqId, RtdbPath path) {
      if (putSeqId >= 0) {
        _connection?.CancelWrite(putSeqId);
        putSeqId = -1;
      }
      if (writeId < 0) return;
      var events = new List<DispatchedEvent>();
      lock (_repoLock) {
        if (_writeTree.RemoveWrite(writeId, out _)) {
          RecomputeViewsUnlocked(path, events);
        }
        writeId = -1;
      }
      EnqueueEvents(events);
    }

    internal void OnServerHello(long serverTimestampMs) {
      long delta = serverTimestampMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
      Interlocked.Exchange(ref _serverTimeOffsetMs, delta);
      UpdateDotInfo("serverTimeOffset", new LeafNode(delta));
    }

    internal void OnConnected() {
      UpdateDotInfo("connected", new LeafNode(true));
    }

    internal void OnDisconnected() {
      UpdateDotInfo("connected", new LeafNode(false));

      var events = new List<DispatchedEvent>();
      var txToAbort = new List<ActiveTransaction>();
      long persistSeq = -1;
      RtdbPersistence persistence = null;

      lock (_repoLock) {
        if (_disposed) return;
        foreach (KeyValuePair<QuerySpec, QueryView> kv in _views) {
          kv.Value.ResetServerSyncedUnlocked();
        }
        bool hadOnDisconnect = false;
        _onDisconnectTree.ForEachTree(RtdbPath.Empty, (prefixPath, unresolvedNode) => {
          hadOnDisconnect = true;
          RtdbNode existing = _serverRoot.GetChild(prefixPath);
          RtdbNode resolved = ServerValues.ResolveNode(unresolvedNode, existing, _serverTimeOffsetMs);
          _serverRoot = _serverRoot.UpdateChild(prefixPath, resolved);
          _serverCompletePaths[prefixPath] = ++_lruClock;
          AlsoUpdateOverlappingTaggedCachesUnlocked(prefixPath, resolved);

          List<ActiveTransaction> hit = CollectOverlappingTransactionsUnlocked(prefixPath);
          if (hit != null) txToAbort.AddRange(hit);
        });
        _onDisconnectTree.Clear();
        if (hadOnDisconnect) {
          RecomputeViewsUnlocked(RtdbPath.Empty, events);
        }

        if (hadOnDisconnect && _persistence != null) {
          persistence = _persistence;
          persistSeq = ++_persistSeq;
        }
      }

      AbortTransactions(txToAbort, DatabaseError.OverriddenBySet);
      if (persistence != null) {
        SchedulePersistenceSave(persistence, persistSeq);
      }
      EnqueueEvents(events);
    }

    internal void OnAuthStatusChanged(bool authenticated) {
      UpdateDotInfo("authenticated", new LeafNode(authenticated));
    }

    internal void OnServerDataPush(RtdbPath path, object rawPayload, bool isMerge, long? tag) {
      var events = new List<DispatchedEvent>();
      long persistSeq = -1;
      RtdbPersistence persistence = null;

      lock (_repoLock) {
        if (_disposed) return;

        if (tag.HasValue) {
          if (_taggedServerCache.TryGetValue(tag.Value, out (QuerySpec Spec, RtdbNode Node, bool Complete) entry)) {
            RtdbNode updated = entry.Node;
            if (!isMerge) {
              RtdbNode parsed = RtdbNodeFactory.NodeFromServerWire(rawPayload);
              if (RtdbPath.TryGetRelative(entry.Spec.Path, path, out RtdbPath relToPush)) {
                updated = updated.UpdateChild(relToPush, parsed);
              } else if (RtdbPath.TryGetRelative(path, entry.Spec.Path, out RtdbPath relToSpec)) {
                updated = parsed.GetChild(relToSpec);
              }
            } else if (rawPayload is Dictionary<string, object> mergeMap) {
              foreach (KeyValuePair<string, object> kv in mergeMap) {
                RtdbPath childFullPath = path.Child(RtdbPath.ParseUnchecked(kv.Key));
                RtdbNode childNode = RtdbNodeFactory.NodeFromServerWire(kv.Value);
                if (RtdbPath.TryGetRelative(entry.Spec.Path, childFullPath, out RtdbPath rel)) {
                  updated = updated.UpdateChild(rel, childNode);
                }
              }
            }
            _taggedServerCache[tag.Value] = (entry.Spec, updated, true);
            if (_views.TryGetValue(entry.Spec, out QueryView taggedView)) {
              taggedView.ReceivedServerDataPush = true;
              taggedView.MarkServerSyncedUnlocked();
              RefreshSingleViewUnlocked(taggedView, events);
            }
          }
        } else {
          if (!isMerge) {
            RtdbNode parsed = RtdbNodeFactory.NodeFromServerWire(rawPayload);
            _serverRoot = _serverRoot.UpdateChild(path, parsed);
            _serverCompletePaths[path] = ++_lruClock;
            AlsoUpdateOverlappingTaggedCachesUnlocked(path, parsed);
            foreach (KeyValuePair<QuerySpec, QueryView> kv in _views) {
              if (RtdbPath.TryGetRelative(path, kv.Key.Path, out RtdbPath relToSpec)) {
                if (kv.Value.Tag.HasValue) {
                  _taggedServerCache[kv.Value.Tag.Value] = (kv.Key, parsed.GetChild(relToSpec), true);
                  kv.Value.NoIndexFallback = true;
                }
                kv.Value.ReceivedServerDataPush = true;
                kv.Value.MarkServerSyncedUnlocked();
              } else if (path.StartsWith(kv.Key.Path)) {
                if (!kv.Value.Tag.HasValue) {
                  kv.Value.ReceivedServerDataPush = true;
                }
                kv.Value.ServerUpdateTcs.TrySetResult(true);
              }
            }
          } else if (rawPayload is Dictionary<string, object> mergeMap) {
            foreach (KeyValuePair<string, object> kv in mergeMap) {
              RtdbPath childFull = path.Child(RtdbPath.ParseUnchecked(kv.Key));
              RtdbNode parsed = RtdbNodeFactory.NodeFromServerWire(kv.Value);
              _serverRoot = _serverRoot.UpdateChild(childFull, parsed);
              _serverCompletePaths[childFull] = ++_lruClock;
              AlsoUpdateOverlappingTaggedCachesUnlocked(childFull, parsed);
              foreach (KeyValuePair<QuerySpec, QueryView> vk in _views) {
                if (RtdbPath.TryGetRelative(childFull, vk.Key.Path, out RtdbPath relToSpec)) {
                  if (vk.Value.Tag.HasValue) {
                    _taggedServerCache[vk.Value.Tag.Value] = (vk.Key, parsed.GetChild(relToSpec), true);
                    vk.Value.NoIndexFallback = true;
                  }
                  vk.Value.ReceivedServerDataPush = true;
                  vk.Value.MarkServerSyncedUnlocked();
                } else if (childFull.StartsWith(vk.Key.Path)) {
                  if (!vk.Value.Tag.HasValue) {
                    vk.Value.ReceivedServerDataPush = true;
                  }
                  vk.Value.ServerUpdateTcs.TrySetResult(true);
                }
              }
            }
          }
          RecomputeViewsUnlocked(path, events);
          if (_persistence != null) {
            persistence = _persistence;
            persistSeq = ++_persistSeq;
          }
        }
      }

      if (persistence != null) {
        SchedulePersistenceSave(persistence, persistSeq);
      }
      EnqueueEvents(events);
    }

    internal void OnListenRevoked(RtdbPath path, long? tag) {
      DatabaseError err = DatabaseError.FromCode(DatabaseError.PermissionDenied);
      var events = new List<DispatchedEvent>();
      lock (_repoLock) {
        if (_disposed) return;
        var toRemove = new List<QuerySpec>();
        foreach (KeyValuePair<QuerySpec, QueryView> kv in _views) {
          bool matches = tag.HasValue ? kv.Value.Tag == tag.Value : kv.Key.Path.Equals(path);
          if (matches) {
            kv.Value.ServerSyncTcs.TrySetResult(false);
            kv.Value.ServerUpdateTcs.TrySetResult(false);
            kv.Value.CancelAll(err, events);
            if (kv.Value.Tag.HasValue) {
              _taggedServerCache.Remove(kv.Value.Tag.Value);
            }
            toRemove.Add(kv.Key);
          }
        }
        for (int i = 0; i < toRemove.Count; i++) {
          _views.Remove(toRemove[i]);
        }
      }
      EnqueueEvents(events);
    }

    private void HandleListenAck(QuerySpec spec, long? tag, string status, string detail, bool hasNoIndexWarning) {
      var events = new List<DispatchedEvent>();
      bool isOk = string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase);

      lock (_repoLock) {
        if (_disposed) return;
        if (!_views.TryGetValue(spec, out QueryView view)) {
          return;
        }
        if (isOk) {
          if (tag.HasValue) {
            if (hasNoIndexWarning) {
              view.NoIndexFallback = true;
            }
            if (!view.ReceivedServerDataPush) {
              RtdbNode fallback = (hasNoIndexWarning && HasSyncedCoveringViewUnlocked(view, spec.Path))
                  ? _serverRoot.GetChild(spec.Path)
                  : EmptyNode.Instance;
              _taggedServerCache[tag.Value] = (spec, fallback, true);
            } else if (_taggedServerCache.TryGetValue(tag.Value, out (QuerySpec Spec, RtdbNode Node, bool Complete) cur) && !cur.Complete) {
              _taggedServerCache[tag.Value] = (cur.Spec, cur.Node, true);
            }
          } else {
            if (!view.ReceivedServerDataPush) {
              _serverRoot = _serverRoot.UpdateChild(spec.Path, EmptyNode.Instance);
            }
            _serverCompletePaths[spec.Path] = ++_lruClock;
          }
          view.MarkServerSyncedUnlocked();
          RefreshSingleViewUnlocked(view, events);
        } else {
          DatabaseError err = DatabaseError.FromStatus(status, detail);
          view.ServerSyncTcs.TrySetResult(false);
          view.ServerUpdateTcs.TrySetResult(false);
          view.CancelAll(err, events);
          if (tag.HasValue) {
            _taggedServerCache.Remove(tag.Value);
          }
          _views.Remove(spec);
        }
      }

      EnqueueEvents(events);
    }

    private bool HasSyncedCoveringViewUnlocked(QueryView targetView, RtdbPath path) {
      foreach (KeyValuePair<QuerySpec, QueryView> kv in _views) {
        if (ReferenceEquals(kv.Value, targetView)) continue;
        if (!kv.Value.ServerSynced) continue;
        if (kv.Value.Tag.HasValue && !kv.Value.NoIndexFallback) continue;
        if (path.StartsWith(kv.Key.Path)) {
          return true;
        }
      }
      return false;
    }

    private void HandleWriteAck(
        long writeId,
        RtdbPath path,
        string status,
        string detail,
        TaskCompletionSource<bool> tcs) {
      if (string.Equals(status, "write_canceled", StringComparison.OrdinalIgnoreCase)) {
        tcs?.TrySetException(DatabaseError.FromCode(DatabaseError.WriteCanceled).ToException());
        return;
      }

      bool isOk = string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase);
      var events = new List<DispatchedEvent>();
      long persistSeq = -1;
      RtdbPersistence persistence = null;

      lock (_repoLock) {
        if (_disposed) {
          tcs?.TrySetException(new DatabaseException("FirebaseDatabase has been disposed."));
          return;
        }

        if (_writeTree.RemoveWrite(writeId, out UserWriteRecord removed)) {
          if (isOk) {
            // Apply confirmed write into _serverRoot and mark complete for offline/persistence cache
            if (!removed.IsMerge) {
              _serverRoot = _serverRoot.UpdateChild(removed.Path, removed.ResolvedOverwrite);
              _serverCompletePaths[removed.Path] = ++_lruClock;
              AlsoUpdateOverlappingTaggedCachesUnlocked(removed.Path, removed.ResolvedOverwrite);
            } else {
              for (int m = 0; m < removed.ResolvedMerge.Count; m++) {
                KeyValuePair<RtdbPath, RtdbNode> entry = removed.ResolvedMerge[m];
                RtdbPath target = removed.Path.Child(entry.Key);
                _serverRoot = _serverRoot.UpdateChild(target, entry.Value);
                _serverCompletePaths[target] = ++_lruClock;
                AlsoUpdateOverlappingTaggedCachesUnlocked(target, entry.Value);
              }
            }
          }
          RecomputeViewsUnlocked(path, events);
          if (_persistence != null) {
            persistence = _persistence;
            persistSeq = ++_persistSeq;
          }
        }
      }

      if (persistence != null) {
        SchedulePersistenceSave(persistence, persistSeq);
      }
      EnqueueEvents(events);

      if (isOk) {
        tcs?.TrySetResult(true);
      } else {
        tcs?.TrySetException(DatabaseError.FromStatus(status, detail).ToException());
      }
    }

    private void UpdateDotInfo(string key, RtdbNode value) {
      var events = new List<DispatchedEvent>();
      lock (_repoLock) {
        if (_disposed) return;
        _infoRoot = _infoRoot.UpdateImmediateChild(key, value);
        RtdbPath infoPath = RtdbPath.ParseUnchecked(".info").ChildKey(key);
        foreach (KeyValuePair<QuerySpec, QueryView> kv in _views) {
          if (kv.Key.Path.IsDotInfo &&
              (kv.Key.Path.StartsWith(infoPath) || infoPath.StartsWith(kv.Key.Path))) {
            RefreshSingleViewUnlocked(kv.Value, events);
          }
        }
      }
      EnqueueEvents(events);
    }

    private QueryView GetOrCreateViewUnlocked(QuerySpec spec, out bool created) {
      if (_views.TryGetValue(spec, out QueryView existing)) {
        created = false;
        return existing;
      }
      created = true;
      long? tag = null;
      if (!spec.LoadsAllData && !spec.Path.IsDotInfo) {
        long t = ++_nextTagId;
        tag = t;
        _taggedServerCache[t] = (spec, EmptyNode.Instance, false);
      }
      var view = new QueryView(_database, spec, tag);
      _views[spec] = view;
      return view;
    }

    private void AlsoUpdateOverlappingTaggedCachesUnlocked(RtdbPath changedPath, RtdbNode newNode) {
      if (_taggedServerCache.Count == 0) return;
      var keys = new List<long>(_taggedServerCache.Keys);
      for (int i = 0; i < keys.Count; i++) {
        long k = keys[i];
        (QuerySpec Spec, RtdbNode Node, bool Complete) entry = _taggedServerCache[k];
        if (!entry.Complete) continue;
        if (RtdbPath.TryGetRelative(changedPath, entry.Spec.Path, out RtdbPath relToSpec)) {
          _taggedServerCache[k] = (entry.Spec, newNode.GetChild(relToSpec), true);
        } else if (RtdbPath.TryGetRelative(entry.Spec.Path, changedPath, out RtdbPath relToChange)) {
          _taggedServerCache[k] = (entry.Spec, entry.Node.UpdateChild(relToChange, newNode), true);
        }
      }
    }

    private void RecomputeViewsUnlocked(RtdbPath affectedPath, List<DispatchedEvent> outEvents) {
      if (_views.Count == 0) return;
      var viewsSnapshot = new List<QueryView>(_views.Values);
      for (int i = 0; i < viewsSnapshot.Count; i++) {
        QueryView view = viewsSnapshot[i];
        if (view.Spec.Path.IsDotInfo) continue;
        if (affectedPath.IsEmpty ||
            view.Spec.Path.StartsWith(affectedPath) ||
            affectedPath.StartsWith(view.Spec.Path)) {
          RefreshSingleViewUnlocked(view, outEvents);
        }
      }
    }

    private void RefreshSingleViewUnlocked(QueryView view, List<DispatchedEvent> outEvents) {
      QuerySpec spec = view.Spec;
      if (spec.Path.IsDotInfo) {
        RtdbNode infoNode = _infoRoot.GetChild(spec.Path.PopFront());
        if (outEvents != null) {
          view.UpdateFromCompleteNode(infoNode, complete: true, outEvents);
        } else {
          var discard = new List<DispatchedEvent>();
          view.UpdateFromCompleteNode(infoNode, complete: true, discard);
        }
        return;
      }

      if (view.HasOnlyOneShotListeners && !view.ServerSynced && _connection != null && _connection.IsDesiredOnline) {
        return;
      }

      RtdbNode unfiltered = CalcQueryBaseNodeUnlocked(spec, view.Tag, null, out bool complete);
      if (!complete) return;

      if (outEvents != null) {
        view.UpdateFromCompleteNode(unfiltered, complete: true, outEvents);
      } else {
        var discard = new List<DispatchedEvent>();
        view.UpdateFromCompleteNode(unfiltered, complete: true, discard);
      }
    }

    private RtdbNode CalcQueryBaseNodeUnlocked(
        QuerySpec spec,
        long? tag,
        HashSet<long> ignoreWriteIds,
        out bool complete) {
      if (tag.HasValue && _taggedServerCache.TryGetValue(tag.Value, out (QuerySpec Spec, RtdbNode Node, bool Complete) tagged)) {
        if (tagged.Complete) {
          return _writeTree.CalcCompleteNode(spec.Path, tagged.Node, serverComplete: true, ignoreWriteIds, out complete);
        }
        // Also check if complete untagged server cache covers spec.Path!
        if (IsServerPathCompleteUnlocked(spec.Path)) {
          RtdbNode serverSlice = _serverRoot.GetChild(spec.Path);
          return _writeTree.CalcCompleteNode(spec.Path, serverSlice, serverComplete: true, ignoreWriteIds, out complete);
        }
        return _writeTree.CalcCompleteNode(spec.Path, EmptyNode.Instance, serverComplete: false, ignoreWriteIds, out complete);
      }

      return CalcLocalNodeUnlocked(spec.Path, ignoreWriteIds, out complete);
    }

    private RtdbNode CalcLocalNodeUnlocked(RtdbPath path, HashSet<long> ignoreWriteIds, out bool complete) {
      bool serverComplete = IsServerPathCompleteUnlocked(path);
      RtdbNode serverSlice = serverComplete ? _serverRoot.GetChild(path) : EmptyNode.Instance;
      return _writeTree.CalcCompleteNode(path, serverSlice, serverComplete, ignoreWriteIds, out complete);
    }

    private bool IsServerPathCompleteUnlocked(RtdbPath path) {
      foreach (KeyValuePair<RtdbPath, long> kv in _serverCompletePaths) {
        if (path.StartsWith(kv.Key)) {
          return true;
        }
      }
      return false;
    }

    private void TouchLruUnlocked(RtdbPath path) {
      foreach (RtdbPath key in new List<RtdbPath>(_serverCompletePaths.Keys)) {
        if (path.StartsWith(key)) {
          _serverCompletePaths[key] = ++_lruClock;
        }
      }
    }

    private List<ActiveTransaction> CollectOverlappingTransactionsUnlocked(RtdbPath path) {
      if (_activeTransactions.Count == 0) return null;
      List<ActiveTransaction> list = null;
      for (int i = 0; i < _activeTransactions.Count; i++) {
        ActiveTransaction tx = _activeTransactions[i];
        if (path.IsEmpty || tx.Path.StartsWith(path) || path.StartsWith(tx.Path)) {
          list ??= new List<ActiveTransaction>();
          list.Add(tx);
        }
      }
      return list;
    }

    private static void AbortTransactions(List<ActiveTransaction> list, int errorCode) {
      if (list == null) return;
      for (int i = 0; i < list.Count; i++) {
        list[i].Abort(errorCode);
      }
    }

    private void SchedulePersistenceSave(RtdbPersistence persistence, long seq) {
      List<PersistedCacheEntry> cacheEntries;
      List<UserWriteRecord> writesSnapshot;

      lock (_repoLock) {
        if (_disposed) return;
        PruneServerCacheIfNeededUnlocked();
        cacheEntries = new List<PersistedCacheEntry>(_serverCompletePaths.Count);
        foreach (KeyValuePair<RtdbPath, long> kv in _serverCompletePaths) {
          RtdbNode node = _serverRoot.GetChild(kv.Key);
          cacheEntries.Add(new PersistedCacheEntry {
            Path = kv.Key,
            Node = node,
            LastAccessTick = kv.Value,
            ApproxBytes = RtdbPersistence.EstimateNodeBytes(node),
          });
        }
        writesSnapshot = new List<UserWriteRecord>(_writeTree.AllWrites);
      }

      _ = Task.Run(() => {
        try {
          persistence.Save(seq, cacheEntries, writesSnapshot);
        } catch (Exception ex) {
          Log(LogLevel.Warning, $"RTDB persistence save failed: {ex.Message}");
        }
      });
    }

    private void PruneServerCacheIfNeededUnlocked() {
      if (_serverCompletePaths.Count == 0) return;

      var candidates = new List<(RtdbPath Path, long Tick, long Bytes, bool Pinned)>(_serverCompletePaths.Count);
      long totalBytes = 0;
      foreach (KeyValuePair<RtdbPath, long> kv in _serverCompletePaths) {
        RtdbNode node = _serverRoot.GetChild(kv.Key);
        long bytes = RtdbPersistence.EstimateNodeBytes(node);
        totalBytes += bytes;
        bool pinned = IsPathPinnedUnlocked(kv.Key);
        candidates.Add((kv.Key, kv.Value, bytes, pinned));
      }

      if (totalBytes <= _persistenceCacheSizeBytes) return;

      // Sort oldest LRU tick first.
      candidates.Sort((a, b) => a.Tick.CompareTo(b.Tick));
      for (int i = 0; i < candidates.Count && totalBytes > _persistenceCacheSizeBytes; i++) {
        if (candidates[i].Pinned) continue;
        RtdbPath evictPath = candidates[i].Path;
        _serverCompletePaths.Remove(evictPath);
        _serverRoot = _serverRoot.UpdateChild(evictPath, EmptyNode.Instance);
        totalBytes -= candidates[i].Bytes;
      }
    }

    private bool IsPathPinnedUnlocked(RtdbPath path) {
      foreach (KeyValuePair<QuerySpec, QueryView> kv in _views) {
        if (!kv.Value.NeedsNetworkListen) continue;
        if (kv.Key.Path.StartsWith(path) || path.StartsWith(kv.Key.Path)) {
          return true;
        }
      }
      return false;
    }

    private void EnqueueEvents(List<DispatchedEvent> events) {
      if (events == null || events.Count == 0 || _disposed) return;
      bool queuedAny = false;
      for (int i = 0; i < events.Count; i++) {
        DispatchedEvent ev = events[i];
        ListenerRegistration reg = ev.Registration;
        if (reg == null || !reg.Active) continue;
        if (reg.IsOneShot) {
          ValueChangedEventArgs args = ev.Error != null
              ? new ValueChangedEventArgs(ev.Error)
              : new ValueChangedEventArgs(ev.Snapshot);
          InvokeSafely(() => reg.ValueHandler?.Invoke(null, args));
          reg.Active = false;
        } else {
          _eventQueue.Enqueue(ev);
          queuedAny = true;
        }
      }
      if (queuedAny) {
        ScheduleEventDrain();
      }
    }

    private void ScheduleEventDrain() {
      if (Interlocked.CompareExchange(ref _drainScheduled, 1, 0) != 0) {
        return;
      }
      FirebaseHandler handler = FirebaseHandler.DefaultInstance;
      if (handler != null) {
        FirebaseHandler.RunOnMainThreadAsync(() => {
          DrainEventsOnTargetThread();
          return true;
        });
      } else {
        ThreadPool.QueueUserWorkItem(_ => DrainEventsOnTargetThread());
      }
    }

    private void DrainEventsOnTargetThread() {
      try {
        while (_eventQueue.TryDequeue(out DispatchedEvent ev)) {
          if (_disposed) continue;
          ListenerRegistration reg = ev.Registration;
          if (reg == null || !reg.Active) {
            continue;
          }
          if (reg.Kind == EventKind.ValueChanged) {
            ValueChangedEventArgs args = ev.Error != null
                ? new ValueChangedEventArgs(ev.Error)
                : new ValueChangedEventArgs(ev.Snapshot);
            InvokeSafely(() => {
              if (reg.Active && !_disposed) {
                if (reg.IsOneShot) {
                  reg.Active = false;
                }
                reg.ValueHandler?.Invoke(null, args);
              }
            });
          } else {
            ChildChangedEventArgs args = ev.Error != null
                ? new ChildChangedEventArgs(ev.Error)
                : new ChildChangedEventArgs(ev.Snapshot, ev.PreviousChildName);
            InvokeSafely(() => {
              if (reg.Active && !_disposed) {
                reg.ChildHandler?.Invoke(null, args);
              }
            });
          }
        }
      } finally {
        Interlocked.Exchange(ref _drainScheduled, 0);
        if (!_disposed && !_eventQueue.IsEmpty) {
          ScheduleEventDrain();
        }
      }
    }

    private static void InvokeSafely(Action userCallback) {
      try {
        userCallback();
      } catch (Exception ex) {
        ExceptionAggregator.LogException(ex);
      }
    }

    private static Task<T> InvokeOnMainThreadAsync<T>(Func<T> func, bool allowInlineIfAlreadyMainThread) {
      FirebaseHandler handler = FirebaseHandler.DefaultInstance;
      if (handler == null) {
        try {
          return Task.FromResult(func());
        } catch (Exception ex) {
          return Task.FromException<T>(ex);
        }
      }
      if (allowInlineIfAlreadyMainThread && handler.IsMainThread()) {
        try {
          return Task.FromResult(func());
        } catch (Exception ex) {
          return Task.FromException<T>(ex);
        }
      }
      return FirebaseHandler.RunOnMainThreadAsync(func);
    }

    internal void Log(LogLevel level, string message) {
      if (level < _logLevel) return;
      try {
        LogUtil.LogMessage(level, "[Firebase.Database] " + message);
      } catch (Exception) {
        // Ignore logging failures in headless tests when native logger is absent.
      }
    }

    public void Dispose() {
      RtdbConnection conn;
      List<ActiveTransaction> activeTx;
      lock (_repoLock) {
        if (_disposed) return;
        _disposed = true;
        conn = _connection;
        _connection = null;
        foreach (KeyValuePair<QuerySpec, QueryView> kv in _views) {
          kv.Value.DeactivateAll();
        }
        _views.Clear();
        _taggedServerCache.Clear();
        activeTx = new List<ActiveTransaction>(_activeTransactions);
        _activeTransactions.Clear();
      }

      while (_eventQueue.TryDequeue(out _)) { }
      AbortTransactions(activeTx, DatabaseError.Disconnected);
      conn?.Dispose();
    }
  }
}
