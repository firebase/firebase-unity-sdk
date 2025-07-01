/*
 * Copyright 2024 Google LLC
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

namespace Firebase.Messaging {

/// @brief A class to configure the behavior of Firebase Cloud Messaging.
/// 
/// This class contains various configuration options that control some of
/// Firebase Cloud Messaging's behavior.
public sealed class MessagingOptions {
  public MessagingOptions() { }

  public bool SuppressNotificationPermissionPrompt {
    get; set;
  }
}

}  // namespace Firebase.Messaging
