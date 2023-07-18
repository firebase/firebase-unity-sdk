// Copyright 2021 Google LLC
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

/* Provides methods for building test apps from the command line, in batch mode.
 *
 * The Build<buildTarget> methods can be called from the command line to automate the
 * build process for testapps. The following flags are available:
 *
 * -AppBuilderHelper.outputDir
 * The directory for build artifacts to be placed.
 *
 * -AppBuilderHelper.targetIosSdk (iOS and tvOS only)
 * Determines whether the iOS or tvOS app will run on simulator or device. Must be
 * either "simulator" or "device", accordingly. Anything else will cause the build
 * to fail.
 *
 * -AppBuilderHelper.noSymlinkLibraries (optional, iOS only)
 * Disable symlinking of iOS libraries into the generated xcode project.
 * Symlinking speeds up build times and is enabled by default.
 *
 * -AppBuilderHelper.forceXcodeProject (optional, iOS only)
 * Force the iOS Resolver to use the xcodeproject as the method of integration,
 * rather than the workspace. By default, will use the workspace if present.
 *
 * -AppBuilderHelper.minify (optional, Android only)
 * Set to 'proguard' to cause unity to use proguarding for the android
 * minification stage. The default is to not minify.
 *
 * -AppBuilderHelper.buildForCI (optional)
 * Flag to determine if the apps are being built for use with a CI.
 *
 * In addition to flags, this script depends on optional environment variables:
 *
 * UNITY_ANDROID_SDK
 * UNITY_ANDROID_NDK
 * UNITY_ANDROID_JDK
 *
 * These will set/override the corresponding values in the Unity preferences.
 * These environment variables need to be set if building for Android, and if
 * the SDK/NDK/JDK cannot be set manually through Unity's GUI. This
 * should only be necessary in automated build environments where the installation
 * of Unity is automated, and thus manually setting these values
 * through the GUI is not possible.
 */

#define EDM4U_IS_ENABLED
#define FIREBASE_IS_ENABLED

using UnityEngine;
using UnityEditor;
#if UNITY_ANDROID 
using UnityEditor.Android;
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

[InitializeOnLoad]
public sealed class AppBuilderHelper {
  static readonly string outputDir;
  static readonly string targetIosSdk;
  static readonly string buildTarget;

  static readonly bool symlinkLibraries = true;
  static readonly bool forceXcodeProject;
  static readonly string minify;
  static readonly bool buildForCI = false;

  // General extensionless name for a testapp executable, apk, ipa, etc.
  // Having a unified name makes it easier to grab artifacts with a script.
  const string APP_NAME = "testapp";

  const string ANDROID_SDK_ENVVAR = "UNITY_ANDROID_SDK";
  const string ANDROID_NDK_ENVVAR = "UNITY_ANDROID_NDK";
  const string ANDROID_JDK_ENVVAR = "UNITY_ANDROID_JDK";

  const string ANDROID_SDK_KEY = "AndroidSdkRoot";
  const string ANDROID_NDK_KEY = "AndroidNdkRoot";
  const string ANDROID_JDK_KEY = "JdkPath";

  const string MACOS_SUBDIR = "MacOSTestapp";
  const string WINDOWS_SUBDIR = "WindowsTestapp";
  const string LINUX_SUBDIR = "LinuxTestapp";

  static AppBuilderHelper() {
    string[] args = System.Environment.GetCommandLineArgs();
    for (int i = 0; i < args.Length; i++) {
      if (args[i] == "-AppBuilderHelper.outputDir") {
        outputDir = args[++i];
        continue;
      }
      if (args[i] == "-AppBuilderHelper.targetIosSdk") {
        targetIosSdk = args[++i];
        continue;
      }
      if (args[i] == "-AppBuilderHelper.noSymlinkLibraries") {
        symlinkLibraries = false;
        continue;
      }
      if (args[i] == "-AppBuilderHelper.forceXcodeProject") {
        forceXcodeProject = true;
        continue;
      }
      if (args[i] == "-AppBuilderHelper.minify") {
        minify = args[++i];
        continue;
      }
      if (args[i] == "-buildTarget") {
        buildTarget = args[++i];
        continue;
      }
      if (args[i] == "-AppBuilderHelper.buildForCI") {
        buildForCI = true;
        continue;
      }
    }
    // This will set the appropriate values in Unity Preferences -> External Tools.
    SetUnityPrefWithEnvVar(ANDROID_SDK_KEY, ANDROID_SDK_ENVVAR);
    SetUnityPrefWithEnvVar(ANDROID_NDK_KEY, ANDROID_NDK_ENVVAR);
    SetUnityPrefWithEnvVar(ANDROID_JDK_KEY, ANDROID_JDK_ENVVAR);
#if UNITY_ANDROID 
#if UNITY_2019_3_OR_NEWER
    // Unity 2019.3+ introduced new method of set values in Unity Preferences -> External Tools
    AndroidExternalToolsSettings.sdkRootPath = System.Environment.GetEnvironmentVariable(ANDROID_SDK_ENVVAR);
    AndroidExternalToolsSettings.ndkRootPath = System.Environment.GetEnvironmentVariable(ANDROID_NDK_ENVVAR);
    AndroidExternalToolsSettings.jdkRootPath = System.Environment.GetEnvironmentVariable(ANDROID_JDK_ENVVAR);
    AndroidExternalToolsSettings.gradlePath = null; // use default Gradle tool integrated in Unity
#endif
#endif
  }

