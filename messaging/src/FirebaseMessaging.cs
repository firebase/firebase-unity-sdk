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

/// @brief Firebase Cloud Messaging API.
///
/// Firebase Cloud Messaging allows you to send data from your server to your
/// users' devices, and receive messages from devices on the same connection
/// if you're using a XMPP server.
///
/// The FCM service handles all aspects of queueing of messages and delivery
/// to client applications running on target devices.
public static class FirebaseMessaging {

  /// Enable or disable token registration during initialization of Firebase
  /// Cloud Messaging.
  ///
  /// This token is what identifies the user to Firebase, so disabling this
  /// avoids creating any new identity and automatically sending it to Firebase,
  /// unless consent has been granted.
  ///
  /// If this setting is enabled, it triggers the token registration refresh
  /// immediately. This setting is persisted across app restarts and overrides
  /// the setting "firebase_messaging_auto_init_enabled" specified in your
  /// Android manifest (on Android) or Info.plist (on iOS and tvOS).
  ///
  /// <p>By default, token registration during initialization is enabled.
  ///
  /// The registration happens before you can programmatically disable it, so
  /// if you need to change the default, (for example, because you want to
  /// prompt the user before FCM generates/refreshes a registration token on app
  /// startup), add to your application’s manifest:
  ///
  /// @if NOT_DOXYGEN
  ///   <meta-data android:name="firebase_messaging_auto_init_enabled"
  ///   android:value="false" />
  /// @else
  /// @code
  ///   &lt;meta-data android:name="firebase_messaging_auto_init_enabled"
  ///   android:value="false" /&gt;
  /// @endcode
  /// @endif
  ///
  /// or on iOS or tvOS to your Info.plist:
  ///
  /// @if NOT_DOXYGEN
  ///   <key>FirebaseMessagingAutoInitEnabled</key>
  ///   <false/>
  /// @else
  /// @code
  ///   &lt;key&gt;FirebaseMessagingAutoInitEnabled&lt;/key&gt;
  ///   &lt;false/&gt;
  /// @endcode
  /// @endif
  public static bool TokenRegistrationOnInitEnabled {
    get {
      return FirebaseMessagingInternal.IsTokenRegistrationOnInitEnabled();
    }
    set {
      FirebaseMessagingInternal.SetTokenRegistrationOnInitEnabled(value);
    }
  }

  /// Enables or disables Firebase Cloud Messaging message delivery metrics
  /// export to BigQuery.
  ///
  /// By default, message delivery metrics are not exported to BigQuery. Use
  /// this method to enable or disable the export at runtime. In addition, you
  /// can enable the export by adding to your manifest. Note that the run-time
  /// method call will override the manifest value.
  ///
  /// @code
  /// <meta-data android:name= "delivery_metrics_exported_to_big_query_enabled"
  ///            android:value="true"/>
  /// @endcode
  ///
  /// @note This function is currently only implemented on Android, and has no
  /// behavior on other platforms.
  public static bool DeliveryMetricsExportedToBigQueryEnabled {
    get {
      return FirebaseMessagingInternal.DeliveryMetricsExportToBigQueryEnabled();
    }
    set {
      FirebaseMessagingInternal.SetDeliveryMetricsExportToBigQuery(value);
    }
  }

  /// @brief This creates a Firebase Installations ID, if one does not exist, and
  /// sends information about the application and the device where it's running to
  /// the Firebase backend.
  ///
  /// @return A task with the token.
  public static System.Threading.Tasks.Task<string> GetTokenAsync() {
    return FirebaseMessagingInternal.GetTokenAsync();
  }

  /// @brief Deletes the default token for this Firebase project.
  ///
  /// Note that this does not delete the Firebase Installations ID that may have
  /// been created when generating the token. See Installations.Delete() for
  /// deleting that.
  ///
  /// @return A task that completes when the token is deleted.
  public static System.Threading.Tasks.Task DeleteTokenAsync() {
    return FirebaseMessagingInternal.DeleteTokenAsync();
  }

#if DOXYGEN
  /// Called on the client when a message arrives.
  public static event System.EventHandler<MessageReceivedEventArgs> MessageReceived;
#else
  public static event System.EventHandler<MessageReceivedEventArgs> MessageReceived {
    add {
      FirebaseMessagingInternal.MessageReceived += value;
    }
    remove {
      FirebaseMessagingInternal.MessageReceived -= value;
    }
  }
#endif  // DOXYGEN

#if DOXYGEN
  /// Called on the client when a registration token message arrives.
  public static event System.EventHandler<TokenReceivedEventArgs> TokenReceived;
#else
  public static event System.EventHandler<TokenReceivedEventArgs> TokenReceived {
    add {
      FirebaseMessagingInternal.TokenReceived += value;
    }
    remove {
      FirebaseMessagingInternal.TokenReceived -= value;
    }
  }
#endif

  /// @brief Displays a prompt to the user requesting permission to display
  ///        notifications.
  ///
  /// The permission prompt only appears on iOS and tvOS. If the user has
  /// already agreed to allow notifications, no prompt is displayed and the
  /// returned future is completed immediately.
  ///
  /// @return A Task that completes when the notification prompt has been
  ///         dismissed.
  public static System.Threading.Tasks.Task RequestPermissionAsync() {
    return FirebaseMessagingInternal.RequestPermissionAsync();
  }

  /// @brief Subscribe to receive all messages to the specified topic.
  ///
  /// Subscribes an app instance to a topic, enabling it to receive messages
  /// sent to that topic.
  ///
  /// @param[in] topic The name of the topic to subscribe. Must match the
  ///            following regular expression: `[a-zA-Z0-9-_.~%]{1,900}`.
  public static System.Threading.Tasks.Task SubscribeAsync(string topic) {
    return FirebaseMessagingInternal.SubscribeAsync(topic);
  }

  /// @brief Unsubscribe from a topic.
  ///
  /// Unsubscribes an app instance from a topic, stopping it from receiving
  /// any further messages sent to that topic.
  ///
  /// @param[in] topic The name of the topic to unsubscribe from. Must match the
  ///            following regular expression: `[a-zA-Z0-9-_.~%]{1,900}`.
  public static System.Threading.Tasks.Task UnsubscribeAsync(string topic) {
    return FirebaseMessagingInternal.UnsubscribeAsync(topic);
  }
}

}  // namespace Firebase.Messaging
