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
using System.Text.RegularExpressions;

namespace Firebase.RemoteConfig {

  /// @brief Wrapper for a Remote Config parameter value, with methods to get
  /// it as different types, such as bools and doubles, along with information
  /// about where the data came from.
  public struct ConfigValue {
    // Regular expressions used to determine if the config value is a "valid" bool.
    // Written to match what is used internally by the Java implementation.
    internal static Regex booleanTruePattern = new Regex(@"^(1|true|t|yes|y|on)$",
                                                         RegexOptions.IgnoreCase);
    internal static Regex booleanFalsePattern = new Regex(@"^(0|false|f|no|n|off|)$",
                                                          RegexOptions.IgnoreCase);

    /// Constructor used to pass along the data.
    ///
    /// param[in] data The underlying byte data, which will be converted.
    /// param[in] source Where the data was obtained from.
    internal ConfigValue(byte[] data, ValueSource source) : this() {
      Data = data;
      Source = source;
    }

    /// @brief Gets the value as a bool.
    ///
    /// @throws System.FormatException if the data fails be converted to a bool.
    /// @returns Bool representation of this parameter value.
    public bool BooleanValue {
      get {
        string stringValue = StringValue;
        if (booleanTruePattern.IsMatch(stringValue)) return true;
        if (booleanFalsePattern.IsMatch(stringValue)) return false;
        throw new System.FormatException(string.Format("ConfigValue '{0}' is not a boolean value",
                                                       stringValue));
      }
    }

    /// @brief Gets the value as an IEnumerable of byte.
    ///
    /// @returns IEnumerable of byte representation of this parameter value.
    public System.Collections.Generic.IEnumerable<byte> ByteArrayValue {
      get {
        return Data;
      }
    }

    /// @brief Gets the value as a double.
    ///
    /// @throws System.FormatException if the data fails be converted to a
    /// double.
    /// @returns Double representation of this parameter value.
    public double DoubleValue {
      get {
        return System.Convert.ToDouble(StringValue,
            System.Globalization.CultureInfo.InvariantCulture);
      }
    }

    /// @brief Gets the value as a long.
    ///
    /// @throws System.FormatException if the data fails be converted to a long.
    /// @returns Long representation of this parameter value.
    public long LongValue {
      get {
        return System.Convert.ToInt64(StringValue,
            System.Globalization.CultureInfo.InvariantCulture);
      }
    }

    /// @brief Gets the value as a string.
    ///
    /// @returns String representation of this parameter value.
    public string StringValue {
      get {
        return System.Text.Encoding.UTF8.GetString(Data);
      }
    }

    /// The internal data.
    internal byte[] Data { get; set; }

    /// @brief Indicates which source this value came from.
    ///
    /// @returns The ValueSource corresponding to where the value came from,
    /// either the server, the default value provided, or static, if neither.
    public ValueSource Source { get; internal set; }
  }

}  // namespace Firebase.RemoteConfig

