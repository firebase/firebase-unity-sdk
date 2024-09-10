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

using System.Threading.Tasks;

namespace Firebase.Analytics {

public static partial class FirebaseAnalytics {

  /// Get the instance ID from the analytics service.
  ///
  /// @returns A Task with the Analytics instance ID.
  public static System.Threading.Tasks.Task<string> GetAnalyticsInstanceIdAsync() {
    return FirebaseAnalyticsInternal.GetAnalyticsInstanceIdAsync();
  }

  /// Asynchronously retrieves the identifier of the current app
  /// session.
  ///
  /// The session ID retrieval could fail due to Analytics collection
  /// disabled, or if the app session was expired.
  ///
  /// @returns A Task with the identifier of the current app session.
    public static System.Threading.Tasks.Task<long> GetSessionIdAsync() {
    return FirebaseAnalyticsInternal.GetSessionIdAsync();
  }

  /// Initiates on-device conversion measurement given a user email address on iOS
  /// and tvOS (no-op on Android). On iOS and tvOS, this method requires the
  /// dependency GoogleAppMeasurementOnDeviceConversion to be linked in,
  /// otherwise the invocation results in a no-op.
  ///
  /// @param emailAddress User email address. Include a domain name for all
  ///   email addresses (e.g. gmail.com or hotmail.co.jp).
  public static void InitiateOnDeviceConversionMeasurementWithEmailAddress(string emailAddress) {
    FirebaseAnalyticsInternal.InitiateOnDeviceConversionMeasurementWithEmailAddress(emailAddress);
  }

  /// Initiates on-device conversion measurement given a sha256-hashed user email address. 
  /// Requires dependency GoogleAppMeasurementOnDeviceConversion to be linked in, otherwise it is
  /// a no-op.
  ///
  /// @param hashedEmailAddress User email address as a UTF8-encoded string normalized and 
  ///   hashed according to the instructions at 
  ///   https://firebase.google.com/docs/tutorials/ads-ios-on-device-measurement/step-3.
  public static void InitiateOnDeviceConversionMeasurementWithHashedEmailAddress(byte[] hashedEmailAddress) {
    FirebaseAnalyticsInternal.InitiateOnDeviceConversionMeasurementWithHashedEmailAddress(new CharVector(hashedEmailAddress));
  }

  /// Initiates on-device conversion measurement given a sha256-hashed phone number in E.164 
  /// format. Requires dependency GoogleAppMeasurementOnDeviceConversion to be linked in, 
  /// otherwise it is a no-op.
  ///
  /// @param hashedPhoneNumber UTF8-encoded user phone number in E.164 format and then hashed 
  ///   according to the instructions at 
  ///   https://firebase.google.com/docs/tutorials/ads-ios-on-device-measurement/step-3.
  public static void InitiateOnDeviceConversionMeasurementWithHashedPhoneNumber(byte[] hashedPhoneNumber) {
    FirebaseAnalyticsInternal.InitiateOnDeviceConversionMeasurementWithHashedPhoneNumber(new CharVector(hashedPhoneNumber));
  }

  /// Initiates on-device conversion measurement given a phone number in E.164
  /// format on iOS (no-op on Android). On iOS, requires dependency
  /// GoogleAppMeasurementOnDeviceConversion to be linked in, otherwise it is a
  /// no-op.
  ///
  /// @param phoneNumber User phone number. Must be in E.164 format, which means
  /// it must be
  ///   limited to a maximum of 15 digits and must include a plus sign (+) prefix
  ///   and country code with no dashes, parentheses, or spaces.
  public static void InitiateOnDeviceConversionMeasurementWithPhoneNumber(string phoneNumber) {
    FirebaseAnalyticsInternal.InitiateOnDeviceConversionMeasurementWithPhoneNumber(phoneNumber);
  }

