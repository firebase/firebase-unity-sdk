/*
 * Copyright 2018 Google LLC
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
using System.Reflection;

namespace Firebase.Platform {

internal sealed class FirebaseEditorDispatcher {

  // Gets the type of UnityEditor.EditorApplication, which is needed for Reflection.
  // We do this via Reflection since the UnityEditor.dll is not normally included.
  static Type EditorApplicationType {
    get {
      // EditorApplication is confirmed present in 4.6 -> 2017.3
      Type editorApplication = Type.GetType("UnityEditor.EditorApplication, UnityEditor");
      return editorApplication;
    }
  }

  // Gets the value of UnityEditor.EditorApplication.isPlaying, via Reflection.
  // This represents being in play mode.
  public static bool EditorIsPlaying {
    get {
      Type editorApplication = EditorApplicationType;
      if (editorApplication != null) {
        // isPlaying is confirmed present in 4.6 -> 2017.3
        var isPlaying = editorApplication.GetProperty("isPlaying");
        if (isPlaying != null) {
          return (bool)isPlaying.GetValue(null, null);
        }
      }
      // If we couldn't find the Editor information, we assume we are not using
      // the Unity editor, and thus are going to be in play mode.
      return true;
    }
  }

  // Gets the value of UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode,
  // via Reflection.
  public static bool EditorIsPlayingOrWillChangePlaymode {
    get {
      Type editorApplication = EditorApplicationType;
      if (editorApplication != null) {
        // isPlayingOrWillChangePlaymode is confirmed present in 4.6 -> 2017.3
        var isPlaying = editorApplication.GetProperty("isPlayingOrWillChangePlaymode");
        if (isPlaying != null) {
          return (bool)isPlaying.GetValue(null, null);
        }
      }
      // If we couldn't find the Editor information, we assume we are not using
      // the Unity editor, and thus are going to be in playmode.
      return true;
    }
  }

  // Subscribes to the event UnityEditor.EditorApplication.update, via Reflection,
  // since UnityEditor.dll may not be included in the build.
  // Note, this assumes it is only called when using the Editor, and it is called
  // from the main Unity thread.
  public static void StartEditorUpdate() {
    Type editorApplication = EditorApplicationType;
    if (editorApplication != null) {
      // For some reason, update is considered a field when using reflection,
      // instead of an event. As such, it needs to be cast to a delegate, and
      // manipulated manually, instead of using event reflection.
      // update is confirmed present as such from 4.6 -> 2017.3
      AddRemoveCallbackToField(editorApplication.GetField("update"), Update,
          errorMessage: "Firebase failed to register for editor update calls. " +
                        "Most Firebase features will fail, as callbacks will not" +
                        "work properly. This is caused by being unable to resolve" +
                        "the necessary fields from the UnityEditor.dll.");
    }
  }

  // Unsubscribes from the event UnityEditor.EditorApplication.update,
  // via Reflection.
  public static void StopEditorUpdate() {
    Type editorApplication = EditorApplicationType;
    if (editorApplication != null) {
      // For some reason, update uses a field, not an event.
      // update is confirmed present as such from 4.6 -> 2017.3
      AddRemoveCallbackToField(editorApplication.GetField("update"),
                               Update, add: false);
    }
  }

  public static void Update() {
    FirebaseHandler.DefaultInstance.Update();
  }

  // Subscribes to the event UnityEditor.EditorApplication.playmodeStateChanged,
  // via Reflection. If false is provided, unsubscribes instead.
  public static void ListenToPlayState(bool start = true) {
    Type editorApplication = EditorApplicationType;
    if (editorApplication != null) {
      // First, check for the version introduced in 2017, that uses an event with an argument,
      // and has "playMode" in camelcase.
      var stateEvent = editorApplication.GetEvent("playModeStateChanged");
      if (stateEvent != null) {
        // The event playModeStateChanged is of type Action<PlayModeStateChange>.
        // Since PlayModeStateChange is an enum defined in UnityEditor, we need to use
        // reflection to get that type, then we need to use a callback function that
        // uses a generic argument, that can be set to be the reflected enum type.
        var playModeEnum = Type.GetType("UnityEditor.PlayModeStateChange, UnityEditor");
        if (playModeEnum != null) {
          // Get the generic version of the callback, and set the type to the enum.
          var methodInfo = typeof(FirebaseEditorDispatcher).GetMethod(
            "PlayModeStateChangedWithArg", BindingFlags.NonPublic | BindingFlags.Static);
          methodInfo = methodInfo.MakeGenericMethod(playModeEnum);
          Delegate toAdd = Delegate.CreateDelegate(stateEvent.EventHandlerType, null, methodInfo);
          if (start) {
            stateEvent.AddEventHandler(null, toAdd);
          } else {
            stateEvent.RemoveEventHandler(null, toAdd);
          }
          return;
        }
      }

      // Otherwise, we likely need to use the version of the event used in 5.6 and older,
      // which is defined as a field, doesn't take an argument, and has "playmode" in lowercase.
      AddRemoveCallbackToField(editorApplication.GetField("playmodeStateChanged"),
                               PlayModeStateChanged, add: start);
    }
  }

  private static void PlayModeStateChanged() {
    if (!FirebaseHandler.DefaultInstance.IsPlayMode && EditorIsPlaying) {
      // If we have entered Play mode, start up the monobehaviour.
      StopEditorUpdate();
      FirebaseHandler.DefaultInstance.StartMonoBehaviour();
      FirebaseHandler.DefaultInstance.IsPlayMode = true;
    } else if (FirebaseHandler.DefaultInstance.IsPlayMode &&
               !EditorIsPlayingOrWillChangePlaymode) {
      // If the editor is about to leave play mode, we need to tear down the
      // monobehaviour, and start following editor updates.
      FirebaseHandler.DefaultInstance.StopMonoBehaviour();
      StartEditorUpdate();
      FirebaseHandler.DefaultInstance.IsPlayMode = false;
    }
  }

  // Function to subscribe with when using newer (2017+) versions of Unity, as
  // they take in a UnityEditor defined enum parameter.
  private static void PlayModeStateChangedWithArg<T>(T t) {
    PlayModeStateChanged();
  }

  // Adds or removes the given action to the given field. Used for managing
  // callbacks via reflection. If an error message is provided, it will be logged
  // if it fails to modify the field.
  static void AddRemoveCallbackToField(FieldInfo eventField, Action callback,
                                       object target = null, bool add = true,
                                       string errorMessage = null) {
    if (eventField != null) {
      Delegate oldEvent = eventField.GetValue(null) as Delegate;
      if (add) {
        Delegate toAdd = Delegate.CreateDelegate(eventField.FieldType, target,
                                                 callback.Method);
        if (oldEvent != null) {
          eventField.SetValue(null, Delegate.Combine(oldEvent, toAdd));
        } else {
          eventField.SetValue(null, toAdd);
        }
        return;
      } else if (oldEvent != null) {
        Delegate toRemove = Delegate.CreateDelegate(eventField.FieldType, target,
                                                    callback.Method);
        eventField.SetValue(null, Delegate.Remove(oldEvent, toRemove));
        return;
      }
    }

    if (!string.IsNullOrEmpty(errorMessage)) {
      FirebaseLogger.LogMessage(PlatformLogLevel.Error, errorMessage);
    }
  }

  // Note: This should not assume that it is only run in the Unity Editor.
  public static void Terminate(bool isPlayMode) {
    ListenToPlayState(false);
    if (!isPlayMode) {
      FirebaseEditorDispatcher.StopEditorUpdate();
    }
  }
}

}  // namespace Firebase.Platform
