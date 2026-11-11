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
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Internal;

namespace Firebase.Firestore.Internal {
  internal sealed class WriteCommitResult {
    internal Timestamp UpdateTime { get; }
    internal IReadOnlyList<FirestoreValue> TransformResults { get; }

    internal WriteCommitResult(Timestamp updateTime, IReadOnlyList<FirestoreValue> transformResults) {
      UpdateTime = updateTime;
      TransformResults = transformResults ?? Array.Empty<FirestoreValue>();
    }
  }

  internal sealed class CommitBatchResponse {
    internal Timestamp CommitTime { get; }
    internal IReadOnlyList<WriteCommitResult> WriteResults { get; }

    internal CommitBatchResponse(Timestamp commitTime, IReadOnlyList<WriteCommitResult> writeResults) {
      CommitTime = commitTime;
      WriteResults = writeResults ?? Array.Empty<WriteCommitResult>();
    }
  }

#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class FirestoreRpcClient : IDisposable {
    private static readonly object SharedClientLock = new object();
    private static HttpClient _sharedUnaryClient;
    private static HttpClient _sharedStreamClient;

    static FirestoreRpcClient() {
      FirebaseInstanceCache.RegisterResetHook(ResetSharedClientsForReload);
    }

    private static void ResetSharedClientsForReload() {
      HttpClient u, s;
      lock (SharedClientLock) {
        u = _sharedUnaryClient;
        s = _sharedStreamClient;
        _sharedUnaryClient = null;
        _sharedStreamClient = null;
      }
      try {
        u?.Dispose();
      } catch (Exception) {
        // Ignore teardown race on reload.
      }
      try {
        s?.Dispose();
      } catch (Exception) {
        // Ignore teardown race on reload.
      }
    }

    private readonly FirebaseApp _app;
    private readonly string _projectId;
    private readonly string _databaseId;
    private readonly string _baseOrigin;
    private readonly string _documentsBaseUrl;
    private readonly string _webChannelBaseUrl;
    private readonly string _databaseResourceName;
    private readonly string _requestParamsHeader;

    private readonly object _customClientLock = new object();
    private Func<HttpMessageHandler> _boundFactory;
    private HttpClient _customUnaryClient;
    private HttpClient _customStreamClient;
    private volatile bool _disposed;

    internal string ProjectId => _projectId;
    internal string DatabaseId => _databaseId;
    internal string DatabaseResourceName => _databaseResourceName;

    internal FirestoreRpcClient(
        FirebaseApp app,
        string projectId,
        string databaseId,
        string host,
        bool sslEnabled) {
      _app = app;
      _projectId = projectId ?? string.Empty;
      _databaseId = string.IsNullOrEmpty(databaseId) ? "(default)" : databaseId;
      string cleanHost = (host ?? "firestore.googleapis.com").TrimEnd('/');
      string scheme = sslEnabled ? "https" : "http";
      _baseOrigin = $"{scheme}://{cleanHost}";
      string encProj = Uri.EscapeDataString(_projectId);
      string encDb = Uri.EscapeDataString(_databaseId);
      _documentsBaseUrl = $"{_baseOrigin}/v1/projects/{encProj}/databases/{encDb}/documents";
      _webChannelBaseUrl = $"{_baseOrigin}/google.firestore.v1.Firestore/Listen/channel";
      _databaseResourceName = $"projects/{_projectId}/databases/{_databaseId}";
      _requestParamsHeader = $"project_id={encProj}&database_id={encDb}";
    }

    internal HttpClient GetUnaryClient() => GetClient(streaming: false);
    internal HttpClient GetStreamClient() => GetClient(streaming: true);

    private HttpClient GetClient(bool streaming) {
      Func<HttpMessageHandler> customFactory = HttpHelpers.CustomHandlerFactory;
      if (customFactory == null) {
        lock (SharedClientLock) {
          if (streaming) {
            return _sharedStreamClient ??=
                HttpHelpers.CreateHttpClient(Timeout.InfiniteTimeSpan, maxConnectionsPerServer: 32);
          }
          return _sharedUnaryClient ??=
              HttpHelpers.CreateHttpClient(TimeSpan.FromSeconds(60), maxConnectionsPerServer: 32);
        }
      }

      lock (_customClientLock) {
        if (!ReferenceEquals(_boundFactory, customFactory)) {
          try {
            _customUnaryClient?.Dispose();
          } catch (Exception) {
            // Ignore old client dispose.
          }
          try {
            _customStreamClient?.Dispose();
          } catch (Exception) {
            // Ignore old client dispose.
          }
          _boundFactory = customFactory;
          _customUnaryClient =
              HttpHelpers.CreateHttpClient(TimeSpan.FromSeconds(60), maxConnectionsPerServer: 32);
          _customStreamClient =
              HttpHelpers.CreateHttpClient(Timeout.InfiniteTimeSpan, maxConnectionsPerServer: 32);
        }
        return streaming ? _customStreamClient : _customUnaryClient;
      }
    }

    internal async Task PopulateHeadersAsync(HttpRequestMessage req) {
      req.Headers.TryAddWithoutValidation("x-goog-request-params", _requestParamsHeader);
      req.Headers.TryAddWithoutValidation(
          "x-goog-api-client", $"gl-csharp/8.0 fire/{FirebaseInterops.GetVersionInfoSdkVersion()}");
      if (_app != null) {
        string apiKey = null;
        try {
          apiKey = _app.Options?.ApiKey;
        } catch (Exception) {
          // App may be tearing down.
        }
        if (!string.IsNullOrEmpty(apiKey)) {
          req.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        }
        HttpHelpers.SetDataCollectionHeaders(req, _app);
        await FirebaseInterops.AddFirebaseTokensAsync(req, _app, authTokenPrefix: "Bearer")
            .ConfigureAwait(false);
      }
    }

