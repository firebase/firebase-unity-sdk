/*
 * Copyright 2026 Google LLC
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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Firebase.AI.Internal;

namespace Firebase.AI
{
  /// <summary>
  /// A `JsonSchema` object allows the definition of input and output data types.
  ///
  /// These types can be objects, but also primitives and arrays. Represents a select subset of an
  /// [JsonSchema object](https://json-schema.org/specification).
  /// </summary>
  public class JsonSchema
  {

    /// <summary>
    /// The value type of a `Schema`.
    /// </summary>
    public enum SchemaType
    {
      String,
      Number,
      Integer,
      Boolean,
      Array,
      Object,
    }

    private string TypeAsString =>
      Type switch
      {
        SchemaType.String => "string",
        SchemaType.Number => "number",
        SchemaType.Integer => "integer",
        SchemaType.Boolean => "boolean",
        SchemaType.Array => "array",
        SchemaType.Object => "object",
        null => null,
        _ => throw new ArgumentOutOfRangeException(nameof(Type), Type, "Invalid SchemaType value")
      };

    /// <summary>
    /// Modifiers describing the expected format of a string `JsonSchema`.
    /// </summary>
    public readonly struct StringFormat
    {
      internal string Format { get; }

      private StringFormat(string format)
      {
        Format = format;
      }

      /// <summary>
      /// A custom string format.
      /// </summary>
      public static StringFormat Custom(string format)
      {
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
    /// JsonSchema of the elements of type "Array".
    /// </summary>
    public JsonSchema Items { get; }
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
    public IReadOnlyDictionary<string, JsonSchema> Properties { get; }

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
    /// An array of `JsonSchema` objects. The generated data must be valid against *any* (one or more)
    /// of the schemas listed in this array. This allows specifying multiple possible structures or
    /// types for a single field.
    ///
    /// For example, a value could be either a `String` or an `Int`:
    /// ```
    /// JsonSchema.AnyOf(new [] { JsonSchema.String(), JsonSchema.Int() })
    /// ```
    /// </summary>
    public IReadOnlyList<JsonSchema> AnyOfSchemas { get; }

    /// <summary>
    /// A reference to a JsonSchema defined in a parent Object's SchemaDefinitions.
    /// Should generally be the form "#/$defs/schema_name"
    /// </summary>
    public string SchemaReference { get; }

    /// <summary>
    /// A set of JsonSchema definitions, that can be used be JsonSchema.Ref objects.
    /// </summary>
    public IReadOnlyDictionary<string, JsonSchema> SchemaDefinitions { get; }

    private JsonSchema(
        SchemaType? type,
        string description = null,
        string title = null,
        bool? nullable = null,
        string format = null,
        IEnumerable<string> enumValues = null,
        JsonSchema items = null,
        int? minItems = null,
        int? maxItems = null,
        double? minimum = null,
        double? maximum = null,
        IDictionary<string, JsonSchema> properties = null,
        IEnumerable<string> requiredProperties = null,
        IEnumerable<string> propertyOrdering = null,
        IEnumerable<JsonSchema> anyOf = null,
        string schemaReference = null,
        IDictionary<string, JsonSchema> schemaDefinitions = null)
    {
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
      Properties = (properties == null) ? null : new Dictionary<string, JsonSchema>(properties);
      RequiredProperties = requiredProperties?.ToList();
      PropertyOrdering = propertyOrdering?.ToList();
      AnyOfSchemas = anyOf?.ToList();
      SchemaReference = schemaReference;
      SchemaDefinitions = (schemaDefinitions == null) ? null : new Dictionary<string, JsonSchema>(schemaDefinitions);
    }

    /// <summary>
    /// Returns a `JsonSchema` representing a boolean value.
    /// </summary>
    /// <param name="description">An optional description of what the boolean should contain or represent.</param>
    /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
    public static JsonSchema Boolean(
        string description = null,
        bool nullable = false)
    {
      return new JsonSchema(SchemaType.Boolean,
          description: description,
          nullable: nullable
        );
    }

    /// <summary>
    /// Returns a `JsonSchema` for a 32-bit signed integer number.
    /// 
    /// **Important:** This `JsonSchema` provides a hint to the model that it should generate a 32-bit
    ///   integer, but only guarantees that the value will be an integer. Therefore it's *possible*
    ///   that decoding it as an `int` could overflow.
    /// </summary>
    /// <param name="description">An optional description of what the integer should contain or represent.</param>
    /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
    /// <param name="minimum">If specified, instructs the model that the value should be greater than or
    ///   equal to the specified minimum.</param>
    /// <param name="maximum">If specified, instructs the model that the value should be less than or
    ///   equal to the specified maximum.</param>
    public static JsonSchema Int(
        string description = null,
        bool nullable = false,
        int? minimum = null,
        int? maximum = null)
    {
      return new JsonSchema(SchemaType.Integer,
          description: description,
          nullable: nullable,
          minimum: minimum,
          maximum: maximum
        );
    }

    /// <summary>
    /// Returns a `JsonSchema` for a 64-bit signed integer number.
    /// </summary>
    /// <param name="description">An optional description of what the number should contain or represent.</param>
    /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
    /// <param name="minimum">If specified, instructs the model that the value should be greater than or
    ///   equal to the specified minimum.</param>
    /// <param name="maximum">If specified, instructs the model that the value should be less than or
    ///   equal to the specified maximum.</param>
    public static JsonSchema Long(
        string description = null,
        bool nullable = false,
        long? minimum = null,
        long? maximum = null)
    {
      return new JsonSchema(SchemaType.Integer,
          description: description,
          nullable: nullable,
          minimum: minimum,
          maximum: maximum
        );
    }

    /// <summary>
    /// Returns a `JsonSchema` for a double-precision floating-point number.
    /// </summary>
    /// <param name="description">An optional description of what the number should contain or represent.</param>
    /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
    /// <param name="minimum">If specified, instructs the model that the value should be greater than or
    ///   equal to the specified minimum.</param>
    /// <param name="maximum">If specified, instructs the model that the value should be less than or
    ///   equal to the specified maximum.</param>
    public static JsonSchema Double(
        string description = null,
        bool nullable = false,
        double? minimum = null,
        double? maximum = null)
    {
      return new JsonSchema(SchemaType.Number,
          description: description,
          nullable: nullable,
          minimum: minimum,
          maximum: maximum
        );
    }

    /// <summary>
    /// Returns a `JsonSchema` for a single-precision floating-point number.
    ///
    /// **Important:** This `JsonSchema` provides a hint to the model that it should generate a
    ///   single-precision floating-point number, but only guarantees that the value will be a number.
    ///   Therefore it's *possible* that decoding it as a `float` could overflow.
    /// </summary>
    /// <param name="description">An optional description of what the number should contain or represent.</param>
    /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
    /// <param name="minimum">If specified, instructs the model that the value should be greater than or
    ///   equal to the specified minimum.</param>
    /// <param name="maximum">If specified, instructs the model that the value should be less than or
    ///   equal to the specified maximum.</param>
    public static JsonSchema Float(
        string description = null,
        bool nullable = false,
        float? minimum = null,
        float? maximum = null)
    {
      return new JsonSchema(SchemaType.Number,
          description: description,
          nullable: nullable,
          minimum: minimum,
          maximum: maximum
        );
    }

    /// <summary>
    /// Returns a `JsonSchema` for a string.
    /// </summary>
    /// <param name="description">An optional description of what the string should contain or represent.</param>
    /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
    /// <param name="format">An optional pattern that values need to adhere to.</param>
    public static JsonSchema String(
        string description = null,
        bool nullable = false,
        StringFormat? format = null)
    {
      return new JsonSchema(SchemaType.String,
          description: description,
          nullable: nullable,
          format: format?.Format
        );
    }

    /// <summary>
    /// Returns a `JsonSchema` representing an object.
    ///
    /// This schema instructs the model to produce data of type "Object", which has keys of type
    /// "String" and values of any other data type (including nested "Objects"s).
    ///
    /// **Example:** A `City` could be represented with the following object `JsonSchema`.
    /// ```
    /// JsonSchema.Object(properties: new Dictionary() {
    ///   { "name", JsonSchema.String() },
    ///   { "population", JsonSchema.Integer() }
    /// })
    /// ```
    /// </summary>
    /// <param name="properties">The map of the object's property names to their `JsonSchema`s.</param>
    /// <param name="optionalProperties">The list of optional properties. They must correspond to the keys
    ///   provided in the `properties` map. By default it's empty, signaling the model that all
    ///   properties are to be included.</param>
    /// <param name="propertyOrdering">An optional hint to the model suggesting the order for keys in the
    ///   generated JSON string.</param>
    /// <param name="description">An optional description of what the object represents.</param>
    /// <param name="title">An optional human-readable name/summary for the object schema.</param>
    /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
    public static JsonSchema Object(
        IDictionary<string, JsonSchema> properties,
        IEnumerable<string> optionalProperties = null,
        IEnumerable<string> propertyOrdering = null,
        string description = null,
        string title = null,
        bool nullable = false,
        IDictionary<string, JsonSchema> schemaDefinitions = null)
    {
      if (properties == null)
      {
        throw new ArgumentNullException(nameof(properties));
      }

      if (optionalProperties != null)
      {
        // Check for invalid optional properties
        var invalidOptionalProperties = optionalProperties.Except(properties.Keys).ToList();
        if (invalidOptionalProperties.Any())
        {
          throw new ArgumentException(
              "The following optional properties are not present in the properties dictionary: " +
                $"{string.Join(", ", invalidOptionalProperties)}",
              nameof(optionalProperties));
        }
      }

      // Determine required properties
      var required =
          properties.Keys.Except(optionalProperties ?? Enumerable.Empty<string>()).ToList();

      return new JsonSchema(SchemaType.Object,
          description: description,
          title: title,
          nullable: nullable,
          properties: properties,
          requiredProperties: required,
          propertyOrdering: propertyOrdering,
          schemaDefinitions: schemaDefinitions
        );
    }

    /// <summary>
    /// Returns a `JsonSchema` for an array.
    /// </summary>
    /// <param name="items">The `JsonSchema` of the elements stored in the array.</param>
    /// <param name="description">An optional description of what the array represents.</param>
    /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
    /// <param name="minItems">Instructs the model to produce at least the specified minimum number of elements
    ///   in the array.</param>
    /// <param name="maxItems">Instructs the model to produce at most the specified minimum number of elements
    ///   in the array.</param>
    public static JsonSchema Array(
        JsonSchema items,
        string description = null,
        bool nullable = false,
        int? minItems = null,
        int? maxItems = null)
    {
      return new JsonSchema(SchemaType.Array,
          description: description,
          nullable: nullable,
          items: items,
          minItems: minItems,
          maxItems: maxItems
        );
    }

    /// <summary>
    /// Returns a `JsonSchema` for an enumeration.
    ///
    /// For example, the cardinal directions can be represented as:
    /// ```
    /// JsonSchema.Enum(new string[]{ "North", "East", "South", "West" }, "Cardinal directions")
    /// ```
    /// </summary>
    /// <param name="values">The list of valid values for this enumeration.</param>
    /// <param name="description">An optional description of what the enum represents.</param>
    /// <param name="nullable">Indicates whether the value can be `null`. Defaults to `false`.</param>
    public static JsonSchema Enum(
        IEnumerable<string> values,
        string description = null,
        bool nullable = false)
    {
      return new JsonSchema(SchemaType.String,
          description: description,
          nullable: nullable,
          enumValues: values,
          format: "enum"
        );
    }

    /// <summary>
    /// Returns a `JsonSchema` representing a value that must conform to *any* (one or more) of the
    /// provided sub-schemas.
    ///
    /// This schema instructs the model to produce data that is valid against at least one of the
    /// schemas listed in the `schemas` array. This is useful when a field can accept multiple
    /// distinct types or structures.
    /// </summary>
    /// <param name="schemas">An array of `JsonSchema` objects. The generated data must be valid against at least
    ///   one of these schemas. The array must not be empty.</param>
    public static JsonSchema AnyOf(
        IEnumerable<JsonSchema> schemas)
    {
      if (schemas == null || !schemas.Any())
      {
        throw new ArgumentException("The `AnyOf` schemas array cannot be empty.");
      }

      // The backend doesn't define a SchemaType for AnyOf, instead using the existence of 'anyOf'.
      return new JsonSchema(null,
          anyOf: schemas
        );
    }

    /// <summary>
    /// Returns a `JsonSchema` that references a definition in a parent object.
    /// </summary>
    /// <param name="schemaReference">The path to the definition, typically "$/defs/class_name"</param>
    public static JsonSchema Ref(string schemaReference)
    {
      return new JsonSchema(null,
        schemaReference: schemaReference
      );
    }

    /// <summary>
    /// Intended for internal use only.
    /// This method is used for serializing the object to JSON for the API request.
    /// </summary>
    internal Dictionary<string, object> ToJson()
    {
      Dictionary<string, object> json = new();
      json.AddIfHasValue("type", TypeAsString);
      if (!string.IsNullOrWhiteSpace(Description))
      {
        json["description"] = Description;
      }
      if (!string.IsNullOrWhiteSpace(Title))
      {
        json["title"] = Title;
      }
      if (Nullable.HasValue)
      {
        json["nullable"] = Nullable.Value;
      }
      if (EnumValues != null && EnumValues.Any())
      {
        json["enum"] = EnumValues.ToList();
      }
      if (Items != null)
      {
        json["items"] = Items.ToJson();
      }
      if (MinItems != null)
      {
        json["minItems"] = MinItems.Value;
      }
      if (MaxItems != null)
      {
        json["maxItems"] = MaxItems.Value;
      }
      if (Minimum != null)
      {
        json["minimum"] = Minimum.Value;
      }
      if (Maximum != null)
      {
        json["maximum"] = Maximum.Value;
      }
      if (Properties != null && Properties.Any())
      {
        Dictionary<string, object> propertiesJson = new();
        foreach (var kvp in Properties)
        {
          propertiesJson[kvp.Key] = kvp.Value.ToJson();
        }
        json["properties"] = propertiesJson;

      }
      if (RequiredProperties != null && RequiredProperties.Any())
      {
        json["required"] = RequiredProperties.ToList();
      }
      if (PropertyOrdering != null && PropertyOrdering.Any())
      {
        json["propertyOrdering"] = PropertyOrdering.ToList();
      }
      if (AnyOfSchemas != null && AnyOfSchemas.Any())
      {
        json["anyOf"] = AnyOfSchemas.Select(s => s.ToJson()).ToList();
      }
      if (!string.IsNullOrWhiteSpace(SchemaReference))
      {
        json["$ref"] = SchemaReference;
      }
      if (SchemaDefinitions != null && SchemaDefinitions.Any())
      {
        Dictionary<string, object> defsJson = new();
        foreach (var kvp in SchemaDefinitions)
        {
          defsJson[kvp.Key] = kvp.Value.ToJson();
        }
        json["$defs"] = defsJson;
      }

      return json;
    }

    /// <summary>
    /// Generates a JsonSchema for the given type, using reflection.
    /// Note that if the type implements: static JsonSchema ToJsonSchema(), that function
    /// will be called to generate the JsonSchema.
    /// </summary>
    /// <param name="type">The type to construct the JsonSchema of.</param>
    /// <param name="description">The description to use for the returned JsonSchema</param>
    public static JsonSchema FromType(Type type, string description = null)
    {
      return FromTypeInternal(type, null, description, new Dictionary<string, JsonSchema>(), true, out _);
    }

    private static JsonSchema FromTypeInternal(Type type, MemberInfo memberInfo, string description,
        Dictionary<string, JsonSchema> definitions, bool topLevel, out bool optional)
    {
      optional = false;

      if (type == null) return null;

      // Handle Nullable<T> by unwrapping it.
      bool isNullableValueType = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
      if (isNullableValueType)
      {
        type = System.Nullable.GetUnderlyingType(type);
      }

      // If the given type has Schema info, pull it from that
      var schemaInfo = memberInfo == null ?
          type.GetCustomAttribute<SchemaInfoAttribute>() :
          memberInfo.GetCustomAttribute<SchemaInfoAttribute>();

      optional = schemaInfo?.Optional ?? false;

      // Check if there is a defined function on the type to make the JsonSchema
      var toSchemaMethod = type.GetMethod("ToJsonSchema",
          BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
      if (toSchemaMethod != null && toSchemaMethod.ReturnType == typeof(JsonSchema) &&
          toSchemaMethod.GetParameters().Length == 0)
      {
        return (JsonSchema)toSchemaMethod.Invoke(null, null);
      }

      // If not provided a description, check the schemaInfo object
      description ??= schemaInfo?.Description;
      bool nullable = schemaInfo != null && schemaInfo.Nullable;

      // Check for the commonly used attribute Range, for Min and Max
      float? min = null;
      float? max = null;
      var rangeAttr = memberInfo == null ?
          type.GetCustomAttribute<UnityEngine.RangeAttribute>() :
          memberInfo.GetCustomAttribute<UnityEngine.RangeAttribute>();
      if (rangeAttr != null)
      {
        min = rangeAttr.min;
        max = rangeAttr.max;
      }

      if (type.IsPrimitive)
      {
        // Possible primitives:
        //   bool
        //   byte, sbyte, short, ushort, int, uint, long, ulong, float, double
        //   char  *Not clearly mapped*
        //   IntPtr, UIntPtr  *Not clearly mapped*
        if (type == typeof(bool))
        {
          return Boolean(description: description, nullable: nullable);
        }
        else if (type == typeof(float))
        {
          return Float(description: description, nullable: nullable,
              minimum: min, maximum: max);
        }
        else if (type == typeof(double))
        {
          return Double(description: description, nullable: nullable,
              minimum: min, maximum: max);
        }
        else if (type == typeof(long) || type == typeof(ulong))
        {
          return Long(description: description, nullable: nullable,
              minimum: (long?)min, maximum: (long?)max);
        }
        else
        {
          // Treat everything else as an Int. While there could be logic to add to set
          // minimum and maximums based on the type, it will likely be unnecessary.
          return Int(description: description, nullable: nullable,
              minimum: (int?)min, maximum: (int?)max);
        }
      }
      else if (type.IsEnum)
      {
        return Enum(type.GetEnumNames(), description: description, nullable: nullable);
      }
      else if (type == typeof(string))
      {
        return String(description: description, nullable: nullable);
      }
      else if (type.IsArray)
      {
        Type elementType = type.GetElementType();
        JsonSchema elementSchema = FromTypeInternal(elementType, null, null, definitions, false, out _);
        return Array(elementSchema, description: description, nullable: nullable);
      }
      else if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
      {
        // There isn't a great way to handle dictionaries, so bail out.
        if (typeof(IDictionary).IsAssignableFrom(type))
        {
          return null;
        }
        Type elementType = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0] ?? typeof(object);
        JsonSchema elementSchema = FromTypeInternal(elementType, null, null, definitions, false, out _);
        return Array(elementSchema, description, nullable: nullable);
      }
      else
      {
        // Assume it is an Object
        // If this is not at the top level, we want to add it to the definitions, and use a ref instead
        if (!topLevel)
        {
          string key = type.FullName;
          if (!definitions.ContainsKey(key))
          {
            definitions[key] = null; // Placeholder to prevent infinite recursion.
            JsonSchema jsonSchema = GenerateObject(type, schemaInfo, description, definitions, false);
            definitions[key] = jsonSchema;
          }

          return Ref($"#/$defs/{key}");
        }

        // Generate the top level object, which will include any found definitions.
        return GenerateObject(type, schemaInfo, description, definitions, topLevel);
      }
    }

    private static JsonSchema GenerateObject(Type type, SchemaInfoAttribute schemaInfo, string description,
        Dictionary<string, JsonSchema> definitions, bool includeDefinitions)
    {
      bool nullable = schemaInfo != null && schemaInfo.Nullable;

      Dictionary<string, JsonSchema> properties = new();
      List<string> optionalProperties = new();
      // Get the public Fields and Properties
      var infos = type.FindMembers(
          MemberTypes.Field | MemberTypes.Property,
          BindingFlags.Instance | BindingFlags.Public,
          null, null);
      foreach (var info in infos)
      {
        JsonSchema jsonSchema = null;
        bool optional = false;
        if (info is FieldInfo fieldInfo)
        {
          jsonSchema = FromTypeInternal(fieldInfo.FieldType, info, null, definitions, false, out optional);
        }
        else if (info is PropertyInfo propertyInfo)
        {
          jsonSchema = FromTypeInternal(propertyInfo.PropertyType, info, null, definitions, false, out optional);
        }

        if (jsonSchema != null)
        {
          properties[info.Name] = jsonSchema;
          if (optional)
          {
            optionalProperties.Add(info.Name);
          }
        }
      }

      return Object(properties, optionalProperties: optionalProperties,
          description: description, title: schemaInfo?.Title,
          nullable: nullable, schemaDefinitions: includeDefinitions ? definitions : null);
    }
  }

}
