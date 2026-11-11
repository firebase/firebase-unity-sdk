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

using System;
using System.Collections.Generic;

namespace Firebase.Messaging {

/// @brief Data structure used to send messages to, and receive messages from,
/// cloud messaging.
public sealed class FirebaseMessage {
  public FirebaseMessage() { }
  internal static Uri ParseLinkUri(string s) => !string.IsNullOrEmpty(s) && Uri.TryCreate(s, UriKind.Absolute, out Uri u) ? u : null;

  /// Gets the collapse key used for collapsible messages.
  public string CollapseKey { get; internal set; } = string.Empty;

  /// Gets or sets the metadata, including all original key/value pairs.
  /// Includes some of the HTTP headers used when sending the message. `gcm`,
  /// `google` and `goog` prefixes are reserved for internal use.
  public IDictionary<string, string> Data { get; internal set; } = new Dictionary<string, string>(StringComparer.Ordinal);

  /// Gets the error code. Used in "nack" messages for CCS, and in responses
  /// from the server.
  /// See the CCS specification for the externally-supported list.
  public string Error { get; internal set; } = string.Empty;

  /// Gets the human readable details about the error.
  public string ErrorDescription { get; internal set; } = string.Empty;

  /// Gets the authenticated ID of the sender. This is a project number in most cases.
  public string From { get; internal set; } = string.Empty;

  /// The link into the app from the message.
  public Uri Link { get; internal set; }

  /// Gets or sets the message ID. This can be specified by sender. Internally a
  /// hash of the message ID and other elements will be used for storage. The ID
  /// must be unique for each topic subscription - using the same ID may result
  /// in overriding the original message or duplicate delivery.
  public string MessageId { get; internal set; } = string.Empty;

  /// Gets the message type, equivalent with a content-type.
  /// CCS uses "ack", "nack" for flow control and error handling.
  /// "control" is used by CCS for connection control.
  public string MessageType { get; internal set; } = string.Empty;

  /// Optional notification to show. This only set if a notification was
  /// received with this message, otherwise it is null.
  public FirebaseNotification Notification { get; internal set; }

  /// Gets a flag indicating whether this message was opened by tapping a
  /// notification in the OS system tray. If the message was received this way
  /// this flag is set to true.
  public bool NotificationOpened { get; internal set; }

  /// Gets the priority level. Defined values are "normal" and "high".
  /// By default messages are sent with normal priority.
  public string Priority { get; internal set; } = string.Empty;

  /// Gets the binary payload. For webpush and non-json messages, this is the
  /// body of the request entity.
  public byte[] RawData { get; internal set; } = Array.Empty<byte>();

  /// The Time To Live (TTL) for the message.
  public TimeSpan TimeToLive { get; internal set; }

  /// Gets or sets recipient of a message.
  ///
  /// For example it can be a registration token, a topic name, a IID or project
  /// ID.
  public string To { get; internal set; } = string.Empty;
}

}  // namespace Firebase.Messaging
