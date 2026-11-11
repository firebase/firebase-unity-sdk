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

using System;
using System.Collections.Generic;

namespace Firebase.Auth {
  /// <summary>
  /// Additional user data returned from an identity provider.
  /// </summary>
  public sealed class AdditionalUserInfo : IDisposable {
    /// <summary>
    /// Additional identity-provider specific information.
    /// Most likely a hierarchical key-value mapping, like a parsed JSON file.
    /// </summary>
    public IDictionary<string, object> Profile { get; }

    /// <summary>The provider identifier.</summary>
    public string ProviderId { get; }

    /// <summary>
    /// On a nonce-based credential link failure where the user has already linked
    /// to the provider, the Firebase auth service may provide an updated
    /// <see cref="Credential" />. If <see cref="Credential.IsValid()" /> returns <c>true</c>
    /// on this credential, then it may be passed to a new
    /// <see cref="FirebaseAuth.SignInWithCredentialAsync" /> request to sign
    /// the user in with the provider.
    /// </summary>
    public Credential UpdatedCredential { get; set; }

    /// <summary>The name of the user.</summary>
    public string UserName { get; }

    internal AdditionalUserInfo(
        string providerId = null,
        string userName = null,
        IDictionary<string, object> profile = null,
        Credential updatedCredential = null) {
      ProviderId = providerId ?? string.Empty;
      UserName = userName ?? string.Empty;
      Profile = profile ?? new Dictionary<string, object>();
      UpdatedCredential = updatedCredential;
    }

    /// <summary>Releases resources held by this instance.</summary>
    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed and unmanaged resources held by this instance.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()" />.</param>
    public void Dispose(bool disposing) {
    }
  }

  /// <summary>
  /// The result of operations that can affect authentication state.
  /// </summary>
  public sealed class AuthResult : IDisposable {
    private FirebaseAuth _auth;
    private FirebaseUser _user;
    private AdditionalUserInfo _additionalUserInfo;
    private Credential _credential;

    /// <summary>Initializes an empty <see cref="AuthResult" />.</summary>
    public AuthResult() {
      _additionalUserInfo = new AdditionalUserInfo();
    }

    internal AuthResult(
        FirebaseAuth auth,
        FirebaseUser user,
        AdditionalUserInfo additionalUserInfo,
        Credential credential) {
      _auth = auth;
      _user = user;
      _additionalUserInfo = additionalUserInfo ?? new AdditionalUserInfo();
      _credential = credential;
    }

    /// <summary>
    /// Identity-provider specific information for the user, if the provider is
    /// one of Facebook, GitHub, Google, or Twitter.
    /// </summary>
    public AdditionalUserInfo AdditionalUserInfo => _additionalUserInfo;

    /// <summary>A <see cref="Auth.Credential" /> instance for the recently signed-in user.</summary>
    public Credential Credential => _credential;

    /// <summary>The currently signed-in <see cref="FirebaseUser" />, or <c>null</c> if there isn't one.</summary>
    public FirebaseUser User => _user ?? _auth?.CurrentUser;

    /// <summary>Releases resources held by this instance.</summary>
    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed and unmanaged resources held by this instance.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()" />.</param>
    public void Dispose(bool disposing) {
      if (disposing) {
        _auth = null;
        _user = null;
      }
    }
  }
}
