/*
 * Copyright 2026 Google LLC
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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace UnityEngine {
  public enum LogType { Error, Assert, Warning, Log, Exception }
  public enum StackTraceLogType { None, ScriptOnly, Full }
  public enum RuntimePlatform {
    OSXEditor, OSXPlayer, WindowsPlayer, IPhonePlayer, Android, LinuxPlayer, tvOS
  }
  public enum KeyCode { None, Escape }

  public sealed class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }

  public struct Vector2 {
    public float x, y;
    public static Vector2 zero => new Vector2(0, 0);
    public Vector2(float x, float y) { this.x = x; this.y = y; }
  }
  public struct Rect {
    public float x, y, width, height;
    public Rect(float x, float y, float w, float h) {
      this.x = x; this.y = y; this.width = w; this.height = h;
    }
  }
  public struct Color {
    public float r, g, b, a;
    public Color(float r, float g, float b, float a = 1f) {
      this.r = r; this.g = g; this.b = b; this.a = a;
    }
    public static Color black => new Color(0, 0, 0, 1);
    public static Color grey => new Color(0.5f, 0.5f, 0.5f, 1);
  }

  public class Object {
    public string name { get; set; }
  }
  public class Component : Object {
    public GameObject gameObject { get; set; }
  }
  public class GameObject : Object {
    public GameObject(string name) {
      this.name = name;
    }
    public T AddComponent<T>() where T : Component, new() {
      var c = new T { gameObject = this };
      MonoBehaviourRunner.Register(c as MonoBehaviour);
      return c;
    }
    public Component AddComponent(Type t) {
      var c = (Component)Activator.CreateInstance(t);
      c.gameObject = this;
      MonoBehaviourRunner.Register(c as MonoBehaviour);
      return c;
    }
  }
  public abstract class CustomYieldInstruction : IEnumerator {
    public abstract bool keepWaiting { get; }
    public object Current => null;
    public bool MoveNext() => keepWaiting;
    public void Reset() { }
  }
  public class WaitForSeconds {
    public float Seconds { get; }
    public WaitForSeconds(float seconds) { Seconds = seconds; }
  }
  public class WaitUntil : CustomYieldInstruction {
    private readonly Func<bool> _predicate;
    public WaitUntil(Func<bool> predicate) { _predicate = predicate; }
    public override bool keepWaiting => _predicate != null && !_predicate();
  }
  public class WaitWhile : CustomYieldInstruction {
    private readonly Func<bool> _predicate;
    public WaitWhile(Func<bool> predicate) { _predicate = predicate; }
    public override bool keepWaiting => _predicate != null && _predicate();
  }
  public class WWW : CustomYieldInstruction {
    public string text => "";
    public WWW(string url) { }
    public override bool keepWaiting => false;
  }
  public class Coroutine { }
  public class MonoBehaviour : Component {
    public static void print(object message) => Debug.Log(message);
    public Coroutine StartCoroutine(IEnumerator routine) {
      return MonoBehaviourRunner.StartCoroutine(routine);
    }
  }

  public static class MonoBehaviourRunner {
    internal sealed class CoroutineState {
      public readonly Stack<IEnumerator> Stack = new Stack<IEnumerator>();
      public float WaitUntilTime;
      public CoroutineState(IEnumerator root) {
        Stack.Push(root);
      }
    }

    public static readonly List<MonoBehaviour> Behaviours = new List<MonoBehaviour>();
    private static readonly HashSet<MonoBehaviour> Initialized = new HashSet<MonoBehaviour>();
    private static readonly List<CoroutineState> Coroutines = new List<CoroutineState>();

    public static void Register(MonoBehaviour mb) {
      if (mb != null && !Behaviours.Contains(mb)) {
        Behaviours.Add(mb);
      }
    }

    public static void InitializePending() {
      for (int i = 0; i < Behaviours.Count; i++) {
        var mb = Behaviours[i];
        if (Initialized.Add(mb)) {
          InvokeLifecycleMethod(mb, "Awake");
          InvokeLifecycleMethod(mb, "Start");
        }
      }
    }

    public static void UpdateAll() {
      InitializePending();
      for (int i = 0; i < Behaviours.Count; i++) {
        InvokeLifecycleMethod(Behaviours[i], "Update");
      }
      StepCoroutines();
    }

    public static Coroutine StartCoroutine(IEnumerator r) {
      if (r == null) return new Coroutine();
      var state = new CoroutineState(r);
      Coroutines.Add(state);
      return new Coroutine();
    }

    public static void StepCoroutines() {
      for (int i = Coroutines.Count - 1; i >= 0; i--) {
        var state = Coroutines[i];
        if (Time.time < state.WaitUntilTime) {
          continue;
        }
        while (state.Stack.Count > 0) {
          var current = state.Stack.Peek();
          bool movedNext;
          try {
            movedNext = current.MoveNext();
          } catch (Exception ex) {
            Debug.LogException(ex);
            state.Stack.Clear();
            break;
          }
          if (!movedNext) {
            state.Stack.Pop();
            continue;
          }
          var yielded = current.Current;
          if (yielded is WaitForSeconds wfs) {
            state.WaitUntilTime = Time.time + wfs.Seconds;
            break;
          } else if (yielded is IEnumerator nested) {
            state.Stack.Push(nested);
            continue;
          } else {
            break;
          }
        }
        if (state.Stack.Count == 0) {
          Coroutines.RemoveAt(i);
        }
      }
    }

    private static void InvokeLifecycleMethod(MonoBehaviour mb, string methodName) {
      var type = mb.GetType();
      while (type != null && type != typeof(MonoBehaviour)) {
        var m = type.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (m != null) {
          m.Invoke(mb, null);
          return;
        }
        type = type.BaseType;
      }
    }
  }

  public static class Debug {
    public static void Log(object message) {
      Console.WriteLine(message);
      Application.InvokeLog(message?.ToString() ?? "", "", LogType.Log);
    }
    public static void LogFormat(string format, params object[] args) => Log(string.Format(format, args));
    public static void LogError(object message) {
      Console.WriteLine("ERROR: " + message);
      Application.InvokeLog(message?.ToString() ?? "", "", LogType.Error);
    }
    public static void LogException(Exception exception) {
      Console.WriteLine("EXCEPTION: " + exception);
      Application.InvokeLog(exception?.ToString() ?? "", exception?.StackTrace ?? "", LogType.Exception);
    }
  }

  public static class Application {
    public delegate void LogCallback(string condition, string stackTrace, LogType type);
    public static event LogCallback logMessageReceived;
    public static bool HasQuit { get; set; }
    public static RuntimePlatform platform => RuntimePlatform.LinuxPlayer;
    public static string persistentDataPath { get; } =
        WorkDirForProcess(Process.GetCurrentProcess().Id);
    internal static string WorkDirForProcess(int processId) =>
        Path.Combine(Path.GetTempPath(), "firebase_mono_testapp_" + processId);
    public static string temporaryCachePath => persistentDataPath;
    public static string streamingAssetsPath { get; set; } = Directory.GetCurrentDirectory();
    public static void Quit(int exitCode = 0) { HasQuit = true; }
    public static void OpenURL(string url) { }
    public static void SetStackTraceLogType(LogType logType, StackTraceLogType stackTraceType) { }
    internal static void InvokeLog(string condition, string stackTrace, LogType type) {
      logMessageReceived?.Invoke(condition, stackTrace, type);
    }
  }

  public static class Time {
    private static readonly DateTime _start = DateTime.UtcNow;
    public static float time => (float)(DateTime.UtcNow - _start).TotalSeconds;
    public static float realtimeSinceStartup => time;
  }

  public static class SystemInfo {
    public static string graphicsDeviceName => "MonoHeadlessGPU";
  }

  public static class Screen {
    public static int width => 1280;
    public static int height => 720;
  }

  public static class Input {
    public static bool GetKeyDown(KeyCode key) => false;
  }

  public class Texture2D : Object {
    public int width { get; set; }
    public int height { get; set; }
    private byte[] _rawData = Array.Empty<byte>();
    public Texture2D(int width, int height) { this.width = width; this.height = height; }
    public bool LoadImage(byte[] data) { _rawData = data ?? Array.Empty<byte>(); return true; }
    public byte[] EncodeToPNG() => _rawData;
  }
  public static class ImageConversion {
    public static byte[] EncodeToPNG(this Texture2D tex) => tex.EncodeToPNG();
  }

  public class GUIStyleState {
    public Color textColor { get; set; }
  }
  public class GUIStyle {
    public GUIStyle() { }
    public GUIStyle(GUIStyle other) { }
    public GUIStyleState normal { get; set; } = new GUIStyleState();
    public GUIStyleState active { get; set; } = new GUIStyleState();
    public int fontSize { get; set; }
    public bool stretchHeight { get; set; }
    public Vector2 CalcSize(GUIContent content) => Vector2.zero;
  }
  public class GUIContent {
    public GUIContent(string text) { }
    public GUIContent(string text, string tooltip) { }
  }
  public class GUISkin : Object {
    public GUIStyle button { get; set; } = new GUIStyle();
    public GUIStyle label { get; set; } = new GUIStyle();
    public GUIStyle textField { get; set; } = new GUIStyle();
    public GUIStyle textArea { get; set; } = new GUIStyle();
    public GUIStyle box { get; set; } = new GUIStyle();
  }
  public class GUILayoutOption { }
  public static class GUI {
    public static GUISkin skin { get; set; } = new GUISkin();
    public static bool enabled { get; set; } = true;
    public static Color contentColor { get; set; } = new Color(1, 1, 1, 1);
  }
  public static class GUILayout {
    public static GUILayoutOption Width(float width) => new GUILayoutOption();
    public static GUILayoutOption MinHeight(float minHeight) => new GUILayoutOption();
    public static GUILayoutOption ExpandWidth(bool expand) => new GUILayoutOption();
    public static void BeginArea(Rect screenRect) { }
    public static void EndArea() { }
    public static void BeginVertical(params GUILayoutOption[] options) { }
    public static void BeginVertical(GUIStyle style, params GUILayoutOption[] options) { }
    public static void EndVertical() { }
    public static void BeginHorizontal(params GUILayoutOption[] options) { }
    public static void EndHorizontal() { }
    public static Vector2 BeginScrollView(Vector2 scrollPosition, params GUILayoutOption[] options) => scrollPosition;
    public static Vector2 BeginScrollView(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, params GUILayoutOption[] options) => scrollPosition;
    public static void EndScrollView() { }
    public static void Label(string text, params GUILayoutOption[] options) { }
    public static void Label(string text, GUIStyle style, params GUILayoutOption[] options) { }
    public static bool Button(string text, params GUILayoutOption[] options) => false;
    public static bool Button(string text, GUIStyle style, params GUILayoutOption[] options) => false;
    public static bool Button(GUIContent content, params GUILayoutOption[] options) => false;
    public static string TextField(string text, params GUILayoutOption[] options) => text;
    public static string PasswordField(string password, char maskChar, params GUILayoutOption[] options) => password;
    public static string TextArea(string text, params GUILayoutOption[] options) => text;
    public static int SelectionGrid(int selected, string[] texts, int xCount, params GUILayoutOption[] options) => selected;
    public static void Space(float pixels) { }
  }

  public class AndroidJavaObject {
    public AndroidJavaObject(string className, params object[] args) { }
    public T Call<T>(string methodName, params object[] args) => default;
    public void Call(string methodName, params object[] args) { }
    public T GetStatic<T>(string fieldName) => default;
  }
  public class AndroidJavaClass : AndroidJavaObject {
    public AndroidJavaClass(string className) : base(className) { }
  }
}

namespace UnityEngine.Events {
  public delegate void UnityAction();
}

namespace UnityEngine.Networking {
  // Only has to compile: the FirebaseAI testapp uses UnityWebRequest for Android "jar:" paths
  // alone. A request reports failure, which sends that testapp to its own download path.
  public class DownloadHandler {
    public string text => "";
  }
  public class UnityWebRequestAsyncOperation {
    public event Action<UnityWebRequestAsyncOperation> completed {
      add { value?.Invoke(this); }
      remove { }
    }
  }
  public class UnityWebRequest {
    public enum Result { InProgress, Success, ConnectionError }
    public DownloadHandler downloadHandler { get; } = new DownloadHandler();
    public Result result => Result.ConnectionError;
    public static UnityWebRequest Get(string uri) => new UnityWebRequest();
    public UnityWebRequestAsyncOperation SendWebRequest() => new UnityWebRequestAsyncOperation();
  }
}

namespace Firebase.Sample {
  public static class MonoTestAppEntryPoint {
    private const string DummyGoogleServicesJson = @"{
  ""project_info"": {
    ""project_number"": ""123456789000"",
    ""firebase_url"": ""https://unity-test-app-b1baa.firebaseio.com"",
    ""project_id"": ""unity-test-app-b1baa"",
    ""storage_bucket"": ""unity-test-app-b1baa.appspot.com""
  },
  ""client"": [
    {
      ""client_info"": {
        ""mobilesdk_app_id"": ""1:123456789000:android:abcdef1234567890"",
        ""android_client_info"": {
          ""package_name"": ""com.google.firebase.unity.testapp""
        }
      },
      ""api_key"": [
        {
          ""current_key"": ""AIzaSyDummyKeyForLocalMonoTestRunner00000""
        }
      ]
    }
  ],
  ""configuration_version"": ""1""
}";

    private static readonly Dictionary<string, HashSet<string>> OfflineTestsByProduct =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase) {
          ["appcheck"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
          ["functions"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
          ["auth"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "TestCreateDestroy",
          },
          ["remoteconfig"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "TestSetConfigSettings",
            "TestDisplayData",
            "TestDisplayAllKeys",
          },
          ["storage"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "TestCreateDestroy",
            "TestCreateDestroyRace",
            "TestStorageReferenceNavigation",
            "TestUrl",
            "TestGetReference",
            "TestGetStorageInvalidUris",
            "TestGetStorageWrongBucket",
          },
          ["database"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "TestDbRefParent",
            "TestDbRefRoot",
            "TestDbRefChild",
            "TestDbRefPush",
            "TestDbRefKey",
            "TestDbRefToString",
            "TestQueryKeepSynced",
            "TestQueryReference",
            "TestCreateDestroy",
            "TestCreateDestroyRaceCondition",
            "TestLogLevel",
          },
          ["firestore"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "TestCanTraverseCollectionsAndDocuments",
            "TestCanTraverseCollectionAndDocumentParents",
            "TestSnapshotMetadataEqualsAndGetHashCode",
            "TestDefaultInstanceStable",
            "TestTerminate",
            "TestClearPersistence",
            "TestFirestoreSingleton",
            "TestAssertTaskFaults",
            "TestInternalExceptions",
            "TestQueryMethodOnAggregateQuery",
            "TestAggregateQueryEqualsAndGetHashCode",
            "TestQueryEqualsAndGetHashCode",
            "TestFirestoreDispose",
          },
          ["firebaseai"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "TestCreateModel_GoogleAI",
            "TestCreateModel_AgentPlatform",
            "TestFirebaseAIInstanceCaching",
            "InternalTestBasicReplyShort",
            "InternalTestFinishReasonExpanded",
            "InternalTestImageConfigSerialization",
            "InternalTestSpeechConfigSerialization",
            "InternalTestSpeechConfigValidations",
            "InternalTestCitations",
            "InternalTestBlockedSafetyWithMessage",
            "InternalTestFinishReasonSafetyNoContent",
            "InternalTestUnknownEnumSafetyRatings",
            "InternalTestFunctionCallWithArguments",
            "InternalTestMapsGrounding",
            "InternalTestGroundingMetadata_Empty",
            "InternalTestSegment_Empty",
            "InternalTestCountTokenResponse",
            "InternalTestBasicResponseLongUsageMetadata",
            "InternalTestGoogleAIBasicReplyShort",
            "InternalTestGoogleAICitations",
            "InternalTestThoughtSummary",
            "InternalTestCodeExecution",
            "InternalTestUrlContextMixedValidity",
            "InternalTestUsageMetadataWithCaching",
            "InternalTestRealtimeInputConfigSerialization",
          },
        };

    private static readonly string[] DefaultOfflineSuites = {
      "Analytics",
      "Auth",
      "Crashlytics",
      "Database",
      "FirebaseAI",
      "Firestore",
      "Installations",
      "Messaging",
      "RemoteConfig",
      "Storage",
    };

    // How long one suite may run. The parent waits a little longer before killing the child.
    private const double MaxSuiteSeconds = 300.0;

    public static int Main(string[] args) {
      int rc = Run(Assembly.GetExecutingAssembly(), args);
      Environment.Exit(rc);
      return rc;
    }

    private static string GetNamespaceProductName(Type handlerType) {
      string ns = handlerType.Namespace ?? "";
      const string prefix = "Firebase.Sample.";
      return ns.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
          ? ns.Substring(prefix.Length)
          : handlerType.FullName;
    }

    private static string SetupIsolatedWorkingDirectory(string initialCwd) {
      string workDir = UnityEngine.Application.persistentDataPath;
      Directory.CreateDirectory(workDir);

      string srcJson = Path.Combine(initialCwd, "google-services.json");
      string srcDesktopJson = Path.Combine(initialCwd, "google-services-desktop.json");
      string dstJson = Path.Combine(workDir, "google-services.json");
      string dstDesktopJson = Path.Combine(workDir, "google-services-desktop.json");

      if (File.Exists(srcJson)) {
        File.Copy(srcJson, dstJson, true);
        if (!File.Exists(srcDesktopJson)) {
          File.Copy(srcJson, dstDesktopJson, true);
        }
      }
      if (File.Exists(srcDesktopJson)) {
        File.Copy(srcDesktopJson, dstDesktopJson, true);
        if (!File.Exists(dstJson)) {
          File.Copy(srcDesktopJson, dstJson, true);
        }
      }
      if (!File.Exists(dstJson) && !File.Exists(dstDesktopJson)) {
        File.WriteAllText(dstJson, DummyGoogleServicesJson);
        File.WriteAllText(dstDesktopJson, DummyGoogleServicesJson);
      }

      Directory.SetCurrentDirectory(workDir);

      // Point streamingAssetsPath at firebaseai/testapp/Assets/StreamingAssets if located nearby
      // so offline JSON parsing tests load local TestData/*.json without hitting network.
      foreach (string root in new[] { initialCwd, AppContext.BaseDirectory }) {
        string cur = root;
        for (int depth = 0; depth < 7 && !string.IsNullOrEmpty(cur); depth++) {
          string candidate = Path.Combine(cur, "firebaseai", "testapp", "Assets", "StreamingAssets");
          if (Directory.Exists(candidate)) {
            UnityEngine.Application.streamingAssetsPath = candidate;
            break;
          }
          cur = Path.GetDirectoryName(cur);
        }
      }

      return File.ReadAllText(dstJson);
    }

    private static MethodInfo s_runOnMainThreadIntMethod;
    private static MethodInfo s_terminateMethod;

    private static void EnsureFirebaseHandlerInitialized() {
      Firebase.FirebaseApp.CheckAndFixDependenciesAsync().GetAwaiter().GetResult();
      if (s_runOnMainThreadIntMethod == null) {
        Assembly platformAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Firebase.Platform");
        Type fhType = platformAsm?.GetType("Firebase.Platform.FirebaseHandler");
        if (fhType != null) {
          MethodInfo genericRun = fhType.GetMethod(
              "RunOnMainThread",
              BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
          if (genericRun != null && genericRun.IsGenericMethodDefinition) {
            s_runOnMainThreadIntMethod = genericRun.MakeGenericMethod(typeof(int));
          }
          s_terminateMethod = fhType.GetMethod(
              "Terminate",
              BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }
      }
    }

    private static void RunOnFirebaseMainThread(Action action) {
      if (s_runOnMainThreadIntMethod != null) {
        Func<int> func = () => {
          action();
          return 0;
        };
        try {
          s_runOnMainThreadIntMethod.Invoke(null, new object[] { func });
          return;
        } catch (TargetInvocationException tie) when (tie.InnerException != null) {
          throw tie.InnerException;
        }
      }
      action();
    }

    private static void TerminateFirebaseHandler() {
      try {
        s_terminateMethod?.Invoke(null, null);
      } catch {
      }
    }

    public static int Run(Assembly testAppAssembly, string[] args) {
      string singleApi = null;
      bool offlineOnly = false;
      bool checkUnityRefs = false;

      foreach (string a in args) {
        if (a == "--check-no-unity-refs") {
          checkUnityRefs = true;
        } else if (a == "--offline") {
          offlineOnly = true;
        } else if (a.StartsWith("--api=", StringComparison.OrdinalIgnoreCase)) {
          singleApi = a.Substring("--api=".Length);
        } else {
          // A misspelt --offline must not fall through to running every suite online.
          Console.Error.WriteLine("Unknown argument: " + a);
          Console.Error.WriteLine(
              "Usage: MonoTestApp [--check-no-unity-refs] [--offline] [--api=<Product>]");
          return 2;
        }
      }

      if (checkUnityRefs) {
        string baseDir = AppContext.BaseDirectory;
        string[] dlls = Directory.GetFiles(baseDir, "*.dll")
            .Where(p => {
              string fn = Path.GetFileName(p);
              return fn.StartsWith("Firebase.", StringComparison.OrdinalIgnoreCase) ||
                     fn.Equals("Google.MiniJson.dll", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(p => p)
            .ToArray();
        foreach (string dll in dlls) {
          Assembly asm = Assembly.LoadFrom(dll);
          foreach (AssemblyName refName in asm.GetReferencedAssemblies()) {
            if (refName.Name.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase) ||
                refName.Name.StartsWith("UnityEditor", StringComparison.OrdinalIgnoreCase)) {
              Console.Error.WriteLine($"FAIL: {Path.GetFileName(dll)} references {refName.FullName}");
              return 1;
            }
          }
        }
        Console.WriteLine($"Verified {dlls.Length} SDK DLLs have zero UnityEngine/UnityEditor references.");
      }

      Type[] allHandlers = testAppAssembly.GetTypes()
          .Where(t => t.Name == "UIHandlerAutomated" && typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(t))
          .OrderBy(GetNamespaceProductName)
          .ToArray();

      // If no single --api was specified, orchestrate running each handler (with --offline,
      // each default offline suite) in an isolated child invocation of this binary.
      if (string.IsNullOrEmpty(singleApi)) {
        Type[] selected = offlineOnly
            ? allHandlers.Where(t => DefaultOfflineSuites.Contains(GetNamespaceProductName(t))).ToArray()
            : allHandlers;

        if (selected.Length == 0) {
          Console.Error.WriteLine("No UIHandlerAutomated suites found in " + testAppAssembly.FullName);
          return 1;
        }

        Console.WriteLine($"=== MonoTestApp: Running {selected.Length} product testapp suite(s) ===");
        int suitesPassed = 0;
        int suitesFailed = 0;
        string selfDll = testAppAssembly.Location;
        // Relaunch through the host running this process. `dotnet MonoTestApp.dll` needs the
        // DLL path again; the MonoTestApp apphost does not.
        string host = Environment.ProcessPath;
        bool hostIsDotnet = Path.GetFileNameWithoutExtension(host)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        int waitMs = (int)(MaxSuiteSeconds * 1000.0) + 30000;

        foreach (Type handlerType in selected) {
          string prodName = GetNamespaceProductName(handlerType);
          var childArgs = new List<string>();
          if (hostIsDotnet) {
            childArgs.Add($"\"{selfDll}\"");
          }
          childArgs.Add($"--api={prodName}");
          if (offlineOnly) childArgs.Add("--offline");

          var psi = new ProcessStartInfo {
            FileName = host,
            Arguments = string.Join(" ", childArgs),
            UseShellExecute = false,
          };
          using (Process proc = Process.Start(psi)) {
            if (!proc.WaitForExit(waitMs)) {
              try {
                proc.Kill();
              } catch {
              }
              Console.Error.WriteLine($"TIMEOUT: Suite {prodName} exceeded {waitMs / 1000}s and was terminated.");
              suitesFailed++;
            } else if (proc.ExitCode == 0) {
              suitesPassed++;
            } else {
              Console.Error.WriteLine(
                  $"FAILED: Suite {prodName} exited with code {proc.ExitCode}.");
              suitesFailed++;
            }
            // A child that crashed or was killed could not remove its working directory.
            DeleteWorkDir(proc.Id);
          }
        }

        // The default offline list must not shrink unnoticed: a suite on it that is not built
        // into this binary fails the run.
        if (offlineOnly) {
          foreach (string suite in DefaultOfflineSuites) {
            if (!selected.Any(t => GetNamespaceProductName(t) == suite)) {
              Console.Error.WriteLine(
                  $"MISSING: Suite {suite} is in the default offline list but is not built into " +
                  "this MonoTestApp. Build all products, or run one suite with --api=<Product>.");
              suitesFailed++;
            }
          }
        }

        Console.WriteLine($"=== MonoTestApp Summary: {suitesPassed} suite(s) passed, {suitesFailed} suite(s) failed ===");
        return suitesFailed == 0 ? 0 : 1;
      }

      string initialCwd = Directory.GetCurrentDirectory();
      string configJson = SetupIsolatedWorkingDirectory(initialCwd);

      Type targetHandlerType = allHandlers.FirstOrDefault(
          t => GetNamespaceProductName(t).Equals(singleApi, StringComparison.OrdinalIgnoreCase));

      if (targetHandlerType == null) {
        Console.Error.WriteLine($"Could not find UIHandlerAutomated for '{singleApi}' in {testAppAssembly.FullName}");
        return 1;
      }

      try {
        return RunSingleHandler(targetHandlerType, configJson, offlineOnly);
      } finally {
        TerminateFirebaseHandler();
        try {
          Directory.SetCurrentDirectory(initialCwd);
          if (Directory.Exists(UnityEngine.Application.persistentDataPath)) {
            Directory.Delete(UnityEngine.Application.persistentDataPath, true);
          }
        } catch {
        }
      }
    }

    private static int RunSingleHandler(Type handlerType, string configJson, bool offlineOnly) {
      EnsureFirebaseHandlerInitialized();

      Console.WriteLine("=== Running Mono TestApp: " + handlerType.FullName + " ===");
      UnityEngine.MonoBehaviour handler = null;
      RunOnFirebaseMainThread(() => {
        var go = new UnityEngine.GameObject("TestRunnerObject");
        handler = (UnityEngine.MonoBehaviour)go.AddComponent(handlerType);
        ConfigureDefaultPlaceholders(handler, configJson);
        UnityEngine.MonoBehaviourRunner.InitializePending();
      });

      FieldInfo runnerField = handlerType.GetField(
          "testRunner",
          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      FieldInfo passedField = typeof(AutomatedTestRunner).GetField(
          "passedTestsCount",
          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      FieldInfo failedField = typeof(AutomatedTestRunner).GetField(
          "failedTestsCount",
          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

      HashSet<string> offlineAllowlist = null;
      if (offlineOnly) {
        OfflineTestsByProduct.TryGetValue(GetNamespaceProductName(handlerType), out offlineAllowlist);
      }

      bool configuredRunner = false;
      var unmatchedTests = new List<string>();
      bool finished = false;
      int passedCount = 0;
      int failedCount = 0;
      var sw = Stopwatch.StartNew();

      while (!UnityEngine.Application.HasQuit && sw.Elapsed.TotalSeconds < MaxSuiteSeconds) {
        RunOnFirebaseMainThread(() => {
          if (runnerField != null && !configuredRunner) {
            var preRunner = runnerField.GetValue(handler) as AutomatedTestRunner;
            if (preRunner != null) {
              unmatchedTests = ConfigureTestRunner(preRunner, offlineAllowlist);
              configuredRunner = true;
            }
          }
          if (unmatchedTests.Count == 0) {
            UnityEngine.MonoBehaviourRunner.UpdateAll();
          }
        });

        if (unmatchedTests.Count > 0) {
          Console.Error.WriteLine(
              $"ERROR: the offline list for {GetNamespaceProductName(handlerType)} names " +
              $"{unmatchedTests.Count} test(s) that {handlerType.FullName} does not have: " +
              string.Join(", ", unmatchedTests) +
              ". Update OfflineTestsByProduct in MonoTestAppRunner.cs.");
          return 1;
        }

        if (runnerField != null) {
          var runner = runnerField.GetValue(handler) as AutomatedTestRunner;
          if (runner != null && runner.Finished) {
            finished = true;
            if (passedField != null) passedCount = (int)passedField.GetValue(runner);
            if (failedField != null) failedCount = (int)failedField.GetValue(runner);
            break;
          }
        }

        Thread.Sleep(25);
      }

      if (!finished) {
        Console.Error.WriteLine(UnityEngine.Application.HasQuit
            ? $"QUIT: {handlerType.FullName} called Application.Quit() before its tests finished."
            : $"TIMEOUT: {handlerType.FullName} did not finish within {MaxSuiteSeconds:F0}s.");
        return 1;
      }
      Console.WriteLine("=== Finished Mono TestApp: " + handlerType.FullName +
                        $" (PASS: {passedCount}, FAIL: {failedCount}, elapsed {sw.Elapsed.TotalSeconds:F1}s) ===");
      return (failedCount == 0 && passedCount > 0) ? 0 : 1;
    }

    // Removes every test that is not on the offline list. Returns the listed names that matched no
    // test, so that a test renamed or removed in a testapp fails the run instead of dropping out.
    private static List<string> ConfigureTestRunner(
        AutomatedTestRunner runner, HashSet<string> offlineAllowlist) {
      if (offlineAllowlist == null) {
        return new List<string>();
      }
      var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var testsField = typeof(AutomatedTestRunner).GetField(
          "tests", BindingFlags.Instance | BindingFlags.NonPublic);
      var testToRunField = typeof(AutomatedTestCase).GetField(
          "testToRun", BindingFlags.Instance | BindingFlags.NonPublic);
      var customNameField = typeof(AutomatedTestCase).GetField(
          "customName", BindingFlags.Instance | BindingFlags.NonPublic);
      if (testsField?.GetValue(runner) is IList testsList) {
        for (int i = testsList.Count - 1; i >= 0; i--) {
          object tc = testsList[i];
          var fn = testToRunField?.GetValue(tc) as Func<Task>;
          var customName = customNameField?.GetValue(tc) as string;
          string name = !string.IsNullOrEmpty(customName) ? customName : (fn?.Method.Name ?? "");
          if (offlineAllowlist.Contains(name)) {
            matched.Add(name);
          } else {
            testsList.RemoveAt(i);
          }
        }
      }
      return offlineAllowlist.Where(name => !matched.Contains(name))
          .OrderBy(name => name, StringComparer.Ordinal).ToList();
    }

    // Removes the working directory left by a child process that could not remove its own.
    private static void DeleteWorkDir(int processId) {
      try {
        string dir = UnityEngine.Application.WorkDirForProcess(processId);
        if (Directory.Exists(dir)) {
          Directory.Delete(dir, true);
        }
      } catch {
      }
    }

    private static string ExtractJsonStringField(string json, string key) {
      if (string.IsNullOrEmpty(json)) return null;
      string token = "\"" + key + "\"";
      int idx = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
      if (idx < 0) return null;
      int colon = json.IndexOf(':', idx + token.Length);
      if (colon < 0) return null;
      int pos = colon + 1;
      while (pos < json.Length && char.IsWhiteSpace(json[pos])) {
        pos++;
      }
      if (pos >= json.Length || json[pos] != '"') return null;
      int q2 = json.IndexOf('"', pos + 1);
      if (q2 < 0) return null;
      return json.Substring(pos + 1, q2 - pos - 1);
    }

    private static void ConfigureDefaultPlaceholders(object handler, string configJson) {
      string dbUrl = ExtractJsonStringField(configJson, "firebase_url");
      if (string.IsNullOrEmpty(dbUrl)) {
        dbUrl = "https://unity-test-app-b1baa.firebaseio.com/";
      } else if (!dbUrl.EndsWith("/")) {
        dbUrl += "/";
      }
      string bucket = ExtractJsonStringField(configJson, "storage_bucket");
      if (string.IsNullOrEmpty(bucket)) {
        bucket = "gs://unity-test-app-b1baa.appspot.com/";
      } else {
        if (!bucket.StartsWith("gs://", StringComparison.OrdinalIgnoreCase)) {
          bucket = "gs://" + bucket;
        }
        if (!bucket.EndsWith("/")) {
          bucket += "/";
        }
      }

      var type = handler.GetType();
      while (type != null && type != typeof(object)) {
        var fields = type.GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        foreach (var field in fields) {
          if (field.FieldType == typeof(string)) {
            var val = field.GetValue(handler) as string;
            if (val == "REPLACE_WITH_YOUR_DATABASE_URL") {
              field.SetValue(handler, dbUrl);
            } else if (val == "REPLACE_WITH_YOUR_STORAGE_BUCKET") {
              field.SetValue(handler, bucket);
            }
          } else if (field.FieldType == typeof(UnityEngine.Texture2D)) {
            if (field.GetValue(handler) == null) {
              var tex = new UnityEngine.Texture2D(2, 2);
              tex.LoadImage(new byte[] {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
                0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
                0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
                0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
                0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
                0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D,
                0xB0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
                0x44, 0xAE, 0x42, 0x60, 0x82
              });
              field.SetValue(handler, tex);
            }
          }
        }
        type = type.BaseType;
      }
    }
  }
}
