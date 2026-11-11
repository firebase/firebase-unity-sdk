/*
 * Copyright 2018 Google LLC
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

#if (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using Firebase.Platform;

namespace Firebase.Crashlytics {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class IOSImpl : IDisposable {
    private const string ObjCLib = "/usr/lib/libobjc.A.dylib", SystemLib = "/usr/lib/libSystem.B.dylib";
    private static readonly IntPtr RtldDefault = new IntPtr(-2);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void RecordInternalKeyValueDelegate(IntPtr nsKey, IntPtr nsValue);

    [DllImport(ObjCLib)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjCLib)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjCLib)] private static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(ObjCLib)] private static extern void objc_autoreleasePoolPop(IntPtr pool);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr receiver, IntPtr sel);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtrPtr(IntPtr receiver, IntPtr sel, IntPtr a1);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtrPtrPtr(IntPtr receiver, IntPtr sel, IntPtr a1, IntPtr a2);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtrPtrPtrNInt(IntPtr receiver, IntPtr sel, IntPtr a1, IntPtr a2, nint a3);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtrNUInt(IntPtr receiver, IntPtr sel, nuint a1);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern void SendVoidPtr(IntPtr receiver, IntPtr sel, IntPtr a1);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern void SendVoidPtrPtr(IntPtr receiver, IntPtr sel, IntPtr a1, IntPtr a2);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern void SendVoidBool(IntPtr receiver, IntPtr sel, [MarshalAs(UnmanagedType.I1)] bool a1);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SendBool(IntPtr receiver, IntPtr sel);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SendBoolPtr(IntPtr receiver, IntPtr sel, IntPtr a1);
    [DllImport(SystemLib)] private static extern IntPtr dlsym(IntPtr handle, string symbol);

    private static RecordInternalKeyValueDelegate s_internalKeyFn;
    private static bool s_internalKeyProbed;
    private readonly IntPtr _crashlyticsClass, _exceptionModelClass, _stackFrameClass, _nsStringClass, _nsMutableArrayClass;
    private bool _disposed;

    internal IOSImpl() {
      IntPtr pool = objc_autoreleasePoolPush();
      try {
        _crashlyticsClass = objc_getClass("FIRCrashlytics"); _exceptionModelClass = objc_getClass("FIRExceptionModel");
        _stackFrameClass = objc_getClass("FIRStackFrame"); _nsStringClass = objc_getClass("NSString");
        _nsMutableArrayClass = objc_getClass("NSMutableArray");
        IntPtr instance = GetInstance();
        if (instance == IntPtr.Zero) { Impl.SafeLog(PlatformLogLevel.Warning, "FIRCrashlytics is not available on this iOS/tvOS build."); return; }
        IntPtr selDevName = sel_registerName("setDevelopmentPlatformName:"), selDevVer = sel_registerName("setDevelopmentPlatformVersion:");
        if (RespondsTo(instance, selDevName)) SendVoidPtr(instance, selDevName, ToNSString("Unity"));
        if (RespondsTo(instance, selDevVer)) SendVoidPtr(instance, selDevVer, ToNSString(MetadataBuilder.CachedUnityVersion));
        string metadataJson = MetadataBuilder.GenerateMetadataJSON();
        if (!string.IsNullOrEmpty(metadataJson)) SetInternalKeyValue(instance, MetadataBuilder.METADATA_KEY, metadataJson);
      } finally { objc_autoreleasePoolPop(pool); }
    }

    internal bool IsInitialized => !_disposed && _crashlyticsClass != IntPtr.Zero && WithInstance(false, _ => true);
    internal bool DidCrashOnPreviousExecution() => WithInstance(false, inst => SendBool(inst, sel_registerName("didCrashDuringPreviousExecution")));
    internal void Log(string message) => WithInstance(true, inst => { SendVoidPtr(inst, sel_registerName("log:"), ToNSString(message)); return true; });
    internal void SetCustomKey(string key, string value) => WithInstance(true, inst => {
      if (key != null) SendVoidPtrPtr(inst, sel_registerName("setCustomValue:forKey:"), ToNSString(value), ToNSString(key));
      return true;
    });
    internal void SetUserId(string identifier) => WithInstance(true, inst => { SendVoidPtr(inst, sel_registerName("setUserID:"), ToNSString(identifier)); return true; });
    internal bool IsCrashlyticsCollectionEnabled() => WithInstance(false, inst => SendBool(inst, sel_registerName("isCrashlyticsCollectionEnabled")));
    internal void SetCrashlyticsCollectionEnabled(bool enabled) => WithInstance(true, inst => { SendVoidBool(inst, sel_registerName("setCrashlyticsCollectionEnabled:"), enabled); return true; });

    internal void RecordException(LoggedException loggedException, bool isFatal) {
      if (loggedException == null) return;
      StackFrameData[] frames = loggedException.Frames;
      // if for some reason we don't get stack trace from exception, we add current stack trace in
      // remove RecordException and Environment.StackTrace frame for fault blame on crashlytics sdk
      if (isFatal && frames.Length == 0) frames = StackTraceParser.ParseFrames(Environment.StackTrace, skipLeadingFrames: 2);
      _ = WithInstance(true, instance => {
        if (_exceptionModelClass == IntPtr.Zero || _stackFrameClass == IntPtr.Zero) return false;
        IntPtr framesArray = SendPtrNUInt(_nsMutableArrayClass, sel_registerName("arrayWithCapacity:"), (nuint)frames.Length);
        IntPtr selAddObj = sel_registerName("addObject:"), selWithSymbol = sel_registerName("stackFrameWithSymbol:file:line:"), selWithAddress = sel_registerName("stackFrameWithAddress:");
        for (int i = 0; i < frames.Length; i++) {
          StackFrameData f = frames[i];
          IntPtr nativeFrame = f.IsAddressOnly
              ? SendPtrNUInt(_stackFrameClass, selWithAddress, (nuint)f.Address)
              : SendPtrPtrPtrNInt(_stackFrameClass, selWithSymbol, ToNSString(f.MethodName), ToNSString(f.FileName), (nint)f.LineNumber);
          if (nativeFrame != IntPtr.Zero) SendVoidPtr(framesArray, selAddObj, nativeFrame);
        }
        IntPtr model = SendPtrPtrPtr(_exceptionModelClass, sel_registerName("exceptionModelWithName:reason:"), ToNSString(loggedException.Name), ToNSString(loggedException.Message));
        if (model == IntPtr.Zero) return false;
        SendVoidPtr(model, sel_registerName("setStackTrace:"), framesArray);
        if (isFatal) {
          IntPtr selOnDemand = sel_registerName("setOnDemand:"), selIsFatal = sel_registerName("setIsFatal:"), selRecordOnDemand = sel_registerName("recordOnDemandExceptionModel:");
          if (RespondsTo(model, selOnDemand)) SendVoidBool(model, selOnDemand, true);
          if (RespondsTo(model, selIsFatal)) SendVoidBool(model, selIsFatal, true);
          if (RespondsTo(instance, selRecordOnDemand)) { SendVoidPtr(instance, selRecordOnDemand, model); return true; }
        }
        SendVoidPtr(instance, sel_registerName("recordExceptionModel:"), model);
        return true;
      });
    }

    private T WithInstance<T>(T fallback, Func<IntPtr, T> work) {
      IntPtr pool = objc_autoreleasePoolPush();
      try { IntPtr inst = GetInstance(); return inst != IntPtr.Zero ? work(inst) : fallback; }
      finally { objc_autoreleasePoolPop(pool); }
    }
    private IntPtr GetInstance() => !_disposed && _crashlyticsClass != IntPtr.Zero ? SendPtr(_crashlyticsClass, sel_registerName("crashlytics")) : IntPtr.Zero;
    private static bool RespondsTo(IntPtr obj, IntPtr sel) => obj != IntPtr.Zero && sel != IntPtr.Zero && SendBoolPtr(obj, sel_registerName("respondsToSelector:"), sel);
    private void SetInternalKeyValue(IntPtr instance, string key, string value) {
      if (!s_internalKeyProbed) {
        s_internalKeyProbed = true;
        IntPtr sym = dlsym(RtldDefault, "FIRCLSUserLoggingRecordInternalKeyValue");
        if (sym != IntPtr.Zero) s_internalKeyFn = Marshal.GetDelegateForFunctionPointer<RecordInternalKeyValueDelegate>(sym);
      }
      IntPtr nsKey = ToNSString(key), nsVal = ToNSString(value);
      if (s_internalKeyFn != null) s_internalKeyFn(nsKey, nsVal);
      else SendVoidPtrPtr(instance, sel_registerName("setCustomValue:forKey:"), nsVal, nsKey);
    }
    private IntPtr ToNSString(string value) {
      if (value == null || _nsStringClass == IntPtr.Zero) return IntPtr.Zero;
      byte[] rented = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(value.Length) + 1);
      GCHandle pin = default;
      try {
        int written = Encoding.UTF8.GetBytes(value, 0, value.Length, rented, 0);
        rented[written] = 0; pin = GCHandle.Alloc(rented, GCHandleType.Pinned);
        return SendPtrPtr(_nsStringClass, sel_registerName("stringWithUTF8String:"), pin.AddrOfPinnedObject());
      } finally { if (pin.IsAllocated) pin.Free(); ArrayPool<byte>.Shared.Return(rented); }
    }
    public void Dispose() => _disposed = true;
  }
}
#endif
