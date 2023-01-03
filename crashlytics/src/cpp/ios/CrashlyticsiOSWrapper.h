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
//
//  CrashlyticsWrapper.h
//  CrashlyticsWrapper
//

#pragma once

#import <Foundation/Foundation.h>

__BEGIN_DECLS


typedef struct Frame {
  const char *library;
  const char *symbol;
  const char *fileName;
  const char *lineNumber;
} Frame;


// This method can be used to record a single exception structure in a report.
// This is particularly useful when your code interacts with non-native
// languages like Lua, C#, or Javascript. This call can be expensive and should
// only be used shortly before process termination. This API is not intended be
// to used to log NSException objects. All safely-reportable NSExceptions are
// automatically captured by Crashlytics.
void CLURecordCustomException(const char *name, const char *reason,
                              Frame *frames, int frameCount, bool isOnDemand);

// Returns true when the Crashlytics SDK is initialized.
bool CLUIsInitialized();

#pragma mark Log, key-value, user info

// Set a custom attributes to be sent up with a crash.
void CLUSetKeyValue(const char *key, const char *value);

// provides as easy way to gather more information in your log messages that are
// sent with your crash data. CLS_LOG prepends your custom log message with the
// function name and line number where the macro was used.
void CLULog(const char *msg);

// Specify a user identifier which will be visible in the Crashlytics UI.
void CLUSetUserIdentifier(const char *identifier);

// Get whether data collection is enabled
bool CLUIsCrashlyticsCollectionEnabled();

// Set whether data collection is enabled.
void CLUSetCrashlyticsCollectionEnabled(bool enabled);

__END_DECLS
