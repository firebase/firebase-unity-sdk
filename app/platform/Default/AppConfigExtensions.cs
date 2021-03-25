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

using System;
using System.Collections.Generic;

namespace Firebase.Platform.Default {

  internal class AppConfigExtensions : IAppConfigExtensions {
    private static readonly Uri DefaultUpdateUrl =
       new Uri("https://www.gstatic.com/firebase/ssl/roots.pem");
    private static readonly string Default = "DEFAULT";
    private static readonly object Sync = new object();
    private static AppConfigExtensions _instance = new AppConfigExtensions();

    public static IAppConfigExtensions Instance { get { return _instance; } }

    protected AppConfigExtensions() {
    }

    private static readonly Dictionary<int, Dictionary<string, string>> SStringState =
      new Dictionary<int, Dictionary<string, string>>();

    /// <summary>
    /// Returns a valid writeable path for the app.
    /// </summary>
    public virtual string GetWriteablePath(IFirebaseAppPlatform app) {
      return string.Empty;
    }

    public virtual void SetDatabaseUrl(IFirebaseAppPlatform app, string databaseUrl) {
      SetState(app, (int) ExtraStringState.DatabaseUrl, databaseUrl, SStringState);
    }

    public virtual string GetDatabaseUrl(IFirebaseAppPlatform app) {
      var url = GetState(app, (int) ExtraStringState.DatabaseUrl, SStringState);
      if (string.IsNullOrEmpty(url) && (app != null)) {
        var dburl = app.DatabaseUrl;
        url = dburl != null ? dburl.ToString() : null;
      }
      return url;
    }

    /// <summary>
    /// Sets the service account password.
    /// A service account can be used to access your FirebaseDatabase securely.
    /// If you do not set up service account access, the Editor will only be able
    /// to access a FirebaseDatabase if its rules allow open access.
    /// </summary>
    /// <param name="app">App.</param>
    /// <param name="p12Password">P12 password.</param>
    public virtual void SetEditorP12Password(IFirebaseAppPlatform app, string p12Password) {
      SetState(app, (int) ExtraStringState.P12Password, p12Password, SStringState);
    }

    /// <summary>
    /// Gets the editor p12 password.
    /// </summary>
    /// <returns>The editor p12 password.</returns>
    /// <param name="app">App.</param>
    public virtual string GetEditorP12Password(IFirebaseAppPlatform app) {
      return GetState(app, (int) ExtraStringState.P12Password, SStringState);
    }

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
    public virtual void SetEditorP12FileName(IFirebaseAppPlatform app, string p12Filename) {
      SetState(app, (int) ExtraStringState.P12FileName, p12Filename, SStringState);
    }

    /// <summary>
    /// Gets the name of the editor p12 file.
    /// </summary>
    /// <returns>The editor p12 file name.</returns>
    /// <param name="app">App.</param>
    public virtual string GetEditorP12FileName(IFirebaseAppPlatform app) {
      return GetState(app, (int) ExtraStringState.P12FileName, SStringState);
    }

    /// <summary>
    /// Sets the editor service account email.
    /// A service account can be used to access your FirebaseDatabase securely.
    /// If you do not set up service account access, the Editor will only be able
    /// to access a FirebaseDatabase if its rules allow open access.
    /// </summary>
    /// <param name="app">App.</param>
    /// <param name="email">Email.</param>
    public virtual void SetEditorServiceAccountEmail(IFirebaseAppPlatform app, string email) {
      SetState(app, (int) ExtraStringState.ServiceAccountEmail, email, SStringState);
    }

    /// <summary>
    /// Gets the editor service account email.
    /// </summary>
    /// <returns>The editor service account email.</returns>
    /// <param name="app">App.</param>
    public virtual string GetEditorServiceAccountEmail(IFirebaseAppPlatform app) {
      return GetState(app, (int) ExtraStringState.ServiceAccountEmail, SStringState);
    }

    /// <summary>
    /// Sets the cert pem file.
    /// This optionally provides an extra set of root ssl certificates that will be installed within
    /// mono configuration.  By default, the SDK includes a set of root certificates that
    /// Google requires to validate the Ssl connection with the Firebase server.
    /// </summary>
    /// <param name="app">App.</param>
    /// <param name="certName">Cert name.</param>
    public virtual void SetCertPemFile(IFirebaseAppPlatform app, string certName) {
      SetState(app, (int) ExtraStringState.CertTxtFileName, certName, SStringState);
    }

    /// <summary>
    /// Gets the cert pem file.
    /// </summary>
    /// <returns>The cert pem file.</returns>
    /// <param name="app">App.</param>
    public virtual string GetCertPemFile(IFirebaseAppPlatform app) {
      return GetState(app, (int) ExtraStringState.CertTxtFileName, SStringState);
    }

    /// <summary>
    /// Sets the cert update url.
    /// </summary>
    /// <param name="app">App.</param>
    /// <param name="certUrl">Url where updated root ssl certs are stored.</param>
    public void SetCertUpdateUrl(IFirebaseAppPlatform app, Uri certUrl) {
      SetState(app, (int) ExtraStringState.WebCertUpdateUrl, certUrl.ToString(), SStringState);
    }

    /// <summary>
    /// Gets the cert update url.
    /// </summary>
    /// <returns>The cert pem file.</returns>
    /// <param name="app">App.</param>
    public Uri GetCertUpdateUrl(IFirebaseAppPlatform app) {
      var result = GetState(app, (int) ExtraStringState.WebCertUpdateUrl, SStringState);
      if (String.IsNullOrEmpty(result)) {
        return DefaultUpdateUrl;
      }
      return new Uri(result);
    }

    private static T GetState<T>(IFirebaseAppPlatform app, int state,
                                 Dictionary<int, Dictionary<string, T>> store) {
      if (app == null) {
        app = FirebaseHandler.AppUtils.GetDefaultInstance();
      }

      lock (Sync) {
        var name = app.Name;
        if (string.IsNullOrEmpty(name)) {
          name = Default;
        }

        Dictionary<string, T> stateStorage;
        if (!store.TryGetValue(state, out stateStorage)) {
          stateStorage = new Dictionary<string, T>();
          store[state] = stateStorage;
        }
        T value;
        if (!stateStorage.TryGetValue(name, out value)) {
          return default(T);
        }
        return value;
      }
    }

    private static void SetState<T>(IFirebaseAppPlatform app,
      int state,
      T value,
      Dictionary<int, Dictionary<string, T>> store) {
      if (app == null) {
        app = FirebaseHandler.AppUtils.GetDefaultInstance();
      }
      lock (Sync) {
        var name = app.Name;
        if (string.IsNullOrEmpty(name)) {
          name = Default;
        }
        Dictionary<string, T> stateStorage;
        if (!store.TryGetValue(state, out stateStorage)) {
          stateStorage = new Dictionary<string, T>();
          store[state] = stateStorage;
        }
        stateStorage[name] = value;
      }
    }

    private enum ExtraStringState {
      DatabaseUrl = 0,
      P12FileName,
      P12Password,
      ServiceAccountEmail,
      CertTxtFileName,
      WebCertUpdateUrl
    }
  }
}
