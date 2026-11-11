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
  /// Contains information required to authenticate with a third party
  /// provider.
  /// </summary>
  public class FederatedProviderData : IDisposable {
    private string _providerId = string.Empty;

    /// <summary>Initializes an empty <see cref="FederatedProviderData" />.</summary>
    public FederatedProviderData() { }

    /// <summary>
    /// Gets or sets the id of the provider to be used during sign-in, link, or
    /// reauthentication requests.
    /// </summary>
    public string ProviderId {
      get => _providerId;
      set => _providerId = value ?? string.Empty;
    }

    /// <summary>Releases resources held by this instance.</summary>
    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>Releases resources held by this instance.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()" />.</param>
    public virtual void Dispose(bool disposing) { }
  }

  /// <summary>Contains information to identify an OAuth provider.</summary>
  public class FederatedOAuthProviderData : FederatedProviderData {
    private IEnumerable<string> _scopes = new List<string>();
    private IDictionary<string, string> _customParameters = new Dictionary<string, string>();

    /// <summary>Initializes an empty <see cref="FederatedOAuthProviderData" /> instance.</summary>
    public FederatedOAuthProviderData() { }

    /// <summary>Initializes the provider data with a provider id.</summary>
    /// <param name="provider">The id of the federated OAuth provider.</param>
    public FederatedOAuthProviderData(string provider) => ProviderId = provider;

    /// <summary>Gets or sets OAuth parameters which are provided to the federated provider service.</summary>
    public IDictionary<string, string> CustomParameters {
      get => _customParameters;
      set => _customParameters = value != null ? new Dictionary<string, string>(value) : new Dictionary<string, string>();
    }

    /// <summary>Gets or sets OAuth parameters which specify which rights of access are being requested.</summary>
    public IEnumerable<string> Scopes {
      get => _scopes;
      set => _scopes = value != null ? new List<string>(value) : new List<string>();
    }

    /// <summary>Releases resources held by this instance.</summary>
    /// <param name="disposing">True when called from <see cref="FederatedProviderData.Dispose()" />.</param>
    public override void Dispose(bool disposing) => base.Dispose(disposing);
  }

  /// <summary>Used to authenticate with Federated Auth Providers.</summary>
  /// <remarks>
  /// The federated auth provider implementation may facilitate multiple provider
  /// types in the future, with support for OAuth to start.
  /// </remarks>
  public class FederatedAuthProvider : IDisposable {
    internal FederatedAuthProvider() { }

    /// <summary>Releases resources held by this instance.</summary>
    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>Releases resources held by this instance.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()" />.</param>
    public virtual void Dispose(bool disposing) { }
  }

  /// <summary>
  /// Authenticates with Federated OAuth Providers via the
  /// <see cref="FirebaseAuth" /> and <see cref="FirebaseUser" /> classes.
  /// </summary>
  /// <remarks>
  /// Once configured with a provider id, and with OAuth scope and OAuth custom
  /// parameters via a <see cref="FederatedOAuthProviderData" /> instance, an object of
  /// this class may be used via <see cref="FirebaseAuth.SignInWithProviderAsync" /> to sign-in users,
  /// or via <see cref="FirebaseUser.LinkWithProviderAsync" /> and
  /// <see cref="FirebaseUser.ReauthenticateWithProviderAsync" /> for cross account linking and
  /// user reauthentication, respectively.
  /// </remarks>
  public sealed class FederatedOAuthProvider : FederatedAuthProvider {
    internal FederatedOAuthProviderData Data { get; private set; }

    /// <summary>Constructs an unconfigured provider.</summary>
    public FederatedOAuthProvider() => Data = new FederatedOAuthProviderData();

    /// <summary>Constructs a <see cref="FederatedOAuthProvider" /> preconfigured with provider data.</summary>
    /// <param name="providerData">
    /// Contains the federated provider id and OAuth scopes and OAuth custom
    /// parameters required for user authentication and user linking.
    /// </param>
    public FederatedOAuthProvider(FederatedOAuthProviderData providerData) => SetProviderData(providerData);

    /// <summary>Configures the provider with OAuth provider information.</summary>
    /// <param name="providerData">
    /// Contains the federated provider id and OAuth scopes and OAuth custom
    /// parameters required for user authentication and user linking.
    /// </param>
    public void SetProviderData(FederatedOAuthProviderData providerData) =>
        Data = providerData ?? new FederatedOAuthProviderData();

    /// <summary>Releases resources held by this instance.</summary>
    /// <param name="disposing">True when called from <see cref="FederatedAuthProvider.Dispose()" />.</param>
    public override void Dispose(bool disposing) => base.Dispose(disposing);
  }
}
