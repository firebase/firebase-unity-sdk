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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Firebase.Internal;

namespace Firebase.Auth.Internal {
  internal sealed class AuthProviderInfoSnapshot {
    internal string UserId { get; set; } = string.Empty;
    internal string Email { get; set; } = string.Empty;
    internal string DisplayName { get; set; } = string.Empty;
    internal string PhotoUrl { get; set; } = string.Empty;
    internal string ProviderId { get; set; } = string.Empty;
    internal string PhoneNumber { get; set; } = string.Empty;
  }

  internal sealed class AuthUserSnapshot {
    internal string Uid { get; }
    internal string Email { get; }
    internal string DisplayName { get; }
    internal string PhotoUrl { get; }
    internal Uri ParsedPhotoUrl { get; }
    internal string PhoneNumber { get; }
    internal string ProviderId { get; }
    internal bool IsAnonymous { get; }
    internal bool IsEmailVerified { get; }
    internal bool HasPasswordCredential { get; }
    internal long CreationTimestampMs { get; }
    internal long LastSignInTimestampMs { get; }
    internal string IdToken { get; }
    internal string RefreshToken { get; }
    internal long ExpiresAtUnixSeconds { get; }
    internal IReadOnlyList<AuthProviderInfoSnapshot> ProviderInfos { get; }
    internal IReadOnlyList<IUserInfo> CachedProviderData { get; }
    internal UserMetadata CachedMetadata { get; }
    internal Task<string> CachedTokenTask { get; }

    internal AuthUserSnapshot(
        string uid, string email, string displayName, string photoUrl, string phoneNumber,
        string providerId, bool isAnonymous, bool isEmailVerified, bool hasPasswordCredential,
        long creationTimestampMs, long lastSignInTimestampMs, string idToken, string refreshToken,
        long expiresAtUnixSeconds, IReadOnlyList<AuthProviderInfoSnapshot> providerInfos) {
      Uid = uid ?? string.Empty;
      Email = email ?? string.Empty;
      DisplayName = displayName ?? string.Empty;
      PhotoUrl = photoUrl ?? string.Empty;
      ParsedPhotoUrl = AuthUrlHelper.StringToUri(PhotoUrl);
      PhoneNumber = phoneNumber ?? string.Empty;
      ProviderId = string.IsNullOrEmpty(providerId) ? "Firebase" : providerId;
      IsAnonymous = isAnonymous;
      IsEmailVerified = isEmailVerified;
      HasPasswordCredential = hasPasswordCredential;
      CreationTimestampMs = creationTimestampMs;
      LastSignInTimestampMs = lastSignInTimestampMs;
      IdToken = idToken ?? string.Empty;
      RefreshToken = refreshToken ?? string.Empty;
      ExpiresAtUnixSeconds = expiresAtUnixSeconds;
      ProviderInfos = providerInfos ?? Array.Empty<AuthProviderInfoSnapshot>();

      var list = new UserInfoInterface[ProviderInfos.Count];
      for (int i = 0; i < ProviderInfos.Count; i++) {
        list[i] = new UserInfoInterface(ProviderInfos[i]);
      }
      CachedProviderData = list;
      CachedMetadata = new UserMetadata(CreationTimestampMs, LastSignInTimestampMs);
      CachedTokenTask = !string.IsNullOrEmpty(IdToken) ? Task.FromResult(IdToken) : null;
    }

    internal AuthUserSnapshot WithTokens(string newIdToken, string newRefreshToken, long newExpiresAtUnixSeconds) =>
        new AuthUserSnapshot(
            Uid, Email, DisplayName, PhotoUrl, PhoneNumber, ProviderId,
            IsAnonymous, IsEmailVerified, HasPasswordCredential,
            CreationTimestampMs, LastSignInTimestampMs,
            newIdToken,
            string.IsNullOrEmpty(newRefreshToken) ? RefreshToken : newRefreshToken,
            newExpiresAtUnixSeconds,
            ProviderInfos);
  }

  internal sealed class AuthUserRecord {
    internal volatile AuthUserSnapshot Snapshot;
    internal AuthUserRecord(AuthUserSnapshot initial) {
      Snapshot = initial ?? throw new ArgumentNullException(nameof(initial));
    }
  }