  /// @brief Log an event with one string parameter.
  ///
  /// @param[in] name Name of the event to log. Should contain 1 to 40
  /// alphanumeric characters or underscores. The name must start with an
  /// alphabetic character. Some event names are reserved.
  /// See the FirebaseAnalytics.Event properties for the list of reserved event
  /// names.
  /// The "firebase_" prefix is reserved and should not be used. Note that event
  /// names are case-sensitive and that logging two events whose names differ
  /// only in case will result in two distinct events.
  ///
  /// @param[in] parameterName Name of the parameter to log.
  /// For more information, see @ref Parameter.
  ///
  /// @param[in] parameterValue Value of the parameter to log.
  ///
  /// @see LogEvent(string, Parameter[])
  public static void LogEvent(string name, string parameterName, string parameterValue) {
    FirebaseAnalyticsInternal.LogEvent(name, parameterName, parameterValue);
  }

  /// @brief Log an event with one float parameter.
  ///
  /// @param[in] name Name of the event to log. Should contain 1 to 40
  /// alphanumeric characters or underscores. The name must start with an
  /// alphabetic character. Some event names are reserved.
  /// See the FirebaseAnalytics.Event properties for the list of reserved event
  /// names.
  /// The "firebase_" prefix is reserved and should not be used. Note that event
  /// names are case-sensitive and that logging two events whose names differ
  /// only in case will result in two distinct events.
  ///
  /// @param[in] parameterName Name of the parameter to log.
  /// For more information, see @ref Parameter.
  ///
  /// @param[in] parameterValue Value of the parameter to log.
  ///
  /// @see LogEvent(string, Parameter[])
  public static void LogEvent(string name, string parameterName, double parameterValue) {
    FirebaseAnalyticsInternal.LogEvent(name, parameterName, parameterValue);
  }

  /// @brief Log an event with one 64-bit integer parameter.
  ///
  /// @param[in] name Name of the event to log. Should contain 1 to 40
  /// alphanumeric characters or underscores. The name must start with an
  /// alphabetic character. Some event names are reserved.
  /// See the FirebaseAnalytics.Event properties for the list of reserved event
  /// names.
  /// The "firebase_" prefix is reserved and should not be used. Note that event
  /// names are case-sensitive and that logging two events whose names differ
  /// only in case will result in two distinct events.
  ///
  /// @param[in] parameterName Name of the parameter to log.
  /// For more information, see @ref Parameter.
  ///
  /// @param[in] parameterValue Value of the parameter to log.
  ///
  /// @see LogEvent(string, Parameter[])
  public static void LogEvent(string name, string parameterName, long parameterValue) {
    FirebaseAnalyticsInternal.LogEvent(name, parameterName, parameterValue);
  }

  /// @brief Log an event with one integer parameter
  /// (stored as a 64-bit integer).
  ///
  /// @param[in] name Name of the event to log. Should contain 1 to 40
  /// alphanumeric characters or underscores. The name must start with an
  /// alphabetic character. Some event names are reserved.
  /// See the FirebaseAnalytics.Event properties for the list of reserved event
  /// names.
  /// The "firebase_" prefix is reserved and should not be used. Note that event
  /// names are case-sensitive and that logging two events whose names differ
  /// only in case will result in two distinct events.
  ///
  /// @param[in] parameterName Name of the parameter to log.
  /// For more information, see @ref Parameter.
  ///
  /// @param[in] parameterValue Value of the parameter to log.
  ///
  /// @see LogEvent(string, Parameter[])
  public static void LogEvent(string name, string parameterName, int parameterValue) {
    FirebaseAnalyticsInternal.LogEvent(name, parameterName, parameterValue);
  }

  /// @brief Log an event with no parameters.
  ///
  /// @param[in] name Name of the event to log. Should contain 1 to 40
  /// alphanumeric characters or underscores. The name must start with an
  /// alphabetic character. Some event names are reserved.
  /// See the FirebaseAnalytics.Event properties for the list of reserved event
  /// names.
  /// The "firebase_" prefix is reserved and should not be used. Note that event
  /// names are case-sensitive and that logging two events whose names differ
  /// only in case will result in two distinct events.
  ///
  /// @see LogEvent(string, Parameter[])
  public static void LogEvent(string name) {
    FirebaseAnalyticsInternal.LogEvent(name);
  }

