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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.AI.Internal
{
  // Include this to just shorthand it in this file
  using JsonDict = Dictionary<string, object>;

  // Options used by the extensions below to modify logic, mostly around throwing exceptions.
  [Flags]
  internal enum JsonParseOptions
  {
    None = 0,
    // Throw if the key is missing from the dictionary, useful for required fields.
    ThrowMissingKey = 1 << 0,
    // Throw if the key was found, but it failed to cast to the expected type.
    ThrowInvalidCast = 1 << 1,
    // Combination of the above.
    ThrowEverything = ThrowMissingKey | ThrowInvalidCast,
  }

  // Contains extension methods for commonly done logic.
  internal static class FirebaseAIExtensions
  {

    // Tries to find the object with the key, and cast it to the type T
    public static bool TryParseValue<T>(this JsonDict jsonDict, string key,
        out T value, JsonParseOptions options = JsonParseOptions.ThrowInvalidCast)
    {
      if (jsonDict.TryGetValue(key, out object obj))
      {
        if (obj is T tObj)
        {
          value = tObj;
          return true;
        }
        else if (obj is long asLong &&
            (typeof(T) == typeof(int) ||
             typeof(T) == typeof(float) ||
             typeof(T) == typeof(double)))
        {
          // MiniJson puts all ints as longs, so special case it
          // Also, if the number didn't have a decimal point, we still want to
          // allow it to be a float or double.
          value = (T)Convert.ChangeType(asLong, typeof(T));
          return true;
        }
        else if (typeof(T) == typeof(float) && obj is double asDouble)
        {
          // Similarly, floats are stored as doubles.
          value = (T)(object)(float)asDouble;
          return true;
        }
        else if (options.HasFlag(JsonParseOptions.ThrowInvalidCast))
        {
          throw new InvalidCastException(
              $"Invalid JSON format: '{key}' is not of type '{typeof(T).Name}', " +
              $"Actual type: {obj.GetType().FullName}.");
        }
      }
      else if (options.HasFlag(JsonParseOptions.ThrowMissingKey))
      {
        throw new KeyNotFoundException(
            $"Invalid JSON format: Unable to locate expected key {key}");
      }
      value = default;
      return false;
    }

    // Casts the found object to type T, otherwise returns default (or throws)
    public static T ParseValue<T>(this JsonDict jsonDict, string key,
        JsonParseOptions options = JsonParseOptions.ThrowInvalidCast,
        T defaultValue = default)
    {
      if (TryParseValue(jsonDict, key, out T value, options))
      {
        return value;
      }
      else
      {
        return defaultValue;
      }
    }

    // Casts the found object to type T, otherwise returns null (or throws)
    public static T? ParseNullableValue<T>(this JsonDict jsonDict, string key,
        JsonParseOptions options = JsonParseOptions.ThrowInvalidCast) where T : struct
    {
      if (TryParseValue(jsonDict, key, out T value, options))
      {
        return value;
      }
      else
      {
        return null;
      }
    }

    // Tries to convert the string found with the key to an enum, using the given function.
    public static bool TryParseEnum<T>(this JsonDict jsonDict, string key,
        Func<string, T> parseFunc, out T value,
        JsonParseOptions options = JsonParseOptions.ThrowInvalidCast)
    {
      if (jsonDict.TryParseValue(key, out string enumStr, options))
      {
        value = parseFunc(enumStr);
        return true;
      }
      else
      {
        value = default;
        return false;
      }
    }

    // Casts the found string to an enum, otherwise returns default (or throws)
    public static T ParseEnum<T>(this JsonDict jsonDict, string key,
        Func<string, T> parseFunc,
        JsonParseOptions options = JsonParseOptions.ThrowInvalidCast,
        T defaultValue = default)
    {
      if (TryParseEnum(jsonDict, key, parseFunc, out T value, options))
      {
        return value;
      }
      else
      {
        return defaultValue;
      }
    }

    // Casts the found string to an enum, otherwise returns null (or throws)
    public static T? ParseNullableEnum<T>(this JsonDict jsonDict, string key,
        Func<string, T> parseFunc,
        JsonParseOptions options = JsonParseOptions.ThrowInvalidCast) where T : struct, Enum
    {
      if (TryParseEnum(jsonDict, key, parseFunc, out T value, options))
      {
        return value;
      }
      else
      {
        return null;
      }
    }

    // Tries to convert the given Dictionary found at the key with the given function.
    public static bool TryParseObject<T>(this JsonDict jsonDict, string key,
        Func<JsonDict, T> parseFunc, out T value,
        JsonParseOptions options = JsonParseOptions.ThrowInvalidCast)
    {
      if (TryParseValue(jsonDict, key, out JsonDict innerDict, options))
      {
        value = parseFunc(innerDict);
        return true;
      }
      else
      {
        value = default;
        return false;
      }
    }

    // Casts the found Dictionary to an object, otherwise returns default (or throws)
    public static T ParseObject<T>(this JsonDict jsonDict, string key,
        Func<JsonDict, T> parseFunc,
        JsonParseOptions options = JsonParseOptions.ThrowInvalidCast,
        T defaultValue = default)
    {
      if (TryParseObject(jsonDict, key, parseFunc, out T value, options))
      {
        return value;
      }
      else
      {
        return defaultValue;
      }
    }

    // Casts the found Dictionary to an object, otherwise returns null (or throws)
    public static T? ParseNullableObject<T>(this JsonDict jsonDict, string key,
        Func<JsonDict, T> parseFunc,
        JsonParseOptions options = JsonParseOptions.ThrowInvalidCast) where T : struct
    {
      if (TryParseObject(jsonDict, key, parseFunc, out T value, options))
      {
        return value;
      }
      else
      {
        return null;
      }
    }

    // Tries to convert the found List of objects to a List of strings.
    public static bool TryParseStringList(this JsonDict jsonDict, string key,
        out List<string> values,
        JsonParseOptions options = JsonParseOptions.ThrowInvalidCast)
    {
      if (jsonDict.TryParseValue(key, out List<object> list, options))
      {
        values = list.OfType<string>().ToList();
        return true;
      }
      else
      {
        values = null;
        return false;
      }
    }

    // Casts the found List to a string List, otherwise returns default (or throws)
    public static List<string> ParseStringList(this JsonDict jsonDict, string key,
        JsonParseOptions options = JsonParseOptions.ThrowInvalidCast)
    {
      TryParseStringList(jsonDict, key, out List<string> values, options);
      return values;
    }

    // Tries to convert the found List of Dictionaries, using the given function.
    public static bool TryParseObjectList<T>(this JsonDict jsonDict, string key,
        Func<JsonDict, T> parseFunc, out List<T> values,
        JsonParseOptions options = JsonParseOptions.ThrowInvalidCast)
    {
      if (jsonDict.TryParseValue(key, out List<object> list, options))
      {
        values = list.ConvertJsonList(parseFunc);
        return true;
      }
      else
      {
        values = null;
        return false;
      }
    }

    // Casts the found List to an object List, otherwise returns default (or throws)
    public static List<T> ParseObjectList<T>(this JsonDict jsonDict, string key,
        Func<JsonDict, T> parseFunc,
        JsonParseOptions options = JsonParseOptions.ThrowInvalidCast)
    {
      TryParseObjectList(jsonDict, key, parseFunc, out List<T> values, options);
      return values;
    }

    // Converts the given List of Dictionaries into a list of T, using the converter.
    public static List<T> ConvertJsonList<T>(this List<object> list,
        Func<JsonDict, T> converter)
    {
      return list
          .Select(o => o as JsonDict)
          .Where(dict => dict != null)
          .Select(converter)
          .ToList();
    }

    public static ModelContent ConvertRole(this ModelContent content, string role)
    {
      if (content.Role == role)
      {
        return content;
      }
      else
      {
        return new ModelContent(role, content.Parts);
      }
    }

    public static ModelContent ConvertToUser(this ModelContent content)
    {
      return content.ConvertRole("user");
    }

    public static ModelContent ConvertToModel(this ModelContent content)
    {
      return content.ConvertRole("model");
    }

    public static ModelContent ConvertToSystem(this ModelContent content)
    {
      return content.ConvertRole("system");
    }

    public static void AddIfHasValue<T>(this JsonDict jsonDict, string key,
        T? value) where T : struct
    {
      if (value.HasValue)
      {
        jsonDict.Add(key, value.Value);
      }
    }

    public static void AddIfHasValue<T>(this JsonDict jsonDict, string key,
        T value) where T : class
    {
      if (value != null)
      {
        jsonDict.Add(key, value);
      }
    }
  }

  internal static class AutomatedHelpers
  {
    // Checks the response for automatic functions, and handles calling them.
    // Requires that all the requested functions are automatic.
    public async static Task<List<ModelContent.Part>> HandleAutoFunctionCallsAsync(
        GenerateContentResponse response,
        Dictionary<string, BaseAutoFunctionDeclaration> autoFunctionDeclarations)
    {
      // If we have no auto functions, we can exit early
      if (autoFunctionDeclarations == null || !autoFunctionDeclarations.Any())
      {
        return null;
      }

      // We need to verify we can handle all the requested functions.
      // If not, we pass them all back to the user.
      if (!response.FunctionCalls.Select(fc => fc.Name).All(name => autoFunctionDeclarations.ContainsKey(name)))
      {
        return null;
      }

      List<ModelContent.Part> results = new();
      // We have AutoFunctions for all the calls, so execute each
      foreach (var functionCall in response.FunctionCalls)
      {
        var autoFunction = autoFunctionDeclarations[functionCall.Name];

        try
        {
          var part = await HandleAutoFunctionCallAsync(functionCall, autoFunction);
          results.Add(part);
        }
        catch (Exception e)
        {
          UnityEngine.Debug.LogException(e);
        }
      }

      return results;
    }

    // Handle a specific Function call request with the matching AutoFunctionDeclaration
    private async static Task<ModelContent.FunctionResponsePart> HandleAutoFunctionCallAsync(
        ModelContent.FunctionCallPart functionCall,
        BaseAutoFunctionDeclaration autoFunctionDeclaration)
    {
      // The parameters from the function call are not guaranteed to be in the correct order.
      // So we need to use reflection to get the correct order.
      List<object> args = new();
      foreach (var pInfo in autoFunctionDeclaration.Callable.Method.GetParameters())
      {
        if (functionCall.Args.TryGetValue(pInfo.Name, out var arg))
        {
          // Convert the arg into the approriate type
          object convertedArg = SerializationHelpers.ObjectToType(arg, pInfo.ParameterType);

          args.Add(convertedArg);
        }
        else if (pInfo.HasDefaultValue)
        {
          args.Add(pInfo.DefaultValue);
        }
      }

      var result = autoFunctionDeclaration.Callable.DynamicInvoke(args.ToArray());
      if (result is Task task)
      {
        await task;

        var resultType = result.GetType();
        if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Task<>))
        {
          // Pull the underlying Result from the task.
          result = resultType.GetProperty("Result").GetValue(task);
        }
        else
        {
          // It was a regular Task without a result, so treat it as null
          result = null;
        }
      }

      return new ModelContent.FunctionResponsePart(
          functionCall.Name,
          new Dictionary<string, object> { { "result", result } },
          functionCall.Id);
    }
  }

  internal static class ChatSessionHelpers
  {
    internal static async Task<GenerateContentResponse> SendMessageAsync(
        List<ModelContent> chatHistory,
        Dictionary<string, BaseAutoFunctionDeclaration> autoFunctionDeclarations,
        int autoFunctionTurnLimit,
        IEnumerable<ModelContent> requestContent,
        Func<List<ModelContent>, Task<GenerateContentResponse>> generateContentFunc)
    {
      // Make sure that the request is set to to role "user".
      List<ModelContent> fixedRequests = requestContent.Select(FirebaseAIExtensions.ConvertToUser).ToList();
      // Set up the context to send in the history and request
      List<ModelContent> fullRequest = new(chatHistory);
      fullRequest.AddRange(fixedRequests);

      // Note: GenerateContentAsync can throw exceptions if there was a problem, but
      // we allow it to just be passed back to the user.
      GenerateContentResponse response = await generateContentFunc(fullRequest);

      // If there are any FunctionCalls, we want to resolve the Auto ones.
      int turnsTaken = 0;
      while (response.FunctionCalls.Any() && turnsTaken < autoFunctionTurnLimit)
      {
        turnsTaken++;
        var functionResponses = await AutomatedHelpers.HandleAutoFunctionCallsAsync(response, autoFunctionDeclarations);
        if (functionResponses != null)
        {
          fixedRequests.Add(response.Candidates.First().Content);
          fixedRequests.Add(new ModelContent(functionResponses));

          fullRequest.Clear();
          fullRequest.AddRange(chatHistory);
          fullRequest.AddRange(fixedRequests);

          response = await generateContentFunc(fullRequest);
        }
        else
        {
          break;
        }
      }

      // Only after getting a valid response, add both to the history for later.
      // But either way pass the response along to the user.
      if (response.Candidates.Any())
      {
        ModelContent responseContent = response.Candidates.First().Content;

        chatHistory.AddRange(fixedRequests);
        chatHistory.Add(responseContent.ConvertToModel());
      }

      return response;
    }

    internal static async IAsyncEnumerable<GenerateContentResponse> SendMessageStreamAsync(
        List<ModelContent> chatHistory,
        Dictionary<string, BaseAutoFunctionDeclaration> autoFunctionDeclarations,
        int autoFunctionTurnLimit,
        IEnumerable<ModelContent> requestContent,
        Func<List<ModelContent>, IAsyncEnumerable<GenerateContentResponse>> generateContentStreamFunc)
    {
      // Make sure that the requests are set to to role "user".
      List<ModelContent> fixedRequests = requestContent.Select(FirebaseAIExtensions.ConvertToUser).ToList();
      // Set up the context to send in the request
      List<ModelContent> fullRequest = new(chatHistory);
      fullRequest.AddRange(fixedRequests);

      List<ModelContent> responseContents = new();
      bool saveHistory = true;
      bool autoFunctionCalled = false;
      int autoFunctionTurns = 0;
      do
      {
        autoFunctionTurns++;
        autoFunctionCalled = false;
        // Note: GenerateContentStreamAsync can throw exceptions if there was a problem, but
        // we allow it to just be passed back to the user.
        await foreach (GenerateContentResponse response in generateContentStreamFunc(fullRequest))
        {
          // If the response had a problem, we still want to pass it along to the user for context,
          // but we don't want to save the history anymore.
          if (response.Candidates.Any())
          {
            ModelContent responseContent = response.Candidates.First().Content;
            var content = responseContent.ConvertToModel();
            responseContents.Add(content);
            fullRequest.Add(content);

            // If the response include a Function call, we want to try to automatically call it
            if (response.FunctionCalls.Any())
            {
              var functionResponses = await AutomatedHelpers.HandleAutoFunctionCallsAsync(response, autoFunctionDeclarations);
              if (functionResponses != null)
              {
                // Add the result of the function calls to the request for next time.
                var functionResult = new ModelContent(functionResponses);
                responseContents.Add(functionResult);
                fullRequest.Add(functionResult);
                autoFunctionCalled = true;
                // We don't want to pass the response back to the user.
                continue;
              }
            }
          }
          else
          {
            saveHistory = false;
          }

          yield return response;
        }
        // If an AutoFunction was called, and we aren't past the limit yet, we go again.
      } while (autoFunctionCalled && autoFunctionTurns <= autoFunctionTurnLimit);

      // After getting all the responses, and they were all valid, add everything to the history
      if (saveHistory)
      {
        chatHistory.AddRange(fixedRequests);
        chatHistory.AddRange(responseContents);
      }
    }
  }
}
