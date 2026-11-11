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
using System.Collections.Generic;
using System.Globalization;
using System.Net;

namespace Firebase.Database.Internal {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal readonly struct RtdbPath : IEquatable<RtdbPath>, IComparable<RtdbPath> {
    internal static readonly RtdbPath Empty = new RtdbPath(Array.Empty<string>(), 0, 0);

    private readonly string[] _segments;
    private readonly int _offset;
    private readonly int _length;

    private RtdbPath(string[] segments, int offset, int length) {
      _segments = segments ?? Array.Empty<string>();
      _offset = offset;
      _length = length;
    }

    internal int Count => _length;
    internal bool IsEmpty => _length == 0;
    internal string Front => _length == 0 ? null : _segments[_offset];
    internal string Back => _length == 0 ? null : _segments[_offset + _length - 1];
    internal string this[int index] => _segments[_offset + index];

    internal RtdbPath PopFront() =>
        _length <= 1 ? Empty : new RtdbPath(_segments, _offset + 1, _length - 1);

    internal RtdbPath Parent =>
        _length <= 1 ? Empty : new RtdbPath(_segments, _offset, _length - 1);

    internal static RtdbPath ParseAndValidate(string raw, string paramName, bool allowMetaKeys = false) {
      if (raw == null) {
        throw new ArgumentException("Firebase Database path must not be null.", paramName);
      }
      string[] parts = SplitSegments(raw);
      for (int i = 0; i < parts.Length; i++) {
        if (!IsValidPathSegment(parts[i], isFirstSegment: i == 0, allowMetaKeys: allowMetaKeys)) {
          throw new ArgumentException(
              $"Invalid Firebase Database path '{raw}'. Paths must not contain '.', '#', '$', '[', or ']'.",
              paramName);
        }
      }
      return parts.Length == 0 ? Empty : new RtdbPath(parts, 0, parts.Length);
    }

    internal static RtdbPath ParseUnchecked(string raw) {
      if (string.IsNullOrEmpty(raw)) return Empty;
      string[] parts = SplitSegments(raw);
      return parts.Length == 0 ? Empty : new RtdbPath(parts, 0, parts.Length);
    }

    internal RtdbPath Child(string relativePath, string paramName = null, bool validate = true) {
      if (validate) {
        RtdbPath parsed = ParseAndValidate(relativePath, paramName ?? nameof(relativePath), allowMetaKeys: IsEmpty);
        return Child(parsed);
      }
      return Child(ParseUnchecked(relativePath));
    }

    internal RtdbPath Child(RtdbPath child) {
      if (child.IsEmpty) return this;
      if (IsEmpty) return child;
      var combined = new string[_length + child._length];
      Array.Copy(_segments, _offset, combined, 0, _length);
      Array.Copy(child._segments, child._offset, combined, _length, child._length);
      return new RtdbPath(combined, 0, combined.Length);
    }

    internal RtdbPath ChildKey(string singleKey) {
      if (string.IsNullOrEmpty(singleKey)) return this;
      var combined = new string[_length + 1];
      if (_length > 0) {
        Array.Copy(_segments, _offset, combined, 0, _length);
      }
      combined[_length] = singleKey;
      return new RtdbPath(combined, 0, combined.Length);
    }

    internal bool StartsWith(RtdbPath prefix) {
      if (prefix._length > _length) return false;
      for (int i = 0; i < prefix._length; i++) {
        if (!string.Equals(_segments[_offset + i], prefix._segments[prefix._offset + i], StringComparison.Ordinal)) {
          return false;
        }
      }
      return true;
    }

    internal static bool TryGetRelative(RtdbPath from, RtdbPath to, out RtdbPath relative) {
      if (!to.StartsWith(from)) {
        relative = Empty;
        return false;
      }
      int rem = to._length - from._length;
      relative = rem == 0 ? Empty : new RtdbPath(to._segments, to._offset + from._length, rem);
      return true;
    }

    internal bool IsDotInfo => _length > 0 && string.Equals(Front, ".info", StringComparison.Ordinal);

    internal string ToWireString() => _length == 0 ? "/" : ToRelativeString();

    internal string ToRelativeString() {
      if (_length == 0) return string.Empty;
      if (_length == 1) return _segments[_offset];
      return string.Join("/", _segments, _offset, _length);
    }

    public override string ToString() => _length == 0 ? "/" : "/" + ToRelativeString();

    public bool Equals(RtdbPath other) {
      if (_length != other._length) return false;
      for (int i = 0; i < _length; i++) {
        if (!string.Equals(_segments[_offset + i], other._segments[other._offset + i], StringComparison.Ordinal)) {
          return false;
        }
      }
      return true;
    }

    public override bool Equals(object obj) => obj is RtdbPath other && Equals(other);

