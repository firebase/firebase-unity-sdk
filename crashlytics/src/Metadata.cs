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

namespace Firebase.Crashlytics {
  using System;
  using UnityEngine;

  /// <summary>
  /// Represents the Unity-specific metadata object that is transformed into JSON.
  ///
  /// Metadata keys are acronyms to save space as there are limits to the maximum size of Keys in
  /// the Android, iOS and tvOS SDKs.
  /// </summary>
  internal class Metadata {
    // unityVersion: Version of Unity Engine
    public string uv;
    // isDebugBuild: Whether "Development Build" is checked
    public bool idb;

    // processorType
    public string pt;
    // processorCount: Number of cores
    public int pc;
    // processorFrequency
    public int pf;

    // systemMemorySize: RAM size
    public int sms;

    // graphicsMemorySize
    public int gms;
    // graphicsDeviceID
    public int gdid;
    // graphicsDeviceVendorID
    public int gdvid;
    // graphicsDeviceName
    public string gdn;
    // graphicsDeviceVendor
    public string gdv;
    // graphicsDeviceVersion
    public string gdver;
    // graphicsDeviceType
    public UnityEngine.Rendering.GraphicsDeviceType gdt;

    // graphicsShaderLevel
    // https://docs.unity3d.com/540/Documentation/ScriptReference/SystemInfo-graphicsShaderLevel.html
    public int gsl;
    // graphicsRenderTargetCount
    public int grtc;
    // graphicsCopyTextureSupport
    public UnityEngine.Rendering.CopyTextureSupport gcts;
    // graphicsMaxTextureSize
    public int gmts;

    // screenSize
    public string ss;
    // screenDPI
    public float sdpi;
    // screenRefreshRate
    public int srr;

    public Metadata() {
      uv = Application.unityVersion;
      idb = Debug.isDebugBuild;

      pt = SystemInfo.processorType;
      pc = SystemInfo.processorCount;
      pf = SystemInfo.processorFrequency;

      sms = SystemInfo.systemMemorySize;

      gms = SystemInfo.graphicsMemorySize;
      gdid = SystemInfo.graphicsDeviceID;
      gdvid = SystemInfo.graphicsDeviceVendorID;
      gdn = SystemInfo.graphicsDeviceName;
      gdv = SystemInfo.graphicsDeviceVendor;
      gdver = SystemInfo.graphicsDeviceVersion;
      gdt = SystemInfo.graphicsDeviceType;

      gsl = SystemInfo.graphicsShaderLevel;
      grtc = SystemInfo.supportedRenderTargetCount;
      gcts = SystemInfo.copyTextureSupport;
      gmts = SystemInfo.maxTextureSize;

      ss = String.Format("{0}x{1}", Screen.width, Screen.height);
      sdpi = Screen.dpi;
      srr = Screen.currentResolution.refreshRate;
    }
  }
}
