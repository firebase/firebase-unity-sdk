/*
 * Copyright 2020 Google LLC
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
//  CrashlyticsWrapper.m
//  CrashlyticsWrapper
//

#import "CrashlyticsiOSWrapper.h"
#import "FirebaseCrashlytics.h"
#import "./Crashlytics_PrivateHeaders/Crashlytics_Platform.h"
#import "./Crashlytics_PrivateHeaders/ExceptionModel_Platform.h"

@interface FIRCrashlytics ()
- (BOOL)isCrashlyticsStarted;
@end

#pragma mark Helper methods

// Boxing or transforming C-strings into NSString raises a runtime exception when the C-string is
// NULL. Here we propagate nil instead of crashing, and let the SDK handle the behavior in those
// cases.
NSString *safeCharToNSString(const char *c) {
  NSString *str = c ? [NSString stringWithUTF8String:c] : nil;
  return str;
}

#pragma mark Main API

void CLURecordCustomException(const char *name, const char *reason, Frame *frames, int frameCount, bool isOnDemand) {
  NSString *nameString = safeCharToNSString(name);
  NSString *reasonString = safeCharToNSString(reason);
  NSMutableArray<FIRStackFrame *> *framesArray = [NSMutableArray arrayWithCapacity:frameCount];
  for (int i = 0; i < frameCount; i++) {
    Frame frame = frames[i];
    NSString *library = safeCharToNSString(frame.library); // Not used
    NSString *symbol = safeCharToNSString(frame.symbol);
    NSString *fileName = safeCharToNSString(frame.fileName);
    NSInteger lineNumber = [safeCharToNSString(frame.lineNumber) intValue];
    FIRStackFrame *crashStackFrame =
      [FIRStackFrame stackFrameWithSymbol:symbol file:fileName line:lineNumber];

    [framesArray addObject:crashStackFrame];
  }
  FIRExceptionModel *model =
    [FIRExceptionModel exceptionModelWithName:nameString reason:reasonString];
  model.stackTrace = framesArray;

  if (isOnDemand) {
    // For on demand exception, we log them as fatal
    model.onDemand = YES;
    model.isFatal = YES;
    [[FIRCrashlytics crashlytics] recordOnDemandExceptionModel:model];
    return;
  }
  [[FIRCrashlytics crashlytics] recordExceptionModel:model];
}

bool CLUIsInitialized() { return [FIRCrashlytics crashlytics] != nil; }

#pragma mark Logging, key-value, user info

void CLUSetKeyValue(const char *key, const char *value) {
  [[FIRCrashlytics crashlytics] setCustomValue:safeCharToNSString(value)
                                        forKey:safeCharToNSString(key)];
}

void CLUSetInternalKeyValue(const char *key, const char *value) {
  FIRCLSUserLoggingRecordInternalKeyValue(safeCharToNSString(key), safeCharToNSString(value));
}

void CLULog(const char *msg) { [[FIRCrashlytics crashlytics] log:safeCharToNSString(msg)]; }

void CLUSetUserIdentifier(const char *identifier) {
  [[FIRCrashlytics crashlytics] setUserID:safeCharToNSString(identifier)];
}

void CLUSetDevelopmentPlatform(const char *name, const char *version) {
  [FIRCrashlytics crashlytics].developmentPlatformName = safeCharToNSString(name);
  [FIRCrashlytics crashlytics].developmentPlatformVersion = safeCharToNSString(version);
}

bool CLUIsCrashlyticsCollectionEnabled() {
  return [[FIRCrashlytics crashlytics] isCrashlyticsCollectionEnabled];
}

void CLUSetCrashlyticsCollectionEnabled(bool enabled) {
  [[FIRCrashlytics crashlytics] setCrashlyticsCollectionEnabled:enabled];
}
