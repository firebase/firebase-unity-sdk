/*
 * Copyright 2016 Google LLC
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

package com.google.firebase;

import android.content.Intent;
import android.os.Bundle;
import com.google.firebase.messaging.MessageForwardingService;
import com.unity3d.player.UnityPlayerActivity;

/**
 * MessagingUnityPlayerActivity is a UnityPlayerActivity that updates its intent when new intents
 * are sent to it.
 *
 * This is a workaround for a known issue that prevents Firebase Cloud Messaging from responding to
 * data payloads when both a data and notification payload are sent to the app while it is in the
 * background.
 */
public class MessagingUnityPlayerActivity extends UnityPlayerActivity {
  // The key in the intent's extras that maps to the incoming message's message ID. Only sent by
  // the server, GmsCore sends EXTRA_MESSAGE_ID_KEY below. Server can't send that as it would get
  // stripped by the client.
  private static final String EXTRA_MESSAGE_ID_KEY_SERVER = "message_id";

  // An alternate key value in the intent's extras that also maps to the incoming message's message
  // ID. Used by upstream, and set by GmsCore.
  private static final String EXTRA_MESSAGE_ID_KEY = "google.message_id";

  // The key in the intent's extras that maps to the incoming message's sender value.
  private static final String EXTRA_FROM = "google.message_id";

  /**
   * Workaround for when a message is sent containing both a Data and Notification payload.
   *
   * <p>When the app is in the background, if a message with both a data and notification payload is
   * received the data payload is stored on the Intent passed to onNewIntent. By default, that
   * intent does not get set as the Intent that started the app, so when the app comes back online
   * it doesn't see a new FCM message to respond to. As a workaround, we override onNewIntent so
   * that it sends the intent to the MessageForwardingService which forwards the message to the
   * FirebaseMessagingService which in turn sends the message to the application.
   */
  @Override
  protected void onNewIntent(Intent intent) {
    super.onNewIntent(intent);

    // If we do not have a 'from' field this intent was not a message and should not be handled. It
    // probably means this intent was fired by tapping on the app icon.
    Bundle extras = intent.getExtras();
    if (extras == null) {
      return;
    }
    String from = extras.getString(EXTRA_FROM);
    String messageId = extras.getString(EXTRA_MESSAGE_ID_KEY);
    if (messageId == null) {
      messageId = extras.getString(EXTRA_MESSAGE_ID_KEY_SERVER);
    }
    if (from != null && messageId != null) {
      Intent message = new Intent(this, MessageForwardingService.class);
      message.setAction(MessageForwardingService.ACTION_REMOTE_INTENT);
      message.putExtras(intent);
      message.setData(intent.getData());
      MessageForwardingService.enqueueWork(this, message);
    }
    setIntent(intent);
  }

  /**
   * Dispose of the mUnityPlayer when restarting the app.
   *
   * <p>This ensures that when the app starts up again it does not start with stale data.
   */
  @Override
  protected void onCreate(Bundle savedInstanceState) {
    if (mUnityPlayer != null) {
      mUnityPlayer.quit();
      mUnityPlayer = null;
    }
    super.onCreate(savedInstanceState);
  }
}
