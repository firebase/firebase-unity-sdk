/*
 * Copyright 2021 Google LLC
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
using System.Runtime.CompilerServices;
using System.Threading;
using Firebase.Internal;
using Firebase.Platform;
using UnityEngine;

namespace Firebase.Crashlytics {
  [Serializable]
  [UnityEngine.Scripting.Preserve]
  internal sealed class Metadata {
    public string uv, pt, gdn, gdv, gdver, ss;
    public bool idb;
    public int pc, pf, sms, gms, gdid, gdvid, gsl, grtc, gmts, srr;
    public float sdpi;
    public UnityEngine.Rendering.GraphicsDeviceType gdt;
    public UnityEngine.Rendering.CopyTextureSupport gcts;

    internal Metadata(bool populateFromEngine = true) {
      if (!populateFromEngine) { uv = pt = gdn = gdv = gdver = string.Empty; ss = "0x0"; return; }
      PopulateFromEngine();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void PopulateFromEngine() {
      uv = Application.unityVersion; idb = Debug.isDebugBuild;
      pt = SystemInfo.processorType; pc = SystemInfo.processorCount; pf = SystemInfo.processorFrequency;
      sms = SystemInfo.systemMemorySize; gms = SystemInfo.graphicsMemorySize;
      gdid = SystemInfo.graphicsDeviceID; gdvid = SystemInfo.graphicsDeviceVendorID;
      gdn = SystemInfo.graphicsDeviceName; gdv = SystemInfo.graphicsDeviceVendor; gdver = SystemInfo.graphicsDeviceVersion;
      gdt = SystemInfo.graphicsDeviceType; gsl = SystemInfo.graphicsShaderLevel;
      grtc = SystemInfo.supportedRenderTargetCount; gcts = SystemInfo.copyTextureSupport; gmts = SystemInfo.maxTextureSize;
      ss = $"{Screen.width}x{Screen.height}"; sdpi = Screen.dpi;
      double ratio = Screen.currentResolution.refreshRateRatio.value;
      srr = double.IsNaN(ratio) || double.IsInfinity(ratio) ? 0 : (int)Math.Round(ratio);
    }
  }

  /// <summary>
  /// Automatically collects metadata and serializes to JSON in a space-efficient way.
  /// </summary>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal static class MetadataBuilder {
    internal const string METADATA_KEY = "com.crashlytics.metadata.unity";
    private static volatile int s_mainThreadId;
    private static SynchronizationContext s_mainSyncContext;
    private static string s_cachedMainThreadJson, s_cachedUnityVersion;

    internal static int MainThreadId => s_mainThreadId;
    internal static SynchronizationContext MainSyncContext => Volatile.Read(ref s_mainSyncContext);
    internal static string CachedUnityVersion => Volatile.Read(ref s_cachedUnityVersion) ?? string.Empty;

    internal static void CaptureMainThreadSnapshot() {
      s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
      Volatile.Write(ref s_mainSyncContext, SynchronizationContext.Current);
      try {
        Volatile.Write(ref s_cachedUnityVersion, Application.unityVersion ?? string.Empty);
        Volatile.Write(ref s_cachedMainThreadJson, SerializeMetadata(new Metadata(populateFromEngine: true)));
      } catch (Exception e) { Impl.SafeLog(PlatformLogLevel.Debug, "Skipped early Crashlytics metadata capture: " + e.Message); }
    }

    internal static string GenerateMetadataJSON() {
      if (s_mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId != s_mainThreadId) return Volatile.Read(ref s_cachedMainThreadJson) ?? string.Empty;
      try {
        Volatile.Write(ref s_cachedUnityVersion, Application.unityVersion ?? string.Empty);
        string json = SerializeMetadata(new Metadata(populateFromEngine: true));
        Volatile.Write(ref s_cachedMainThreadJson, json);
        return json;
      } catch (Exception e) {
        string cached = Volatile.Read(ref s_cachedMainThreadJson);
        if (!string.IsNullOrEmpty(cached)) return cached;
        Impl.SafeLog(PlatformLogLevel.Error, "Failed to generate Unity-specific metadata for Crashlytics due to: " + e);
        return string.Empty;
      }
    }

    internal static string SerializeMetadata(Metadata m) {
      if (m == null) return string.Empty;
      try {
        return JsonUtility.ToJson(m);
      } catch (Exception) {
        // Fallback serializer when UnityEngine.JsonUtility ECall is unavailable in standalone .NET tests.
        return JsonHelper.Serialize(new Dictionary<string, object>(20, StringComparer.Ordinal) {
          { "uv", m.uv ?? string.Empty }, { "idb", m.idb }, { "pt", m.pt ?? string.Empty },
          { "pc", m.pc }, { "pf", m.pf }, { "sms", m.sms }, { "gms", m.gms },
          { "gdid", m.gdid }, { "gdvid", m.gdvid }, { "gdn", m.gdn ?? string.Empty },
          { "gdv", m.gdv ?? string.Empty }, { "gdver", m.gdver ?? string.Empty },
          { "gdt", (int)m.gdt }, { "gsl", m.gsl }, { "grtc", m.grtc },
          { "gcts", (int)m.gcts }, { "gmts", m.gmts }, { "ss", m.ss ?? string.Empty },
          { "sdpi", m.sdpi }, { "srr", m.srr },
        });
      }
    }
  }
}
