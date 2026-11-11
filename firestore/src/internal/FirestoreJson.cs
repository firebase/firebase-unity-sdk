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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Firebase.Internal;

namespace Firebase.Firestore.Internal {
  /// <summary>
  /// Pooled segmented UTF-8 JSON buffer (&lt;= 64 KB chunks) that streams directly into
  /// <see cref="HttpContent"/> with zero Large Object Heap allocations.
  /// </summary>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class PooledUtf8JsonWriter : IDisposable {
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly List<byte[]> _chunks = new List<byte[]>(4);
    private readonly List<int> _lengths = new List<int>(4);
    private byte[] _current;
    private int _pos;
    private bool _disposed;

    internal long TotalBytes { get; private set; }

    internal PooledUtf8JsonWriter() {
      _current = ArrayPool<byte>.Shared.Rent(HttpHelpers.PooledBufferSize);
      _pos = 0;
    }

    private void FlushChunk() {
      if (_pos > 0) {
        _chunks.Add(_current);
        _lengths.Add(_pos);
        TotalBytes += _pos;
        _current = ArrayPool<byte>.Shared.Rent(HttpHelpers.PooledBufferSize);
        _pos = 0;
      }
    }

    private void EnsureSpace(int needed) {
      if (_current.Length - _pos < needed) {
        FlushChunk();
      }
    }

    internal void WriteByte(byte b) {
      if (_pos == _current.Length) FlushChunk();
      _current[_pos++] = b;
    }

    internal void WriteAscii(string ascii) {
      int len = ascii.Length;
      int idx = 0;
      while (idx < len) {
        if (_pos == _current.Length) FlushChunk();
        int take = Math.Min(len - idx, _current.Length - _pos);
        for (int i = 0; i < take; i++) {
          _current[_pos + i] = (byte)ascii[idx + i];
        }
        _pos += take;
        idx += take;
      }
    }

    internal void WriteString(string value) {
      if (value == null) {
        WriteAscii("null");
        return;
      }
      WriteByte((byte)'"');
      int runStart = 0;
      for (int i = 0; i < value.Length; i++) {
        char c = value[i];
        if (c < 0x80 && c >= 0x20 && c != '"' && c != '\\') {
          continue;
        }
        if (i > runStart) {
          WriteAsciiSlice(value, runStart, i - runStart);
        }
        switch (c) {
          case '"':
            WriteAscii("\\\"");
            break;
          case '\\':
            WriteAscii("\\\\");
            break;
          case '\b':
            WriteAscii("\\b");
            break;
          case '\f':
            WriteAscii("\\f");
            break;
          case '\n':
            WriteAscii("\\n");
            break;
          case '\r':
            WriteAscii("\\r");
            break;
          case '\t':
            WriteAscii("\\t");
            break;
          default:
            if (c < 0x20) {
              EnsureSpace(6);
              _current[_pos++] = (byte)'\\';
              _current[_pos++] = (byte)'u';
              _current[_pos++] = (byte)'0';
              _current[_pos++] = (byte)'0';
              _current[_pos++] = HexDigit((c >> 4) & 0xF);
              _current[_pos++] = HexDigit(c & 0xF);
            } else {
              // Encode contiguous non-ASCII run via UTF8Encoding into pooled buffer.
              int nonAsciiStart = i;
              while (i + 1 < value.Length && value[i + 1] >= 0x80) {
                i++;
              }
              WriteUtf8Slice(value, nonAsciiStart, i - nonAsciiStart + 1);
            }
            break;
        }
        runStart = i + 1;
      }
      if (runStart < value.Length) {
        WriteAsciiSlice(value, runStart, value.Length - runStart);
      }
      WriteByte((byte)'"');
    }

    private void WriteAsciiSlice(string s, int offset, int count) {
      while (count > 0) {
        if (_pos == _current.Length) FlushChunk();
        int take = Math.Min(count, _current.Length - _pos);
        for (int i = 0; i < take; i++) {
          _current[_pos + i] = (byte)s[offset + i];
        }
        _pos += take;
        offset += take;
        count -= take;
      }
    }

    private void WriteUtf8Slice(string s, int offset, int charCount) {
      while (charCount > 0) {
        // Max 4 UTF-8 bytes per UTF-16 code unit; keep chunked well within 64 KB.
        int maxChars = Math.Min(charCount, 8192);
        // Never split a surrogate pair across chunks.
        if (maxChars < charCount && char.IsHighSurrogate(s[offset + maxChars - 1])) {
          maxChars--;
        }
        int needed = Utf8NoBom.GetMaxByteCount(maxChars);
        EnsureSpace(needed);
        int written = Utf8NoBom.GetBytes(s, offset, maxChars, _current, _pos);
        _pos += written;
        offset += maxChars;
        charCount -= maxChars;
      }
    }

    private static byte HexDigit(int v) => (byte)(v < 10 ? ('0' + v) : ('a' + (v - 10)));

    internal void WriteInt64(long value) {
      WriteAscii(value.ToString(CultureInfo.InvariantCulture));
    }

    internal void WriteInt64String(long value) {
      WriteByte((byte)'"');
      WriteAscii(value.ToString(CultureInfo.InvariantCulture));
      WriteByte((byte)'"');
    }

    internal void WriteDouble(double value) {
      if (double.IsNaN(value)) {
        WriteAscii("\"NaN\"");
      } else if (double.IsPositiveInfinity(value)) {
        WriteAscii("\"Infinity\"");
      } else if (double.IsNegativeInfinity(value)) {
        WriteAscii("\"-Infinity\"");
      } else {
        WriteAscii(value.ToString("G17", CultureInfo.InvariantCulture));
      }
    }

    internal void WriteBool(bool value) => WriteAscii(value ? "true" : "false");

    internal void WriteTimestampString(Timestamp ts) {
      EnsureSpace(36);
      DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(ts.Seconds);
      _current[_pos++] = (byte)'"';
      WriteDigits4(dt.Year);
      _current[_pos++] = (byte)'-';
      WriteDigits2(dt.Month);
      _current[_pos++] = (byte)'-';
      WriteDigits2(dt.Day);
      _current[_pos++] = (byte)'T';
      WriteDigits2(dt.Hour);
      _current[_pos++] = (byte)':';
      WriteDigits2(dt.Minute);
      _current[_pos++] = (byte)':';
      WriteDigits2(dt.Second);
      int nanos = ts.Nanoseconds;
      if (nanos != 0) {
        _current[_pos++] = (byte)'.';
        WriteDigits9(nanos);
      }
      _current[_pos++] = (byte)'Z';
      _current[_pos++] = (byte)'"';
    }

