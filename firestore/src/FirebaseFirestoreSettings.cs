// Copyright 2017 Google LLC
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
using Firebase.Firestore.Internal;

namespace Firebase.Firestore {
  /// <summary>
  /// Settings used to configure a <see cref="FirebaseFirestore"/> instance.
  /// </summary>
  public sealed class FirebaseFirestoreSettings {
    /// Constant to use when setting <see
    /// cref="FirebaseFirestoreSettings.CacheSizeBytes"/> to disable garbage
    /// collection.
    public static readonly long CacheSizeUnlimited =
      SettingsProxy.kCacheSizeUnlimited;

    // Note: must be kept in sync with the C++ Settings::kDefaultCacheSizeBytes.
    private static readonly long defaultCacheSizeBytes = 100 * 1024 * 1024;

    private string _host = "firestore.googleapis.com";

    internal static FirebaseFirestoreSettings FromProxy(SettingsProxy proxy) {
      return new FirebaseFirestoreSettings {
        Host = proxy.host(),
        SslEnabled = proxy.is_ssl_enabled(),
        PersistenceEnabled = proxy.is_persistence_enabled(),
        CacheSizeBytes = proxy.cache_size_bytes()
      };
    }

    internal SettingsProxy Proxy {
      get {
        var proxy = new SettingsProxy();
        proxy.set_host(Host);
        proxy.set_ssl_enabled(SslEnabled);
        proxy.set_persistence_enabled(PersistenceEnabled);
        proxy.set_cache_size_bytes(CacheSizeBytes);

        return proxy;
      }
    }

    /// <summary>
    /// The host of the Cloud Firestore backend.
    /// </summary>
    public string Host {
      get {
        return _host;
      }
      set {
        Preconditions.CheckNotNullOrEmpty(value, nameof(value));
        _host = value;
      }
    }

    /// <summary>
    /// Whether or not to use SSL for communication.
    /// </summary>
    public bool SslEnabled {get; set;} = true;

    /// <summary>
    /// Whether or not to use local persistence storage.
    /// </summary>
    public bool PersistenceEnabled {get; set;} = true;

    /// <summary>
    /// Sets an approximate cache size threshold for the on-disk data. If the
    /// cache grows beyond this size, Cloud Firestore will start removing data
    /// that hasn't been recently used. The size is not a guarantee that the
    /// cache will stay below that size, only that if the cache exceeds the
    /// given size, cleanup will be attempted.
    ///
    /// By default, collection is enabled with a cache size of 100 MB. The
    /// minimum value is 1 MB.
    /// </summary>
    public long CacheSizeBytes {get; set;} = defaultCacheSizeBytes;

    /// <inheritdoc />
    public override bool Equals(object obj) {
      if (obj is FirebaseFirestoreSettings) {
        return this.Equals((FirebaseFirestoreSettings)obj);
      }
      return false;
    }

    /// <inheritdoc />
    public bool Equals(FirebaseFirestoreSettings other) {
      return Host.Equals(other.Host) &&
             SslEnabled.Equals(other.SslEnabled) &&
             PersistenceEnabled.Equals(other.PersistenceEnabled) &&
             CacheSizeBytes.Equals(other.CacheSizeBytes);
    }

    /// <inheritdoc />
    public override int GetHashCode() {
      int hashCode = this.Host.GetHashCode();
      hashCode = hashCode * 31 + this.SslEnabled.GetHashCode();
      hashCode = hashCode * 31 + this.PersistenceEnabled.GetHashCode();
      hashCode = hashCode * 31 + this.CacheSizeBytes.GetHashCode();
      return hashCode;
    }
  }
}
