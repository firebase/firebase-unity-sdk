/*
 * Copyright 2025 Google LLC
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
using System.Collections.ObjectModel;
using System.Linq;

namespace Firebase.VertexAI {

/// <summary>
/// A `Schema` object allows the definition of input and output data types.
///
/// These types can be objects, but also primitives and arrays. Represents a select subset of an
/// [OpenAPI 3.0 schema object](https://spec.openapis.org/oas/v3.0.3#schema).
/// </summary>
public class Schema {

  /// <summary>
  /// The value type of a `Schema`.
  /// </summary>
  public enum SchemaType {
    String,
    Number,
    Integer,
    Boolean,
    Array,
    Object,
  }

  private string TypeAsString =>
    Type switch {
      SchemaType.String => "STRING",
      SchemaType.Number => "NUMBER",
      SchemaType.Integer => "INTEGER",
      SchemaType.Boolean => "BOOLEAN",
      SchemaType.Array => "ARRAY",
      SchemaType.Object => "OBJECT",
      _ => throw new ArgumentOutOfRangeException(nameof(Type), Type, "Invalid SchemaType value")
    };

  /// <summary>
  /// Modifiers describing the expected format of a string `Schema`.
  /// </summary>
  public readonly struct StringFormat {
    internal string Format { get; }

    private StringFormat(string format) {
      Format = format;
    }

    /// <summary>
    /// A custom string format.
    /// </summary>
    public static StringFormat Custom(string format) {
      return new StringFormat(format);
    }
  }

  /// <summary>
  /// The data type.
  /// </summary>
  public SchemaType Type { get; }
  /// <summary>
  /// A brief description of the parameter.
  /// </summary>
  public string Description { get; }
  /// <summary>
  /// Indicates if the value may be null.
  /// </summary>
  public bool? Nullable { get; }
  /// <summary>
  /// The format of the data.
  /// </summary>
  public string Format { get; }

  private readonly ReadOnlyCollection<string> _enumValues;
  /// <summary>
  /// Possible values of the element of type "String" with "enum" format.
  /// </summary>
  public IEnumerable<string> EnumValues => _enumValues;
  /// <summary>
  /// Schema of the elements of type "Array".
  /// </summary>
  public Schema Items { get; }

  /// <summary>
  /// Properties of type "Object".
  /// </summary>
  public IReadOnlyDictionary<string, Schema> Properties { get; }
  private readonly ReadOnlyCollection<string> _requiredProperties;
  /// <summary>
  /// Required properties of type "Object".
  /// </summary>
  public IEnumerable<string> RequiredProperties => _requiredProperties;

  private Schema(
      SchemaType type,
      string description = null,
      bool? nullable = null,
      string format = null,
      IEnumerable<string> enumValues = null,
      Schema items = null,
      IDictionary<string, Schema> properties = null,
      IEnumerable<string> requiredProperties = null) {
    Type = type;
    Description = description;
    Nullable = nullable;
    Format = format;
    _enumValues = new ReadOnlyCollection<string>(
        enumValues?.ToList() ?? new List<string>());
    Items = items;
    if (properties == null) {
      Properties = new Dictionary<string, Schema>();
    } else {
      Properties = new Dictionary<string, Schema>(properties);
    }
    _requiredProperties = new ReadOnlyCollection<string>(
        requiredProperties?.ToList() ?? new List<string>());
  }

  /// <summary>
  /// Returns a `Schema` representing a boolean value.
  /// </summary>
  /// <param name="description">An optional description of what the boolean should contain or represent.</param>
  /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
  public static Schema Boolean(
      string description = null,
      bool nullable = false) {
    return new Schema(SchemaType.Boolean,
        description: description,
        nullable: nullable
      );
  }

  /// <summary>
  /// Returns a `Schema` for a 32-bit signed integer number.
  /// 
  /// **Important:** This `Schema` provides a hint to the model that it should generate a 32-bit
  ///   integer, but only guarantees that the value will be an integer. Therefore it's *possible*
  ///   that decoding it as an `int` could overflow.
  /// </summary>
  /// <param name="description">An optional description of what the integer should contain or represent.</param>
  /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
  public static Schema Int(
      string description = null,
      bool nullable = false) {
    return new Schema(SchemaType.Integer,
        description: description,
        nullable: nullable,
        format: "int32"
      );
  }

  /// <summary>
  /// Returns a `Schema` for a 64-bit signed integer number.
  /// </summary>
  /// <param name="description">An optional description of what the number should contain or represent.</param>
  /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
  public static Schema Long(
      string description = null,
      bool nullable = false) {
    return new Schema(SchemaType.Integer,
        description: description,
        nullable: nullable
      );
  }

  /// <summary>
  /// Returns a `Schema` for a double-precision floating-point number.
  /// </summary>
  /// <param name="description">An optional description of what the number should contain or represent.</param>
  /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
  public static Schema Double(
      string description = null,
      bool nullable = false) {
    return new Schema(SchemaType.Number,
        description: description,
        nullable: nullable
      );
  }

  /// <summary>
  /// Returns a `Schema` for a single-precision floating-point number.
  ///
  /// **Important:** This `Schema` provides a hint to the model that it should generate a
  ///   single-precision floating-point number, but only guarantees that the value will be a number.
  ///   Therefore it's *possible* that decoding it as a `float` could overflow.
  /// </summary>
  /// <param name="description">An optional description of what the number should contain or represent.</param>
  /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
  public static Schema Float(
      string description = null,
      bool nullable = false) {
    return new Schema(SchemaType.Number,
        description: description,
        nullable: nullable,
        format: "float"
      );
  }

  /// <summary>
  /// Returns a `Schema` for a string.
  /// </summary>
  /// <param name="description">An optional description of what the string should contain or represent.</param>
  /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
  /// <param name="format">An optional pattern that values need to adhere to.</param>
  public static Schema String(
      string description = null,
      bool nullable = false,
      StringFormat? format = null) {
    return new Schema(SchemaType.String,
        description: description,
        nullable: nullable,
        format: format?.Format
      );
  }

  /// <summary>
  /// Returns a `Schema` representing an object.
  ///
  /// This schema instructs the model to produce data of type "Object", which has keys of type
  /// "String" and values of any other data type (including nested "Objects"s).
  ///
  /// **Example:** A `City` could be represented with the following object `Schema`.
  /// ```
  /// Schema.Object(properties: new Dictionary() {
  ///   { "name", Schema.String() },
  ///   { "population", Schema.Integer() }
  /// })
  /// ```
  /// </summary>
  /// <param name="properties">The map of the object's property names to their `Schema`s.</param>
  /// <param name="optionalProperties">The list of optional properties. They must correspond to the keys
  ///   provided in the `properties` map. By default it's empty, signaling the model that all
  ///   properties are to be included.</param>
  /// <param name="description">An optional description of what the object represents.</param>
  /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
  public static Schema Object(
      IDictionary<string, Schema> properties,
      IEnumerable<string> optionalProperties = null,
      string description = null,
      bool nullable = false) {
    if (properties == null) {
      throw new ArgumentNullException(nameof(properties));
    }

    if (optionalProperties != null) {
      // Check for invalid optional properties
      var invalidOptionalProperties = optionalProperties.Except(properties.Keys).ToList();
      if (invalidOptionalProperties.Any()) {
        throw new ArgumentException(
            "The following optional properties are not present in the properties dictionary: " +
              $"{string.Join(", ", invalidOptionalProperties)}",
            nameof(optionalProperties));
      }
    }

    // Determine required properties
    var required =
        properties.Keys.Except(optionalProperties ?? Enumerable.Empty<string>()).ToList();

    foreach (string key in properties.Keys) {
      if (optionalProperties == null || !optionalProperties.Contains(key)) {
        required.Add(key);
      }
    }

    return new Schema(SchemaType.Object,
        description: description,
        nullable: nullable,
        properties: properties,
        requiredProperties: required
      );
  }

  /// <summary>
  /// Returns a `Schema` for an array.
  /// </summary>
  /// <param name="items">The `Schema` of the elements stored in the array.</param>
  /// <param name="description">An optional description of what the array represents.</param>
  /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
  public static Schema Array(
      Schema items,
      string description = null,
      bool nullable = false) {
    return new Schema(SchemaType.Array,
        description: description,
        nullable: nullable,
        items: items
      );
  }

  /// <summary>
  /// Returns a [Schema] for an enumeration.
  ///
  /// For example, the cardinal directions can be represented as:
  /// ```
  /// Schema.Enum(new string[]{ "North", "East", "South", "West" }, "Cardinal directions")
  /// ```
  /// </summary>
  /// <param name="values">The list of valid values for this enumeration.</param>
  /// <param name="description">An optional description of what the enum represents.</param>
  /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
  public static Schema Enum(
      IEnumerable<string> values,
      string description = null,
      bool nullable = false) {
    return new Schema(SchemaType.String,
        description: description,
        nullable: nullable,
        enumValues: values,
        format: "enum"
      );
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for serializing the object to JSON for the API request.
  /// </summary>
  internal Dictionary<string, object> ToJson() {
    Dictionary<string, object> json = new() {
      { "type", TypeAsString }
    };
    if (!string.IsNullOrWhiteSpace(Description)) {
      json["description"] = Description;
    }
    if (Nullable.HasValue) {
      json["nullable"] = Nullable.Value;
    }
    if (EnumValues != null && EnumValues.Any()) {
      json["enum"] = EnumValues.ToList();
    }
    if (Items != null) {
      json["items"] = Items.ToJson();
    }
    if (Properties != null && Properties.Any()) {
      Dictionary<string, object> propertiesJson = new();
      foreach (var kvp in Properties) {
        propertiesJson[kvp.Key] = kvp.Value.ToJson();
      }
      json["properties"] = propertiesJson;
      
    }
    if (RequiredProperties != null && RequiredProperties.Any()) {
      json["required"] = RequiredProperties.ToList();
    }

    return json;
  }
}

}
