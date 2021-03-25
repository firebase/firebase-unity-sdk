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

using UnityEditor;
using UnityEngine;
using System.Collections;

namespace Firebase.Editor {

  internal class ApiInfo {
    public Texture2D Image { get; protected set; }
    public virtual string Name { get; set; }
    public virtual string Description { get; set; }

    // Links
    public virtual string ApiReference { get; set; }

    // Guides
    public virtual string GuideButton { get; set; }
    public virtual string GuideLink { get; set; }

    // Images (not the 2x versions) should be pulled from the Android Studio Firebase plugin.
    public ApiInfo(string image_path) {
      Image = (Texture2D)UnityEditor.EditorGUIUtility.Load(
        "Firebase/fb_" + image_path + (EditorGUIUtility.isProSkin ? "_dark" : "") + ".png");
    }

    public static ApiInfo Analytics() {
      return new ApiInfo("analytics")
      {
        Name = DocRef.AnalyticsName,
        Description = DocRef.AnalyticsDescription,
        ApiReference = Link.AnalyticsReference,
        GuideButton = DocRef.AnalyticsGuideSummary,
        GuideLink = Link.AnalyticsGuide
      };
    }

    public static ApiInfo Auth() {
      return new ApiInfo("auth")
      {
        Name = DocRef.AuthName,
        Description = DocRef.AuthDescription,
        ApiReference = Link.AuthReference,
        GuideButton = DocRef.AuthGuideSummary,
        GuideLink = Link.AuthGuide
      };
    }

    public static ApiInfo CloudMessaging() {
      return new ApiInfo("cloud_messaging")
      {
        Name = DocRef.CloudMessagingName,
        Description = DocRef.CloudMessagingDescription,
        ApiReference = Link.CloudMessagingReference,
        GuideButton = DocRef.CloudMessagingGuideSummary,
        GuideLink = Link.CloudMessagingGuide
      };
    }

    public static ApiInfo Crashlytics() {
      return new ApiInfo("crashlytics")
      {
        Name = DocRef.CrashlyticsName,
        Description = DocRef.CrashlyticsDescription,
        ApiReference = Link.CrashlyticsReference,
        GuideButton = DocRef.CrashlyticsGuideSummary,
        GuideLink = Link.CrashlyticsGuide
      };
    }

    public static ApiInfo Database() {
      return new ApiInfo("database")
      {
        Name = DocRef.DatabaseName,
        Description = DocRef.DatabaseDescription,
        ApiReference = Link.DatabaseReference,
        GuideButton = DocRef.DatabaseGuideSummary,
        GuideLink = Link.DatabaseGuide
      };
    }

    public static ApiInfo DynamicLinks() {
      return new ApiInfo("dynamic_links")
      {
        Name = DocRef.DynamicLinksName,
        Description = DocRef.DynamicLinksDescription,
        ApiReference = Link.DynamicLinksReference,
        GuideButton = DocRef.DynamicLinksGuideSummary,
        GuideLink = Link.DynamicLinksGuide
      };
    }

    public static ApiInfo Functions() {
      return new ApiInfo("functions")
      {
        Name = DocRef.FunctionsName,
        Description = DocRef.FunctionsDescription,
        ApiReference = Link.FunctionsReference,
        GuideButton = DocRef.FunctionsGuideSummary,
        GuideLink = Link.FunctionsGuide
      };
    }

    public static ApiInfo RemoteConfig() {
      return new ApiInfo("config")
      {
        Name = DocRef.RemoteConfigName,
        Description = DocRef.RemoteConfigDescription,
        ApiReference = Link.RemoteConfigReference,
        GuideButton = DocRef.RemoteConfigGuideSummary,
        GuideLink = Link.RemoteConfigGuide
      };
    }

    public static ApiInfo Storage() {
      return new ApiInfo("storage")
      {
        Name = DocRef.StorageName,
        Description = DocRef.StorageDescription,
        ApiReference = Link.StorageReference,
        GuideButton = DocRef.StorageGuideSummary,
        GuideLink = Link.StorageGuide
      };
    }
  }

}  // Firebase.Editor
