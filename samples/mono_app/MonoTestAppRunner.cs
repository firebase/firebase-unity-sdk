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
using System.Runtime.InteropServices;
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
    internal MonoBehaviourRunner.CoroutineState State;
  }
  public class MonoBehaviour : Behaviour {
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

    public static void Reset() {
      Behaviours.Clear();
      Initialized.Clear();
      Coroutines.Clear();
      Application.HasQuit = false;
    }

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
      return new Coroutine { State = state };
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
          } else if (yielded is Coroutine c) {
            if (c.State != null && c.State.Stack.Count > 0) {
              break;
            }
            continue;
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
    public static string persistentDataPath { get; } = Path.Combine(
        Path.GetTempPath(), "firebase_mono_testapp_" + Process.GetCurrentProcess().Id);
    public static string temporaryCachePath => persistentDataPath;
    public static string dataPath { get; set; } = Directory.GetCurrentDirectory();
    public static string streamingAssetsPath { get; set; } = Directory.GetCurrentDirectory();
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
    public static Color contentColor { get; set; } = new Color(1, 1, 1, 1);
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
            "TestGetInstanceByURL",
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
            "TestFirestoreSettings",
            "TestDocumentReferenceTaskFailures",
            "TestCollectionReferenceTaskFailures",
            "TestAssertTaskFaults",
            "TestInternalExceptions",
            "TestListenerRegistrationDispose",
            "TestQueryMethodOnAggregateQuery",
            "TestAggregateQueryEqualsAndGetHashCode",
            "TestQueryEqualsAndGetHashCode",
            "TestInvalidArgumentAssertions",
            "TestFirestoreDispose",
            "TestFirestoreGetInstance",
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
            "InternalTestAgentPlatformGrounding",
            "InternalTestGoogleAIGrounding",
            "InternalTestGoogleAIGroundingEmptyChunks",
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

    private static string NormalizeApiKey(string name) {
      return (name ?? "").Replace("_", "").Replace("-", "").Trim().ToLowerInvariant();
    }

    private static void RegisterNativeResolver(string extraNativeDir) {
#if NET5_0_OR_GREATER || NETCOREAPP3_1_OR_GREATER
      string baseDir = AppContext.BaseDirectory;
      string buildRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
      var searchDirs = new List<string>();
      if (!string.IsNullOrEmpty(extraNativeDir) && Directory.Exists(extraNativeDir)) {
        searchDirs.Add(Path.GetFullPath(extraNativeDir));
      }
      searchDirs.Add(baseDir);
      if (Directory.Exists(buildRoot)) {
        searchDirs.Add(buildRoot);
        foreach (string sub in new[] {
          "analytics", "app_check", "auth", "crashlytics", "database",
          "firestore", "installations", "messaging", "remote_config", "storage"
        }) {
          string p = Path.Combine(buildRoot, sub);
          if (Directory.Exists(p)) searchDirs.Add(p);
        }
      }

      DllImportResolver resolver = (libraryName, assembly, searchPath) => {
        foreach (string dir in searchDirs) {
          foreach (string candidate in new[] {
            Path.Combine(dir, libraryName + ".so"),
            Path.Combine(dir, "lib" + libraryName + ".so"),
            Path.Combine(dir, libraryName + ".bundle"),
            Path.Combine(dir, "lib" + libraryName + ".dylib"),
            Path.Combine(dir, libraryName + ".dll"),
          }) {
            if (File.Exists(candidate)) {
              return NativeLibrary.Load(candidate);
            }
          }
        }
        return IntPtr.Zero;
      };

      var registered = new HashSet<Assembly>();
      foreach (string dll in Directory.GetFiles(baseDir, "Firebase.*.dll")) {
        try {
          Assembly asm = Assembly.LoadFrom(dll);
          if (registered.Add(asm)) {
            NativeLibrary.SetDllImportResolver(asm, resolver);
          }
        } catch {
        }
      }
#endif
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
      string apisCsv = null;
      string nativeDir = null;
      string filterCsv = null;
      bool offlineOnly = false;
      bool listOnly = false;
      bool checkUnityRefs = false;
      int maxTestsArg = 0;
      float testTimeoutArg = 0f;
      double totalTimeoutArg = 0.0;

      for (int i = 0; i < args.Length; i++) {
        string a = args[i];
        if (a == "--list") {
          listOnly = true;
        } else if (a == "--check-no-unity-refs") {
          checkUnityRefs = true;
        } else if (a == "--offline") {
          offlineOnly = true;
        } else if (a.StartsWith("--api=", StringComparison.OrdinalIgnoreCase)) {
          singleApi = a.Substring("--api=".Length);
        } else if (a == "--api" && i + 1 < args.Length) {
          singleApi = args[++i];
        } else if (a.StartsWith("--apis=", StringComparison.OrdinalIgnoreCase)) {
          apisCsv = a.Substring("--apis=".Length);
        } else if (a == "--apis" && i + 1 < args.Length) {
          apisCsv = args[++i];
        } else if (a.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase)) {
          filterCsv = a.Substring("--filter=".Length);
        } else if (a == "--filter" && i + 1 < args.Length) {
          filterCsv = args[++i];
        } else if (a.StartsWith("--native-dir=", StringComparison.OrdinalIgnoreCase)) {
          nativeDir = a.Substring("--native-dir=".Length);
        } else if (a == "--native-dir" && i + 1 < args.Length) {
          nativeDir = args[++i];
        } else if (a.StartsWith("--max-tests=", StringComparison.OrdinalIgnoreCase)) {
          int.TryParse(a.Substring("--max-tests=".Length), out maxTestsArg);
        } else if (a.StartsWith("--test-timeout=", StringComparison.OrdinalIgnoreCase)) {
          float.TryParse(a.Substring("--test-timeout=".Length), out testTimeoutArg);
        } else if (a.StartsWith("--timeout=", StringComparison.OrdinalIgnoreCase)) {
          double.TryParse(a.Substring("--timeout=".Length), out totalTimeoutArg);
        }
      }

      RegisterNativeResolver(nativeDir);

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

      if (listOnly) {
        foreach (Type t in allHandlers) {
          Console.WriteLine($"{GetNamespaceProductName(t)} ({t.FullName})");
        }
        return 0;
      }

      // If no single --api was specified, select the requested handlers and
      // orchestrate running each handler in an isolated child invocation of this binary.
      if (string.IsNullOrEmpty(singleApi)) {
        if (offlineOnly && string.IsNullOrEmpty(apisCsv)) {
          apisCsv = string.Join(",", DefaultOfflineSuites);
        }
        HashSet<string> filter = null;
        if (!string.IsNullOrEmpty(apisCsv) && !apisCsv.Equals("all", StringComparison.OrdinalIgnoreCase)) {
          filter = new HashSet<string>(
              apisCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                  .Select(NormalizeApiKey));
        }

        Type[] selected = filter == null
            ? allHandlers
            : allHandlers.Where(t => filter.Contains(NormalizeApiKey(GetNamespaceProductName(t)))).ToArray();

        if (selected.Length == 0) {
          Console.Error.WriteLine("No matching UIHandlerAutomated suites found for --apis=" + (apisCsv ?? "<all>"));
          return 1;
        }

        Console.WriteLine($"=== MonoTestApp: Running {selected.Length} product testapp suite(s) ===");
        int suitesPassed = 0;
        int suitesFailed = 0;
        string selfDll = testAppAssembly.Location;
        int waitMs = (int)((totalTimeoutArg > 0 ? totalTimeoutArg : 300.0) * 1000.0) + 30000;

        foreach (Type handlerType in selected) {
          string prodName = GetNamespaceProductName(handlerType);
          var childArgs = new List<string>();
          bool isDll = selfDll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
          bool isExeOnUnix = selfDll.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                             Environment.OSVersion.Platform != PlatformID.Win32NT;
          string fileName;
          bool prependSelfPath;
          if (isDll) {
            fileName = "dotnet";
            prependSelfPath = true;
#if NET6_0_OR_GREATER
            if (!string.IsNullOrEmpty(Environment.ProcessPath)) {
              fileName = Environment.ProcessPath;
              string hostName = Path.GetFileNameWithoutExtension(fileName);
              prependSelfPath = hostName.Equals("dotnet", StringComparison.OrdinalIgnoreCase);
            }
#endif
          } else if (isExeOnUnix) {
            fileName = "mono";
            prependSelfPath = true;
          } else {
            fileName = selfDll;
            prependSelfPath = false;
          }
          if (prependSelfPath) {
            childArgs.Add($"\"{selfDll}\"");
          }
          childArgs.Add($"--api={prodName}");
          if (offlineOnly) childArgs.Add("--offline");
          if (!string.IsNullOrEmpty(filterCsv)) childArgs.Add($"--filter=\"{filterCsv}\"");
          if (!string.IsNullOrEmpty(nativeDir)) childArgs.Add($"--native-dir=\"{nativeDir}\"");
          if (maxTestsArg > 0) childArgs.Add($"--max-tests={maxTestsArg}");
          if (testTimeoutArg > 0) childArgs.Add($"--test-timeout={testTimeoutArg}");
          if (totalTimeoutArg > 0) childArgs.Add($"--timeout={totalTimeoutArg}");

          var psi = new ProcessStartInfo {
            FileName = fileName,
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
              suitesFailed++;
            }
          }
        }

        Console.WriteLine($"=== MonoTestApp Summary: {suitesPassed} suite(s) passed, {suitesFailed} suite(s) failed ===");
        return suitesFailed == 0 ? 0 : 1;
      }

      string initialCwd = Directory.GetCurrentDirectory();
      string configJson = SetupIsolatedWorkingDirectory(initialCwd);

      Type targetHandlerType = string.IsNullOrEmpty(singleApi)
          ? allHandlers.FirstOrDefault()
          : allHandlers.FirstOrDefault(t => NormalizeApiKey(GetNamespaceProductName(t)) == NormalizeApiKey(singleApi) ||
                                            t.FullName.Equals(singleApi, StringComparison.OrdinalIgnoreCase));

      if (targetHandlerType == null) {
        Console.Error.WriteLine($"Could not find UIHandlerAutomated for '{singleApi ?? "<default>"}' in {testAppAssembly.FullName}");
        return 1;
      }

      try {
        return RunSingleHandler(targetHandlerType, configJson, offlineOnly, filterCsv, maxTestsArg, testTimeoutArg, totalTimeoutArg);
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

    private static int RunSingleHandler(
        Type handlerType,
        string configJson,
        bool offlineOnly,
        string filterCsv,
        int maxTestsArg,
        float testTimeoutArg,
        double totalTimeoutArg) {
      UnityEngine.MonoBehaviourRunner.Reset();
      EnsureFirebaseHandlerInitialized();

      Console.WriteLine("=== Running Mono TestApp: " + handlerType.FullName + " ===");
      UnityEngine.MonoBehaviour handler = null;
      RunOnFirebaseMainThread(() => {
        var go = new UnityEngine.GameObject("TestRunnerObject");
        handler = (UnityEngine.MonoBehaviour)go.AddComponent(handlerType);
        ConfigureDefaultPlaceholders(handler, configJson);
        UnityEngine.MonoBehaviourRunner.InitializePending();
      });

      bool hasCustomTimeout = testTimeoutArg > 0;
      float customTimeout = testTimeoutArg;
      if (!hasCustomTimeout && float.TryParse(
          Environment.GetEnvironmentVariable("MONO_TEST_TIMEOUT_SECONDS"),
          out float envTimeout)) {
        hasCustomTimeout = true;
        customTimeout = envTimeout;
      }

      int maxTests = maxTestsArg;
      if (maxTests <= 0) {
        int.TryParse(Environment.GetEnvironmentVariable("MONO_TEST_MAX_TESTS"), out maxTests);
      }

      double maxTotalSec = totalTimeoutArg > 0 ? totalTimeoutArg : 300.0;
      if (totalTimeoutArg <= 0 && double.TryParse(
          Environment.GetEnvironmentVariable("MONO_TEST_MAX_TOTAL_SECONDS"),
          out double envTotal)) {
        maxTotalSec = envTotal;
      }

      FieldInfo runnerField = handlerType.GetField(
          "testRunner",
          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      FieldInfo tlmField = typeof(AutomatedTestRunner).GetField(
          "testLabManager",
          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      FieldInfo passedField = typeof(AutomatedTestRunner).GetField(
          "passedTestsCount",
          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      FieldInfo failedField = typeof(AutomatedTestRunner).GetField(
          "failedTestsCount",
          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

      string prodKey = NormalizeApiKey(GetNamespaceProductName(handlerType));
      HashSet<string> offlineAllowlist = null;
      if (offlineOnly) {
        OfflineTestsByProduct.TryGetValue(prodKey, out offlineAllowlist);
      }
      string[] filterTerms = string.IsNullOrEmpty(filterCsv)
          ? null
          : filterCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

      bool configuredRunner = false;
      bool finished = false;
      int passedCount = 0;
      int failedCount = 0;
      var sw = Stopwatch.StartNew();

      while (!UnityEngine.Application.HasQuit && sw.Elapsed.TotalSeconds < maxTotalSec) {
        RunOnFirebaseMainThread(() => {
          UnityEngine.Time.frameCount++;
          if (runnerField != null && !configuredRunner) {
            var preRunner = runnerField.GetValue(handler) as AutomatedTestRunner;
            if (preRunner != null) {
              ConfigureTestRunner(preRunner, hasCustomTimeout, customTimeout, offlineAllowlist, filterTerms, maxTests);
              configuredRunner = true;
            }
          }
          UnityEngine.MonoBehaviourRunner.UpdateAll();
          if (runnerField != null && !configuredRunner) {
            var postRunner = runnerField.GetValue(handler) as AutomatedTestRunner;
            if (postRunner != null) {
              ConfigureTestRunner(postRunner, hasCustomTimeout, customTimeout, offlineAllowlist, filterTerms, maxTests);
              configuredRunner = true;
            }
          }
        });

        if (runnerField != null) {
          var runner = runnerField.GetValue(handler) as AutomatedTestRunner;
          if (runner != null && runner.Finished) {
            finished = true;
            if (passedField != null) passedCount = (int)passedField.GetValue(runner);
            if (failedField != null) failedCount = (int)failedField.GetValue(runner);
            var tlm = tlmField?.GetValue(runner) as Firebase.TestLab.TestLabManager;
            tlm?.NotifyHarnessTestIsComplete();
            break;
          }
        }

        Thread.Sleep(25);
      }

      Console.WriteLine("=== Finished Mono TestApp: " + handlerType.FullName +
                        $" (PASS: {passedCount}, FAIL: {failedCount}, elapsed {sw.Elapsed.TotalSeconds:F1}s) ===");
      return (finished && failedCount == 0 && passedCount > 0) ? 0 : 1;
    }

    private static void ConfigureTestRunner(
        AutomatedTestRunner runner,
        bool hasCustomTimeout,
        float customTimeout,
        HashSet<string> offlineAllowlist,
        string[] filterTerms,
        int maxTests) {
      if (hasCustomTimeout) {
        runner.TestTimeoutSeconds = customTimeout;
      }
      var testsField = typeof(AutomatedTestRunner).GetField(
          "tests", BindingFlags.Instance | BindingFlags.NonPublic);
      var testToRunField = typeof(AutomatedTestCase).GetField(
          "testToRun", BindingFlags.Instance | BindingFlags.NonPublic);
      var customNameField = typeof(AutomatedTestCase).GetField(
          "customName", BindingFlags.Instance | BindingFlags.NonPublic);
      if (testsField?.GetValue(runner) is IList testsList) {
        if (offlineAllowlist != null || filterTerms != null) {
          for (int i = testsList.Count - 1; i >= 0; i--) {
            object tc = testsList[i];
            var fn = testToRunField?.GetValue(tc) as Func<Task>;
            var customName = customNameField?.GetValue(tc) as string;
            string name = !string.IsNullOrEmpty(customName) ? customName : (fn?.Method.Name ?? "");
            bool keep = true;
            if (offlineAllowlist != null && !offlineAllowlist.Contains(name)) {
              keep = false;
            }
            if (keep && filterTerms != null) {
              keep = filterTerms.Any(term => name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            if (!keep) {
              testsList.RemoveAt(i);
            }
          }
        }
        if (maxTests > 0 && testsList.Count > maxTests) {
          while (testsList.Count > maxTests) {
            testsList.RemoveAt(testsList.Count - 1);
          }
        }
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
