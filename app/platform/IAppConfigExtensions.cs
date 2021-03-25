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

using System;
using System.Collections.Generic;

namespace Firebase.Platform {

  /// <summary>
  ///  Holds extra configuration state on a per app basis.
  /// </summary>
  internal interface IAppConfigExtensions {

    /// <summary>
    /// Returns a valid writeable path for the app.
    /// </summary>
    string GetWriteablePath(IFirebaseAppPlatform app);

    void SetDatabaseUrl(IFirebaseAppPlatform app, string databaseUrl);
    string GetDatabaseUrl(IFirebaseAppPlatform app);

    /// <summary>
    /// Sets the service account password.
    /// A service account can be used to access your FirebaseDatabase securely.
    /// If you do not set up service account access, the Editor will only be able
    /// to access a FirebaseDatabase if its rules allow open access.
    /// </summary>
    /// <param name="app">App.</param>
    /// <param name="p12Password">P12 password.</param>
    void SetEditorP12Password(IFirebaseAppPlatform app, string p12Password);

    /// <summary>
    /// Gets the editor p12 password.
    /// </summary>
    /// <returns>The editor p12 password.</returns>
    /// <param name="app">App.</param>
    string GetEditorP12Password(IFirebaseAppPlatform app);

    /// <summary>
    /// Sets the name of the editor p12 file.
    /// When running in the editor, a P12 file can be supplied with service account credentials.
    /// This allows you to run with service account authentication and impersonate any user
    /// when making updates.
    /// If you do not set up service account access, the Editor will only be able
    /// to access a FirebaseDatabase if its rules allow open access.
    /// </summary>
    /// <param name="app">App.</param>
    /// <param name="p12Filename">P12 filename.</param>
    void SetEditorP12FileName(IFirebaseAppPlatform app, string p12Filename);

    /// <summary>
    /// Gets the name of the editor p12 file.
    /// </summary>
    /// <returns>The editor p12 file name.</returns>
    /// <param name="app">App.</param>
    string GetEditorP12FileName(IFirebaseAppPlatform app);

    /// <summary>
    /// Sets the editor service account email.
    /// A service account can be used to access your FirebaseDatabase securely.
    /// If you do not set up service account access, the Editor will only be able
    /// to access a FirebaseDatabase if its rules allow open access.
    /// </summary>
    /// <param name="app">App.</param>
    /// <param name="email">Email.</param>
    void SetEditorServiceAccountEmail(IFirebaseAppPlatform app, string email);

    /// <summary>
    /// Gets the editor service account email.
    /// </summary>
    /// <returns>The editor service account email.</returns>
    /// <param name="app">App.</param>
    string GetEditorServiceAccountEmail(IFirebaseAppPlatform app);

    /// <summary>
    /// Sets the cert pem file.
    /// This optionally provides an extra set of root ssl certificates that will be installed within
    /// mono configuration.  By default, the SDK includes a set of root certificates that
    /// Google requires to validate the Ssl connection with the Firebase server.
    /// </summary>
    /// <param name="app">App.</param>
    /// <param name="certName">Cert name.</param>
    void SetCertPemFile(IFirebaseAppPlatform app, string certName);

    /// <summary>
    /// Gets the cert pem file.
    /// </summary>
    /// <returns>The cert pem file.</returns>
    /// <param name="app">App.</param>
    string GetCertPemFile(IFirebaseAppPlatform app);

    /// <summary>
    /// Sets the update url where updated root certs will be downloaded.
    /// </summary>
    /// <param name="app">App.</param>
    /// <param name="certUrl">Url where updated root ssl certs are stored.</param>
    void SetCertUpdateUrl(IFirebaseAppPlatform app, Uri certUrl);

    /// <summary>
    /// Gets the cert update url.
    /// </summary>
    /// <returns>The cert pem file.</returns>
    /// <param name="app">App.</param>
    Uri GetCertUpdateUrl(IFirebaseAppPlatform app);
  }
}
