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
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace UnityEngine {
  public enum LogType { Error, Assert, Warning, Log, Exception }
  public enum StackTraceLogType { None, ScriptOnly, Full }
  public enum RuntimePlatform {
    OSXEditor, OSXPlayer, WindowsPlayer, OSXWebPlayer, OSXDashboardPlayer,
    WindowsWebPlayer, WindowsEditor, IPhonePlayer, PS3, XBOX360, Android, NaCl,
    LinuxPlayer, FlashPlayer, LinuxEditor, WebGLPlayer, MetroPlayerX86,
    WSAPlayerX86 = MetroPlayerX86, MetroPlayerX64, WSAPlayerX64 = MetroPlayerX64,
    MetroPlayerARM, WSAPlayerARM = MetroPlayerARM, WP8Player, BB10Player,
    BlackBerryPlayer = BB10Player, TizenPlayer, PSP2, PS4, PSM, XboxOne,
    SamsungTVPlayer, WiiU, tvOS, Switch, Lumin, Stadia, CloudRendering,
    GameCoreXboxSeries, GameCoreXboxOne, PS5, EmbeddedLinuxArm64,
    EmbeddedLinuxArm32, EmbeddedLinuxX64, EmbeddedLinuxX86, Server
  }
  public enum KeyCode { None, Escape, Return, Space, Backspace }
  public enum ScreenOrientation {
    Portrait, PortraitUpsideDown, LandscapeLeft, LandscapeRight, AutoRotation,
    Unknown, Landscape = LandscapeLeft
  }
  public enum TouchPhase { Began, Moved, Stationary, Ended, Canceled }
  public enum TextureFormat { RGBA32, RGB24, ARGB32 }

  [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
  public sealed class RangeAttribute : Attribute {
    public readonly float min;
    public readonly float max;
    public RangeAttribute(float min, float max) {
      this.min = min;
      this.max = max;
    }
  }

  public class SerializeField : Attribute { }
  public class HeaderAttribute : Attribute { public HeaderAttribute(string h) { } }
  public class TooltipAttribute : Attribute { public TooltipAttribute(string t) { } }

  public struct Vector2 {
    public float x, y;
    public static Vector2 zero => new Vector2(0, 0);
    public Vector2(float x, float y) { this.x = x; this.y = y; }
  }
  public struct Vector3 {
    public float x, y, z;
    public static Vector3 zero => new Vector3(0, 0, 0);
    public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
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
    public static Color white => new Color(1, 1, 1, 1);
    public static Color black => new Color(0, 0, 0, 1);
    public static Color red => new Color(1, 0, 0, 1);
    public static Color green => new Color(0, 1, 0, 1);
    public static Color blue => new Color(0, 0, 1, 1);
    public static Color clear => new Color(0, 0, 0, 0);
    public static Color grey => new Color(0.5f, 0.5f, 0.5f, 1);
    public static Color gray => grey;
  }

  public class Object {
    public string name { get; set; }
    public static void Destroy(Object obj) { }
    public static void DontDestroyOnLoad(Object target) { }
    public static T FindObjectOfType<T>() where T : Object => null;
    public static implicit operator bool(Object exists) => exists != null;
  }
  public class Component : Object {
    public GameObject gameObject { get; set; }
    public Transform transform => gameObject?.transform;
    public T GetComponent<T>() where T : Component => gameObject?.GetComponent<T>();
  }
  public class Transform : Component { }
  public class GameObject : Object {
    private readonly List<Component> _components = new List<Component>();
    public Transform transform { get; }
    public GameObject() : this(null) { }
    public GameObject(string name) {
      this.name = name;
      this.transform = new Transform { gameObject = this };
    }
    public T AddComponent<T>() where T : Component, new() {
      var c = new T { gameObject = this };
      _components.Add(c);
      MonoBehaviourRunner.Register(c as MonoBehaviour);
      return c;
    }
    public Component AddComponent(Type t) {
      var c = (Component)Activator.CreateInstance(t);
      c.gameObject = this;
      _components.Add(c);
      MonoBehaviourRunner.Register(c as MonoBehaviour);
      return c;
    }
    public T GetComponent<T>() where T : Component {
      foreach (var c in _components) {
        if (c is T t) return t;
      }
      return null;
    }
  }
  public class Behaviour : Component {
    public bool enabled { get; set; } = true;
  }
  public class YieldInstruction { }
  public abstract class CustomYieldInstruction : IEnumerator {
    public abstract bool keepWaiting { get; }
    public object Current => null;
    public bool MoveNext() => keepWaiting;
    public void Reset() { }
  }
  public class WaitForSeconds : YieldInstruction {
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
  public class WWW : CustomYieldInstruction, IDisposable {
    public string url { get; }
    public string text { get; set; } = "";
    public byte[] bytes { get; set; } = Array.Empty<byte>();
    public string error { get; set; }
    public WWW(string url) { this.url = url; }
    public override bool keepWaiting => false;
    public void Dispose() { }
  }
  public class Coroutine : YieldInstruction {
    internal IEnumerator Routine;
  }
  public class MonoBehaviour : Behaviour {
    public static void print(object message) => Debug.Log(message);
    public Coroutine StartCoroutine(IEnumerator routine) {
      var c = new Coroutine { Routine = routine };
      MonoBehaviourRunner.StartCoroutine(routine);
      return c;
    }
  }

  public static class MonoBehaviourRunner {
    public static readonly List<MonoBehaviour> Behaviours = new List<MonoBehaviour>();
    private static readonly HashSet<MonoBehaviour> Initialized = new HashSet<MonoBehaviour>();
    private static readonly List<IEnumerator> Coroutines = new List<IEnumerator>();

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

    public static void StartCoroutine(IEnumerator r) {
      if (r != null) Coroutines.Add(r);
    }

    public static void StepCoroutines() {
      for (int i = Coroutines.Count - 1; i >= 0; i--) {
        if (!Coroutines[i].MoveNext()) {
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
    public static void LogWarning(object message) {
      Console.WriteLine("WARNING: " + message);
      Application.InvokeLog(message?.ToString() ?? "", "", LogType.Warning);
    }
    public static void LogWarningFormat(string format, params object[] args) => LogWarning(string.Format(format, args));
    public static void LogError(object message) {
      Console.WriteLine("ERROR: " + message);
      Application.InvokeLog(message?.ToString() ?? "", "", LogType.Error);
    }
    public static void LogErrorFormat(string format, params object[] args) => LogError(string.Format(format, args));
    public static void LogException(Exception exception) {
      Console.WriteLine("EXCEPTION: " + exception);
      Application.InvokeLog(exception?.ToString() ?? "", exception?.StackTrace ?? "", LogType.Exception);
    }
    public static void Assert(bool condition, string message = "") {
      if (!condition) LogError("Assertion failed: " + message);
    }
  }

  public static class Application {
    public delegate void LogCallback(string condition, string stackTrace, LogType type);
    public static event LogCallback logMessageReceived;
    public static bool HasQuit { get; set; }
    public static RuntimePlatform platform => RuntimePlatform.LinuxPlayer;
    public static string persistentDataPath { get; } = Path.Combine(Path.GetTempPath(), "firebase_mono_testapp");
    public static string temporaryCachePath => persistentDataPath;
    public static string dataPath { get; } = Directory.GetCurrentDirectory();
    public static string streamingAssetsPath { get; } = Directory.GetCurrentDirectory();
    public static string identifier => "com.google.firebase.unity.testapp";
    public static string version => "1.0";
    public static string unityVersion => "6000.0.0f1";
    public static bool isPlaying => !HasQuit;
    public static bool isEditor => false;
    public static int targetFrameRate { get; set; } = 60;
    public static void Quit(int exitCode = 0) { HasQuit = true; }
    public static void OpenURL(string url) { }
    public static void SetStackTraceLogType(LogType logType, StackTraceLogType stackTraceType) { }
    internal static void InvokeLog(string condition, string stackTrace, LogType type) {
      Directory.CreateDirectory(persistentDataPath);
      logMessageReceived?.Invoke(condition, stackTrace, type);
    }
  }

  public static class Time {
    private static readonly DateTime _start = DateTime.UtcNow;
    public static float time => (float)(DateTime.UtcNow - _start).TotalSeconds;
    public static float realtimeSinceStartup => time;
    public static float deltaTime => 0.033f;
    public static float unscaledDeltaTime => 0.033f;
    public static int frameCount { get; set; }
  }

  public static class SystemInfo {
    public static string deviceUniqueIdentifier => "mono-desktop-device-id";
    public static string operatingSystem => Environment.OSVersion.ToString();
    public static string deviceModel => "LinuxMonoDesktop";
    public static string graphicsDeviceName => "MonoHeadlessGPU";
  }

  public static class Screen {
    public static int width => 1280;
    public static int height => 720;
    public static float dpi => 96f;
    public static ScreenOrientation orientation { get; set; } = ScreenOrientation.LandscapeLeft;
  }

  public struct Touch {
    public Vector2 position;
    public Vector2 deltaPosition;
    public TouchPhase phase;
  }
  public static class Input {
    public static bool GetKeyDown(KeyCode key) => false;
    public static bool GetKey(KeyCode key) => false;
    public static int touchCount => 0;
    public static Touch GetTouch(int index) => new Touch();
    public static Vector3 mousePosition => Vector3.zero;
  }

  public class Font : Object { }
  public class Texture : Object {
    public virtual int width { get; set; }
    public virtual int height { get; set; }
  }
  public class Texture2D : Texture {
    private byte[] _rawData = Array.Empty<byte>();
    public Texture2D(int width, int height) { this.width = width; this.height = height; }
    public Texture2D(int width, int height, TextureFormat format, bool mipChain) { this.width = width; this.height = height; }
    public void SetPixel(int x, int y, Color color) { }
    public Color GetPixel(int x, int y) => Color.white;
    public Color[] GetPixels() => new Color[Math.Max(1, width * height)];
    public void SetPixels(Color[] colors) { }
    public void Apply() { }
    public bool LoadImage(byte[] data) { _rawData = data ?? Array.Empty<byte>(); return true; }
    public byte[] EncodeToPNG() => _rawData;
    public byte[] EncodeToJPG() => _rawData;
  }
  public static class ImageConversion {
    public static bool LoadImage(this Texture2D tex, byte[] data) => tex.LoadImage(data);
    public static byte[] EncodeToPNG(this Texture2D tex) => tex.EncodeToPNG();
    public static byte[] EncodeToJPG(this Texture2D tex) => tex.EncodeToJPG();
  }

  public class GUIStyleState {
    public Color textColor { get; set; }
    public Texture2D background { get; set; }
  }
  public class RectOffset {
    public int left, right, top, bottom;
    public RectOffset() { }
    public RectOffset(int l, int r, int t, int b) { left = l; right = r; top = t; bottom = b; }
  }
  public enum TextAnchor {
    UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight,
    LowerLeft, LowerCenter, LowerRight
  }
  public class GUIStyle {
    public GUIStyle() { }
    public GUIStyle(GUIStyle other) { }
    public GUIStyleState normal { get; set; } = new GUIStyleState();
    public GUIStyleState active { get; set; } = new GUIStyleState();
    public GUIStyleState hover { get; set; } = new GUIStyleState();
    public GUIStyleState focused { get; set; } = new GUIStyleState();
    public int fontSize { get; set; }
    public Font font { get; set; }
    public TextAnchor alignment { get; set; }
    public bool wordWrap { get; set; }
    public RectOffset margin { get; set; } = new RectOffset();
    public RectOffset padding { get; set; } = new RectOffset();
    public float fixedWidth { get; set; }
    public float fixedHeight { get; set; }
    public bool stretchWidth { get; set; }
    public bool stretchHeight { get; set; }
    public Vector2 CalcSize(GUIContent content) => Vector2.zero;
  }
  public class GUIContent {
    public string text { get; set; }
    public string tooltip { get; set; }
    public GUIContent() { }
    public GUIContent(string text) { this.text = text; }
    public GUIContent(string text, string tooltip) { this.text = text; this.tooltip = tooltip; }
  }
  public class GUISkin : Object {
    public Font font { get; set; }
    public GUIStyle button { get; set; } = new GUIStyle();
    public GUIStyle label { get; set; } = new GUIStyle();
    public GUIStyle textField { get; set; } = new GUIStyle();
    public GUIStyle textArea { get; set; } = new GUIStyle();
    public GUIStyle box { get; set; } = new GUIStyle();
    public GUIStyle toggle { get; set; } = new GUIStyle();
    public GUIStyle window { get; set; } = new GUIStyle();
    public GUIStyle horizontalSlider { get; set; } = new GUIStyle();
    public GUIStyle horizontalSliderThumb { get; set; } = new GUIStyle();
    public GUIStyle verticalScrollbar { get; set; } = new GUIStyle();
    public GUIStyle verticalScrollbarThumb { get; set; } = new GUIStyle();
    public GUIStyle customStyles { get; set; } = new GUIStyle();
    public GUIStyle GetStyle(string styleName) => new GUIStyle();
  }
  public class GUILayoutOption { }
  public static class GUI {
    public static GUISkin skin { get; set; } = new GUISkin();
    public static bool enabled { get; set; } = true;
    public static Color color { get; set; } = Color.white;
    public static Color backgroundColor { get; set; } = Color.white;
    public static Color contentColor { get; set; } = Color.white;
    public static Matrix4x4 matrix { get; set; }
    public static void Box(Rect position, string text) { }
    public static void Label(Rect position, string text) { }
    public static bool Button(Rect position, string text) => false;
    public static void FocusControl(string name) { }
    public static void SetNextControlName(string name) { }
  }
  public struct Matrix4x4 {
    public static Matrix4x4 identity => new Matrix4x4();
    public static Matrix4x4 TRS(Vector3 pos, Quaternion q, Vector3 s) => new Matrix4x4();
  }
  public struct Quaternion {
    public static Quaternion identity => new Quaternion();
  }
  public static class GUILayout {
    public static GUILayoutOption Width(float width) => new GUILayoutOption();
    public static GUILayoutOption Height(float height) => new GUILayoutOption();
    public static GUILayoutOption MinWidth(float minWidth) => new GUILayoutOption();
    public static GUILayoutOption MaxWidth(float maxWidth) => new GUILayoutOption();
    public static GUILayoutOption MinHeight(float minHeight) => new GUILayoutOption();
    public static GUILayoutOption MaxHeight(float maxHeight) => new GUILayoutOption();
    public static GUILayoutOption ExpandWidth(bool expand) => new GUILayoutOption();
    public static GUILayoutOption ExpandHeight(bool expand) => new GUILayoutOption();
    public static void BeginArea(Rect screenRect) { }
    public static void BeginArea(Rect screenRect, GUIStyle style) { }
    public static void EndArea() { }
    public static void BeginVertical(params GUILayoutOption[] options) { }
    public static void BeginVertical(GUIStyle style, params GUILayoutOption[] options) { }
    public static void BeginVertical(string text, GUIStyle style, params GUILayoutOption[] options) { }
    public static void EndVertical() { }
    public static void BeginHorizontal(params GUILayoutOption[] options) { }
    public static void BeginHorizontal(GUIStyle style, params GUILayoutOption[] options) { }
    public static void EndHorizontal() { }
    public static Vector2 BeginScrollView(Vector2 scrollPosition, params GUILayoutOption[] options) => scrollPosition;
    public static Vector2 BeginScrollView(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, params GUILayoutOption[] options) => scrollPosition;
    public static void EndScrollView() { }
    public static void Label(string text, params GUILayoutOption[] options) { }
    public static void Label(string text, GUIStyle style, params GUILayoutOption[] options) { }
    public static void Label(GUIContent content, params GUILayoutOption[] options) { }
    public static void Label(GUIContent content, GUIStyle style, params GUILayoutOption[] options) { }
    public static void Label(Texture image, params GUILayoutOption[] options) { }
    public static void Box(string text, params GUILayoutOption[] options) { }
    public static void Box(Texture image, params GUILayoutOption[] options) { }
    public static bool Button(string text, params GUILayoutOption[] options) => false;
    public static bool Button(string text, GUIStyle style, params GUILayoutOption[] options) => false;
    public static bool Button(GUIContent content, params GUILayoutOption[] options) => false;
    public static bool Button(GUIContent content, GUIStyle style, params GUILayoutOption[] options) => false;
    public static string TextField(string text, params GUILayoutOption[] options) => text;
    public static string TextField(string text, int maxLength, params GUILayoutOption[] options) => text;
    public static string TextField(string text, GUIStyle style, params GUILayoutOption[] options) => text;
    public static string PasswordField(string password, char maskChar, params GUILayoutOption[] options) => password;
    public static string TextArea(string text, params GUILayoutOption[] options) => text;
    public static string TextArea(string text, GUIStyle style, params GUILayoutOption[] options) => text;
    public static bool Toggle(bool value, string text, params GUILayoutOption[] options) => value;
    public static bool Toggle(bool value, string text, GUIStyle style, params GUILayoutOption[] options) => value;
    public static int SelectionGrid(int selected, string[] texts, int xCount, params GUILayoutOption[] options) => selected;
    public static int SelectionGrid(int selected, GUIContent[] contents, int xCount, params GUILayoutOption[] options) => selected;
    public static void Space(float pixels) { }
    public static void FlexibleSpace() { }
  }

  public static class JsonUtility {
    public static string ToJson(object obj) => "{}";
    public static T FromJson<T>(string json) where T : new() => new T();
  }
  public class TextAsset : Object {
    public string text { get; set; } = "";
    public byte[] bytes { get; set; } = Array.Empty<byte>();
  }
  public static class Resources {
    public static T Load<T>(string path) where T : Object => null;
  }

  public class AndroidJavaObject : IDisposable {
    public AndroidJavaObject(string className, params object[] args) { }
    public T Call<T>(string methodName, params object[] args) => default;
    public void Call(string methodName, params object[] args) { }
    public T CallStatic<T>(string methodName, params object[] args) => default;
    public void CallStatic(string methodName, params object[] args) { }
    public T Get<T>(string fieldName) => default;
    public T GetStatic<T>(string fieldName) => default;
    public void Set<T>(string fieldName, T val) { }
    public void Dispose() { }
  }
  public class AndroidJavaClass : AndroidJavaObject {
    public AndroidJavaClass(string className) : base(className) { }
  }
}

namespace UnityEngine.Events {
  public delegate void UnityAction();
}

namespace UnityEngine.Networking {
  public class DownloadHandler : IDisposable {
    public string text { get; set; } = "";
    public byte[] data { get; set; } = Array.Empty<byte>();
    public void Dispose() { }
  }
  public class DownloadHandlerBuffer : DownloadHandler { }
  public class UnityWebRequestAsyncOperation : UnityEngine.CustomYieldInstruction {
    private Action<UnityWebRequestAsyncOperation> _completed;
    public override bool keepWaiting => false;
    public bool isDone => true;
    public event Action<UnityWebRequestAsyncOperation> completed {
      add {
        _completed += value;
        if (isDone) value?.Invoke(this);
      }
      remove {
        _completed -= value;
      }
    }
  }
  public class UnityWebRequest : IDisposable {
    public enum Result { InProgress, Success, ConnectionError, ProtocolError, DataProcessingError }
    public string url { get; set; }
    public DownloadHandler downloadHandler { get; set; } = new DownloadHandlerBuffer();
    public long responseCode { get; set; } = 200;
    public Result result { get; set; } = Result.Success;
    public string error { get; set; }
    public bool isNetworkError => result == Result.ConnectionError;
    public bool isHttpError => result == Result.ProtocolError;
    public UnityWebRequest() { }
    public UnityWebRequest(string url) { this.url = url; }
    public static UnityWebRequest Get(string uri) => new UnityWebRequest(uri);
    public UnityWebRequestAsyncOperation SendWebRequest() {
      try {
        if (!string.IsNullOrEmpty(url) && !url.StartsWith("http://") && !url.StartsWith("https://") && File.Exists(url)) {
          downloadHandler.data = File.ReadAllBytes(url);
          downloadHandler.text = File.ReadAllText(url);
          responseCode = 200;
          result = Result.Success;
        } else {
          using (var client = new HttpClient()) {
            var resp = client.GetAsync(url).Result;
            responseCode = (long)resp.StatusCode;
            downloadHandler.data = resp.Content.ReadAsByteArrayAsync().Result;
            downloadHandler.text = resp.Content.ReadAsStringAsync().Result;
            result = resp.IsSuccessStatusCode ? Result.Success : Result.ProtocolError;
            if (!resp.IsSuccessStatusCode) error = resp.ReasonPhrase;
          }
        }
      } catch (Exception e) {
        result = Result.ConnectionError;
        error = e.Message;
      }
      return new UnityWebRequestAsyncOperation();
    }
    public void Dispose() { }
  }
}

namespace UnityEngine.SocialPlatforms.GameCenter {
  public class GameCenterPlatform { }
}

namespace Firebase.Sample {
  public static class MonoTestAppEntryPoint {
    public static int Run(Assembly testAppAssembly, string[] args) {
      Directory.CreateDirectory(UnityEngine.Application.persistentDataPath);

      var handlerType = testAppAssembly.GetTypes()
          .FirstOrDefault(t => t.Name == "UIHandlerAutomated" && typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(t));
      if (handlerType == null) {
        Console.Error.WriteLine("Could not find UIHandlerAutomated in " + testAppAssembly.FullName);
        return 1;
      }

      Console.WriteLine("=== Running Mono TestApp: " + handlerType.FullName + " ===");
      var go = new UnityEngine.GameObject("TestRunnerObject");
      var handler = (UnityEngine.MonoBehaviour)go.AddComponent(handlerType);

      ConfigureDefaultPlaceholders(handler);

      UnityEngine.MonoBehaviourRunner.InitializePending();

      float testTimeoutSec = 5.0f;
      if (float.TryParse(Environment.GetEnvironmentVariable("MONO_TEST_TIMEOUT_SECONDS"), out float customTimeout)) {
        testTimeoutSec = customTimeout;
      }
      int maxTests = 0;
      int.TryParse(Environment.GetEnvironmentVariable("MONO_TEST_MAX_TESTS"), out maxTests);
      double maxTotalSec = 30.0;
      if (double.TryParse(Environment.GetEnvironmentVariable("MONO_TEST_MAX_TOTAL_SECONDS"), out double customTotal)) {
        maxTotalSec = customTotal;
      }

      FieldInfo runnerField = handlerType.GetField(
          "testRunner",
          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

      bool configuredRunner = false;
      var sw = Stopwatch.StartNew();

      while (!UnityEngine.Application.HasQuit && sw.Elapsed.TotalSeconds < maxTotalSec) {
        UnityEngine.Time.frameCount++;
        UnityEngine.MonoBehaviourRunner.UpdateAll();

        if (runnerField != null) {
          var runner = runnerField.GetValue(handler) as AutomatedTestRunner;
          if (runner != null) {
            if (!configuredRunner) {
              runner.TestTimeoutSeconds = testTimeoutSec;
              if (maxTests > 0) {
                var testsField = typeof(AutomatedTestRunner).GetField(
                    "tests", BindingFlags.Instance | BindingFlags.NonPublic);
                if (testsField?.GetValue(runner) is IList testsList && testsList.Count > maxTests) {
                  while (testsList.Count > maxTests) {
                    testsList.RemoveAt(testsList.Count - 1);
                  }
                }
              }
              configuredRunner = true;
            }
            if (runner.Finished) {
              break;
            }
          }
        }

        Thread.Sleep(25);
      }

      Console.WriteLine("=== Finished Mono TestApp: " + handlerType.FullName + " (elapsed " + sw.Elapsed.TotalSeconds.ToString("F1") + "s) ===");
      Environment.Exit(0);
      return 0;
    }

    private static void ConfigureDefaultPlaceholders(object handler) {
      var type = handler.GetType();
      while (type != null && type != typeof(object)) {
        var fields = type.GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        foreach (var field in fields) {
          if (field.FieldType == typeof(string)) {
            var val = field.GetValue(handler) as string;
            if (val == "REPLACE_WITH_YOUR_DATABASE_URL") {
              field.SetValue(handler, "https://austinbenoit-sandbox-778231-default-rtdb.firebaseio.com/");
            } else if (val == "REPLACE_WITH_YOUR_STORAGE_BUCKET") {
              field.SetValue(handler, "gs://austinbenoit-sandbox-778231.appspot.com/");
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
