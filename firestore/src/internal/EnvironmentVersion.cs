// Copyright 2020 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Versioning;

namespace Firebase.Firestore.Internal {
    // Note: this code is a greatly simplified version of
    // https://github.com/googleapis/gax-dotnet/blob/master/Google.Api.Gax/VersionHeaderBuilder.cs

    /// <summary>
    /// Gets the version of the .NET environment.
    /// </summary>
    sealed class EnvironmentVersion
    {
        public static string GetEnvironmentVersion()
        {
            string systemEnvironmentVersion =
#if NETSTANDARD1_3
                null;
#else
                FormatVersion(Environment.Version);
#endif
            return systemEnvironmentVersion ?? "";
        }

        private static string FormatVersion(Version version) =>
            version != null ?
            String.Format("{0}.{1}.{2}", version.Major, version.Minor,
                (version.Build != -1 ? version.Build : 0)) :
            ""; // Empty string means "unknown"

    }
}
