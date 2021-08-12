// Copyright 2017 Google LLC
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
using System.Globalization;
using System.Text;

// TODO(unity-api): This should be moved to a Firebase common Timestamp class.
namespace Firebase.Firestore {
  /// <summary>
  /// A nanosecond-precision immutable timestamp. When this is stored as part of a document in
  /// Firestore, it is truncated to the microsecond, towards the start of time.
  /// </summary>
  /// <remarks>
  /// A Timestamp represents a point in time independent of any time zone or calendar, represented
  /// as seconds and fractions of seconds at nanosecond resolution in UTC Epoch time. It is encoded
  /// using the Proleptic Gregorian Calendar which extends the Gregorian calendar backwards to year
  /// one. It is encoded assuming all minutes are 60 seconds long, i.e. leap seconds are "smeared"
  /// so that no leap second table is needed for interpretation. Range is from 0001-01-01T00:00:00Z
  /// to 9999-12-31T23:59:59.999999999Z. By restricting to that range, we ensure that we can convert
  /// to and from RFC 3339 date strings.
  /// </remarks>
  public struct Timestamp : IEquatable<Timestamp>, IComparable, IComparable<Timestamp> {
    private static readonly DateTime s_unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Constants copied from Timestamp in Protobuf
    internal const long MinSeconds = -62135596800;
    internal const long MaxSeconds = 253402300799;
    private const long BclSecondsAtUnixEpoch = 62135596800;
    private const int NanosecondsPerTick = 100;

    /// <summary>
    /// Seconds since the Unix epoch. Negative values indicate timestamps before the Unix epoch.
    /// </summary>
    private readonly long _seconds;

    /// <summary>
    /// Nanoseconds within the second; always non-negative. (For example, the nanosecond before the
    /// Unix epoch has a seconds value of -1, and a nanoseconds value of 999,999,999.
    /// </summary>
    private readonly int _nanoseconds;

    // See https://github.com/google/protobuf/blob/master/src/google/protobuf/timestamp.proto for
    // more details.

    /// <summary>
    /// Converts this timestamp to a <see cref="DateTime"/> with a kind of
    /// <see cref="DateTimeKind.Utc"/>. This can lose information as DateTime has a precision of a
    /// tick (100 nanoseconds). If the timestamp is not a precise number of ticks, it will be
    /// truncated towards the start of time.
    /// </summary>
    /// <returns>A <see cref="DateTime"/> representation of this timestamp.</returns>
    public DateTime ToDateTime() {
      return s_unixEpoch.AddSeconds(_seconds).AddTicks(_nanoseconds / NanosecondsPerTick);
    }

    /// <summary>
    /// Converts this timestamp into a <see cref="DateTimeOffset"/>.
    /// </summary>
    /// <remarks>
    /// The resulting <c>DateTimeOffset</c> will always have an <c>Offset</c> of zero.  If the
    /// timestamp is not a precise number of ticks, it will be truncated towards the start of time.
    /// <see cref="DateTimeOffset"/> value precisely on a second.
    /// </remarks>
    /// <returns>This timestamp as a <c>DateTimeOffset</c>.</returns>
    public DateTimeOffset ToDateTimeOffset() {
      return new DateTimeOffset(ToDateTime(), TimeSpan.Zero);
    }

    /// <summary>
    /// Converts a <see cref="DateTime"/> to a <see cref="Timestamp"/>.
    /// </summary>
    /// <param name="dateTime">The value to convert; its kind must be
    /// <see cref="DateTimeKind.Utc"/>.</param>
    /// <returns>A <c>Timestamp</c> representation of <paramref name="dateTime"/>. </returns>
    public static Timestamp FromDateTime(DateTime dateTime) {
      dateTime = dateTime.ToUniversalTime();
      long secondsSinceBclEpoch = dateTime.Ticks / TimeSpan.TicksPerSecond;
      int nanoseconds = (int)(dateTime.Ticks % TimeSpan.TicksPerSecond) * NanosecondsPerTick;
      return new Timestamp(secondsSinceBclEpoch - BclSecondsAtUnixEpoch, nanoseconds);
    }

    /// <summary>
    /// Converts the given <see cref="DateTimeOffset"/> to a <see cref="Timestamp"/>
    /// </summary>
    /// <remarks>
    /// The offset is taken into consideration when converting the value (so the same instant in
    /// time is represented) but is not a separate part of the resulting value. In other words,
    /// there is no round-trip operation that can retrieve the original <c>DateTimeOffset</c>.
    /// </remarks>
    /// <param name="dateTimeOffset">The date and time (with UTC offset) to convert to a
    /// timestamp.</param>
    /// <returns>The converted timestamp.</returns>
    public static Timestamp FromDateTimeOffset(DateTimeOffset dateTimeOffset) {
      return FromDateTime(dateTimeOffset.UtcDateTime);
    }

    /// <summary>
    /// Returns the current timestamp according to the system clock. The system time zone is
    /// irrelevant, as a timestamp represents an instant in time.
    /// </summary>
    /// <returns>The current timestamp according to the system clock.</returns>
    public static Timestamp GetCurrentTimestamp() {
      return FromDateTime(DateTime.UtcNow);
    }

    /// <inheritdoc />
    public override bool Equals(object obj) {
      return obj is Timestamp && Equals((Timestamp)obj);
    }

    /// <inheritdoc />
    public override int GetHashCode() {
      long result = 37 * _seconds + _nanoseconds;
      // TODO(zxu): call the one in Hash.cs
      return (int)((result & result >> 32) & 0xFFFFFFFF);
    }

