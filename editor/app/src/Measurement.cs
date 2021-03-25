/*
 * Copyright 2020 Google LLC
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

using Google;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;

namespace Firebase.Editor {

internal static class Measurement {
    // Firebase package name prefix to remove to retrieve the component name..
    private const string PACKAGE_NAME_PREFIX = "Firebase";
    // Google Analytics tracking ID.
    private const string GA_TRACKING_ID = "UA-54627617-4";
    // Privacy policy for analytics data usage.
    private const string PRIVACY_POLICY = "https://policies.google.com/privacy";

    // Logger for this class.
    internal static Google.Logger logger = new Google.Logger();

    // Analytics reporter.
    internal static EditorMeasurement analytics = new EditorMeasurement(
        Settings.Instance, logger, GA_TRACKING_ID, "com.google.firebase", "Firebase", "",
        PRIVACY_POLICY) {
        BasePath = "/firebase/",
        BaseQuery = Measurement.BaseQuery,
        BaseReportName = "Firebase: ",
        InstallSourceFilename = Assembly.GetAssembly(typeof(Measurement)).Location
    };

    /// <summary>
    /// Build a common query string based upon the set of installed Firebase components to report
    /// with each event.
    /// </summary>
    private static string BaseQuery {
        get {
            var components = new List<string>();
            long maxVersionNumber = 0;
            var manifests = VersionHandlerImpl.ManifestReferences.FindAndReadManifests();
            foreach (var pkg in manifests) {
                if (!String.IsNullOrEmpty(pkg.filenameCanonical) && pkg.metadataByVersion != null) {
                    var packageName = Path.GetFileNameWithoutExtension(pkg.filenameCanonical);
                    if (!packageName.StartsWith(PACKAGE_NAME_PREFIX)) continue;
                    packageName = packageName.Substring(PACKAGE_NAME_PREFIX.Length).ToLower();
                    var version = pkg.metadataByVersion.MostRecentVersion.versionString;
                    components.Add(String.Format("{0}-{1}", packageName, version));
                    var versionNumber = VersionHandlerImpl.FileMetadata.CalculateVersion(version);
                    maxVersionNumber = Math.Max(versionNumber, maxVersionNumber);
                }
            }
            var query = String.Join("&", new [] {
                    String.Format("version={0}",
                                  VersionHandlerImpl.FileMetadata.VersionNumberToString(
                                      maxVersionNumber)),
                    String.Format("plugins={0}", String.Join(",", components.ToArray()))
                });
            logger.Log(String.Format("Base Query: {0}", query), level: LogLevel.Debug);
            return query;
        }
    }

    /// <summary>
    /// Report an event with the current build target.
    /// </summary>
    /// <param name="reportPath">Path to send with the report.</param>
    /// <param name="parameters">Key value pairs to add as a query string to the URL.</param>
    /// <param name="reportName">Human readable name to report with the URL.</param>  
    public static void ReportWithBuildTarget(string reportPath,
                                             ICollection<KeyValuePair<string, string>> parameters,
                                             string reportName) {
        var reportParameters = new List<KeyValuePair<string, string>>() {
            new KeyValuePair<string, string>(
                "buildTarget", EditorUserBuildSettings.activeBuildTarget.ToString())
        };
        if (parameters != null) reportParameters.AddRange(parameters);
        analytics.Report(reportPath, reportParameters, reportName);
    }
}

}
