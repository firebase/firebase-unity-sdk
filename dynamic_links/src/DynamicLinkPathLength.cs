/*
 * Copyright 2017 Google LLC
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

namespace Firebase.DynamicLinks {

using System;

/// @brief The desired path length for shortened Dynamic Link URLs.
public enum DynamicLinkPathLength {
  /// Uses the server-default for the path length.
  /// See https://goo.gl/8yDAqC for more information.
  Default = 0,
  /// Typical short link for non-sensitive links.
  Short,
  /// Short link that uses a very long path to make it more difficult to
  /// guess. Useful for sensitive links.
  Unguessable,
}

internal static class DynamicLinkPathLengthExtensions {
  // Convert DynamicLinkPathLength to PathLengthInternal.
  internal static PathLengthInternal ConvertToInternal(this DynamicLinkPathLength pathLength) {
    switch (pathLength) {
      case DynamicLinkPathLength.Default:
        return PathLengthInternal.PathLengthDefault;
      case DynamicLinkPathLength.Short:
        return PathLengthInternal.PathLengthShort;
      case DynamicLinkPathLength.Unguessable:
        return PathLengthInternal.PathLengthUnguessable;
      default:
        throw new Firebase.FirebaseException(0, String.Format("Invalid path length {0}",
                                                              pathLength));
    }
  }
}

}
