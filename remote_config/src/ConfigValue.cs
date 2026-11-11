/*
 * Copyright 2016 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Firebase.RemoteConfig {
  /// <summary>Describes the source a <see cref="ConfigValue"/> was retrieved from.</summary>
  public enum ValueSource {
    /// <summary>The value was not specified and no default was set, so a static empty value was returned.</summary>
    StaticValue = 0,
    /// <summary>The value was retrieved from the activated remote configuration.</summary>
    RemoteValue = 1,
    /// <summary>The value was retrieved from the in-app defaults.</summary>
    DefaultValue = 2,
  }

  /// @brief Wrapper for a Remote Config parameter value, with methods to get
  /// it as different types, such as bools and doubles, along with information
  /// about where the data came from.
  public struct ConfigValue {
    private readonly string _stringValue;
    private readonly IEnumerable<byte> _bytes;

    internal ConfigValue(string stringValue, IEnumerable<byte> bytes, ValueSource source) {
      (_stringValue, _bytes, Source) = (stringValue ?? string.Empty, bytes ?? Array.Empty<byte>(), source);
    }

    internal static ConfigValue Create(string value, ValueSource source) {
      string norm = value ?? string.Empty;
      return new ConfigValue(norm, norm.Length == 0 ? Array.Empty<byte>() : Array.AsReadOnly(Encoding.UTF8.GetBytes(norm)), source);
    }

    /// @brief Gets the value as a bool.
    ///
    /// @throws System.FormatException if the data fails be converted to a bool.
    /// @returns Bool representation of this parameter value.
    public bool BooleanValue {
      get {
        string s = StringValue;
        if (s.Length == 0 || s.Equals("0", StringComparison.Ordinal) || s.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("f", StringComparison.OrdinalIgnoreCase) || s.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("n", StringComparison.OrdinalIgnoreCase) || s.Equals("off", StringComparison.OrdinalIgnoreCase)) return false;
        if (s.Equals("1", StringComparison.Ordinal) || s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("t", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("y", StringComparison.OrdinalIgnoreCase) || s.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
        throw new FormatException($"ConfigValue '{s}' is not a boolean value");
      }
    }

    /// @brief Gets the value as an IEnumerable of byte.
    ///
    /// @returns IEnumerable of byte representation of this parameter value.
    public IEnumerable<byte> ByteArrayValue => _bytes ?? Array.Empty<byte>();

    /// @brief Gets the value as a double.
    ///
    /// @throws System.FormatException if the data fails be converted to a
    /// double.
    /// @returns Double representation of this parameter value.
    public double DoubleValue => Convert.ToDouble(StringValue, CultureInfo.InvariantCulture);

    /// @brief Gets the value as a long.
    ///
    /// @throws System.FormatException if the data fails be converted to a long.
    /// @returns Long representation of this parameter value.
    public long LongValue => Convert.ToInt64(StringValue, CultureInfo.InvariantCulture);

    /// @brief Gets the value as a string.
    ///
    /// @returns String representation of this parameter value.
    public string StringValue => _stringValue ?? string.Empty;

    /// @brief Indicates which source this value came from.
    ///
    /// @returns The ValueSource corresponding to where the value came from,
    /// either the server, the default value provided, or static, if neither.
    public ValueSource Source { get; internal set; }
  }
}  // namespace Firebase.RemoteConfig