    public override int GetHashCode() {
      int hash = 17;
      for (int i = 0; i < _length; i++) {
        hash = unchecked(hash * 31 + StringComparer.Ordinal.GetHashCode(_segments[_offset + i]));
      }
      return hash;
    }

    public int CompareTo(RtdbPath other) {
      int min = Math.Min(_length, other._length);
      for (int i = 0; i < min; i++) {
        int cmp = ChildKeyComparer.Instance.Compare(_segments[_offset + i], other._segments[other._offset + i]);
        if (cmp != 0) return cmp;
      }
      return _length.CompareTo(other._length);
    }

    internal static bool IsValidKey(string key, bool allowMeta = false) {
      if (string.IsNullOrEmpty(key)) return false;
      if (allowMeta && (key == ".priority" || key == ".value" || key == ".sv" || key == ".info")) {
        return true;
      }
      for (int i = 0; i < key.Length; i++) {
        char c = key[i];
        if (c == '.' || c == '$' || c == '#' || c == '[' || c == ']' || c == '/' || c <= 0x1F || c == 0x7F) {
          return false;
        }
      }
      return true;
    }

    private static bool IsValidPathSegment(string seg, bool isFirstSegment, bool allowMetaKeys) {
      if (string.IsNullOrEmpty(seg)) return false;
      if (isFirstSegment && seg == ".info") return true;
      if (allowMetaKeys && (seg == ".priority" || seg == ".value")) return true;
      return IsValidKey(seg, allowMeta: false);
    }

    private static string[] SplitSegments(string raw) {
      if (string.IsNullOrEmpty(raw)) return Array.Empty<string>();
      List<string> list = null;
      int i = 0;
      int n = raw.Length;
      while (i < n) {
        while (i < n && raw[i] == '/') i++;
        if (i >= n) break;
        int start = i;
        while (i < n && raw[i] != '/') i++;
        list ??= new List<string>(4);
        list.Add(raw.Substring(start, i - start));
      }
      return list == null ? Array.Empty<string>() : list.ToArray();
    }
  }

  internal sealed class ParsedDatabaseUrl {
    internal string Host { get; }
    internal string Namespace { get; }
    internal bool Secure { get; }
    internal bool HasExplicitNamespace { get; }
    internal bool IsLocalEndpoint { get; }
    internal RtdbPath Path { get; }
    internal string BaseUrl { get; }
    internal string CacheKey { get; }

    private ParsedDatabaseUrl(
        string host,
        string ns,
        bool secure,
        bool hasExplicitNs,
        bool isLocalEndpoint,
        RtdbPath path) {
      Host = host;
      Namespace = ns;
      Secure = secure;
      HasExplicitNamespace = hasExplicitNs;
      IsLocalEndpoint = isLocalEndpoint;
      Path = path;
      BaseUrl = (secure ? "https://" : "http://") + host;
      CacheKey = host.ToLowerInvariant() + "|ns=" + ns + "|sec=" + (secure ? "1" : "0");
    }

    internal Uri BuildWebSocketUri(string redirectHost, string lastSessionId) {
      bool useSecure = Secure;
      string targetHost = !string.IsNullOrEmpty(redirectHost) ? redirectHost : Host;

      if (string.IsNullOrEmpty(redirectHost) && !IsLocalEndpoint) {
        string emuHost = Environment.GetEnvironmentVariable("FIREBASE_DATABASE_EMULATOR_HOST");
        if (!string.IsNullOrWhiteSpace(emuHost)) {
          string trimmed = emuHost.Trim();
          if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) {
            trimmed = trimmed.Substring(7);
          } else if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
            trimmed = trimmed.Substring(8);
          }
          trimmed = trimmed.TrimEnd('/');
          if (!string.IsNullOrEmpty(trimmed)) {
            targetHost = trimmed;
            useSecure = false;
          }
        }
      }

