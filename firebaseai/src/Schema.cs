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
using System.Linq;

namespace Firebase.AI {

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
      null => null,
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
  public SchemaType? Type { get; }
  /// <summary>
  /// A human-readable explanation of the purpose of the schema or property. While not strictly
  /// enforced on the value itself, good descriptions significantly help the model understand the
  /// context and generate more relevant and accurate output.
  /// </summary>
  public string Description { get; }
  /// <summary>
  /// A human-readable name/summary for the schema or a specific property. This helps document the
  /// schema's purpose but doesn't typically constrain the generated value. It can subtly guide the
  /// model by clarifying the intent of a field.
  /// </summary>
  public string Title { get; }
  /// <summary>
  /// Indicates if the value may be null.
  /// </summary>
  public bool? Nullable { get; }
  /// <summary>
  /// The format of the data.
  /// </summary>
  public string Format { get; }

  /// <summary>
  /// Possible values of the element of type "String" with "enum" format.
  /// </summary>
  public IReadOnlyList<string> EnumValues { get; }
  /// <summary>
  /// Schema of the elements of type "Array".
  /// </summary>
  public Schema Items { get; }
  /// <summary>
  /// An integer specifying the minimum number of items the generated "Array" must contain.
  /// </summary>
  public int? MinItems { get; }
  /// <summary>
  /// An integer specifying the maximum number of items the generated "Array" must contain.
  /// </summary>
  public int? MaxItems { get; }

  /// <summary>
  /// The minimum value of a numeric type.
  /// </summary>
  public double? Minimum { get; }
  /// <summary>
  /// The maximum value of a numeric type.
  /// </summary>
  public double? Maximum { get; }

  /// <summary>
  /// Properties of type "Object".
  /// </summary>
  public IReadOnlyDictionary<string, Schema> Properties { get; }

  /// <summary>
  /// Required properties of type "Object".
  /// </summary>
  public IReadOnlyList<string> RequiredProperties { get; }

  /// <summary>
  /// A specific hint provided to the Gemini model, suggesting the order in which the keys should
  /// appear in the generated JSON string. Important: Standard JSON objects are inherently unordered
  /// collections of key-value pairs. While the model will try to respect PropertyOrdering in its
  /// textual JSON output, subsequent parsing into native C# objects (like Dictionaries) might
  /// not preserve this order. This parameter primarily affects the raw JSON string
  /// serialization.
  /// </summary>
  public IReadOnlyList<string> PropertyOrdering { get; }

  /// <summary>
  /// An array of `Schema` objects. The generated data must be valid against *any* (one or more)
  /// of the schemas listed in this array. This allows specifying multiple possible structures or
  /// types for a single field.
  ///
  /// For example, a value could be either a `String` or an `Int`:
  /// ```
  /// Schema.AnyOf(new [] { Schema.String(), Schema.Int() })
  /// ```
  /// </summary>
  public IReadOnlyList<Schema> AnyOfSchemas { get; }

