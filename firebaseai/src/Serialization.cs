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
using System.Reflection;
using Google.MiniJSON;

namespace Firebase.AI
{
  /// <summary>
  /// Attribute that can be used to define various fields when generating
  /// the JsonSchema for it.
  /// </summary>
  [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Field)]
  public class SchemaInfoAttribute : Attribute
  {
    /// <summary>
    /// A human-readable explanation of the purpose of the schema or property.
    /// </summary>
    public string Description { get; set; } = null;

    /// <summary>
    /// A human-readable name/summary for the schema or a specific property.
    /// </summary>
    public string Title { get; set; } = null;

    /// <summary>
    /// Indicates if the value may be null.
    /// </summary>
    public bool Nullable { get; set; } = false;

    /// <summary>
    /// The format of the data.
    /// </summary>
    public string Format { get; set; } = null;

    /// <summary>
    /// Indicates that the property should be considered as optional.
    /// Properties are considered required by default.
    /// </summary>
    public bool Optional { get; set; } = false;
  }

  /// <summary>
  /// Interface to define a method to construct the object from a Dictionary<string, object>.
  /// 
  /// The Firebase AI Logic SDK by default will attempt to use reflection when deserializing objects,
  /// like in `GenerateObjectResponse`, but this allows developers to override that logic with their own.
  /// </summary>
  public interface IFirebaseDeserializable
  {
    /// <summary>
    /// Populate an object's fields with the given dictionary.
    /// </summary>
    /// <param name="dict">A deserialized Json blob, received from the underlying model.</param>
    public void FromDictionary(IDictionary<string, object> dict);
  }

  namespace Internal
  {
    // Internal class that contains logic for serialization and deserialization.
    internal static class SerializationHelpers
    {
      // Given a serialized Json string, tries to convert it to the given type.
      internal static object JsonStringToType(string jsonString, Type type)
      {
        var resultDict = Json.Deserialize(jsonString);
        return ObjectToType(resultDict, type);
      }

      // Given an object received from the model, tries to convert it to the given type.
      internal static object ObjectToType(object obj, Type type)
      {
        if (obj == null) return null;

        // If the type is an interface, try to convert it to something we can handle.
        if (type.IsInterface)
        {
          if (type.IsGenericType)
          {
            Type genericDef = type.GetGenericTypeDefinition();
            // Common interfaces to List<T>
            if (genericDef == typeof(IEnumerable<>) ||
                genericDef == typeof(IList<>) ||
                genericDef == typeof(ICollection<>))
            {
              type = typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]);
            }
            else if (type == typeof(IList) ||
                type == typeof(IEnumerable))
            {
              type = typeof(List<object>);
            }
          }
        }

        // Convert the arg into the approriate type
        if (obj is Type t)
        {
          return t;
        }
        else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
          return ObjectToType(obj, Nullable.GetUnderlyingType(type));
        }
        else if (type.IsEnum)
        {
          if (obj is string str) return Enum.Parse(type, str);
          return Enum.ToObject(type, obj);
        }
        else if (type.IsArray)
        {
          Type elementType = type.GetElementType();
          if (obj is not System.Collections.IList inputList) return null;

          Array array = Array.CreateInstance(elementType, inputList.Count);
          for (int i = 0; i < inputList.Count; i++)
          {
            array.SetValue(ObjectToType(inputList[i], elementType), i);
          }
          return array;
        }
        else if (type.IsGenericType && typeof(IList).IsAssignableFrom(type))
        {
          if (obj is not IList inputList) return null;

          Type elementType = type.GetGenericArguments()[0];

          IList list = (IList)Activator.CreateInstance(type);

          foreach (var input in inputList)
          {
            list.Add(ObjectToType(input, elementType));
          }
          return list;
        }
        else if (obj is Dictionary<string, object> dict)
        {
          return DictionaryToType(dict, type);
        }

        try
        {
          return Convert.ChangeType(obj, type);
        }
        catch
        {
          return obj;
        }
      }

      // Given a Json style dictionary, tries to convert it to the given type.
      internal static object DictionaryToType(Dictionary<string, object> dict, Type type)
      {
        object item = Activator.CreateInstance(type);

        // Check the class for the interface, and use that if available.
        if (item is IFirebaseDeserializable deserializable)
        {
          deserializable.FromDictionary(dict);
          return item;
        }

        // Otherwise, fall back to reflection, which will be slower.
        BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
        foreach (var kvp in dict)
        {
          try
          {
            // First, check for fields
            FieldInfo field = type.GetField(kvp.Key, flags);
            if (field != null)
            {
              field.SetValue(item, ObjectToType(kvp.Value, field.FieldType));
              continue;
            }

            // Otherwise, check for properties
            PropertyInfo prop = type.GetProperty(kvp.Key, flags);
            if (prop != null && prop.CanWrite)
            {
              prop.SetValue(item, ObjectToType(kvp.Value, prop.PropertyType));
              continue;
            }
          }
          catch (Exception e)
          {
            UnityEngine.Debug.LogError($"Failed to convert object key {kvp.Key}, {e.Message}");
          }
        }

        return item;
      }
    }
  }
}