      string scheme = useSecure ? "wss" : "ws";
      string url = $"{scheme}://{targetHost}/.ws?ns={Uri.EscapeDataString(Namespace)}&v=5";
      if (!string.IsNullOrEmpty(lastSessionId)) {
        url += $"&ls={Uri.EscapeDataString(lastSessionId)}";
      }
      return new Uri(url);
    }

    internal static ParsedDatabaseUrl Parse(string rawUrl, FirebaseApp app = null) {
      if (string.IsNullOrWhiteSpace(rawUrl)) {
        throw new DatabaseException(
            "Failed to get FirebaseDatabase instance: Specify DatabaseURL within "
            + "FirebaseApp or from your GetInstance() call.");
      }

      string trimmed = rawUrl.Trim();
      if (!trimmed.Contains("://", StringComparison.Ordinal)) {
        bool looksLocal = trimmed.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("127.0.0.1", StringComparison.Ordinal)
            || trimmed.StartsWith("[::1]", StringComparison.Ordinal);
        trimmed = (looksLocal ? "http://" : "https://") + trimmed;
      }

      if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri)) {
        throw new DatabaseException($"Invalid Firebase Database URL: {rawUrl}");
      }

      string scheme = uri.Scheme.ToLowerInvariant();
      bool secure;
      if (scheme == "https" || scheme == "wss") {
        secure = true;
      } else if (scheme == "http" || scheme == "ws") {
        secure = false;
      } else {
        throw new DatabaseException($"Invalid Firebase Database URL scheme '{uri.Scheme}' in '{rawUrl}'.");
      }

      if (string.IsNullOrEmpty(uri.Host)) {
        throw new DatabaseException($"Invalid Firebase Database URL (empty host): {rawUrl}");
      }

      bool isLoopback = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
          || IPAddress.TryParse(uri.Host, out _);
      if (isLoopback && (scheme == "http" || scheme == "ws")) {
        secure = false;
      }

      bool isDefaultPort = uri.IsDefaultPort
          || (secure && uri.Port == 443)
          || (!secure && uri.Port == 80);
      string hostWithPort = isDefaultPort
          ? FormatUriHost(uri)
          : $"{FormatUriHost(uri)}:{uri.Port.ToString(CultureInfo.InvariantCulture)}";

      string explicitNs = ExtractQueryParam(uri.Query, "ns");
      bool hasExplicitNs = !string.IsNullOrEmpty(explicitNs);
      string ns;
      if (hasExplicitNs) {
        ns = explicitNs;
      } else if (isLoopback || uri.Host.IndexOf('.') < 0) {
        string projId = app?.Options?.ProjectId;
        ns = !string.IsNullOrEmpty(projId) ? projId : uri.Host;
      } else {
        int dot = uri.Host.IndexOf('.');
        ns = uri.Host.Substring(0, dot);
      }

      if (string.IsNullOrEmpty(ns)) {
        throw new DatabaseException($"Invalid Firebase Database URL (missing namespace): {rawUrl}");
      }

      string rawPath = Uri.UnescapeDataString(uri.AbsolutePath ?? string.Empty);
      RtdbPath parsedPath;
      try {
        parsedPath = RtdbPath.ParseAndValidate(rawPath, nameof(rawUrl));
      } catch (ArgumentException ex) {
        throw new DatabaseException(ex.Message, ex);
      }

      return new ParsedDatabaseUrl(
          hostWithPort,
          ns,
          secure,
          hasExplicitNs,
          isLocalEndpoint: !secure || isLoopback,
          parsedPath);
    }

    private static string FormatUriHost(Uri uri) =>
        uri.HostNameType == UriHostNameType.IPv6 && !uri.Host.StartsWith("[", StringComparison.Ordinal)
            ? "[" + uri.Host + "]"
            : uri.Host;

    private static string ExtractQueryParam(string query, string key) {
      if (string.IsNullOrEmpty(query)) return null;
      string q = query[0] == '?' ? query.Substring(1) : query;
      if (q.Length == 0) return null;
      string[] pairs = q.Split('&');
      for (int i = 0; i < pairs.Length; i++) {
        string pair = pairs[i];
        int eq = pair.IndexOf('=');
        if (eq <= 0) continue;
        string k = Uri.UnescapeDataString(pair.Substring(0, eq));
        if (string.Equals(k, key, StringComparison.Ordinal)) {
          return Uri.UnescapeDataString(pair.Substring(eq + 1));
        }
      }
      return null;
    }
  }

#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal static class PushIdGenerator {
    private const string PushChars = "-0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz";
    private static readonly object SyncLock = new object();
    private static readonly Random Rng = new Random();
    private static readonly int[] LastRandChars = new int[12];
    private static long _lastPushTimeMs = 0;

    internal static string Generate(long nowMs) {
      Span<char> chars = stackalloc char[20];
      lock (SyncLock) {
        bool duplicateTime = nowMs == _lastPushTimeMs;
        _lastPushTimeMs = nowMs;

        long ts = nowMs;
        for (int i = 7; i >= 0; i--) {
          int mod = (int)(ts % 64);
          if (mod < 0) mod += 64;
          chars[i] = PushChars[mod];
          ts /= 64;
        }

        if (!duplicateTime) {
          for (int i = 0; i < 12; i++) {
            LastRandChars[i] = Rng.Next(64);
          }
        } else {
          for (int i = 11; i >= 0; i--) {
            if (LastRandChars[i] != 63) {
              LastRandChars[i]++;
              break;
            }
            LastRandChars[i] = 0;
          }
        }

        for (int i = 0; i < 12; i++) {
          chars[8 + i] = PushChars[LastRandChars[i]];
        }
      }
      return new string(chars);
    }
  }
}
