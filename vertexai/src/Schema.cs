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

namespace Firebase.VertexAI {

public class Schema {

  public enum SchemaType {
    String,
    Number,
    Integer,
    Boolean,
    Array,
    Object,
  }

  public SchemaType Type { get; }
  public string Description { get; }
  public bool? Nullable { get; }
  public string Format { get; }
  public IEnumerable<string> EnumValues { get; }
  public Schema Items { get; }
  public IDictionary<string, Schema> Properties { get; }
  public IEnumerable<string> RequiredProperties { get; }

  public Schema(
      SchemaType type,
      string description = null,
      bool? nullable = null,
      string format = null,
      IEnumerable<string> enumValues = null,
      Schema items = null,
      IDictionary<string, Schema> properties = null,
      IEnumerable<string> requiredProperties = null) { throw new NotImplementedException(); }

  public static Schema Boolean(
      string description = null,
      bool? nullable = null) { throw new NotImplementedException(); }

  public static Schema Int(
      string description = null,
      bool? nullable = null) { throw new NotImplementedException(); }

  public static Schema Long(
      string description = null,
      bool? nullable = null) { throw new NotImplementedException(); }

  public static Schema Double(
      string description = null,
      bool? nullable = null) { throw new NotImplementedException(); }

  public static Schema Float(
      string description = null,
      bool? nullable = null) { throw new NotImplementedException(); }

  public static Schema String(
      string description = null,
      bool? nullable = null,
      string format = null) { throw new NotImplementedException(); }

  public static Schema Object(
      IDictionary<string, Schema> properties,
      IEnumerable<string> requiredProperties = null,
      string description = null,
      bool? nullable = null) { throw new NotImplementedException(); }

  public static Schema Array(
      Schema items,
      string description = null,
      bool? nullable = null) { throw new NotImplementedException(); }

  public static Schema Enum(
      IEnumerable<string> values,
      string description = null,
      bool? nullable = null) { throw new NotImplementedException(); }
}

}
