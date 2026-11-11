// Copyright 2026 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle("Firebase.App")]
[assembly: AssemblyCompany("Google LLC"), AssemblyProduct("Firebase")]
[assembly: AssemblyCopyright("Copyright 2026 Google LLC. All Rights Reserved."), AssemblyVersion("1.0.0.0")]
[assembly: InternalsVisibleTo("Firebase.Analytics"), InternalsVisibleTo("Firebase.App.Internal"), InternalsVisibleTo("Firebase.AppCheck")]
[assembly: InternalsVisibleTo("Firebase.Auth"), InternalsVisibleTo("Firebase.Crashlytics"), InternalsVisibleTo("Firebase.Database")]
[assembly: InternalsVisibleTo("Firebase.Firestore"), InternalsVisibleTo("Firebase.Functions"), InternalsVisibleTo("Firebase.Installations")]
[assembly: InternalsVisibleTo("Firebase.InstanceId"), InternalsVisibleTo("Firebase.Messaging"), InternalsVisibleTo("Firebase.RemoteConfig")]
[assembly: InternalsVisibleTo("Firebase.Storage"), InternalsVisibleTo("Firebase.TaskExtension"), InternalsVisibleTo("Firebase.FirebaseAI")]
[assembly: InternalsVisibleTo("Firebase.TestLab"), InternalsVisibleTo("Firebase.TestLab.GameLoop")]
[assembly: InternalsVisibleTo("Firebase.App.Tests"), InternalsVisibleTo("Firebase.App.Internal.Tests"), InternalsVisibleTo("Firebase.Tests")]
[assembly: InternalsVisibleTo("Firebase.Benchmarks"), InternalsVisibleTo("Firebase.TestHarness")]
