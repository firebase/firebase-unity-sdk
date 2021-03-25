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

namespace Firebase {

/// An enum that indicates the current status of the dependencies on
/// the current system, required for Firebase to run.
public enum DependencyStatus {
  /// All required dependencies are available.
  Available,

  /// One or more required services are disabled in platform settings.
  /// On Android, this means that Google Play services is disabled.
  UnavailableDisabled,

  /// One or more required services is in an invalid state.
  UnavailableInvalid,

  /// One or more required services is not installed.
  UnavilableMissing,

  /// One or more required services does not have the correct permissions.
  UnavailablePermission,

  /// One or more required services needs to be updated.
  UnavailableUpdaterequired,

  /// One or more required services is currently updating.
  UnavailableUpdating,

  /// One or more required services is unavailable for an unknown reason.
  UnavailableOther,
};

}  // namespace Firebase