    internal async Task<string> BuildWebChannelHttpHeadersParamAsync() {
      var sb = new StringBuilder(256);
      AppendHeaderPair(
          sb, "X-Goog-Api-Client", $"gl-csharp/8.0 fire/{FirebaseInterops.GetVersionInfoSdkVersion()}");
      AppendHeaderPair(sb, "Content-Type", "text/plain");
      AppendHeaderPair(sb, "x-goog-request-params", _requestParamsHeader);
      if (_app != null) {
        string apiKey = null;
        try {
          apiKey = _app.Options?.ApiKey;
        } catch (Exception) {
          // App may be tearing down.
        }
        if (!string.IsNullOrEmpty(apiKey)) {
          AppendHeaderPair(sb, "x-goog-api-key", apiKey);
        }
        string auth = await FirebaseInterops.GetAuthTokenAsync(_app).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(auth)) {
          AppendHeaderPair(sb, "Authorization", $"Bearer {auth}");
        }
        string appCheck = await FirebaseInterops.GetAppCheckTokenAsync(_app).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(appCheck)) {
          AppendHeaderPair(sb, "X-Firebase-AppCheck", appCheck);
        }
      }
      return sb.ToString();
    }

    private static void AppendHeaderPair(StringBuilder sb, string key, string val) {
      if (sb.Length > 0) sb.Append("\r\n");
      sb.Append(key).Append(": ").Append(val);
    }

    internal string WebChannelBaseUrl => _webChannelBaseUrl;

    private void EnsureProjectConfigured() {
      if (string.IsNullOrEmpty(_projectId)) {
        throw new FirestoreException(
            FirestoreError.InvalidArgument, "FirebaseOptions.ProjectId must not be empty.");
      }
    }

    private async Task<T> SendWithRetryAsync<T>(
        Func<PooledUtf8JsonWriter> buildBody,
        string url,
        Func<Stream, Task<T>> parseStreamAsync,
        CancellationToken ct) {
      EnsureProjectConfigured();
      var backoff = new RetryBackoff(
          maxRetryTime: TimeSpan.FromSeconds(30),
          wrapExhaustedError: last => last as FirestoreException ?? MapTransportException(last, ct),
          initialBackoffMs: 200,
          maxBackoffMs: 5000,
          maxJitterMs: 100);

      while (true) {
        ct.ThrowIfCancellationRequested();
        if (_disposed) {
          throw new FirestoreException(FirestoreError.Cancelled, "Firestore client terminated.");
        }

        PooledUtf8JsonWriter writer = buildBody();
        using var req = new HttpRequestMessage(HttpMethod.Post, url) {
          Content = new PooledJsonHttpContent(writer),
        };
        await PopulateHeadersAsync(req).ConfigureAwait(false);

        HttpResponseMessage resp = null;
        try {
          resp = await GetUnaryClient()
              .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
              .ConfigureAwait(false);
          if (resp.IsSuccessStatusCode) {
            using Stream stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            return await parseStreamAsync(stream).ConfigureAwait(false);
          }

          int code = (int)resp.StatusCode;
          string errBody = await HttpHelpers.ReadErrorBodySafeAsync(resp).ConfigureAwait(false);
          FirestoreException fex = FirestoreJson.ParseErrorResponse(resp.StatusCode, errBody);
          if (HttpHelpers.IsRetryableStatusCode(code)) {
            await backoff.DelayOrThrowAsync(transportFailure: false, lastError: fex, token: ct)
                .ConfigureAwait(false);
            continue;
          }
          throw fex;
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
          throw new FirestoreException(FirestoreError.Cancelled, "Operation was cancelled.");
        } catch (FirestoreException) {
          throw;
        } catch (Exception ex) when (HttpHelpers.IsTransportError(ex)) {
          FirestoreException mapped = MapTransportException(ex, ct);
          await backoff.DelayOrThrowAsync(transportFailure: true, lastError: mapped, token: ct)
              .ConfigureAwait(false);
        } finally {
          resp?.Dispose();
        }
      }
    }

    internal static FirestoreException MapTransportException(Exception ex, CancellationToken ct) {
      if (ct.IsCancellationRequested) {
        return new FirestoreException(FirestoreError.Cancelled, "Operation was cancelled.");
      }
      if (ex is TaskCanceledException || ex is TimeoutException) {
        return new FirestoreException(
            FirestoreError.DeadlineExceeded, $"Firestore request timed out: {ex.Message}");
      }
      return new FirestoreException(
          FirestoreError.Unavailable, $"Firestore network transport error: {ex?.Message}");
    }

    internal Task<Dictionary<ResourcePath, MutableDocument>> BatchGetAsync(
        IReadOnlyList<ResourcePath> paths, string transactionId, CancellationToken ct) {
      string url = $"{_documentsBaseUrl}:batchGet";
      return SendWithRetryAsync(
          buildBody: () => {
            var w = new PooledUtf8JsonWriter();
            w.WriteAscii("{\"documents\":[");
            for (int i = 0; i < paths.Count; i++) {
              if (i > 0) w.WriteByte((byte)',');
              w.WriteString(paths[i].ToFullResourceName(_projectId, _databaseId));
            }
            w.WriteByte((byte)']');
            if (!string.IsNullOrEmpty(transactionId)) {
              w.WriteAscii(",\"transaction\":");
              w.WriteString(transactionId);
            }
            w.WriteByte((byte)'}');
            return w;
          },
          url: url,
          parseStreamAsync: async stream => {
            var results = new Dictionary<ResourcePath, MutableDocument>(paths.Count);
            using var r = new Utf8JsonStreamReader(stream);
            if (!await r.ReadAsync().ConfigureAwait(false) || r.Token != JsonToken.StartArray) {
              return results;
            }
            while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndArray) {
              if (r.Token != JsonToken.StartObject) {
                await r.SkipValueAsync().ConfigureAwait(false);
                continue;
              }
              while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndObject) {
                if (r.Token != JsonToken.String) {
                  await r.SkipValueAsync().ConfigureAwait(false);
                  continue;
                }
                string k = r.StringText;
                if (!await r.ReadAsync().ConfigureAwait(false)) break;
                if (k == "found" && r.Token == JsonToken.StartObject) {
                  MutableDocument doc = await FirestoreJson.ReadDocumentAsync(r).ConfigureAwait(false);
                  results[doc.Path] = doc;
                } else if (k == "missing" && r.Token == JsonToken.String) {
                  if (ResourcePath.TryParseResourceName(r.StringText, out _, out _, out ResourcePath mp)) {
                    results[mp] = new MutableDocument(mp, exists: false, fields: null, version: default);
                  }
                } else if (k == "error" && r.Token == JsonToken.StartObject) {
                  throw await ReadStreamInlineErrorAsync(r).ConfigureAwait(false);
                } else {
                  await r.SkipValueAsync().ConfigureAwait(false);
                }
              }
            }
            return results;
          },
          ct: ct);
    }

    internal Task<CommitBatchResponse> CommitAsync(
        IReadOnlyList<MutationSpec> mutations, string transactionId, CancellationToken ct) {
      string url = $"{_documentsBaseUrl}:commit";
      return SendWithRetryAsync(
          buildBody: () => {
            var w = new PooledUtf8JsonWriter();
            w.WriteAscii("{\"writes\":[");
            for (int i = 0; i < mutations.Count; i++) {
              if (i > 0) w.WriteByte((byte)',');
              FirestoreJson.WriteMutation(w, mutations[i], _projectId, _databaseId);
            }
            w.WriteByte((byte)']');
            if (!string.IsNullOrEmpty(transactionId)) {
              w.WriteAscii(",\"transaction\":");
              w.WriteString(transactionId);
            }
            w.WriteByte((byte)'}');
            return w;
          },
          url: url,
          parseStreamAsync: async stream => {
            Timestamp commitTime = default;
            var writeResults = new List<WriteCommitResult>(mutations.Count);
            using var r = new Utf8JsonStreamReader(stream);
            if (!await r.ReadAsync().ConfigureAwait(false) || r.Token != JsonToken.StartObject) {
              return new CommitBatchResponse(commitTime, writeResults);
            }
            while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndObject) {
              if (r.Token != JsonToken.String) {
                await r.SkipValueAsync().ConfigureAwait(false);
                continue;
              }
              string k = r.StringText;
              if (!await r.ReadAsync().ConfigureAwait(false)) break;
              if (k == "commitTime") {
                commitTime = await FirestoreJson.ReadTimestampValueAsync(r).ConfigureAwait(false);
              } else if (k == "writeResults" && r.Token == JsonToken.StartArray) {
                while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndArray) {
                  if (r.Token == JsonToken.StartObject) {
                    writeResults.Add(await ReadSingleWriteResultAsync(r).ConfigureAwait(false));
                  } else {
                    await r.SkipValueAsync().ConfigureAwait(false);
                  }
                }
              } else {
                await r.SkipValueAsync().ConfigureAwait(false);
              }
            }
            return new CommitBatchResponse(commitTime, writeResults);
          },
          ct: ct);
    }

    private static async ValueTask<WriteCommitResult> ReadSingleWriteResultAsync(Utf8JsonStreamReader r) {
      Timestamp updateTime = default;
      List<FirestoreValue> transformResults = null;
      while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndObject) {
        if (r.Token != JsonToken.String) {
          await r.SkipValueAsync().ConfigureAwait(false);
          continue;
        }
        string k = r.StringText;
        if (!await r.ReadAsync().ConfigureAwait(false)) break;
        if (k == "updateTime") {
          updateTime = await FirestoreJson.ReadTimestampValueAsync(r).ConfigureAwait(false);
        } else if (k == "transformResults" && r.Token == JsonToken.StartArray) {
          transformResults = new List<FirestoreValue>();
          while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndArray) {
            if (r.Token == JsonToken.StartObject) {
              transformResults.Add(await FirestoreJson.ReadFirestoreValueAsync(r).ConfigureAwait(false));
            } else {
              await r.SkipValueAsync().ConfigureAwait(false);
            }
          }
        } else {
          await r.SkipValueAsync().ConfigureAwait(false);
        }
      }
      return new WriteCommitResult(updateTime, transformResults);
    }

    internal Task<List<MutableDocument>> RunQueryAsync(
        QuerySpec query, string transactionId, CancellationToken ct) {
      query.ValidateForExecution();
      ResourcePath parentPath = query.IsCollectionGroup
          ? ResourcePath.Empty
          : (query.Path.Parent ?? ResourcePath.Empty);
      string parentUrl = parentPath.IsEmpty
          ? _documentsBaseUrl
          : $"{_documentsBaseUrl}/{EncodeResourceSegments(parentPath)}";
      string url = $"{parentUrl}:runQuery";

      return SendWithRetryAsync(
          buildBody: () => {
            var w = new PooledUtf8JsonWriter();
            w.WriteAscii("{\"structuredQuery\":");
            FirestoreJson.WriteQueryTarget(w, query, _projectId, _databaseId, out _);
            if (!string.IsNullOrEmpty(transactionId)) {
              w.WriteAscii(",\"transaction\":");
              w.WriteString(transactionId);
            }
            w.WriteByte((byte)'}');
            return w;
          },
          url: url,
          parseStreamAsync: async stream => {
            var docs = new List<MutableDocument>();
            using var r = new Utf8JsonStreamReader(stream);
            if (!await r.ReadAsync().ConfigureAwait(false) || r.Token != JsonToken.StartArray) {
              return docs;
            }
            while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndArray) {
              if (r.Token != JsonToken.StartObject) {
                await r.SkipValueAsync().ConfigureAwait(false);
                continue;
              }
              while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndObject) {
                if (r.Token != JsonToken.String) {
                  await r.SkipValueAsync().ConfigureAwait(false);
                  continue;
                }
                string k = r.StringText;
                if (!await r.ReadAsync().ConfigureAwait(false)) break;
                if (k == "document" && r.Token == JsonToken.StartObject) {
                  docs.Add(await FirestoreJson.ReadDocumentAsync(r).ConfigureAwait(false));
                } else if (k == "error" && r.Token == JsonToken.StartObject) {
                  throw await ReadStreamInlineErrorAsync(r).ConfigureAwait(false);
                } else {
                  await r.SkipValueAsync().ConfigureAwait(false);
                }
              }
            }
            if (query.LimitType == LimitKind.Last && docs.Count > 1) {
              docs.Reverse();
            }
            return docs;
          },
          ct: ct);
    }

    internal Task<long> RunCountAggregationAsync(QuerySpec query, CancellationToken ct) {
      query.ValidateForExecution();
      ResourcePath parentPath = query.IsCollectionGroup
          ? ResourcePath.Empty
          : (query.Path.Parent ?? ResourcePath.Empty);
      string parentUrl = parentPath.IsEmpty
          ? _documentsBaseUrl
          : $"{_documentsBaseUrl}/{EncodeResourceSegments(parentPath)}";
      string url = $"{parentUrl}:runAggregationQuery";

      return SendWithRetryAsync(
          buildBody: () => {
            var w = new PooledUtf8JsonWriter();
            w.WriteAscii("{\"structuredAggregationQuery\":{\"structuredQuery\":");
            FirestoreJson.WriteQueryTarget(w, query, _projectId, _databaseId, out _);
            w.WriteAscii(",\"aggregations\":[{\"alias\":\"count_1\",\"count\":{}}]}}");
            return w;
          },
          url: url,
          parseStreamAsync: async stream => {
            long count = 0;
            using var r = new Utf8JsonStreamReader(stream);
            if (!await r.ReadAsync().ConfigureAwait(false) || r.Token != JsonToken.StartArray) {
              return count;
            }
            while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndArray) {
              if (r.Token != JsonToken.StartObject) {
                await r.SkipValueAsync().ConfigureAwait(false);
                continue;
              }
              while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndObject) {
                if (r.Token != JsonToken.String) {
                  await r.SkipValueAsync().ConfigureAwait(false);
                  continue;
                }
                string k = r.StringText;
                if (!await r.ReadAsync().ConfigureAwait(false)) break;
                if (k == "result" && r.Token == JsonToken.StartObject) {
                  count = await ReadAggregationResultCountAsync(r).ConfigureAwait(false);
                } else if (k == "error" && r.Token == JsonToken.StartObject) {
                  throw await ReadStreamInlineErrorAsync(r).ConfigureAwait(false);
                } else {
                  await r.SkipValueAsync().ConfigureAwait(false);
                }
              }
            }
            return count;
          },
          ct: ct);
    }

    private static async ValueTask<long> ReadAggregationResultCountAsync(Utf8JsonStreamReader r) {
      long count = 0;
      while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndObject) {
        if (r.Token != JsonToken.String) {
          await r.SkipValueAsync().ConfigureAwait(false);
          continue;
        }
        string k = r.StringText;
        if (!await r.ReadAsync().ConfigureAwait(false)) break;
        if (k == "aggregateFields" && r.Token == JsonToken.StartObject) {
          while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndObject) {
            if (r.Token != JsonToken.String) {
              await r.SkipValueAsync().ConfigureAwait(false);
              continue;
            }
            if (!await r.ReadAsync().ConfigureAwait(false)) break;
            if (r.Token == JsonToken.StartObject) {
              FirestoreValue v = await FirestoreJson.ReadFirestoreValueAsync(r).ConfigureAwait(false);
              if (v.Kind == FirestoreValueKind.Integer) count = v.IntegerValue;
            } else {
              await r.SkipValueAsync().ConfigureAwait(false);
            }
          }
        } else {
          await r.SkipValueAsync().ConfigureAwait(false);
        }
      }
      return count;
    }

    internal Task<string> BeginTransactionAsync(CancellationToken ct) {
      string url = $"{_documentsBaseUrl}:beginTransaction";
      return SendWithRetryAsync(
          buildBody: () => {
            var w = new PooledUtf8JsonWriter();
            w.WriteAscii("{\"options\":{\"readWrite\":{}}}");
            return w;
          },
          url: url,
          parseStreamAsync: async stream => {
            using var r = new Utf8JsonStreamReader(stream);
            string tx = null;
            if (await r.ReadAsync().ConfigureAwait(false) && r.Token == JsonToken.StartObject) {
              while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndObject) {
                if (r.Token == JsonToken.String && r.StringText == "transaction") {
                  if (await r.ReadAsync().ConfigureAwait(false) && r.Token == JsonToken.String) {
                    tx = r.StringText;
                  }
                } else {
                  if (await r.ReadAsync().ConfigureAwait(false)) {
                    await r.SkipValueAsync().ConfigureAwait(false);
                  }
                }
              }
            }
            return tx ?? string.Empty;
          },
          ct: ct);
    }

    internal async Task RollbackBestEffortAsync(string transactionId, CancellationToken ct) {
      if (string.IsNullOrEmpty(transactionId) || _disposed) return;
      try {
        await SendWithRetryAsync(
            buildBody: () => {
              var w = new PooledUtf8JsonWriter();
              w.WriteAscii("{\"transaction\":");
              w.WriteString(transactionId);
              w.WriteByte((byte)'}');
              return w;
            },
            url: $"{_documentsBaseUrl}:rollback",
            parseStreamAsync: _ => Task.FromResult(true),
            ct: ct).ConfigureAwait(false);
      } catch (Exception) {
        // Best-effort rollback on failed/retried transaction attempt.
      }
    }

    private static async ValueTask<FirestoreException> ReadStreamInlineErrorAsync(Utf8JsonStreamReader r) {
      int code = 2;
      string status = null;
      string message = null;
      while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndObject) {
        if (r.Token != JsonToken.String) {
          await r.SkipValueAsync().ConfigureAwait(false);
          continue;
        }
        string k = r.StringText;
        if (!await r.ReadAsync().ConfigureAwait(false)) break;
        if (k == "code" && (r.Token == JsonToken.Number || r.Token == JsonToken.String)) {
          int.TryParse(r.StringText, NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
        } else if (k == "status" && r.Token == JsonToken.String) {
          status = r.StringText;
        } else if (k == "message" && r.Token == JsonToken.String) {
          message = r.StringText;
        } else {
          await r.SkipValueAsync().ConfigureAwait(false);
        }
      }
      FirestoreError err = !string.IsNullOrEmpty(status)
          ? FirestoreJson.MapGrpcStatusOrHttp(status, HttpStatusCode.BadRequest)
          : FirestoreJson.MapGrpcIntCode(code);
      return new FirestoreException(err, message ?? "Firestore stream error.");
    }

    private static string EncodeResourceSegments(ResourcePath path) {
      if (path.Count == 1) return Uri.EscapeDataString(path.Segments[0]);
      var parts = new string[path.Count];
      for (int i = 0; i < path.Count; i++) {
        parts[i] = Uri.EscapeDataString(path.Segments[i]);
      }
      return string.Join("/", parts);
    }

    public void Dispose() {
      if (_disposed) return;
      _disposed = true;
      lock (_customClientLock) {
        try {
          _customUnaryClient?.Dispose();
        } catch (Exception) {
          // Ignore teardown race.
        }
        try {
          _customStreamClient?.Dispose();
        } catch (Exception) {
          // Ignore teardown race.
        }
        _customUnaryClient = null;
        _customStreamClient = null;
      }
    }
  }

  /// <summary>
  /// Multiplexed Google WebChannel (<c>VER=8</c>) realtime <c>Listen</c> stream matching
  /// <c>firebase-js-sdk</c>'s HTTP push streaming architecture.
  /// </summary>
  internal sealed class FirestoreWatchStream : IDisposable {
    internal sealed class WatchTargetSpec {
      internal int TargetId { get; }
      internal bool IsDocumentTarget { get; }
      internal ResourcePath DocumentPath { get; }
      internal QuerySpec Query { get; }
      internal string ResumeTokenBase64 { get; set; }

      internal WatchTargetSpec(int targetId, ResourcePath docPath) {
        TargetId = targetId;
        IsDocumentTarget = true;
        DocumentPath = docPath;
      }

      internal WatchTargetSpec(int targetId, QuerySpec query) {
        TargetId = targetId;
        IsDocumentTarget = false;
        Query = query;
      }
    }

    private readonly FirestoreRpcClient _rpc;
    private readonly Action<MutableDocument, IReadOnlyList<int>, IReadOnlyList<int>> _onDocChange;
    private readonly Action<ResourcePath, IReadOnlyList<int>> _onDocRemoveOrDelete;
    private readonly Action<IReadOnlyList<int>, string> _onTargetCurrent;
    private readonly Action<IReadOnlyList<int>> _onTargetReset;
    private readonly Action<int, int> _onExistenceFilter;
    private readonly Action<IReadOnlyList<int>, FirestoreException> _onTargetError;
    private readonly Action _onWatchBatchEnd;

    private readonly object _gate = new object();
    private readonly Dictionary<int, WatchTargetSpec> _targets = new Dictionary<int, WatchTargetSpec>();
    private readonly Queue<string> _outboundQueue = new Queue<string>();
    private TaskCompletionSource<bool> _outboundSignal =
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _networkEnabled = true;
    private bool _disposed;
    private CancellationTokenSource _sessionCts;
    private Task _runnerTask;
    private int _ridCounter = 10000;

    internal FirestoreWatchStream(
        FirestoreRpcClient rpc,
        Action<MutableDocument, IReadOnlyList<int>, IReadOnlyList<int>> onDocChange,
        Action<ResourcePath, IReadOnlyList<int>> onDocRemoveOrDelete,
        Action<IReadOnlyList<int>, string> onTargetCurrent,
        Action<IReadOnlyList<int>> onTargetReset,
        Action<int, int> onExistenceFilter,
        Action<IReadOnlyList<int>, FirestoreException> onTargetError,
        Action onWatchBatchEnd) {
      _rpc = rpc;
      _onDocChange = onDocChange;
      _onDocRemoveOrDelete = onDocRemoveOrDelete;
      _onTargetCurrent = onTargetCurrent;
      _onTargetReset = onTargetReset;
      _onExistenceFilter = onExistenceFilter;
      _onTargetError = onTargetError;
      _onWatchBatchEnd = onWatchBatchEnd;
    }

    internal void AddTarget(WatchTargetSpec spec) {
      lock (_gate) {
        if (_disposed) return;
        _targets[spec.TargetId] = spec;
        if (_sessionCts != null) {
          _outboundQueue.Enqueue(BuildAddTargetJson(spec));
          SignalOutboundLocked();
        }
        EnsureRunnerLocked();
      }
    }

    internal void RemoveTarget(int targetId) {
      CancellationTokenSource ctsToCancel = null;
      lock (_gate) {
        if (_disposed || !_targets.Remove(targetId)) return;
        if (_targets.Count == 0) {
          ctsToCancel = _sessionCts;
          _sessionCts = null;
          _runnerTask = null;
        } else if (_sessionCts != null) {
          _outboundQueue.Enqueue(
              $"{{\"database\":{JsonHelper.Serialize(_rpc.DatabaseResourceName)},\"removeTarget\":{targetId}}}");
          SignalOutboundLocked();
        }
      }
      if (ctsToCancel != null) {
        try {
          ctsToCancel.Cancel();
        } catch (Exception) {
          // Ignore teardown race.
        }
      }
    }

    internal void SetNetworkEnabled(bool enabled) {
      CancellationTokenSource ctsToCancel = null;
      lock (_gate) {
        if (_disposed || _networkEnabled == enabled) return;
        _networkEnabled = enabled;
        if (!enabled) {
          ctsToCancel = _sessionCts;
          _sessionCts = null;
          _runnerTask = null;
        } else {
          EnsureRunnerLocked();
        }
      }
      if (ctsToCancel != null) {
        try {
          ctsToCancel.Cancel();
        } catch (Exception) {
          // Ignore teardown race.
        }
      }
    }

    private void SignalOutboundLocked() {
      _outboundSignal.TrySetResult(true);
    }

    private void EnsureRunnerLocked() {
      if (_disposed || !_networkEnabled || _targets.Count == 0) return;
      if (_sessionCts == null || _runnerTask == null || _runnerTask.IsCompleted) {
        var cts = new CancellationTokenSource();
        _sessionCts = cts;
        _runnerTask = Task.Run(() => RunSessionLoopAsync(cts));
      }
    }

    private async Task RunSessionLoopAsync(CancellationTokenSource mySessionCts) {
      CancellationToken loopCt = mySessionCts.Token;
      var backoff = new RetryBackoff(
          maxRetryTime: TimeSpan.FromDays(1),
          initialBackoffMs: 250,
          maxBackoffMs: 10000,
          maxJitterMs: 100);

      try {
        while (!loopCt.IsCancellationRequested) {
          WatchTargetSpec[] initialTargets;
          lock (_gate) {
            if (_disposed || !_networkEnabled || _targets.Count == 0) {
              return;
            }
            _outboundQueue.Clear();
            _outboundSignal = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            initialTargets = _targets.Values.ToArray();
          }

          bool becameHealthy = false;
          try {
            becameHealthy = await RunSingleWebChannelSessionAsync(initialTargets, loopCt)
                .ConfigureAwait(false);
            if (becameHealthy) {
              backoff.Reset();
            }
          } catch (OperationCanceledException) when (loopCt.IsCancellationRequested) {
            break;
          } catch (Exception) when (loopCt.IsCancellationRequested) {
            break;
          } catch (Exception) {
            // Transient stream disconnect or 429/503/network drop: reconnect active targets cleanly.
          }

          lock (_gate) {
            if (_disposed || !_networkEnabled || _targets.Count == 0) {
              return;
            }
          }

          if (becameHealthy) {
            continue;
          }

          try {
            await backoff.DelayOrThrowAsync(transportFailure: false, lastError: null, token: loopCt)
                .ConfigureAwait(false);
          } catch (OperationCanceledException) {
            break;
          }
        }
      } finally {
        lock (_gate) {
          if (ReferenceEquals(_sessionCts, mySessionCts)) {
            _sessionCts = null;
            _runnerTask = null;
            if (!_disposed && _networkEnabled && _targets.Count > 0) {
              EnsureRunnerLocked();
            }
          }
        }
        try {
          mySessionCts.Dispose();
        } catch (Exception) {
          // Ignore teardown race.
        }
      }
    }

    private async Task<bool> RunSingleWebChannelSessionAsync(
        WatchTargetSpec[] initialTargets, CancellationToken ct) {
      string dbParam = Uri.EscapeDataString(_rpc.DatabaseResourceName);
      string httpHeadersParam = Uri.EscapeDataString(
          await _rpc.BuildWebChannelHttpHeadersParamAsync().ConfigureAwait(false));
      int openRid = Interlocked.Increment(ref _ridCounter);
      string openUrl =
          $"{_rpc.WebChannelBaseUrl}?VER=8&database={dbParam}&RID={openRid}&CVER=22" +
          $"&X-HTTP-Session-Id=gsessionid&%24httpHeaders={httpHeadersParam}&zx={NextZx()}&t=1";

      var formSb = new StringBuilder(512);
      formSb.Append("count=").Append(initialTargets.Length).Append("&ofs=0");
      for (int i = 0; i < initialTargets.Length; i++) {
        string json = BuildAddTargetJson(initialTargets[i]);
        formSb.Append("&req").Append(i).Append("___data__=").Append(Uri.EscapeDataString(json));
      }
      int nextOfs = initialTargets.Length;
      long lastAid = 0;
      string sid = null;
      string gsessionId = null;
      bool receivedCurrent = false;

      using (var openReq = new HttpRequestMessage(HttpMethod.Post, openUrl)) {
        openReq.Content = new StringContent(
            formSb.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        await _rpc.PopulateHeadersAsync(openReq).ConfigureAwait(false);
        using HttpResponseMessage openResp = await _rpc.GetUnaryClient()
            .SendAsync(openReq, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (!openResp.IsSuccessStatusCode) {
          string errBody = await HttpHelpers.ReadErrorBodySafeAsync(openResp).ConfigureAwait(false);
          FirestoreException fex = FirestoreJson.ParseErrorResponse(openResp.StatusCode, errBody);
          if (!HttpHelpers.IsRetryableStatusCode((int)openResp.StatusCode) &&
              (openResp.StatusCode == HttpStatusCode.Forbidden ||
               openResp.StatusCode == HttpStatusCode.Unauthorized ||
               openResp.StatusCode == HttpStatusCode.BadRequest)) {
            FailAllActiveTargets(fex);
            return true;
          }
          throw fex;
        }
        if (openResp.Headers.TryGetValues("x-http-session-id", out IEnumerable<string> gsVals)) {
          gsessionId = gsVals.FirstOrDefault();
        }
        using Stream openStream = await openResp.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using (ct.Register(() => {
          try {
            openStream.Dispose();
          } catch (Exception) {
            // Ignore teardown race.
          }
          try {
            openResp.Dispose();
          } catch (Exception) {
            // Ignore teardown race.
          }
        })) {
          try {
            await ReadWebChannelFramesAsync(
                openStream,
                frameArray => {
                  ProcessWebChannelEnvelopeArray(
                      frameArray, ref sid, ref lastAid, ref receivedCurrent, out _);
                },
                ct).ConfigureAwait(false);
          } catch (Exception) when (ct.IsCancellationRequested) {
            throw new OperationCanceledException(ct);
          }
        }
      }

      if (string.IsNullOrEmpty(sid)) {
        throw new IOException("WebChannel session handshake did not return a SID.");
      }

      using var sessionLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      CancellationToken sessionCt = sessionLinkedCts.Token;

      // Launch outbound sender pump for dynamic addTarget/removeTarget mutations on the live session.
      Task senderPump = Task.Run(async () => {
        while (!sessionCt.IsCancellationRequested) {
          List<string> batch = null;
          Task waitTask;
          lock (_gate) {
            if (_outboundQueue.Count > 0) {
              batch = new List<string>(_outboundQueue.Count);
              while (_outboundQueue.Count > 0) batch.Add(_outboundQueue.Dequeue());
              _outboundSignal = new TaskCompletionSource<bool>(
                  TaskCreationOptions.RunContinuationsAsynchronously);
              waitTask = null;
            } else {
              waitTask = _outboundSignal.Task;
            }
          }

          if (waitTask != null) {
            Task completed = await Task.WhenAny(waitTask, Task.Delay(Timeout.Infinite, sessionCt))
                .ConfigureAwait(false);
            if (completed != waitTask) break;
            continue;
          }

          int sendRid = Interlocked.Increment(ref _ridCounter);
          string gsPart = string.IsNullOrEmpty(gsessionId)
              ? string.Empty
              : $"&gsessionid={Uri.EscapeDataString(gsessionId)}";
          string sendUrl =
              $"{_rpc.WebChannelBaseUrl}?VER=8&database={dbParam}{gsPart}" +
              $"&SID={Uri.EscapeDataString(sid)}&RID={sendRid}&AID={Interlocked.Read(ref lastAid)}" +
              $"&zx={NextZx()}&t=1";

          var sendSb = new StringBuilder(256);
          sendSb.Append("count=").Append(batch.Count).Append("&ofs=").Append(nextOfs);
          for (int i = 0; i < batch.Count; i++) {
            sendSb.Append("&req").Append(i).Append("___data__=")
                .Append(Uri.EscapeDataString(batch[i]));
          }
          nextOfs += batch.Count;

          try {
            using var postReq = new HttpRequestMessage(HttpMethod.Post, sendUrl) {
              Content = new StringContent(
                  sendSb.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded"),
            };
            await _rpc.PopulateHeadersAsync(postReq).ConfigureAwait(false);
            using HttpResponseMessage postResp = await _rpc.GetUnaryClient()
                .SendAsync(postReq, sessionCt)
                .ConfigureAwait(false);
            if (!postResp.IsSuccessStatusCode) {
              sessionLinkedCts.Cancel();
              break;
            }
          } catch (Exception) {
            sessionLinkedCts.Cancel();
            break;
          }
        }
      });

      try {
        while (!sessionCt.IsCancellationRequested) {
          lock (_gate) {
            if (_disposed || !_networkEnabled || _targets.Count == 0) break;
          }
          string gsPart = string.IsNullOrEmpty(gsessionId)
              ? string.Empty
              : $"&gsessionid={Uri.EscapeDataString(gsessionId)}";
          string getUrl =
              $"{_rpc.WebChannelBaseUrl}?VER=8&database={dbParam}{gsPart}" +
              $"&RID=rpc&SID={Uri.EscapeDataString(sid)}&CI=0" +
              $"&AID={Interlocked.Read(ref lastAid)}&TYPE=xmlhttp&zx={NextZx()}&t=1";

          using var getReq = new HttpRequestMessage(HttpMethod.Get, getUrl);
          await _rpc.PopulateHeadersAsync(getReq).ConfigureAwait(false);
          using HttpResponseMessage getResp = await _rpc.GetStreamClient()
              .SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead, sessionCt)
              .ConfigureAwait(false);
          if (!getResp.IsSuccessStatusCode) {
            throw new IOException($"WebChannel GET failed with {(int)getResp.StatusCode}.");
          }

          bool streamClosedByServer = false;
          using Stream getStream = await getResp.Content.ReadAsStreamAsync().ConfigureAwait(false);
          using (sessionCt.Register(() => {
            try {
              getStream.Dispose();
            } catch (Exception) {
              // Ignore teardown race.
            }
            try {
              getResp.Dispose();
            } catch (Exception) {
              // Ignore teardown race.
            }
          })) {
            try {
              await ReadWebChannelFramesAsync(
                  getStream,
                  frameArray => {
                    ProcessWebChannelEnvelopeArray(
                        frameArray, ref sid, ref lastAid, ref receivedCurrent, out bool closed);
                    if (closed) streamClosedByServer = true;
                  },
                  sessionCt).ConfigureAwait(false);
            } catch (Exception) when (sessionCt.IsCancellationRequested) {
              if (ct.IsCancellationRequested) {
                throw new OperationCanceledException(ct);
              }
              break;
            }
          }

          if (streamClosedByServer) {
            // Server requested fresh WebChannel session.
            break;
          }
        }
      } finally {
        sessionLinkedCts.Cancel();
        try {
          await senderPump.ConfigureAwait(false);
        } catch (Exception) {
          // Sender cancelled.
        }
      }

      return receivedCurrent;
    }

    private void FailAllActiveTargets(FirestoreException error) {
      List<int> allIds;
      lock (_gate) {
        allIds = _targets.Keys.ToList();
        _targets.Clear();
      }
      if (allIds.Count > 0) {
        _onTargetError?.Invoke(allIds, error);
      }
    }

    /// <summary>
    /// Reads length-prefixed UTF-16 character frames (<c>&lt;charLen&gt;\n&lt;jsonArray&gt;</c>)
    /// from a WebChannel response stream using a pooled <c>char[]</c> buffer.
    /// </summary>
    private static async Task ReadWebChannelFramesAsync(
        Stream stream, Action<List<object>> onEnvelopeArray, CancellationToken ct) {
      using var reader = new StreamReader(
          stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 16384, leaveOpen: true);
      char[] readBuf = ArrayPool<char>.Shared.Rent(16384);
      var pending = new StringBuilder(4096);
      try {
        int parseIdx = 0;
        int expectedFrameChars = -1;

        while (true) {
          ct.ThrowIfCancellationRequested();
          int n = await reader.ReadAsync(readBuf, 0, readBuf.Length).ConfigureAwait(false);
          if (n <= 0) break;
          pending.Append(readBuf, 0, n);

          while (true) {
            if (expectedFrameChars < 0) {
              // Skip leading whitespace before decimal length line.
              while (parseIdx < pending.Length &&
                     (pending[parseIdx] == '\n' || pending[parseIdx] == '\r' || pending[parseIdx] == ' ')) {
                parseIdx++;
              }
              int nl = -1;
              for (int i = parseIdx; i < pending.Length; i++) {
                if (pending[i] == '\n') {
                  nl = i;
                  break;
                }
              }
              if (nl < 0) break;
              int lenVal = 0;
              for (int i = parseIdx; i < nl; i++) {
                char c = pending[i];
                if (c >= '0' && c <= '9') {
                  lenVal = lenVal * 10 + (c - '0');
                }
              }
              parseIdx = nl + 1;
              expectedFrameChars = lenVal;
            }

            if (pending.Length - parseIdx < expectedFrameChars) {
              break;
            }

            string frameJson = pending.ToString(parseIdx, expectedFrameChars);
            parseIdx += expectedFrameChars;
            expectedFrameChars = -1;

            if (parseIdx == pending.Length) {
              pending.Clear();
              parseIdx = 0;
            } else if (parseIdx >= 16384) {
              pending.Remove(0, parseIdx);
              parseIdx = 0;
            }

            if (JsonHelper.Deserialize(frameJson) is List<object> outerArr) {
              onEnvelopeArray(outerArr);
            }
          }
        }
      } finally {
        ArrayPool<char>.Shared.Return(readBuf);
      }
    }

    private void ProcessWebChannelEnvelopeArray(
        List<object> outerArr,
        ref string sid,
        ref long lastAid,
        ref bool receivedCurrent,
        out bool closed) {
      closed = false;
      bool anyWatchMsg = false;
      List<(int TargetId, int Count)> deferredFilters = null;
      for (int i = 0; i < outerArr.Count; i++) {
        if (outerArr[i] is not List<object> item || item.Count < 2) continue;
        if (item[0] is long aidVal) {
          Interlocked.Exchange(ref lastAid, aidVal);
        } else if (item[0] != null &&
                   long.TryParse(item[0].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedAid)) {
          Interlocked.Exchange(ref lastAid, parsedAid);
        }

        if (item[1] is not List<object> payload || payload.Count == 0) continue;
        if (payload[0] is string ctrl) {
          if (ctrl == "c" && payload.Count >= 2 && payload[1] is string newSid) {
            sid = newSid;
          } else if (ctrl == "close") {
            closed = true;
          }
          continue;
        }

        for (int p = 0; p < payload.Count; p++) {
          if (payload[p] is Dictionary<string, object> msg) {
            anyWatchMsg = true;
            DispatchListenResponseMessage(msg, ref receivedCurrent, ref deferredFilters);
          }
        }
      }

      if (anyWatchMsg) {
        _onWatchBatchEnd?.Invoke();
      }
      if (deferredFilters != null) {
        for (int i = 0; i < deferredFilters.Count; i++) {
          _onExistenceFilter?.Invoke(deferredFilters[i].TargetId, deferredFilters[i].Count);
        }
      }
    }

    private void DispatchListenResponseMessage(
        Dictionary<string, object> msg,
        ref bool receivedCurrent,
        ref List<(int TargetId, int Count)> deferredFilters) {
      if (msg.TryGetValue("documentChange", out object dcObj) &&
          dcObj is Dictionary<string, object> dcMap) {
        MutableDocument doc = FirestoreJson.ParseMiniJsonDocument(JsonHelper.GetMap(dcMap, "document"));
        if (doc != null) {
          List<int> targetIds = ReadIntList(dcMap, "targetIds");
          List<int> removedIds = ReadIntList(dcMap, "removedTargetIds");
          _onDocChange?.Invoke(doc, targetIds, removedIds);
        }
        return;
      }

      if (msg.TryGetValue("documentDelete", out object ddObj) &&
          ddObj is Dictionary<string, object> ddMap) {
        string docName = JsonHelper.GetString(ddMap, "document");
        if (ResourcePath.TryParseResourceName(docName, out _, out _, out ResourcePath dp)) {
          _onDocRemoveOrDelete?.Invoke(dp, ReadIntList(ddMap, "removedTargetIds"));
        }
        return;
      }

      if (msg.TryGetValue("documentRemove", out object drObj) &&
          drObj is Dictionary<string, object> drMap) {
        string docName = JsonHelper.GetString(drMap, "document");
        if (ResourcePath.TryParseResourceName(docName, out _, out _, out ResourcePath dp)) {
          _onDocRemoveOrDelete?.Invoke(dp, ReadIntList(drMap, "removedTargetIds"));
        }
        return;
      }

      if (msg.TryGetValue("filter", out object fObj) &&
          fObj is Dictionary<string, object> fMap) {
        int tid = (int)JsonHelper.GetLong(fMap, "targetId", 0);
        int count = (int)JsonHelper.GetLong(fMap, "count", 0);
        (deferredFilters ??= new List<(int, int)>(1)).Add((tid, count));
        return;
      }

      if (msg.TryGetValue("targetChange", out object tcObj) &&
          tcObj is Dictionary<string, object> tcMap) {
        string changeType = JsonHelper.GetString(tcMap, "targetChangeType", "NO_CHANGE");
        List<int> targetIds = ReadIntList(tcMap, "targetIds");
        string resumeToken = JsonHelper.GetString(tcMap, "resumeToken");
        string readTime = JsonHelper.GetString(tcMap, "readTime");
        if (!string.IsNullOrEmpty(resumeToken)) {
          lock (_gate) {
            if (targetIds.Count == 0) {
              foreach (WatchTargetSpec s in _targets.Values) s.ResumeTokenBase64 = resumeToken;
            } else {
              for (int i = 0; i < targetIds.Count; i++) {
                if (_targets.TryGetValue(targetIds[i], out WatchTargetSpec s)) {
                  s.ResumeTokenBase64 = resumeToken;
                }
              }
            }
          }
        }

        switch (changeType) {
          case "CURRENT":
            receivedCurrent = true;
            _onTargetCurrent?.Invoke(targetIds, resumeToken);
            break;
          case "RESET":
            _onTargetReset?.Invoke(targetIds);
            break;
          case "REMOVE": {
            Dictionary<string, object> cause = JsonHelper.GetMap(tcMap, "cause");
            if (cause != null) {
              int code = (int)JsonHelper.GetLong(cause, "code", 2);
              string errMsg = JsonHelper.GetString(cause, "message", "Listen target rejected.");
              var fex = new FirestoreException(FirestoreJson.MapGrpcIntCode(code), errMsg);
              List<int> failedIds = targetIds;
              lock (_gate) {
                if (failedIds.Count == 0) {
                  failedIds = _targets.Keys.ToList();
                  _targets.Clear();
                } else {
                  for (int i = 0; i < failedIds.Count; i++) _targets.Remove(failedIds[i]);
                }
              }
              if (failedIds.Count > 0) {
                _onTargetError?.Invoke(failedIds, fex);
              }
            }
            break;
          }
          case "NO_CHANGE":
            if (targetIds.Count == 0 &&
                (!string.IsNullOrEmpty(readTime) || !string.IsNullOrEmpty(resumeToken))) {
              receivedCurrent = true;
            }
            break;
        }
      }
    }

    private static List<int> ReadIntList(IDictionary<string, object> map, string key) {
      List<object> raw = JsonHelper.GetList(map, key);
      if (raw == null || raw.Count == 0) return new List<int>(0);
      var res = new List<int>(raw.Count);
      for (int i = 0; i < raw.Count; i++) {
        if (raw[i] is long l) res.Add((int)l);
        else if (raw[i] != null &&
                 int.TryParse(raw[i].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) {
          res.Add(v);
        }
      }
      return res;
    }

    private string BuildAddTargetJson(WatchTargetSpec spec) {
      using var w = new PooledUtf8JsonWriter();
      w.WriteAscii("{\"database\":");
      w.WriteString(_rpc.DatabaseResourceName);
      w.WriteAscii(",\"addTarget\":{\"targetId\":");
      w.WriteInt64(spec.TargetId);
      if (spec.IsDocumentTarget) {
        w.WriteAscii(",\"documents\":{\"documents\":[");
        w.WriteString(spec.DocumentPath.ToFullResourceName(_rpc.ProjectId, _rpc.DatabaseId));
        w.WriteAscii("]}");
      } else {
        using var qw = new PooledUtf8JsonWriter();
        FirestoreJson.WriteQueryTarget(
            qw, spec.Query, _rpc.ProjectId, _rpc.DatabaseId, out string parentName);
        w.WriteAscii(",\"query\":{\"parent\":");
        w.WriteString(parentName);
        w.WriteAscii(",\"structuredQuery\":");
        w.WriteAscii(qw.ToUtf8String());
        w.WriteByte((byte)'}');
      }
      if (!string.IsNullOrEmpty(spec.ResumeTokenBase64)) {
        w.WriteAscii(",\"resumeToken\":");
        w.WriteString(spec.ResumeTokenBase64);
      }
      w.WriteAscii("}}");
      return w.ToUtf8String();
    }

    private static string NextZx() => Guid.NewGuid().ToString("N").Substring(0, 12);

    public void Dispose() {
      CancellationTokenSource cts;
      lock (_gate) {
        if (_disposed) return;
        _disposed = true;
        _targets.Clear();
        cts = _sessionCts;
        _sessionCts = null;
        _runnerTask = null;
      }
      try {
        cts?.Cancel();
      } catch (Exception) {
        // Ignore teardown race.
      }
    }
  }
}