    private void WriteDigits2(int v) {
      _current[_pos++] = (byte)('0' + (v / 10));
      _current[_pos++] = (byte)('0' + (v % 10));
    }

    private void WriteDigits4(int v) {
      _current[_pos++] = (byte)('0' + (v / 1000));
      _current[_pos++] = (byte)('0' + ((v / 100) % 10));
      _current[_pos++] = (byte)('0' + ((v / 10) % 10));
      _current[_pos++] = (byte)('0' + (v % 10));
    }

    private void WriteDigits9(int nanos) {
      int rem = nanos;
      int divisor = 100000000;
      for (int i = 0; i < 9; i++) {
        int d = rem / divisor;
        rem %= divisor;
        divisor /= 10;
        _current[_pos++] = (byte)('0' + d);
      }
    }

    internal void Seal() {
      if (_pos > 0) {
        _chunks.Add(_current);
        _lengths.Add(_pos);
        TotalBytes += _pos;
        _current = null;
        _pos = 0;
      } else if (_current != null) {
        ArrayPool<byte>.Shared.Return(_current);
        _current = null;
      }
    }

    internal async Task CopyToStreamAsync(Stream stream) {
      for (int i = 0; i < _chunks.Count; i++) {
        await stream.WriteAsync(_chunks[i], 0, _lengths[i]).ConfigureAwait(false);
      }
    }

    internal string ToUtf8String() {
      Seal();
      if (TotalBytes == 0) return string.Empty;
      if (_chunks.Count == 1) {
        return Utf8NoBom.GetString(_chunks[0], 0, _lengths[0]);
      }
      var sb = new StringBuilder((int)Math.Min(TotalBytes, 65536));
      Decoder decoder = Utf8NoBom.GetDecoder();
      char[] charBuf = ArrayPool<char>.Shared.Rent(16384);
      try {
        for (int i = 0; i < _chunks.Count; i++) {
          byte[] chunk = _chunks[i];
          int byteLen = _lengths[i];
          int byteOffset = 0;
          bool last = i == _chunks.Count - 1;
          while (byteOffset < byteLen) {
            decoder.Convert(
                chunk, byteOffset, byteLen - byteOffset,
                charBuf, 0, charBuf.Length,
                flush: last,
                out int bytesUsed, out int charsUsed, out _);
            sb.Append(charBuf, 0, charsUsed);
            byteOffset += bytesUsed;
          }
        }
        return sb.ToString();
      } finally {
        ArrayPool<char>.Shared.Return(charBuf);
      }
    }

    public void Dispose() {
      if (_disposed) return;
      _disposed = true;
      if (_current != null) {
        ArrayPool<byte>.Shared.Return(_current);
        _current = null;
      }
      for (int i = 0; i < _chunks.Count; i++) {
        ArrayPool<byte>.Shared.Return(_chunks[i]);
      }
      _chunks.Clear();
      _lengths.Clear();
    }
  }

#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class PooledJsonHttpContent : HttpContent {
    private static readonly MediaTypeHeaderValue JsonMediaType =
        new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

    private readonly PooledUtf8JsonWriter _writer;

    internal PooledJsonHttpContent(PooledUtf8JsonWriter writer) {
      _writer = writer;
      _writer.Seal();
      Headers.ContentType = JsonMediaType;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) =>
        _writer.CopyToStreamAsync(stream);

    protected override bool TryComputeLength(out long length) {
      length = _writer.TotalBytes;
      return true;
    }

    protected override void Dispose(bool disposing) {
      if (disposing) {
        _writer.Dispose();
      }
      base.Dispose(disposing);
    }
  }

  /// <summary>
  /// Streaming UTF-8 JSON pull parser backed by a single 64 KB <see cref="ArrayPool{T}"/> buffer.
  /// Parses large query responses directly from network streams without LOH string allocations.
  /// </summary>
  internal enum JsonToken : byte {
    Eof = 0,
    StartObject = 1,
    EndObject = 2,
    StartArray = 3,
    EndArray = 4,
    String = 5,
    Number = 6,
    True = 7,
    False = 8,
    Null = 9,
  }

