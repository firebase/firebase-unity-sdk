# Copyright 2021 Google
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

# This file configures projects and libraries required by Firebase Unity
# provided by external sources

unity_pack_file(
  "${FIREBASE_UNITY_DIR}/docs/readme.md"
  PACK_PATH "Firebase/Editor"
)

unity_pack_file(
  "${FIREBASE_UNITY_DIR}/LICENSE"
  PACK_PATH "Firebase/Editor"
)

unity_pack_file(
  "${FIREBASE_UNITY_DIR}/docs/analytics/AnalyticsReadme.md"
  PACK_PATH "Firebase/Editor"
)

unity_pack_file(
  "${FIREBASE_UNITY_DIR}/docs/app_check/AppCheckReadme.md"
  PACK_PATH "Firebase/Editor"
)

unity_pack_file(
  "${FIREBASE_UNITY_DIR}/docs/auth/AuthReadme.md"
  PACK_PATH "Firebase/Editor"
)

unity_pack_file(
  "${FIREBASE_UNITY_DIR}/docs/crashlytics/CrashlyticsReadme.md"
  PACK_PATH "Firebase/Editor"
)

unity_pack_file(
  "${FIREBASE_UNITY_DIR}/docs/database/DatabaseReadme.md"
  PACK_PATH "Firebase/Editor"
)

unity_pack_file(
  "${FIREBASE_UNITY_DIR}/docs/dynamic_links/DynamicLinksReadme.md"
  PACK_PATH "Firebase/Editor"
)

unity_pack_file(
  "${FIREBASE_UNITY_DIR}/docs/firestore/FirestoreReadme.md"
  PACK_PATH "Firebase/Editor"
)

unity_pack_file(
  "${FIREBASE_UNITY_DIR}/docs/functions/FunctionsReadme.md"
  PACK_PATH "Firebase/Editor"
)

unity_pack_file(
  "${FIREBASE_UNITY_DIR}/docs/installations/InstallationsReadme.md"
  PACK_PATH "Firebase/Editor"
)

unity_pack_file(
  "${FIREBASE_UNITY_DIR}/docs/messaging/MessagingReadme.md"
  PACK_PATH "Firebase/Editor"
)

unity_pack_file(
  "${FIREBASE_UNITY_DIR}/docs/remote_config/RemoteConfigReadme.md"
  PACK_PATH "Firebase/Editor"
)

unity_pack_file(
  "${FIREBASE_UNITY_DIR}/docs/storage/StorageReadme.md"
  PACK_PATH "Firebase/Editor"
)