  private Schema(
      SchemaType? type,
      string description = null,
      string title = null,
      bool? nullable = null,
      string format = null,
      IEnumerable<string> enumValues = null,
      Schema items = null,
      int? minItems = null,
      int? maxItems = null,
      double? minimum = null,
      double? maximum = null,
      IDictionary<string, Schema> properties = null,
      IEnumerable<string> requiredProperties = null,
      IEnumerable<string> propertyOrdering = null,
      IEnumerable<Schema> anyOf = null) {
    Type = type;
    Description = description;
    Title = title;
    Nullable = nullable;
    Format = format;
    EnumValues = enumValues?.ToList();
    Items = items;
    MinItems = minItems;
    MaxItems = maxItems;
    Minimum = minimum;
    Maximum = maximum;
    Properties = (properties == null) ? null : new Dictionary<string, Schema>(properties);
    RequiredProperties = requiredProperties?.ToList();
    PropertyOrdering = propertyOrdering?.ToList();
    AnyOfSchemas = anyOf?.ToList();
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
  /// <param name="minimum">If specified, instructs the model that the value should be greater than or
  ///   equal to the specified minimum.</param>
  /// <param name="maximum">If specified, instructs the model that the value should be less than or
  ///   equal to the specified maximum.</param>
  public static Schema Int(
      string description = null,
      bool nullable = false,
      int? minimum = null,
      int? maximum = null) {
    return new Schema(SchemaType.Integer,
        description: description,
        nullable: nullable,
        format: "int32",
        minimum: minimum,
        maximum: maximum
      );
  }

  /// <summary>
  /// Returns a `Schema` for a 64-bit signed integer number.
  /// </summary>
  /// <param name="description">An optional description of what the number should contain or represent.</param>
  /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
  /// <param name="minimum">If specified, instructs the model that the value should be greater than or
  ///   equal to the specified minimum.</param>
  /// <param name="maximum">If specified, instructs the model that the value should be less than or
  ///   equal to the specified maximum.</param>
  public static Schema Long(
      string description = null,
      bool nullable = false,
      long? minimum = null,
      long? maximum = null) {
    return new Schema(SchemaType.Integer,
        description: description,
        nullable: nullable,
        minimum: minimum,
        maximum: maximum
      );
  }

  /// <summary>
  /// Returns a `Schema` for a double-precision floating-point number.
  /// </summary>
  /// <param name="description">An optional description of what the number should contain or represent.</param>
  /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
  /// <param name="minimum">If specified, instructs the model that the value should be greater than or
  ///   equal to the specified minimum.</param>
  /// <param name="maximum">If specified, instructs the model that the value should be less than or
  ///   equal to the specified maximum.</param>
  public static Schema Double(
      string description = null,
      bool nullable = false,
      double? minimum = null,
      double? maximum = null) {
    return new Schema(SchemaType.Number,
        description: description,
        nullable: nullable,
        minimum: minimum,
        maximum: maximum
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
  /// <param name="minimum">If specified, instructs the model that the value should be greater than or
  ///   equal to the specified minimum.</param>
  /// <param name="maximum">If specified, instructs the model that the value should be less than or
  ///   equal to the specified maximum.</param>
  public static Schema Float(
      string description = null,
      bool nullable = false,
      float? minimum = null,
      float? maximum = null) {
    return new Schema(SchemaType.Number,
        description: description,
        nullable: nullable,
        format: "float",
        minimum: minimum,
        maximum: maximum
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
  /// <param name="propertyOrdering">An optional hint to the model suggesting the order for keys in the
  ///   generated JSON string.</param>
  /// <param name="description">An optional description of what the object represents.</param>
  /// <param name="title">An optional human-readable name/summary for the object schema.</param>
  /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
  public static Schema Object(
      IDictionary<string, Schema> properties,
      IEnumerable<string> optionalProperties = null,
      IEnumerable<string> propertyOrdering = null,
      string description = null,
      string title = null,
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
        title: title,
        nullable: nullable,
        properties: properties,
        requiredProperties: required,
        propertyOrdering: propertyOrdering
      );
  }

  /// <summary>
  /// Returns a `Schema` for an array.
  /// </summary>
  /// <param name="items">The `Schema` of the elements stored in the array.</param>
  /// <param name="description">An optional description of what the array represents.</param>
  /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
  /// <param name="minItems">Instructs the model to produce at least the specified minimum number of elements
  ///   in the array.</param>
  /// <param name="maxItems">Instructs the model to produce at most the specified minimum number of elements
  ///   in the array.</param>
  public static Schema Array(
      Schema items,
      string description = null,
      bool nullable = false,
      int? minItems = null,
      int? maxItems = null) {
    return new Schema(SchemaType.Array,
        description: description,
        nullable: nullable,
        items: items,
        minItems: minItems,
        maxItems: maxItems
      );
  }

  /// <summary>
  /// Returns a `Schema` for an enumeration.
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
  /// Returns a `Schema` representing a value that must conform to *any* (one or more) of the
  /// provided sub-schemas.
  ///
  /// This schema instructs the model to produce data that is valid against at least one of the
  /// schemas listed in the `schemas` array. This is useful when a field can accept multiple
  /// distinct types or structures.
  /// </summary>
  /// <param name="schemas">An array of `Schema` objects. The generated data must be valid against at least
  ///   one of these schemas. The array must not be empty.</param>
  public static Schema AnyOf(
      IEnumerable<Schema> schemas) {
    if (schemas == null || !schemas.Any()) {
      throw new ArgumentException("The `AnyOf` schemas array cannot be empty.");
    }

    // The backend doesn't define a SchemaType for AnyOf, instead using the existence of 'anyOf'.
    return new Schema(null,
        anyOf: schemas
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
    if (!string.IsNullOrWhiteSpace(Title)) {
      json["title"] = Title;
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
    if (MinItems != null) {
      json["minItems"] = MinItems.Value;
    }
    if (MaxItems != null) {
      json["maxItems"] = MaxItems.Value;
    }
    if (Minimum != null) {
      json["minimum"] = Minimum.Value;
    }
    if (Maximum != null) {
      json["maximum"] = Maximum.Value;
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
    if (PropertyOrdering != null && PropertyOrdering.Any()) {
      json["propertyOrdering"] = PropertyOrdering.ToList();
    }
    if (AnyOfSchemas != null && AnyOfSchemas.Any()) {
      json["anyOf"] = AnyOfSchemas.Select(s => s.ToJson()).ToList();
    }

    return json;
  }
}

}