#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class Utf8JsonStreamReader : IDisposable {
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly Stream _stream;
    private byte[] _buf;
    private int _pos;
    private int _len;
    private bool _eof;
    private byte[] _scratch;
    private int _scratchLen;

    internal JsonToken Token { get; private set; }
    internal string StringText { get; private set; }

    internal Utf8JsonStreamReader(Stream stream) {
      _stream = stream;
      _buf = ArrayPool<byte>.Shared.Rent(HttpHelpers.PooledBufferSize);
      _scratch = ArrayPool<byte>.Shared.Rent(4096);
    }

    internal Utf8JsonStreamReader(byte[] utf8Bytes, int offset, int count) {
      _stream = new MemoryStream(utf8Bytes, offset, count, writable: false);
      _buf = ArrayPool<byte>.Shared.Rent(Math.Min(HttpHelpers.PooledBufferSize, Math.Max(256, count)));
      _scratch = ArrayPool<byte>.Shared.Rent(1024);
    }

    public void Dispose() {
      if (_buf != null) {
        ArrayPool<byte>.Shared.Return(_buf);
        _buf = null;
      }
      if (_scratch != null) {
        ArrayPool<byte>.Shared.Return(_scratch);
        _scratch = null;
      }
    }

    private async ValueTask<int> PeekByteAsync() {
      if (_pos < _len) return _buf[_pos];
      if (_eof) return -1;
      _pos = 0;
      _len = await _stream.ReadAsync(_buf, 0, _buf.Length).ConfigureAwait(false);
      if (_len == 0) {
        _eof = true;
        return -1;
      }
      return _buf[0];
    }

    private async ValueTask<int> ReadByteAsync() {
      int b = await PeekByteAsync().ConfigureAwait(false);
      if (b >= 0) _pos++;
      return b;
    }

    private void AppendScratch(byte b) {
      if (_scratchLen == _scratch.Length) {
        int nextSize = _scratch.Length < 32768 ? _scratch.Length * 2 : _scratch.Length + 16384;
        byte[] grown = nextSize <= HttpHelpers.PooledBufferSize
            ? ArrayPool<byte>.Shared.Rent(nextSize)
            : new byte[nextSize];
        Buffer.BlockCopy(_scratch, 0, grown, 0, _scratchLen);
        if (_scratch.Length <= HttpHelpers.PooledBufferSize) {
          ArrayPool<byte>.Shared.Return(_scratch);
        }
        _scratch = grown;
      }
      _scratch[_scratchLen++] = b;
    }

    internal async ValueTask<bool> ReadAsync() {
      while (true) {
        int b = await PeekByteAsync().ConfigureAwait(false);
        if (b < 0) {
          Token = JsonToken.Eof;
          return false;
        }
        // Skip JSON whitespace and ':' / ',' separators for compact pull consumption.
        if (b == ' ' || b == '\t' || b == '\r' || b == '\n' || b == ',' || b == ':') {
          _pos++;
          continue;
        }
        switch (b) {
          case '{':
            _pos++;
            Token = JsonToken.StartObject;
            return true;
          case '}':
            _pos++;
            Token = JsonToken.EndObject;
            return true;
          case '[':
            _pos++;
            Token = JsonToken.StartArray;
            return true;
          case ']':
            _pos++;
            Token = JsonToken.EndArray;
            return true;
          case '"':
            _pos++;
            StringText = await ReadJsonStringAsync().ConfigureAwait(false);
            Token = JsonToken.String;
            return true;
          case 't':
            await SkipExactAsync(4).ConfigureAwait(false);
            Token = JsonToken.True;
            return true;
          case 'f':
            await SkipExactAsync(5).ConfigureAwait(false);
            Token = JsonToken.False;
            return true;
          case 'n':
            await SkipExactAsync(4).ConfigureAwait(false);
            Token = JsonToken.Null;
            return true;
          default:
            StringText = await ReadNumberLexemeAsync().ConfigureAwait(false);
            Token = JsonToken.Number;
            return true;
        }
      }
    }

    private async ValueTask SkipExactAsync(int count) {
      for (int i = 0; i < count; i++) {
        await ReadByteAsync().ConfigureAwait(false);
      }
    }

    private async ValueTask<string> ReadNumberLexemeAsync() {
      _scratchLen = 0;
      while (true) {
        int b = await PeekByteAsync().ConfigureAwait(false);
        if (b < 0 || b == ',' || b == '}' || b == ']' || b == ' ' || b == '\t' || b == '\r' || b == '\n') {
          break;
        }
        _pos++;
        AppendScratch((byte)b);
      }
      return Encoding.ASCII.GetString(_scratch, 0, _scratchLen);
    }

    private async ValueTask<string> ReadJsonStringAsync() {
      // Fast path: unescaped string entirely within the current buffer slice!
      int scan = _pos;
      while (scan < _len) {
        byte c = _buf[scan];
        if (c == '"') {
          string s = Utf8NoBom.GetString(_buf, _pos, scan - _pos);
          _pos = scan + 1;
          return s;
        }
        if (c == '\\') break;
        scan++;
      }

      // Slow path: crosses buffer boundary or contains escape sequences.
      _scratchLen = 0;
      StringBuilder sb = null;
      while (true) {
        int b = await ReadByteAsync().ConfigureAwait(false);
        if (b < 0) throw new FirestoreException(FirestoreError.Internal, "Unexpected EOF in JSON string.");
        if (b == '"') {
          if (sb != null) {
            if (_scratchLen > 0) {
              sb.Append(Utf8NoBom.GetString(_scratch, 0, _scratchLen));
              _scratchLen = 0;
            }
            return sb.ToString();
          }
          return _scratchLen == 0 ? string.Empty : Utf8NoBom.GetString(_scratch, 0, _scratchLen);
        }
        if (b != '\\') {
          AppendScratch((byte)b);
          if (_scratchLen >= 32768) {
            sb ??= new StringBuilder();
            sb.Append(Utf8NoBom.GetString(_scratch, 0, _scratchLen));
            _scratchLen = 0;
          }
          continue;
        }

        // Flush accumulated UTF-8 bytes before appending escaped char when needed.
        int esc = await ReadByteAsync().ConfigureAwait(false);
        switch (esc) {
          case '"': AppendScratch((byte)'"'); break;
          case '\\': AppendScratch((byte)'\\'); break;
          case '/': AppendScratch((byte)'/'); break;
          case 'b': AppendScratch((byte)'\b'); break;
          case 'f': AppendScratch((byte)'\f'); break;
          case 'n': AppendScratch((byte)'\n'); break;
          case 'r': AppendScratch((byte)'\r'); break;
          case 't': AppendScratch((byte)'\t'); break;
          case 'u': {
            sb ??= new StringBuilder();
            if (_scratchLen > 0) {
              sb.Append(Utf8NoBom.GetString(_scratch, 0, _scratchLen));
              _scratchLen = 0;
            }
            int codeUnit = await ReadHex4Async().ConfigureAwait(false);
            sb.Append((char)codeUnit);
            break;
          }
          default:
            AppendScratch((byte)esc);
            break;
        }
      }
    }

    private async ValueTask<int> ReadHex4Async() {
      int val = 0;
      for (int i = 0; i < 4; i++) {
        int b = await ReadByteAsync().ConfigureAwait(false);
        int digit = b >= '0' && b <= '9' ? b - '0'
            : (b >= 'a' && b <= 'f' ? 10 + (b - 'a')
            : (b >= 'A' && b <= 'F' ? 10 + (b - 'A') : 0));
        val = (val << 4) | digit;
      }
      return val;
    }

    internal async ValueTask SkipValueAsync() {
      if (Token == JsonToken.StartObject) {
        int depth = 1;
        while (depth > 0 && await ReadAsync().ConfigureAwait(false)) {
          if (Token == JsonToken.StartObject) depth++;
          else if (Token == JsonToken.EndObject) depth--;
        }
      } else if (Token == JsonToken.StartArray) {
        int depth = 1;
        while (depth > 0 && await ReadAsync().ConfigureAwait(false)) {
          if (Token == JsonToken.StartArray) depth++;
          else if (Token == JsonToken.EndArray) depth--;
        }
      }
    }
  }

  /// <summary>
  /// Firestore v1 REST and WebChannel wire serializer / deserializer.
  /// </summary>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal static class FirestoreJson {
    internal static void WriteFirestoreValue(PooledUtf8JsonWriter w, FirestoreValue val) {
      w.WriteByte((byte)'{');
      switch (val.Kind) {
        case FirestoreValueKind.Null:
          w.WriteAscii("\"nullValue\":null");
          break;
        case FirestoreValueKind.Boolean:
          w.WriteAscii("\"booleanValue\":");
          w.WriteBool(val.BooleanValue);
          break;
        case FirestoreValueKind.Integer:
          w.WriteAscii("\"integerValue\":");
          w.WriteInt64String(val.IntegerValue);
          break;
        case FirestoreValueKind.Double:
          w.WriteAscii("\"doubleValue\":");
          w.WriteDouble(val.DoubleValue);
          break;
        case FirestoreValueKind.Timestamp:
          w.WriteAscii("\"timestampValue\":");
          w.WriteTimestampString(val.TimestampValue);
          break;
        case FirestoreValueKind.ServerTimestamp:
          w.WriteAscii("\"timestampValue\":");
          w.WriteTimestampString(val.ServerTimestampValue.LocalWriteTime);
          break;
        case FirestoreValueKind.String:
          w.WriteAscii("\"stringValue\":");
          w.WriteString(val.StringValue ?? string.Empty);
          break;
        case FirestoreValueKind.Blob:
          w.WriteAscii("\"bytesValue\":");
          w.WriteString(Convert.ToBase64String(val.RawBytes ?? Array.Empty<byte>()));
          break;
        case FirestoreValueKind.Reference:
          w.WriteAscii("\"referenceValue\":");
          w.WriteString(val.ReferenceValue.FullResourceName);
          break;
        case FirestoreValueKind.GeoPoint: {
          GeoPoint gp = val.GeoPointValue;
          w.WriteAscii("\"geoPointValue\":{\"latitude\":");
          w.WriteDouble(gp.Latitude);
          w.WriteAscii(",\"longitude\":");
          w.WriteDouble(gp.Longitude);
          w.WriteByte((byte)'}');
          break;
        }
        case FirestoreValueKind.Array: {
          IReadOnlyList<FirestoreValue> arr = val.ArrayValue;
          w.WriteAscii("\"arrayValue\":{\"values\":[");
          for (int i = 0; i < arr.Count; i++) {
            if (i > 0) w.WriteByte((byte)',');
            WriteFirestoreValue(w, arr[i]);
          }
          w.WriteAscii("]}");
          break;
        }
        case FirestoreValueKind.Map: {
          w.WriteAscii("\"mapValue\":{\"fields\":");
          WriteFieldsMap(w, val.MapValue);
          w.WriteByte((byte)'}');
          break;
        }
        default:
          w.WriteAscii("\"nullValue\":null");
          break;
      }
      w.WriteByte((byte)'}');
    }

    internal static void WriteFieldsMap(
        PooledUtf8JsonWriter w, IDictionary<string, FirestoreValue> fields) {
      w.WriteByte((byte)'{');
      if (fields != null) {
        bool first = true;
        foreach (KeyValuePair<string, FirestoreValue> kv in fields) {
          if (!first) w.WriteByte((byte)',');
          first = false;
          w.WriteString(kv.Key);
          w.WriteByte((byte)':');
          WriteFirestoreValue(w, kv.Value);
        }
      }
      w.WriteByte((byte)'}');
    }

    internal static void WriteMutation(
        PooledUtf8JsonWriter w, MutationSpec m, string projectId, string databaseId) {
      string docResourceName = m.Path.ToFullResourceName(projectId, databaseId);
      w.WriteByte((byte)'{');
      if (m.Kind == MutationKind.Delete) {
        w.WriteAscii("\"delete\":");
        w.WriteString(docResourceName);
        w.WriteByte((byte)'}');
        return;
      }

      w.WriteAscii("\"update\":{\"name\":");
      w.WriteString(docResourceName);
      if (m.Data != null && m.Data.Count > 0) {
        w.WriteAscii(",\"fields\":");
        WriteFieldsMap(w, m.Data);
      }
      w.WriteByte((byte)'}');

      if (m.Kind == MutationKind.SetMergeAll ||
          m.Kind == MutationKind.SetMergeFields ||
          m.Kind == MutationKind.Update) {
        w.WriteAscii(",\"updateMask\":{\"fieldPaths\":[");
        if (m.Mask != null) {
          for (int i = 0; i < m.Mask.Count; i++) {
            if (i > 0) w.WriteByte((byte)',');
            w.WriteString(m.Mask[i].EncodedPath);
          }
        }
        w.WriteAscii("]}");
      }

      if (m.Kind == MutationKind.Update) {
        w.WriteAscii(",\"currentDocument\":{\"exists\":true}");
      }

      if (m.Transforms != null && m.Transforms.Count > 0) {
        w.WriteAscii(",\"updateTransforms\":[");
        for (int i = 0; i < m.Transforms.Count; i++) {
          if (i > 0) w.WriteByte((byte)',');
          FieldTransformSpec tr = m.Transforms[i];
          w.WriteAscii("{\"fieldPath\":");
          w.WriteString(tr.Field.EncodedPath);
          switch (tr.Kind) {
            case FieldTransformKind.ServerTimestamp:
              w.WriteAscii(",\"setToServerValue\":\"REQUEST_TIME\"");
              break;
            case FieldTransformKind.IncrementInteger:
            case FieldTransformKind.IncrementDouble:
              w.WriteAscii(",\"increment\":");
              WriteFirestoreValue(w, tr.Operand);
              break;
            case FieldTransformKind.ArrayUnion: {
              w.WriteAscii(",\"appendMissingElements\":{\"values\":[");
              IReadOnlyList<FirestoreValue> els = tr.Operand.ArrayValue;
              for (int j = 0; j < els.Count; j++) {
                if (j > 0) w.WriteByte((byte)',');
                WriteFirestoreValue(w, els[j]);
              }
              w.WriteAscii("]}");
              break;
            }
            case FieldTransformKind.ArrayRemove: {
              w.WriteAscii(",\"removeAllFromArray\":{\"values\":[");
              IReadOnlyList<FirestoreValue> els = tr.Operand.ArrayValue;
              for (int j = 0; j < els.Count; j++) {
                if (j > 0) w.WriteByte((byte)',');
                WriteFirestoreValue(w, els[j]);
              }
              w.WriteAscii("]}");
              break;
            }
          }
          w.WriteByte((byte)'}');
        }
        w.WriteByte((byte)']');
      }

      w.WriteByte((byte)'}');
    }

    internal static void WriteQueryTarget(
        PooledUtf8JsonWriter w,
        QuerySpec spec,
        string projectId,
        string databaseId,
        out string parentResourceName) {
      ResourcePath parentPath = spec.IsCollectionGroup
          ? ResourcePath.Empty
          : (spec.Path.Parent ?? ResourcePath.Empty);
      parentResourceName = parentPath.ToFullResourceName(projectId, databaseId);

      spec.GetWireTargetParts(
          out OrderBySpec[] wireOrderBys, out BoundSpec wireStart, out BoundSpec wireEnd);

      w.WriteAscii("{\"from\":[{\"collectionId\":");
      w.WriteString(spec.CollectionId);
      if (spec.IsCollectionGroup) {
        w.WriteAscii(",\"allDescendants\":true");
      }
      w.WriteAscii("}]");

      if (spec.Filters.Length > 0) {
        w.WriteAscii(",\"where\":");
        if (spec.Filters.Length == 1) {
          WriteFilter(w, spec.Filters[0]);
        } else {
          w.WriteAscii("{\"compositeFilter\":{\"op\":\"AND\",\"filters\":[");
          for (int i = 0; i < spec.Filters.Length; i++) {
            if (i > 0) w.WriteByte((byte)',');
            WriteFilter(w, spec.Filters[i]);
          }
          w.WriteAscii("]}}");
        }
      }

      if (wireOrderBys.Length > 0) {
        w.WriteAscii(",\"orderBy\":[");
        for (int i = 0; i < wireOrderBys.Length; i++) {
          if (i > 0) w.WriteByte((byte)',');
          w.WriteAscii("{\"field\":{\"fieldPath\":");
          w.WriteString(wireOrderBys[i].Field.EncodedPath);
          w.WriteAscii("},\"direction\":");
          w.WriteString(wireOrderBys[i].Descending ? "DESCENDING" : "ASCENDING");
          w.WriteByte((byte)'}');
        }
        w.WriteByte((byte)']');
      }

      if (wireStart != null) {
        w.WriteAscii(",\"startAt\":{\"values\":[");
        for (int i = 0; i < wireStart.Position.Length; i++) {
          if (i > 0) w.WriteByte((byte)',');
          WriteFirestoreValue(w, wireStart.Position[i]);
        }
        w.WriteAscii("],\"before\":");
        w.WriteBool(wireStart.Inclusive);
        w.WriteByte((byte)'}');
      }

      if (wireEnd != null) {
        w.WriteAscii(",\"endAt\":{\"values\":[");
        for (int i = 0; i < wireEnd.Position.Length; i++) {
          if (i > 0) w.WriteByte((byte)',');
          WriteFirestoreValue(w, wireEnd.Position[i]);
        }
        w.WriteAscii("],\"before\":");
        w.WriteBool(!wireEnd.Inclusive);
        w.WriteByte((byte)'}');
      }

      if (spec.Limit.HasValue) {
        w.WriteAscii(",\"limit\":");
        w.WriteInt64(spec.Limit.Value);
      }

      w.WriteByte((byte)'}');
    }

    private static void WriteFilter(PooledUtf8JsonWriter w, FilterSpec f) {
      if (f.IsComposite) {
        w.WriteAscii("{\"compositeFilter\":{\"op\":");
        w.WriteString(f.CompositeOp == CompositeFilterOp.Or ? "OR" : "AND");
        w.WriteAscii(",\"filters\":[");
        for (int i = 0; i < f.Children.Length; i++) {
          if (i > 0) w.WriteByte((byte)',');
          WriteFilter(w, f.Children[i]);
        }
        w.WriteAscii("]}}");
        return;
      }

      // Null and NaN equality/inequality map to UnaryFilter on the Firestore v1 wire!
      if (f.Operand.IsNull && (f.Op == FieldFilterOp.Equal || f.Op == FieldFilterOp.NotEqual)) {
        w.WriteAscii("{\"unaryFilter\":{\"field\":{\"fieldPath\":");
        w.WriteString(f.Field.EncodedPath);
        w.WriteAscii("},\"op\":");
        w.WriteString(f.Op == FieldFilterOp.Equal ? "IS_NULL" : "IS_NOT_NULL");
        w.WriteAscii("}}");
        return;
      }
      if (f.Operand.Kind == FirestoreValueKind.Double &&
          double.IsNaN(f.Operand.DoubleValue) &&
          (f.Op == FieldFilterOp.Equal || f.Op == FieldFilterOp.NotEqual)) {
        w.WriteAscii("{\"unaryFilter\":{\"field\":{\"fieldPath\":");
        w.WriteString(f.Field.EncodedPath);
        w.WriteAscii("},\"op\":");
        w.WriteString(f.Op == FieldFilterOp.Equal ? "IS_NAN" : "IS_NOT_NAN");
        w.WriteAscii("}}");
        return;
      }

      string wireOp = f.Op switch {
        FieldFilterOp.LessThan => "LESS_THAN",
        FieldFilterOp.LessThanOrEqual => "LESS_THAN_OR_EQUAL",
        FieldFilterOp.GreaterThan => "GREATER_THAN",
        FieldFilterOp.GreaterThanOrEqual => "GREATER_THAN_OR_EQUAL",
        FieldFilterOp.Equal => "EQUAL",
        FieldFilterOp.NotEqual => "NOT_EQUAL",
        FieldFilterOp.ArrayContains => "ARRAY_CONTAINS",
        FieldFilterOp.In => "IN",
        FieldFilterOp.ArrayContainsAny => "ARRAY_CONTAINS_ANY",
        FieldFilterOp.NotIn => "NOT_IN",
        _ => "EQUAL",
      };
      w.WriteAscii("{\"fieldFilter\":{\"field\":{\"fieldPath\":");
      w.WriteString(f.Field.EncodedPath);
      w.WriteAscii("},\"op\":");
      w.WriteString(wireOp);
      w.WriteAscii(",\"value\":");
      WriteFirestoreValue(w, f.Operand);
      w.WriteAscii("}}");
    }

    internal static async ValueTask<FirestoreValue> ReadFirestoreValueAsync(Utf8JsonStreamReader r) {
      // Assumes r.Token == JsonToken.StartObject
      FirestoreValue result = FirestoreValue.Null;
      while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndObject) {
        if (r.Token != JsonToken.String) {
          await r.SkipValueAsync().ConfigureAwait(false);
          continue;
        }
        string key = r.StringText;
        if (!await r.ReadAsync().ConfigureAwait(false)) break;
        switch (key) {
          case "nullValue":
            result = FirestoreValue.Null;
            if (r.Token == JsonToken.StartObject || r.Token == JsonToken.StartArray) {
              await r.SkipValueAsync().ConfigureAwait(false);
            }
            break;
          case "booleanValue":
            result = FirestoreValue.FromBoolean(
                r.Token == JsonToken.True ||
                (r.Token == JsonToken.String && string.Equals(r.StringText, "true", StringComparison.OrdinalIgnoreCase)));
            break;
          case "integerValue":
            result = FirestoreValue.FromInteger(
                long.Parse(r.StringText, NumberStyles.Integer, CultureInfo.InvariantCulture));
            break;
          case "doubleValue":
            result = FirestoreValue.FromDouble(ParseWireDouble(r.StringText));
            break;
          case "timestampValue":
            result = FirestoreValue.FromTimestamp(await ReadTimestampValueAsync(r).ConfigureAwait(false));
            break;
          case "stringValue":
            result = FirestoreValue.FromString(r.Token == JsonToken.Null ? string.Empty : r.StringText);
            break;
          case "bytesValue":
            result = FirestoreValue.FromOwnedBytes(
                string.IsNullOrEmpty(r.StringText)
                    ? Array.Empty<byte>()
                    : Convert.FromBase64String(r.StringText));
            break;
          case "referenceValue": {
            if (ResourcePath.TryParseResourceName(
                r.StringText, out string proj, out string db, out ResourcePath docPath)) {
              result = FirestoreValue.FromReference(new DocumentReferenceValue(proj, db, docPath));
            }
            break;
          }
          case "geoPointValue":
            result = FirestoreValue.FromGeoPoint(await ReadGeoPointAsync(r).ConfigureAwait(false));
            break;
          case "arrayValue":
            result = FirestoreValue.FromArray(await ReadArrayValueAsync(r).ConfigureAwait(false));
            break;
          case "mapValue":
            result = FirestoreValue.FromMap(await ReadMapValueAsync(r).ConfigureAwait(false));
            break;
          default:
            await r.SkipValueAsync().ConfigureAwait(false);
            break;
        }
      }
      return result;
    }

    internal static async ValueTask<Timestamp> ReadTimestampValueAsync(Utf8JsonStreamReader r) {
      if (r.Token == JsonToken.String) {
        return ParseRfc3339Timestamp(r.StringText);
      }
      if (r.Token == JsonToken.StartObject) {
        long seconds = 0;
        int nanos = 0;
        while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndObject) {
          if (r.Token != JsonToken.String) {
            await r.SkipValueAsync().ConfigureAwait(false);
            continue;
          }
          string k = r.StringText;
          if (!await r.ReadAsync().ConfigureAwait(false)) break;
          if (k == "seconds" && (r.Token == JsonToken.String || r.Token == JsonToken.Number)) {
            seconds = long.Parse(r.StringText, NumberStyles.Integer, CultureInfo.InvariantCulture);
          } else if (k == "nanos" && (r.Token == JsonToken.String || r.Token == JsonToken.Number)) {
            nanos = int.Parse(r.StringText, NumberStyles.Integer, CultureInfo.InvariantCulture);
          } else {
            await r.SkipValueAsync().ConfigureAwait(false);
          }
        }
        return new Timestamp(seconds, nanos);
      }
      return default;
    }

    private static async ValueTask<GeoPoint> ReadGeoPointAsync(Utf8JsonStreamReader r) {
      double lat = 0.0, lng = 0.0;
      if (r.Token == JsonToken.StartObject) {
        while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndObject) {
          if (r.Token != JsonToken.String) {
            await r.SkipValueAsync().ConfigureAwait(false);
            continue;
          }
          string k = r.StringText;
          if (!await r.ReadAsync().ConfigureAwait(false)) break;
          if (k == "latitude") lat = ParseWireDouble(r.StringText);
          else if (k == "longitude") lng = ParseWireDouble(r.StringText);
          else await r.SkipValueAsync().ConfigureAwait(false);
        }
      }
      return new GeoPoint(lat, lng);
    }

    private static async ValueTask<FirestoreValue[]> ReadArrayValueAsync(Utf8JsonStreamReader r) {
      List<FirestoreValue> list = null;
      if (r.Token == JsonToken.StartObject) {
        while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndObject) {
          if (r.Token == JsonToken.String && r.StringText == "values") {
            if (await r.ReadAsync().ConfigureAwait(false) && r.Token == JsonToken.StartArray) {
              list = new List<FirestoreValue>();
              while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndArray) {
                if (r.Token == JsonToken.StartObject) {
                  list.Add(await ReadFirestoreValueAsync(r).ConfigureAwait(false));
                } else {
                  await r.SkipValueAsync().ConfigureAwait(false);
                }
              }
            } else {
              await r.SkipValueAsync().ConfigureAwait(false);
            }
          } else {
            if (await r.ReadAsync().ConfigureAwait(false)) {
              await r.SkipValueAsync().ConfigureAwait(false);
            }
          }
        }
      }
      return list == null || list.Count == 0 ? Array.Empty<FirestoreValue>() : list.ToArray();
    }

    internal static async ValueTask<Dictionary<string, FirestoreValue>> ReadMapValueAsync(
        Utf8JsonStreamReader r) {
      var map = new Dictionary<string, FirestoreValue>(StringComparer.Ordinal);
      if (r.Token == JsonToken.StartObject) {
        while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndObject) {
          if (r.Token == JsonToken.String && r.StringText == "fields") {
            if (await r.ReadAsync().ConfigureAwait(false) && r.Token == JsonToken.StartObject) {
              await ReadFieldsObjectIntoAsync(r, map).ConfigureAwait(false);
            } else {
              await r.SkipValueAsync().ConfigureAwait(false);
            }
          } else {
            if (await r.ReadAsync().ConfigureAwait(false)) {
              await r.SkipValueAsync().ConfigureAwait(false);
            }
          }
        }
      }
      return map;
    }

    internal static async ValueTask ReadFieldsObjectIntoAsync(
        Utf8JsonStreamReader r, Dictionary<string, FirestoreValue> target) {
      while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndObject) {
        if (r.Token != JsonToken.String) {
          await r.SkipValueAsync().ConfigureAwait(false);
          continue;
        }
        string fieldName = r.StringText;
        if (!await r.ReadAsync().ConfigureAwait(false)) break;
        if (r.Token == JsonToken.StartObject) {
          target[fieldName] = await ReadFirestoreValueAsync(r).ConfigureAwait(false);
        } else {
          await r.SkipValueAsync().ConfigureAwait(false);
        }
      }
    }

    internal static async ValueTask<MutableDocument> ReadDocumentAsync(Utf8JsonStreamReader r) {
      // Assumes r.Token == JsonToken.StartObject
      ResourcePath docPath = ResourcePath.Empty;
      var fields = new Dictionary<string, FirestoreValue>(StringComparer.Ordinal);
      Timestamp updateTime = default;
      while (await r.ReadAsync().ConfigureAwait(false) && r.Token != JsonToken.EndObject) {
        if (r.Token != JsonToken.String) {
          await r.SkipValueAsync().ConfigureAwait(false);
          continue;
        }
        string k = r.StringText;
        if (!await r.ReadAsync().ConfigureAwait(false)) break;
        switch (k) {
          case "name":
            ResourcePath.TryParseResourceName(r.StringText, out _, out _, out docPath);
            break;
          case "fields":
            if (r.Token == JsonToken.StartObject) {
              await ReadFieldsObjectIntoAsync(r, fields).ConfigureAwait(false);
            } else {
              await r.SkipValueAsync().ConfigureAwait(false);
            }
            break;
          case "updateTime":
            updateTime = await ReadTimestampValueAsync(r).ConfigureAwait(false);
            break;
          default:
            await r.SkipValueAsync().ConfigureAwait(false);
            break;
        }
      }
      return new MutableDocument(docPath, exists: true, fields: fields, version: updateTime);
    }

    internal static double ParseWireDouble(string text) {
      if (string.IsNullOrEmpty(text)) return 0.0;
      if (text == "NaN") return double.NaN;
      if (text == "Infinity") return double.PositiveInfinity;
      if (text == "-Infinity") return double.NegativeInfinity;
      return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    internal static Timestamp ParseRfc3339Timestamp(string s) {
      if (string.IsNullOrEmpty(s)) return default;
      // Fast ASCII path for canonical "YYYY-MM-DDTHH:MM:SS[.f...]Z"
      if (s.Length >= 20 && s[4] == '-' && s[7] == '-' && (s[10] == 'T' || s[10] == 't') &&
          s[13] == ':' && s[16] == ':' && s[s.Length - 1] == 'Z') {
        int year = ParseFixedDigits(s, 0, 4);
        int month = ParseFixedDigits(s, 5, 2);
        int day = ParseFixedDigits(s, 8, 2);
        int hour = ParseFixedDigits(s, 11, 2);
        int minute = ParseFixedDigits(s, 14, 2);
        int second = ParseFixedDigits(s, 17, 2);
        int nanos = 0;
        if (s.Length > 20 && s[19] == '.') {
          int fracLen = s.Length - 21;
          int mult = 100000000;
          for (int i = 0; i < fracLen && i < 9; i++) {
            nanos += (s[20 + i] - '0') * mult;
            mult /= 10;
          }
        }
        if (year >= 1 && month >= 1 && day >= 1) {
          var dt = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
          long unixSecs = (dt.Ticks / TimeSpan.TicksPerSecond) - 62135596800L;
          return new Timestamp(unixSecs, nanos);
        }
      }
      DateTimeOffset dto = DateTimeOffset.Parse(
          s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
      return Timestamp.FromDateTimeOffset(dto);
    }

    private static int ParseFixedDigits(string s, int offset, int len) {
      int val = 0;
      for (int i = 0; i < len; i++) {
        char c = s[offset + i];
        if (c < '0' || c > '9') return -1;
        val = val * 10 + (c - '0');
      }
      return val;
    }

    /// <summary>
    /// Converts Google REST / gRPC JSON error responses into canonical <see cref="FirestoreException"/>s.
    /// </summary>
    internal static FirestoreException ParseErrorResponse(HttpStatusCode statusCode, string body) {
      string grpcStatus = null;
      string message = null;
      if (!string.IsNullOrEmpty(body)) {
        object parsed = null;
        try {
          parsed = JsonHelper.Deserialize(body);
        } catch (Exception) {
          // Non-JSON error payload; fall back to HTTP status mapping below.
        }
        Dictionary<string, object> errObj = null;
        if (parsed is Dictionary<string, object> rootDict) {
          errObj = JsonHelper.GetMap(rootDict, "error") ?? rootDict;
        } else if (parsed is List<object> rootArr && rootArr.Count > 0 &&
                   rootArr[0] is Dictionary<string, object> firstElem) {
          errObj = JsonHelper.GetMap(firstElem, "error") ?? firstElem;
        }
        if (errObj != null) {
          grpcStatus = JsonHelper.GetString(errObj, "status");
          message = JsonHelper.GetString(errObj, "message");
        }
      }

      FirestoreError code = MapGrpcStatusOrHttp(grpcStatus, statusCode);
      if (string.IsNullOrEmpty(message)) {
        message = string.IsNullOrEmpty(body)
            ? $"Firestore RPC failed with HTTP {(int)statusCode} ({statusCode})."
            : body;
      }
      return new FirestoreException(code, message);
    }

    internal static FirestoreError MapGrpcStatusOrHttp(string grpcStatus, HttpStatusCode statusCode) {
      if (!string.IsNullOrEmpty(grpcStatus)) {
        switch (grpcStatus.ToUpperInvariant()) {
          case "OK": return FirestoreError.Ok;
          case "CANCELLED": return FirestoreError.Cancelled;
          case "UNKNOWN": return FirestoreError.Unknown;
          case "INVALID_ARGUMENT": return FirestoreError.InvalidArgument;
          case "DEADLINE_EXCEEDED": return FirestoreError.DeadlineExceeded;
          case "NOT_FOUND": return FirestoreError.NotFound;
          case "ALREADY_EXISTS": return FirestoreError.AlreadyExists;
          case "PERMISSION_DENIED": return FirestoreError.PermissionDenied;
          case "RESOURCE_EXHAUSTED": return FirestoreError.ResourceExhausted;
          case "FAILED_PRECONDITION": return FirestoreError.FailedPrecondition;
          case "ABORTED": return FirestoreError.Aborted;
          case "OUT_OF_RANGE": return FirestoreError.OutOfRange;
          case "UNIMPLEMENTED": return FirestoreError.Unimplemented;
          case "INTERNAL": return FirestoreError.Internal;
          case "UNAVAILABLE": return FirestoreError.Unavailable;
          case "DATA_LOSS": return FirestoreError.DataLoss;
          case "UNAUTHENTICATED": return FirestoreError.Unauthenticated;
        }
      }
      return (int)statusCode switch {
        200 => FirestoreError.Ok,
        400 => FirestoreError.InvalidArgument,
        401 => FirestoreError.Unauthenticated,
        403 => FirestoreError.PermissionDenied,
        404 => FirestoreError.NotFound,
        408 => FirestoreError.DeadlineExceeded,
        409 => FirestoreError.Aborted,
        412 => FirestoreError.FailedPrecondition,
        429 => FirestoreError.ResourceExhausted,
        499 => FirestoreError.Cancelled,
        501 => FirestoreError.Unimplemented,
        503 => FirestoreError.Unavailable,
        504 => FirestoreError.DeadlineExceeded,
        >= 500 and < 600 => FirestoreError.Internal,
        _ => FirestoreError.Unknown,
      };
    }

    internal static FirestoreError MapGrpcIntCode(int code) =>
        code >= 0 && code <= 16 ? (FirestoreError)code : FirestoreError.Unknown;

    internal static FirestoreValue ParseMiniJsonFirestoreValue(object obj) {
      if (obj is not IDictionary<string, object> map) return FirestoreValue.Null;
      if (map.ContainsKey("nullValue")) return FirestoreValue.Null;
      if (map.TryGetValue("booleanValue", out object bv)) {
        return FirestoreValue.FromBoolean(bv is bool b ? b : string.Equals(bv?.ToString(), "true", StringComparison.OrdinalIgnoreCase));
      }
      if (map.TryGetValue("integerValue", out object iv)) {
        long l = iv is long lv ? lv : long.Parse(iv.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture);
        return FirestoreValue.FromInteger(l);
      }
      if (map.TryGetValue("doubleValue", out object dv)) {
        double d = dv is double dd ? dd : (dv is long dl ? dl : ParseWireDouble(dv?.ToString()));
        return FirestoreValue.FromDouble(d);
      }
      if (map.TryGetValue("timestampValue", out object tv)) {
        if (tv is string tsStr) return FirestoreValue.FromTimestamp(ParseRfc3339Timestamp(tsStr));
        if (tv is IDictionary<string, object> tsMap) {
          long secs = JsonHelper.GetLong(tsMap, "seconds", 0);
          int nanos = (int)JsonHelper.GetLong(tsMap, "nanos", 0);
          return FirestoreValue.FromTimestamp(new Timestamp(secs, nanos));
        }
      }
      if (map.TryGetValue("stringValue", out object sv)) {
        return FirestoreValue.FromString(sv?.ToString() ?? string.Empty);
      }
      if (map.TryGetValue("bytesValue", out object byv)) {
        string b64 = byv?.ToString();
        return FirestoreValue.FromOwnedBytes(
            string.IsNullOrEmpty(b64) ? Array.Empty<byte>() : Convert.FromBase64String(b64));
      }
      if (map.TryGetValue("referenceValue", out object rv) &&
          ResourcePath.TryParseResourceName(rv?.ToString(), out string proj, out string db, out ResourcePath rp)) {
        return FirestoreValue.FromReference(new DocumentReferenceValue(proj, db, rp));
      }
      if (map.TryGetValue("geoPointValue", out object gpv) && gpv is IDictionary<string, object> gpMap) {
        return FirestoreValue.FromGeoPoint(
            new GeoPoint(JsonHelper.GetDouble(gpMap, "latitude"), JsonHelper.GetDouble(gpMap, "longitude")));
      }
      if (map.TryGetValue("arrayValue", out object av) && av is IDictionary<string, object> arrMap) {
        List<object> rawList = JsonHelper.GetList(arrMap, "values");
        if (rawList == null || rawList.Count == 0) {
          return FirestoreValue.FromArray(Array.Empty<FirestoreValue>());
        }
        var items = new FirestoreValue[rawList.Count];
        for (int i = 0; i < rawList.Count; i++) {
          items[i] = ParseMiniJsonFirestoreValue(rawList[i]);
        }
        return FirestoreValue.FromArray(items);
      }
      if (map.TryGetValue("mapValue", out object mv) && mv is IDictionary<string, object> mvMap) {
        Dictionary<string, object> rawFields = JsonHelper.GetMap(mvMap, "fields");
        var resMap = new Dictionary<string, FirestoreValue>(
            rawFields?.Count ?? 0, StringComparer.Ordinal);
        if (rawFields != null) {
          foreach (KeyValuePair<string, object> kv in rawFields) {
            resMap[kv.Key] = ParseMiniJsonFirestoreValue(kv.Value);
          }
        }
        return FirestoreValue.FromMap(resMap);
      }
      return FirestoreValue.Null;
    }

    internal static MutableDocument ParseMiniJsonDocument(IDictionary<string, object> docMap) {
      if (docMap == null) return null;
      string name = JsonHelper.GetString(docMap, "name");
      if (!ResourcePath.TryParseResourceName(name, out _, out _, out ResourcePath docPath)) {
        return null;
      }
      var fields = new Dictionary<string, FirestoreValue>(StringComparer.Ordinal);
      Dictionary<string, object> rawFields = JsonHelper.GetMap(docMap, "fields");
      if (rawFields != null) {
        foreach (KeyValuePair<string, object> kv in rawFields) {
          fields[kv.Key] = ParseMiniJsonFirestoreValue(kv.Value);
        }
      }
      Timestamp updateTime = default;
      if (docMap.TryGetValue("updateTime", out object ut)) {
        if (ut is string utStr) updateTime = ParseRfc3339Timestamp(utStr);
        else if (ut is IDictionary<string, object> utMap) {
          updateTime = new Timestamp(
              JsonHelper.GetLong(utMap, "seconds", 0), (int)JsonHelper.GetLong(utMap, "nanos", 0));
        }
      }
      return new MutableDocument(docPath, exists: true, fields: fields, version: updateTime);
    }
  }
}
