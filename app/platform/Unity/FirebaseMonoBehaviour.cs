/*
 * Copyright 2017 Google LLC
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

namespace Firebase.Platform {

// Behavior that controls the lifecycle of Firebase in a Unity application.
[UnityEngine.Scripting.Preserve]
internal sealed class FirebaseMonoBehaviour : UnityEngine.MonoBehaviour {

  // If the FirebaseHandler was destroyed then it's likely this DLL was reloaded in the app domain
  // so tear down the game object that owns this behavior.  Returns reference to the FirebaseHandler
  // if the game object was *not* destroyed, null otherwise.
  FirebaseHandler GetFirebaseHandlerOrDestroyGameObject() {
    var handler = FirebaseHandler.DefaultInstance;
    if (handler == null) UnityEngine.Object.Destroy(gameObject);
    return handler;
  }

  // When this behavior is enabled, destroy the game object if the handler has been destroyed.
  [UnityEngine.Scripting.Preserve]
  void OnEnable() {
    GetFirebaseHandlerOrDestroyGameObject();
  }

  // Poll the callback queue, executing any present.
  [UnityEngine.Scripting.Preserve]
  void Update() {
    var handler = GetFirebaseHandlerOrDestroyGameObject();
    if (handler != null) {
      PlatformInformation.RealtimeSinceStartupSafe =
        PlatformInformation.RealtimeSinceStartup;
      handler.Update();
    }
  }

  // Notify ApplicationFocusChanged event handlers.
  [UnityEngine.Scripting.Preserve]
  void OnApplicationFocus(bool hasFocus) {
    var handler = GetFirebaseHandlerOrDestroyGameObject();
    if (handler != null) handler.OnApplicationFocus(hasFocus);
  }

  void OnDestroy() {
    // Tell the FirebaseHandler that the MonoBehaviour was destroyed, so it
    // doesn't try to use it.
    FirebaseHandler.OnMonoBehaviourDestroyed(this);
  }
}

}