  /// <summary>
  /// Sets a Unity preference based on an environment variable, if present.
  /// <param name="key">The key associated with the Unity preference in question.</param>
  /// <param name="envVar">String representing an environment variable. If the
  /// environment variable is not set, this method will do nothing.</param>
  /// </summary>
  private static void SetUnityPrefWithEnvVar(string key, string envVar) {
    string valueOfVariable = System.Environment.GetEnvironmentVariable(envVar);
    if (valueOfVariable != null) {
      Debug.LogFormat("Setting {0} to: {1}", key, valueOfVariable);
      EditorPrefs.SetString(key, valueOfVariable);
    }
  }

  [InitializeOnLoadMethod]
  public static void OnLoad() {
    // This will run any time the editor is loaded, but we only want this to do something when
    // requesting a build. This can be checked by the presence of a required flag specific to
    // this script: AppBuilderHelper.outputDir. buildTarget is not used since that's a Unity flag
    // that could be provided at any step.
    if (outputDir != null) {
      // Performing a build immediately can result in a race condition with other logic tied
      // to load events. Delay to the first update instead.
      EditorApplication.update += PerformBuild;
    }
  }

  private static void PerformBuild() {
    EditorApplication.update -= PerformBuild; // Update will be invoked multiple times.
    Action build;
    if (buildTarget == "Android") {
      build = BuildAndroid;
    }
    else if (buildTarget == "iOS") {
      build = BuildiOS;
    }
    else if (buildTarget == "tvOS") {
      build = BuildtvOS;
    }
    else if (buildTarget == "Win64") {
      // Windows won't recognize the file as an application unless it has the "exe" extension.
      build = () => StandaloneBuild(BuildTarget.StandaloneWindows64, WINDOWS_SUBDIR, APP_NAME + ".exe");
    }
    else if (buildTarget == "Linux64") {
      build = () => StandaloneBuild(BuildTarget.StandaloneLinux64, LINUX_SUBDIR, APP_NAME);
    }
    else if (buildTarget == "OSXUniversal") {
#if UNITY_2017_3_OR_NEWER
      build = () => StandaloneBuild(BuildTarget.StandaloneOSX, MACOS_SUBDIR, APP_NAME);
#else
      build = () => StandaloneBuild(BuildTarget.StandaloneOSXUniversal, MACOS_SUBDIR, APP_NAME);
#endif
    }
    else if (string.IsNullOrEmpty(buildTarget)) {
      Debug.LogError("No build target specified via -buildTarget flag.");
      EditorApplication.Exit(1);
      return;
    }
    else {
      Debug.LogError("Unrecognized build target: " + buildTarget);
      EditorApplication.Exit(1);
      return;
    }
    // Exceptions will prevent an exit statement from being reached, causing a hang.
    // Catch, log and exit instead.
    try {
      build();
    }
    catch (System.Exception ex) {
      Debug.LogException(ex);
      EditorApplication.Exit(1);
    }
  }

  /// <summary>
  /// Performs a build for Android. Will generate an apk at the directory
  /// specified by outputDir.
  /// </summary>
  private static void BuildAndroid() {
    if (minify == "proguard") {
#if UNITY_2020_1_OR_NEWER
      PlayerSettings.Android.minifyDebug = true;
      PlayerSettings.Android.minifyRelease = true;
      PlayerSettings.Android.minifyWithR8 = false;
#elif UNITY_2017_1_OR_NEWER
      EditorUserBuildSettings.androidReleaseMinification = AndroidMinification.Proguard;
      EditorUserBuildSettings.androidDebugMinification = AndroidMinification.Proguard;
#else
      Debug.Log("Proguarding requested, but not available on this version of Unity. Skipping.");
      EditorApplication.Exit(0);
      return;
#endif
    }

    System.Action<bool> complete = (resolutionSucceeded) => {
      if (resolutionSucceeded) {
#if FIREBASE_IS_ENABLED
        // This forces the generation of an xml file (google services) necessary
        // for running on Android. This workaround is only necessary for versions
        // of Unity prior to 2017, and can be removed when support is deprecated.
        Firebase.Editor.GenerateXmlFromGoogleServicesJson.ForceJsonUpdate();
#endif
        int returnCode = BuildForAndroid();
        EditorApplication.Exit(returnCode);
      }
      else {
        EditorApplication.Exit(1);
      }
    };
#if EDM4U_IS_ENABLED
    GooglePlayServices.PlayServicesResolver.Resolve(resolutionCompleteWithResult: complete);
#else
    complete(true);
#endif
  }

