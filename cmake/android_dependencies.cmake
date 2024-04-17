# Copyright 2019 Google
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#      http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

# This file defines the Android dependencies needed by all the modules.

set(FIREBASE_APP_ANDROID_DEPS
    "com.google.firebase:firebase-common:20.4.3"
    "com.google.firebase:firebase-analytics:21.6.2"
    "com.google.android.gms:play-services-base:18.4.0"
)

set(FIREBASE_ANALYTICS_ANDROID_DEPS
    "com.google.firebase:firebase-analytics:21.6.2"
)

set(FIREBASE_APP_CHECK_ANDROID_DEPS
    "com.google.firebase:firebase-appcheck:17.1.2"
    "com.google.firebase:firebase-appcheck-debug:17.1.2"
    "com.google.firebase:firebase-appcheck-playintegrity:17.1.2"
    "com.google.firebase:firebase-analytics:21.6.2"
)

set(FIREBASE_AUTH_ANDROID_DEPS
    "com.google.firebase:firebase-auth:22.3.1"
    "com.google.firebase:firebase-analytics:21.6.2"
)

set(FIREBASE_CRASHLYTICS_ANDROID_DEPS
    "com.google.firebase:firebase-crashlytics-ndk:18.6.4"
    "com.google.firebase:firebase-analytics:21.6.2"
)

set(FIREBASE_DATABASE_ANDROID_DEPS
    "com.google.firebase:firebase-database:20.3.1"
    "com.google.firebase:firebase-analytics:21.6.2"
    "com.google.android.gms:play-services-base:18.4.0"
)

set(FIREBASE_DYNAMIC_LINKS_ANDROID_DEPS
    "com.google.firebase:firebase-dynamic-links:21.2.0"
    "com.google.firebase:firebase-analytics:21.6.2"
)

set(FIREBASE_FIRESTORE_ANDROID_DEPS
    "com.google.firebase:firebase-firestore:24.11.1"
    "com.google.firebase:firebase-analytics:21.6.2"
)

set(FIREBASE_FUNCTIONS_ANDROID_DEPS
    "com.google.firebase:firebase-functions:20.4.0"
    "com.google.firebase:firebase-analytics:21.6.2"
)

set(FIREBASE_INSTALLATIONS_ANDROID_DEPS
    "com.google.firebase:firebase-installations:17.2.0"
    "com.google.firebase:firebase-analytics:21.6.2"
)

# iid is needed by messaging to avoid a conflict with functions
set(FIREBASE_MESSAGING_ANDROID_DEPS
    "com.google.firebase:firebase-messaging:23.4.1"
    "com.google.firebase:firebase-analytics:21.6.2"
    "com.google.firebase:firebase-iid:21.1.0"
    "com.google.flatbuffers:flatbuffers-java:1.12.0"
)

set(FIREBASE_REMOTE_CONFIG_ANDROID_DEPS
    "com.google.firebase:firebase-config:21.6.3"
    "com.google.firebase:firebase-analytics:21.6.2"
)

set(FIREBASE_STORAGE_ANDROID_DEPS
    "com.google.firebase:firebase-storage:20.3.0"
    "com.google.firebase:firebase-analytics:21.6.2"
)
