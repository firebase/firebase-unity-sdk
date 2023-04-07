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

using System.Reflection;
using System.Runtime.CompilerServices;

// This file is used by every unity firebase C# Assembly we build.
// Tags like %PROJECT_NAME% are replaced with sed, per project.

[assembly: AssemblyTitle("%PROJECT_NAME%")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Google Inc.")]
[assembly: AssemblyProduct("Firebase")]
[assembly: AssemblyCopyright("Copyright 2016 Google Inc. All Rights Reserved.")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Firebase App contains internals that need to be visibly to the other
// Firebase Unity projects, so mark them as friends.
[assembly: InternalsVisibleTo("Firebase.Analytics")]
[assembly: InternalsVisibleTo("Firebase.AppCheck")]
[assembly: InternalsVisibleTo("Firebase.Auth")]
[assembly: InternalsVisibleTo("Firebase.Crash")]
[assembly: InternalsVisibleTo("Firebase.Crashlytics")]
[assembly: InternalsVisibleTo("Firebase.Crashlytics.Editor")]
[assembly: InternalsVisibleTo("Firebase.Database")]
[assembly: InternalsVisibleTo("Firebase.DatabaseInternal")]
[assembly: InternalsVisibleTo("Firebase.Database.Testing")]
[assembly: InternalsVisibleTo("Firebase.DigitalGoods")]
[assembly: InternalsVisibleTo("Firebase.DynamicLinks")]
[assembly: InternalsVisibleTo("Firebase.Firestore")]
[assembly: InternalsVisibleTo("Firebase.Functions")]
[assembly: InternalsVisibleTo("Firebase.Installations")]
[assembly: InternalsVisibleTo("Firebase.Invites")]
[assembly: InternalsVisibleTo("Firebase.InstanceId")]
[assembly: InternalsVisibleTo("Firebase.Messaging")]
[assembly: InternalsVisibleTo("Firebase.ML.NaturalLanguage.LanguageId")]
[assembly: InternalsVisibleTo("Firebase.Performance")]
[assembly: InternalsVisibleTo("Firebase.RemoteConfig")]
[assembly: InternalsVisibleTo("Firebase.Storage")]
[assembly: InternalsVisibleTo("Firebase.Storage.Testing")]
[assembly: InternalsVisibleTo("Firebase.TestLab")]
[assembly: InternalsVisibleTo("Firebase.TestLab.GameLoop")]

// The assembly version has the format "{Major}.{Minor}.{Build}.{Revision}".
// The form "{Major}.{Minor}.*" will automatically update the build and
// revision, and "{Major}.{Minor}.{Build}.*" will update just the revision.

[assembly: AssemblyVersion("1.0.0.0")]
