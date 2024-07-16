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

using System.Collections.Generic;

namespace Firebase.Messaging {

/// @brief Data structure used to send messages to, and receive messages from,
/// cloud messaging.
public sealed class FirebaseMessage {

  internal static FirebaseMessage FromInternal(FirebaseMessageInternal other) {
    if (other == null) return null;

    FirebaseMessage message = new FirebaseMessage();
    message.CollapseKey = other.collapse_key;
    // Make a copy of the dictionary, to not rely on the C++ memory lasting around
    message.Data = new Dictionary<string, string>(other.data);
    message.Error = other.error;
    message.ErrorDescription = other.error_description;
    message.From = other.from;
    message.Link = Firebase.FirebaseApp.UrlStringToUri(other.link);
    message.MessageId = other.message_id;
    message.MessageType = other.message_type;
    message.Notification = FirebaseNotification.FromInternal(other.notification);
    message.NotificationOpened = other.notification_opened;
    message.Priority = other.priority;
    // Make a copy of the array, to not rely on the C++ memory lasting around
    message.RawData = new byte[other.raw_data.Count];
    other.raw_data.CopyTo(message.RawData);
    message.TimeToLive = System.TimeSpan.FromSeconds(other.time_to_live);
    message.To = other.to;
    return message;
  }

  /// Gets the collapse key used for collapsible messages.
  public string CollapseKey { get; private set; }

  /// Gets or sets the metadata, including all original key/value pairs.
  /// Includes some of the HTTP headers used when sending the message. `gcm`,
  /// `google` and `goog` prefixes are reserved for internal use.
  public System.Collections.Generic.IDictionary<string, string> Data { get; private set; }

  /// Gets the error code. Used in "nack" messages for CCS, and in responses
  /// from the server.
  /// See the CCS specification for the externally-supported list.
  public string Error { get; private set; }

  /// Gets the human readable details about the error.
  public string ErrorDescription { get; private set; }

  /// Gets the authenticated ID of the sender. This is a project number in most cases.
  public string From { get; private set; }

  /// The link into the app from the message.
  public System.Uri Link { get; private set; }

  /// Gets or sets the message ID. This can be specified by sender. Internally a
  /// hash of the message ID and other elements will be used for storage. The ID
  /// must be unique for each topic subscription - using the same ID may result
  /// in overriding the original message or duplicate delivery.
  public string MessageId { get; private set; }

  /// Gets the message type, equivalent with a content-type.
  /// CCS uses "ack", "nack" for flow control and error handling.
  /// "control" is used by CCS for connection control.
  public string MessageType { get; private set; }

  /// Optional notification to show. This only set if a notification was
  /// received with this message, otherwise it is null.
  public FirebaseNotification Notification { get; private set; }

  /// Gets a flag indicating whether this message was opened by tapping a
  /// notification in the OS system tray. If the message was received this way
  /// this flag is set to true.
  public bool NotificationOpened { get; private set; }

  /// Gets the priority level. Defined values are "normal" and "high".
  /// By default messages are sent with normal priority.
  public string Priority { get; private set; }

  /// Gets the binary payload. For webpush and non-json messages, this is the
  /// body of the request entity.
  public byte[] RawData { get; private set; }

  /// The Time To Live (TTL) for the message.
  public System.TimeSpan TimeToLive { get; private set; }

  /// Gets or sets recipient of a message.
  ///
  /// For example it can be a registration token, a topic name, a IID or project
  /// ID.
  public string To { get; private set; }

  /// @deprecated No longer needed, will be removed in the future.
  [System.Obsolete("No longer needed, will be removed in the future.")]
  public void Dispose() { }

  /// @deprecated No longer needed, will be removed in the future.
  [System.Obsolete("No longer needed, will be removed in the future.")]
  public void Dispose(bool disposing) { }
}

}  // namespace Firebase.Messaging