  /// <summary>
  /// Performs a build for iOS. Will generate an xcode project (folder) at
  /// the directory given by outputDir. The flag targetIosSdk should
  /// be specified when building for iOS.
  /// </summary>
  private static void BuildiOS() {
#if EDM4U_IS_ENABLED
    if (forceXcodeProject) {
      Google.IOSResolver.CocoapodsIntegrationMethodPref = Google.IOSResolver.CocoapodsIntegrationMethod.Project;
    } else {
      Google.IOSResolver.CocoapodsIntegrationMethodPref = Google.IOSResolver.CocoapodsIntegrationMethod.Workspace;
    }
#endif
    int returnCode = BuildForIOS();
    EditorApplication.Exit(returnCode);
  }

  /// <summary>
  /// Performs a build for tvOS. Will generate an xcode project (folder) at
  /// the directory given by outputDir. The flag targetIosSdk should
  /// be specified when building for iOS.
  /// </summary>
  private static void BuildtvOS() {
#if EDM4U_IS_ENABLED
    if (forceXcodeProject) {
      Google.IOSResolver.CocoapodsIntegrationMethodPref = Google.IOSResolver.CocoapodsIntegrationMethod.Project;
    } else {
      Google.IOSResolver.CocoapodsIntegrationMethodPref = Google.IOSResolver.CocoapodsIntegrationMethod.Workspace;
    }
#endif
    int returnCode = BuildForTVOS();
    EditorApplication.Exit(returnCode);
  }

  /// <summary>
  /// Helper method for the standalone builds. Exits
  /// application after performing a standalone build for the given target.
  /// </summary>
  private static void StandaloneBuild(BuildTarget target, string subdirectory, string fileName) {
    // Standalone builds create more than just the executable, so we package
    // them together into a folder disambiguated by the target name.

    // Note that .NET 3.5 only has the two-argument version of Path.Combine.
    // The 3 arg, 4 arg, and array versions are in 4.0.
    string path = Path.Combine(Path.Combine(outputDir, subdirectory), fileName);
    BuildPlayerOptions playerOptions = GetBuildOptions(target, path);

#if EDM4U_IS_ENABLED
    // Unity's api updater messes up the application id uniquely for standalone when updating
    // very old projects. Versions 2017+ should be unaffected. See b/133860007
    string applicationId = GooglePlayServices.UnityCompat.GetApplicationId(BuildTarget.iOS);
    GooglePlayServices.UnityCompat.ApplicationId = applicationId;
#else
    Debug.LogWarning("EDM4U is disabled, standalone builds will have the wrong application ID.");
#endif

    // Windowed mode for convenience
    PlayerSettings.defaultIsFullScreen = false;
    // Avoid unnecessary resolution window before launching app
    PlayerSettings.displayResolutionDialog = ResolutionDialogSetting.Disabled;

    int returnCode = BuildPlayer(playerOptions);
    EditorApplication.Exit(returnCode);
  }

  /// <summary>
  /// Performs an Android build. Assumes all dependencies are present.
  /// </summary>
  /// <returns>Returns 0 if and only if build succeeded.</returns>
  private static int BuildForAndroid() {
    string path = Path.Combine(outputDir, APP_NAME + ".apk");
    BuildPlayerOptions playerOptions = GetBuildOptions(BuildTarget.Android, path);
    return BuildPlayer(playerOptions);
  }

  /// <summary>
  /// Performs an iOS build. Assumes all dependencies are present.
  /// </summary>
  /// <returns>Returns 0 if and only if build succeeded.</returns>
  private static int BuildForIOS() {
    string path = Path.Combine(outputDir, APP_NAME + "_xcode");
    BuildPlayerOptions playerOptions = GetBuildOptions(BuildTarget.iOS, path);
    if (targetIosSdk == "device") {
      PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
    } else if (targetIosSdk == "simulator") {
      PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
    } else {
      Debug.LogError("Unrecognized iOS target: " + targetIosSdk);
      return -1;
    }
    return BuildPlayer(playerOptions);
  }

