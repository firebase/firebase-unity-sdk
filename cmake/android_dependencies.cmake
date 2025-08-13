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
    "com.google.firebase:firebase-common:22.0.0"
    "com.google.firebase:firebase-analytics:23.0.0"
    "com.google.android.gms:play-services-base:18.7.2"
)

set(FIREBASE_ANALYTICS_ANDROID_DEPS
    "com.google.firebase:firebase-analytics:23.0.0"
)

set(FIREBASE_APP_CHECK_ANDROID_DEPS
    "com.google.firebase:firebase-appcheck:19.0.0"
    "com.google.firebase:firebase-appcheck-debug:19.0.0"
    "com.google.firebase:firebase-appcheck-playintegrity:19.0.0"
    "com.google.firebase:firebase-analytics:23.0.0"
)

set(FIREBASE_AUTH_ANDROID_DEPS
    "com.google.firebase:firebase-auth:24.0.1"
    "com.google.firebase:firebase-analytics:23.0.0"
)

set(FIREBASE_CRASHLYTICS_ANDROID_DEPS
    "com.google.firebase:firebase-crashlytics-ndk:20.0.0"
    "com.google.firebase:firebase-analytics:23.0.0"
)

set(FIREBASE_DATABASE_ANDROID_DEPS
    "com.google.firebase:firebase-database:22.0.0"
    "com.google.firebase:firebase-analytics:23.0.0"
    "com.google.android.gms:play-services-base:18.7.2"
)

set(FIREBASE_FIRESTORE_ANDROID_DEPS
    "com.google.firebase:firebase-firestore:26.0.0"
    "com.google.firebase:firebase-analytics:23.0.0"
)

set(FIREBASE_FUNCTIONS_ANDROID_DEPS
    "com.google.firebase:firebase-functions:22.0.0"
    "com.google.firebase:firebase-analytics:23.0.0"
)

set(FIREBASE_INSTALLATIONS_ANDROID_DEPS
    "com.google.firebase:firebase-installations:19.0.0"
    "com.google.firebase:firebase-analytics:23.0.0"
)

# iid is needed by messaging to avoid a conflict with functions
set(FIREBASE_MESSAGING_ANDROID_DEPS
    "com.google.firebase:firebase-messaging:25.0.0"
    "com.google.firebase:firebase-analytics:23.0.0"
    "com.google.firebase:firebase-iid:21.1.0"
    "com.google.flatbuffers:flatbuffers-java:1.12.0"
)

set(FIREBASE_REMOTE_CONFIG_ANDROID_DEPS
    "com.google.firebase:firebase-config:23.0.0"
    "com.google.firebase:firebase-analytics:23.0.0"
)

set(FIREBASE_STORAGE_ANDROID_DEPS
    "com.google.firebase:firebase-storage:22.0.0"
    "com.google.firebase:firebase-analytics:23.0.0"
)