  internal static class AuthUrlHelper {
    internal static Uri StringToUri(string urlString) {
      if (string.IsNullOrEmpty(urlString)) return null;
      try {
        return new Uri(urlString);
      } catch (UriFormatException) {
        return null;
      }
    }

    internal static string UriToString(Uri uri) => uri != null ? uri.OriginalString : string.Empty;
  }

#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal static class AuthPersistence {
    private const string PayloadPrefix = "v1:";
    private const string MasterKeyPath = "auth/master.key";
    private const int MasterKeyBytes = 32;
    private const int IvSize = 16;
    private const int MacSize = 32;

    private static readonly object MasterKeyLock = new object();
    private static readonly ConditionalWeakTable<AtomicFileStore, string> StoreMasterKeyCache =
        new ConditionalWeakTable<AtomicFileStore, string>();
    private static readonly ConcurrentDictionary<string, (byte[] Enc, byte[] Mac)> KeyCache =
        new ConcurrentDictionary<string, (byte[] Enc, byte[] Mac)>(StringComparer.Ordinal);

    internal static AtomicFileStore CustomStoreOverride { get; set; }

    private static AtomicFileStore GetStore() => CustomStoreOverride ?? AtomicFileStore.Default;

    internal static string GetStorageKey(string appName, string apiKey) {
      using var sha = SHA256.Create();
      byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes($"{appName ?? string.Empty}|{apiKey ?? string.Empty}"));
      var sb = new StringBuilder("auth/session_", 13 + 32 + 4);
      for (int i = 0; i < 16; i++) sb.Append(hash[i].ToString("x2"));
      sb.Append(".dat");
      return sb.ToString();
    }

    internal static void SaveUser(string appName, string apiKey, AuthUserSnapshot snapshot) {
      if (snapshot == null || string.IsNullOrEmpty(snapshot.Uid)) {
        DeleteUser(appName, apiKey);
        return;
      }

      AtomicFileStore store = GetStore();
      var providers = new List<object>(snapshot.ProviderInfos.Count);
      for (int i = 0; i < snapshot.ProviderInfos.Count; i++) {
        AuthProviderInfoSnapshot p = snapshot.ProviderInfos[i];
        providers.Add(new Dictionary<string, object> {
          ["uid"] = p.UserId ?? string.Empty,
          ["email"] = p.Email ?? string.Empty,
          ["displayName"] = p.DisplayName ?? string.Empty,
          ["photoUrl"] = p.PhotoUrl ?? string.Empty,
          ["providerId"] = p.ProviderId ?? string.Empty,
          ["phoneNumber"] = p.PhoneNumber ?? string.Empty,
        });
      }

      var payload = new Dictionary<string, object> {
        ["uid"] = snapshot.Uid,
        ["email"] = snapshot.Email,
        ["displayName"] = snapshot.DisplayName,
        ["photoUrl"] = snapshot.PhotoUrl,
        ["phoneNumber"] = snapshot.PhoneNumber,
        ["providerId"] = snapshot.ProviderId,
        ["isAnonymous"] = snapshot.IsAnonymous,
        ["isEmailVerified"] = snapshot.IsEmailVerified,
        ["hasPassword"] = snapshot.HasPasswordCredential,
        ["creationTimestampMs"] = snapshot.CreationTimestampMs,
        ["lastSignInTimestampMs"] = snapshot.LastSignInTimestampMs,
        ["idToken"] = snapshot.IdToken,
        ["refreshToken"] = snapshot.RefreshToken,
        ["expiresAtUnixSeconds"] = snapshot.ExpiresAtUnixSeconds,
        ["providerData"] = providers,
      };

      string encrypted = EncryptAtRest(store, JsonHelper.Serialize(payload), appName, apiKey);
      try {
        store.WriteAllText(GetStorageKey(appName, apiKey), encrypted);
      } catch (Exception ex) {
        LogUtil.LogMessage(LogLevel.Warning, $"FirebaseAuth: Failed to persist session state ({ex.GetType().Name}).");
      }
    }

    internal static AuthUserSnapshot LoadUser(string appName, string apiKey) {
      AtomicFileStore store = GetStore();
      string raw;
      try {
        raw = store.ReadAllText(GetStorageKey(appName, apiKey));
      } catch (Exception ex) {
        LogUtil.LogMessage(LogLevel.Warning, $"FirebaseAuth: Failed to read session state ({ex.GetType().Name}).");
        return null;
      }
      if (string.IsNullOrEmpty(raw)) return null;

      string json = DecryptAtRest(store, raw, appName, apiKey);
      if (string.IsNullOrEmpty(json)) return null;

      Dictionary<string, object> map = JsonHelper.ParseObject(json);
      if (map == null) return null;
      string uid = JsonHelper.GetString(map, "uid", string.Empty);
      if (string.IsNullOrEmpty(uid)) return null;

      var providerList = new List<AuthProviderInfoSnapshot>();
      List<object> rawProviders = JsonHelper.GetList(map, "providerData");
      if (rawProviders != null) {
        foreach (object item in rawProviders) {
          if (item is IDictionary<string, object> pMap) {
            providerList.Add(new AuthProviderInfoSnapshot {
              UserId = JsonHelper.GetString(pMap, "uid", string.Empty),
              Email = JsonHelper.GetString(pMap, "email", string.Empty),
              DisplayName = JsonHelper.GetString(pMap, "displayName", string.Empty),
              PhotoUrl = JsonHelper.GetString(pMap, "photoUrl", string.Empty),
              ProviderId = JsonHelper.GetString(pMap, "providerId", string.Empty),
              PhoneNumber = JsonHelper.GetString(pMap, "phoneNumber", string.Empty),
            });
          }
        }
      }

      return new AuthUserSnapshot(
          uid: uid,
          email: JsonHelper.GetString(map, "email", string.Empty),
          displayName: JsonHelper.GetString(map, "displayName", string.Empty),
          photoUrl: JsonHelper.GetString(map, "photoUrl", string.Empty),
          phoneNumber: JsonHelper.GetString(map, "phoneNumber", string.Empty),
          providerId: JsonHelper.GetString(map, "providerId", "Firebase"),
          isAnonymous: JsonHelper.GetBool(map, "isAnonymous", false),
          isEmailVerified: JsonHelper.GetBool(map, "isEmailVerified", false),
          hasPasswordCredential: JsonHelper.GetBool(map, "hasPassword", false),
          creationTimestampMs: JsonHelper.GetLong(map, "creationTimestampMs", 0),
          lastSignInTimestampMs: JsonHelper.GetLong(map, "lastSignInTimestampMs", 0),
          idToken: JsonHelper.GetString(map, "idToken", string.Empty),
          refreshToken: JsonHelper.GetString(map, "refreshToken", string.Empty),
          expiresAtUnixSeconds: JsonHelper.GetLong(map, "expiresAtUnixSeconds", 0),
          providerInfos: providerList);
    }

    internal static void DeleteUser(string appName, string apiKey) {
      try {
        GetStore().Delete(GetStorageKey(appName, apiKey));
      } catch (Exception ex) {
        LogUtil.LogMessage(LogLevel.Warning, $"FirebaseAuth: Failed to delete persisted session ({ex.GetType().Name}).");
      }
    }

    private static string GetOrCreateMasterKeyBase64(AtomicFileStore store) {
      if (StoreMasterKeyCache.TryGetValue(store, out string cached)) {
        return cached;
      }
      lock (MasterKeyLock) {
        if (StoreMasterKeyCache.TryGetValue(store, out cached)) {
          return cached;
        }
        string existing = null;
        try {
          existing = store.ReadAllText(MasterKeyPath)?.Trim();
        } catch (Exception) {
          existing = null;
        }

        if (!string.IsNullOrEmpty(existing)) {
          try {
            byte[] decoded = Convert.FromBase64String(existing);
            if (decoded.Length == MasterKeyBytes) {
              StoreMasterKeyCache.Add(store, existing);
              return existing;
            }
          } catch (FormatException) {
            // Regenerate corrupted master key below.
          }
        }

        byte[] generated = new byte[MasterKeyBytes];
        using (var rng = RandomNumberGenerator.Create()) {
          rng.GetBytes(generated);
        }
        string b64 = Convert.ToBase64String(generated);
        try {
          store.WriteAllText(MasterKeyPath, b64);
        } catch (Exception ex) {
          LogUtil.LogMessage(LogLevel.Warning, $"FirebaseAuth: Failed to persist master key ({ex.GetType().Name}).");
        }
        StoreMasterKeyCache.Add(store, b64);
        return b64;
      }
    }

    private static void DeriveKeys(AtomicFileStore store, string appName, string apiKey, out byte[] encKey, out byte[] macKey) {
      string masterKeyB64 = GetOrCreateMasterKeyBase64(store);
      string scope = $"{appName ?? string.Empty}|{apiKey ?? string.Empty}";
      string cacheKey = $"{masterKeyB64}|{scope}";
      if (KeyCache.TryGetValue(cacheKey, out (byte[] Enc, byte[] Mac) cached)) {
        encKey = cached.Enc;
        macKey = cached.Mac;
        return;
      }

      byte[] salt;
      using (var sha = SHA256.Create()) {
        salt = sha.ComputeHash(Encoding.UTF8.GetBytes("Firebase.Auth.v1|" + scope));
      }
      using var kdf = new Rfc2898DeriveBytes(masterKeyB64, salt, 1000, HashAlgorithmName.SHA256);
      encKey = kdf.GetBytes(32);
      macKey = kdf.GetBytes(32);
      KeyCache[cacheKey] = (encKey, macKey);
    }

    private static string EncryptAtRest(AtomicFileStore store, string plaintext, string appName, string apiKey) {
      DeriveKeys(store, appName, apiKey, out byte[] encKey, out byte[] macKey);
      using var aes = Aes.Create();
      aes.KeySize = 256;
      aes.Mode = CipherMode.CBC;
      aes.Padding = PaddingMode.PKCS7;
      aes.Key = encKey;
      aes.GenerateIV();
      byte[] iv = aes.IV;

      byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
      byte[] cipherBytes;
      using (ICryptoTransform encryptor = aes.CreateEncryptor()) {
        cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
      }

      byte[] combined = new byte[IvSize + cipherBytes.Length + MacSize];
      Buffer.BlockCopy(iv, 0, combined, 0, IvSize);
      Buffer.BlockCopy(cipherBytes, 0, combined, IvSize, cipherBytes.Length);
      using (var hmac = new HMACSHA256(macKey)) {
        byte[] tag = hmac.ComputeHash(combined, 0, IvSize + cipherBytes.Length);
        Buffer.BlockCopy(tag, 0, combined, IvSize + cipherBytes.Length, MacSize);
      }
      return PayloadPrefix + Convert.ToBase64String(combined);
    }

    private static string DecryptAtRest(AtomicFileStore store, string payload, string appName, string apiKey) {
      if (!payload.StartsWith(PayloadPrefix, StringComparison.Ordinal)) return null;
      byte[] combined;
      try {
        combined = Convert.FromBase64String(payload.Substring(PayloadPrefix.Length));
      } catch (FormatException) {
        return null;
      }
      if (combined.Length <= IvSize + MacSize) return null;

      DeriveKeys(store, appName, apiKey, out byte[] encKey, out byte[] macKey);
      int cipherLen = combined.Length - IvSize - MacSize;
      using (var hmac = new HMACSHA256(macKey)) {
        byte[] expectedTag = hmac.ComputeHash(combined, 0, IvSize + cipherLen);
        int diff = 0;
        for (int i = 0; i < MacSize; i++) diff |= expectedTag[i] ^ combined[IvSize + cipherLen + i];
        if (diff != 0) return null;
      }

      byte[] iv = new byte[IvSize];
      Buffer.BlockCopy(combined, 0, iv, 0, IvSize);
      try {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = encKey;
        aes.IV = iv;
        using ICryptoTransform decryptor = aes.CreateDecryptor();
        byte[] plainBytes = decryptor.TransformFinalBlock(combined, IvSize, cipherLen);
        return Encoding.UTF8.GetString(plainBytes);
      } catch (CryptographicException) {
        return null;
      }
    }
  }
}