    /// <inheritdoc />
    public bool Equals(Timestamp other) {
      return _seconds == other._seconds && _nanoseconds == other._nanoseconds;
    }

    /// <inheritdoc />
    public int CompareTo(Timestamp other) {
      // Note: assumes normalized form.
      int secondsComparison = _seconds.CompareTo(other._seconds);
      return secondsComparison != 0 ? secondsComparison : _nanoseconds.CompareTo(other._nanoseconds);
    }

    /// <inheritdoc />
    public int CompareTo(object obj) {
      if (obj == null) {
        // Everything comes after null
        return 1;
      } else if (obj is Timestamp) {
        return CompareTo((Timestamp)obj);
      }
      throw new ArgumentException("Can only compare timestamps with each other", nameof(obj));
    }

    /// <summary>
    /// Operator overload to compare two Timestamp values for equality.
    /// </summary>
    /// <param name="lhs">Left value to compare</param>
    /// <param name="rhs">Right value to compare</param>
    /// <returns><c>true</c> if <paramref name="lhs"/> is equal to <paramref name="rhs"/>; otherwise
    /// <c>false</c>.</returns>
    public static bool operator ==(Timestamp lhs, Timestamp rhs) {
      return lhs.Equals(rhs);
    }

    /// <summary>
    /// Operator overload to compare two Timestamp values for inequality.
    /// </summary>
    /// <param name="lhs">Left value to compare</param>
    /// <param name="rhs">Right value to compare</param>
    /// <returns><c>false</c> if <paramref name="lhs"/> is equal to <paramref name="rhs"/>;
    /// otherwise <c>true</c>.</returns>
    public static bool operator !=(Timestamp lhs, Timestamp rhs) {
      return !lhs.Equals(rhs);
    }

    /// <summary>
    /// Compares two timestamps.
    /// </summary>
    /// <param name="lhs">The left timestamp to compare.</param>
    /// <param name="rhs">The right timestamp to compare.</param>
    /// <returns><c>true</c> if <paramref name="lhs"/> is strictly earlier than
    /// <paramref name="rhs"/>; otherwise <c>false</c>.</returns>
    public static bool operator <(Timestamp lhs, Timestamp rhs) {
      return lhs.CompareTo(rhs) < 0;
    }

    /// <summary>
    /// Compares two timestamps.
    /// </summary>
    /// <param name="lhs">The left timestamp to compare.</param>
    /// <param name="rhs">The right timestamp to compare.</param>
    /// <returns><c>true</c> if <paramref name="lhs"/> is earlier than or equal to
    /// <paramref name="rhs"/>; otherwise <c>false</c>.</returns>
    public static bool operator <=(Timestamp lhs, Timestamp rhs) {
      return lhs.CompareTo(rhs) <= 0;
    }

    /// <summary>
    /// Compares two timestamps.
    /// </summary>
    /// <param name="lhs">The left timestamp to compare.</param>
    /// <param name="rhs">The right timestamp to compare.</param>
    /// <returns><c>true</c> if <paramref name="lhs"/> is strictly later than
    /// <paramref name="rhs"/>; otherwise <c>false</c>.</returns>
    public static bool operator >(Timestamp lhs, Timestamp rhs) {
      return lhs.CompareTo(rhs) > 0;
    }

    /// <summary>
    /// Compares two timestamps.
    /// </summary>
    /// <param name="lhs">The left timestamp to compare.</param>
    /// <param name="rhs">The right timestamp to compare.</param>
    /// <returns><c>true</c> if <paramref name="lhs"/> is later than or equal to
    /// <paramref name="rhs"/>; otherwise <c>false</c>.</returns>
    public static bool operator >=(Timestamp lhs, Timestamp rhs) {
      return lhs.CompareTo(rhs) >= 0;
    }

    /// <inheritdoc />
    public override string ToString() {
      DateTime dateTime = s_unixEpoch.AddSeconds(_seconds);
      var builder = new StringBuilder("Timestamp: ");
      builder.Append(dateTime.ToString("yyyy'-'MM'-'dd'T'HH:mm:ss", CultureInfo.InvariantCulture));

      if (_nanoseconds != 0) {
        builder.Append('.');
        // Output to 3, 6 or 9 digits.
        if (_nanoseconds % 1000000 == 0) {
          builder.Append((_nanoseconds / 1000000).ToString("d3", CultureInfo.InvariantCulture));
        } else if (_nanoseconds % 1000 == 0) {
          builder.Append((_nanoseconds / 1000).ToString("d6", CultureInfo.InvariantCulture));
        } else {
          builder.Append(_nanoseconds.ToString("d9", CultureInfo.InvariantCulture));
        }
      }
      builder.Append("Z");
      return builder.ToString();
    }

    internal Timestamp(long seconds, int nanoseconds) {
      if (seconds < MinSeconds || seconds > MaxSeconds) {
        throw new ArgumentException("Timestamp seconds out of range:" + seconds);
      }
      if (nanoseconds < 0 || nanoseconds > 999999999) {
        throw new ArgumentException("Timestamp nanoseconds out of range:" + nanoseconds);
      }
      _seconds = seconds;
      _nanoseconds = nanoseconds;
    }

    internal TimestampProxy ConvertToProxy() {
      return new TimestampProxy(_seconds, _nanoseconds);
    }

    internal static Timestamp ConvertFromProxy(TimestampProxy obj) {
      return new Timestamp(obj.seconds(), obj.nanoseconds());
    }
  }
}
