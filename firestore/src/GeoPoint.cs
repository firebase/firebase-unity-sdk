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

using Firebase.Firestore.Internal;
using System;
using System.Runtime.InteropServices;

namespace Firebase.Firestore {
  /// <summary>
  /// Immutable struct representing a geographic location in Cloud Firestore.
  /// </summary>
  [StructLayout(LayoutKind.Explicit)]
  public struct GeoPoint : IEquatable<GeoPoint> {
    // Specify field offsets explicitly to work around .NET Core 1.0 bug. (Trying to find a
    // reference issue to make sure the fix is okay, and isn't needed elsewhere.) We can't currently
    // apply a field attribute to an automatically implemented property, so it's all explicit now.
    [field: FieldOffset(0)]
    private readonly double _latitude;
    [field: FieldOffset(8)]
    private readonly double _longitude;

    /// <summary>
    /// The latitude, in degrees, in the range -90 to 90 inclusive.
    /// </summary>
    public double Latitude {
      // Cannot use => to autogen property as not supported by the google3 toolchain:
      // Automatically implemented property `Firebase.Firestore.GeoPoint.Latitude` cannot be used
      // inside a type with an explicit StructLayout attribute.
      get { return _latitude; }
    }

    /// <summary>
    /// The longitude, in degrees, in the range -180 to 180 inclusive.
    /// </summary>
    public double Longitude {
      // Cannot use => to autogen property as not supported by the google3 toolchain:
      // Automatically implemented property `Firebase.Firestore.GeoPoint.Latitude` cannot be used
      // inside a type with an explicit StructLayout attribute.
      get { return _longitude; }
    }

    /// <summary>
    /// Creates a new value using the provided latitude and longitude values.
    /// </summary>
    /// <param name="latitude">The latitude of the point in degrees, between -90 and 90
    /// inclusive.</param>
    /// <param name="longitude">The longitude of the point in degrees, between -180 and 180
    /// inclusive.</param>
    public GeoPoint(double latitude, double longitude) {
      if (Double.IsNaN(latitude) || latitude < -90 || latitude > 90) {
        throw new ArgumentException("Latitude must be in the range of [-90, 90]");
      }
      if (Double.IsNaN(longitude) || longitude < -180 || longitude > 180) {
        throw new ArgumentException("Latitude must be in the range of [-180, 180]");
      }
      _latitude = latitude;
      _longitude = longitude;
    }

    /// <inheritdoc />
    public override bool Equals(object obj) {
      return obj is GeoPoint && Equals((GeoPoint)obj);
    }

    /// <inheritdoc />
    public override int GetHashCode() {
      // The hash implementation matches native client SDK but not the .net SDK.  We believe it is a
      // bug of .net SDK.
      return Hash.DoubleBitwiseHash(_latitude) * 31 + Hash.DoubleBitwiseHash(_longitude);
    }

    /// <inheritdoc />
    public bool Equals(GeoPoint other) {
      return Latitude == other.Latitude && Longitude == other.Longitude;
    }

    /// <summary>
    /// Operator overload to compare two GeoPoint values for equality.
    /// </summary>
    /// <param name="lhs">Left value to compare</param>
    /// <param name="rhs">Right value to compare</param>
    /// <returns><c>true</c> if <paramref name="lhs"/> is equal to <paramref name="rhs"/>; otherwise
    /// <c>false</c>.</returns>
    public static bool operator ==(GeoPoint lhs, GeoPoint rhs) {
      return lhs.Equals(rhs);
    }

    /// <summary>
    /// Operator overload to compare two GeoPoint values for inequality.
    /// </summary>
    /// <param name="lhs">Left value to compare</param>
    /// <param name="rhs">Right value to compare</param>
    /// <returns><c>false</c> if <paramref name="lhs"/> is equal to <paramref name="rhs"/>;
    /// otherwise <c>true</c>.</returns>
    public static bool operator !=(GeoPoint lhs, GeoPoint rhs) {
      return !lhs.Equals(rhs);
    }

    /// <inheritdoc />
    public override string ToString() {
      // Cannot use $ to construct string as not supported by google3 toolchain.
      return String.Format("GeoPoint:({0:g},{1:g})", _latitude, _longitude);
    }

    internal GeoPointProxy ConvertToProxy() {
      return new GeoPointProxy(_latitude, _longitude);
    }

    internal static GeoPoint ConvertFromProxy(GeoPointProxy obj) {
      return new GeoPoint(obj.latitude(), obj.longitude());
    }
  }
}