  /// @brief Log an event with associated parameters.
  ///
  /// An Event is an important occurrence in your app that you want to measure.
  /// You can report up to 500 different types of events per app and you can
  /// associate up to 25 unique parameters with each Event type.
  ///
  /// Some common events are in the reference guide via the
  /// FirebaseAnalytics.Event* constants, but you may also choose to specify
  /// custom event types that are associated with your specific app.
  ///
  /// @param[in] name Name of the event to log. Should contain 1 to 40
  /// alphanumeric characters or underscores. The name must start with an
  /// alphabetic character. Some event names are reserved.
  /// See the FirebaseAnalytics.Event properties for the list of reserved event
  /// names.
  /// The "firebase_" prefix is reserved and should not be used. Note that event
  /// names are case-sensitive and that logging two events whose names differ
  /// only in case will result in two distinct events.
  ///
  /// @param[in] parameters A parameter array of `Parameter` instances.
  public static void LogEvent(string name, params Parameter[] parameters) {
    // Convert the Parameter array into a StringList and VariantList to pass to C++
    StringList parameterNames = new StringList();
    VariantList parameterValues = new VariantList();

    foreach (Parameter p in parameters) {
      parameterNames.Add(p.Name);
      parameterValues.Add(Firebase.Variant.FromObject(p.Value));
    }

    FirebaseAnalyticsInternal.LogEvent(name, parameterNames, parameterValues);
  }

  /// Clears all analytics data for this app from the device and resets the app
  /// instance id.
  public static void ResetAnalyticsData() {
    FirebaseAnalyticsInternal.ResetAnalyticsData();
  }

  /// @brief Sets whether analytics collection is enabled for this app on this
  /// device.
  ///
  /// This setting is persisted across app sessions. By default it is enabled.
  ///
  /// @param[in] enabled true to enable analytics collection, false to disable.
  public static void SetAnalyticsCollectionEnabled(bool enabled) {
    FirebaseAnalyticsInternal.SetAnalyticsCollectionEnabled(enabled);
  }

  /// @brief Sets the applicable end user consent state (e.g., for device
  /// identifiers) for this app on this device.
  ///
  /// Use the consent map to specify individual consent type values. Settings are
  /// persisted across app sessions. By default consent types are set to
  /// "granted".
  public static void SetConsent(System.Collections.Generic.IDictionary< ConsentType, ConsentStatus > consentSettings) {
    IntIntMap consentSettingsMap = new IntIntMap();
    foreach (var kv in consentSettings) {
      consentSettingsMap[(int)kv.Key] = (int)kv.Value;
    }
    FirebaseAnalyticsInternal.SetConsentWithInts(consentSettingsMap);
  }

  /// @brief Sets the duration of inactivity that terminates the current session.
  ///
  /// @note The default value is 30 minutes.
  ///
  /// @param timeSpan The duration of inactivity that terminates the current
  /// session.
  public static void SetSessionTimeoutDuration(System.TimeSpan timeSpan) {
    FirebaseAnalyticsInternal.SetSessionTimeoutDuration((long)timeSpan.TotalMilliseconds);
  }

  /// @brief Sets the user ID property.
  ///
  /// This feature must be used in accordance with
  /// <a href="https://www.google.com/policies/privacy">Google's Privacy
  /// Policy</a>
  ///
  /// @param[in] userId The user ID associated with the user of this app on this
  /// device.  The user ID must be non-empty and no more than 256 characters long.
  /// Setting userId to null removes the user ID.
  public static void SetUserId(string userId) {
    FirebaseAnalyticsInternal.SetUserId(userId);
  }

  /// @brief Set a user property to the given value.
  ///
  /// Properties associated with a user allow a developer to segment users
  /// into groups that are useful to their application.  Up to 25 properties
  /// can be associated with a user.
  ///
  /// Suggested property names are listed @ref user_property_names
  /// (%user_property_names.h) but you're not limited to this set. For example,
  /// the "gamertype" property could be used to store the type of player where
  /// a range of values could be "casual", "mid_core", or "core".
  ///
  /// @param[in] name Name of the user property to set.  This must be a
  /// combination of letters and digits (matching the regular expression
  /// [a-zA-Z0-9] between 1 and 40 characters long starting with a letter
  /// [a-zA-Z] character.
  /// @param[in] property Value to set the user property to.  Set this
  /// argument to NULL or nullptr to remove the user property.  The value can be
  /// between 1 to 100 characters long.
  public static void SetUserProperty(string name, string property) {
    FirebaseAnalyticsInternal.SetUserProperty(name, property);
  }
}

}