  /// <summary>
  /// Performs a tvOS build. Assumes all dependencies are present.
  /// </summary>
  /// <returns>Returns 0 if and only if build succeeded.</returns>
  private static int BuildForTVOS() {
    string path = Path.Combine(outputDir, APP_NAME + "_xcode");
    BuildPlayerOptions playerOptions = GetBuildOptions(BuildTarget.tvOS, path);
    if (targetIosSdk == "device") {
      PlayerSettings.tvOS.sdkVersion = tvOSSdkVersion.Device;
    } else if (targetIosSdk == "simulator") {
      PlayerSettings.tvOS.sdkVersion = tvOSSdkVersion.Simulator;
    } else {
      Debug.LogError("Unrecognized tvOS target: " + targetIosSdk);
      return -1;
    }
    return BuildPlayer(playerOptions);
  }

  /// <summary>
  /// This performs an actual build. Returns 0 for a successful build, 1 for a build with errors.
  /// </summary>
  private static int BuildPlayer(BuildPlayerOptions options) {
    string build = string.Format("build {0} for target {1}.", options.locationPathName, options.target);
    Debug.Log("Starting " + build);
    // The build player encountering an error will not cause an exception. Instead, it will return
    // an object with results. The error information is separately caught in a log file,
    // so we just check for any errors that caused the build to fail. This
    // is necessary to correctly determine from outside this script whether the build succeeded.
    var result = BuildPipeline.BuildPlayer(options);
#if UNITY_2018_1_OR_NEWER
    bool succeeded = result.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
#else
    bool succeeded = string.IsNullOrEmpty(result);
#endif
    Debug.Log("Finished " + build);
    return succeeded ? 0 : 1;
  }

  /// <summary>
  /// Returns a reasonable set of default build player options which includes the scene, target,
  /// and build path.
  /// The settings assigned in this method are intended to be used for all platforms.
  /// The returned options can be further customized as required. This overload automatically generates
  /// the build path and disambiguates it from the build paths for other platforms by adding the target to
  /// the name.
  /// </summary>
  private static BuildPlayerOptions GetBuildOptions(BuildTarget target) {
    string defaultName = Path.Combine(outputDir, target.ToString());
    return GetBuildOptions(target, defaultName);
  }

  /// <summary>
  /// Returns a reasonable set of default build player options which includes the scene, target,
  /// and build path.
  /// The settings assigned in this method are intended to be used for all platforms.
  /// The returned options can be further customized as required. This overload requries you to specify
  /// the buildpath. Note that what gets built depends on the platform - for Android, it will build an apk.
  /// For iOS, it will built an entire xcode project, so the buildpath corresponds to the folder for the
  /// project.
  /// </summary>
  private static BuildPlayerOptions GetBuildOptions(BuildTarget target, string buildPath) {
    var playerOptions = new BuildPlayerOptions();
    playerOptions.scenes = GetScenes();
    playerOptions.locationPathName = buildPath;
    playerOptions.target = target;
    // Development builds on iOS can trigger a user permission prompt for Local Network access,
    // so when running on CI we do not want to include it.
    if (!(buildForCI && target == BuildTarget.iOS && targetIosSdk == "device")) {
      playerOptions.options |= BuildOptions.Development;
    }
    playerOptions.options |= BuildOptions.StrictMode;
    if (symlinkLibraries) {
      playerOptions.options |= BuildOptions.SymlinkLibraries;
    }
    return playerOptions;
  }

  /// <summary>
  /// Gather an array of the project's scenes, with the menu scene appearing first.
  /// </summary>
  /// <returns>Array of with string corresponding to the found scene.</returns>
  private static string[] GetScenes() {
    // Note: order is important. The first scene will determine the first scene
    // to be loaded on app startup, which needs to be the menu.
    string menuScene = "Menu.unity";
    string mainScene = "MainScene.unity";
    string autoScene = "MainSceneAutomated.unity";
    string[] expectedScenes = new [] { menuScene, mainScene, autoScene };
    var scenes = Directory.GetFiles("Assets", "*.unity", SearchOption.AllDirectories)
                          .Where(path => expectedScenes.Contains(Path.GetFileName(path)))
                          .OrderBy(path => Array.IndexOf(expectedScenes, Path.GetFileName(path)))
                          .ToArray();
    var sceneNames = scenes.Select(path => Path.GetFileName(path)).ToArray();
    if (!sceneNames.Contains(menuScene)) {
      Debug.LogWarningFormat("Menu scene not found: {0}.", menuScene);
    }
    if (!sceneNames.Contains(autoScene)) {
      Debug.LogWarningFormat("Automated scene not found: {0}.", autoScene);
    }
    if (!sceneNames.Contains(mainScene)) {
      Debug.LogErrorFormat("Main scene not found: {0}.", mainScene);
    }
    return scenes;
  }
}
