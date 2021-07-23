// Copyright 2017 Google Inc. All Rights Reserved.
//
// C# bindings for the Dynamic Links C++ interface.

%module FirebaseDynamicLinks

#ifdef USE_EXPORT_FIX
// Generate a function that we can reference to force linker
// to include the generated swig library symbols correctly
// see export_fix.cc for more details
%include "app/src/export_fix.h"
%{#include "app/src/export_fix.h"%}
#endif

%pragma(csharp) moduleclassmodifiers="internal sealed class"
%feature("flatnested");

%import "app/src/swig/app.i"
%include "app/src/swig/future.i"
%include "app/src/swig/null_check_this.i"
%include "app/src/swig/serial_dispose.i"

// Include require headers for the generated C++ module.
%{
#include "app/src/callback.h"
#include "dynamic_links/src/include/firebase/dynamic_links.h"
#include "dynamic_links/src/include/firebase/dynamic_links/components.h"
%}

// Rename all C# proxy classes for C++ objects and methods to *Internal so
// they're not exposed in the public interface.  This leaves them available to
// use by the implementation in firebase/dynamic_links/client/unity/src.
%rename("AndroidParametersInternal")
  firebase::dynamic_links::AndroidParameters;
%rename("DynamicLinkComponentsInternal")
  firebase::dynamic_links::DynamicLinkComponents;
%rename("DynamicLinkOptionsInternal")
  firebase::dynamic_links::DynamicLinkOptions;
%rename("GeneratedDynamicLinkInternal")
  firebase::dynamic_links::GeneratedDynamicLink;
%rename("GoogleAnalyticsParametersInternal")
  firebase::dynamic_links::GoogleAnalyticsParameters;
%rename("IOSParametersInternal")
  firebase::dynamic_links::IOSParameters;
%rename("ITunesConnectAnalyticsParametersInternal")
  firebase::dynamic_links::ITunesConnectAnalyticsParameters;
%rename("PathLengthInternal")
  firebase::dynamic_links::PathLength;
%rename("SocialMetaTagParametersInternal")
  firebase::dynamic_links::SocialMetaTagParameters;
%rename("GetShortLinkInternal")
  firebase::dynamic_links::GetShortLink;
%rename("GetLongLinkInternal")
  firebase::dynamic_links::GetLongLink;

// SWIG's default typemaps for const char* properties is to allocate a string
// when assigning a value to them but to never deallocate the string.  Override
// this behavior for all structures in dynamic links so that strings are
// deallocated we a new value is assigned.
%typemap(memberin) const char * {
  delete[] ((char*)$1);
  if ($input) {
    $1 = ($1_type) (new char[strlen((const char *)$input)+1]);
    strcpy((char *)$1, (const char *)$input);
  } else {
    $1 = 0;
  }
}

// Deallocate all strings in the dynamic links structures.
namespace firebase {
namespace dynamic_links {

%extend AndroidParameters {
  ~AndroidParameters() {
    if ($self) {
      delete[] ((char*)$self->package_name);
      delete[] ((char*)$self->fallback_url);
      delete $self;
    }
  }
}

%extend DynamicLinkComponents {
  ~DynamicLinkComponents() {
    if ($self) {
      delete[] ((char*)$self->link);
      // Don't delete domain_uri_prefix if it points to the internal string.
      if ($self->domain_uri_prefix != $self->domain_uri_prefix_with_scheme.c_str()) {
        delete[] ((char*)$self->domain_uri_prefix);
      }
      delete $self;
    }
  }
}

%extend GoogleAnalyticsParameters {
  ~GoogleAnalyticsParameters() {
    if ($self) {
      delete[] ((char*)$self->source);
      delete[] ((char*)$self->medium);
      delete[] ((char*)$self->campaign);
      delete[] ((char*)$self->term);
      delete[] ((char*)$self->content);
      delete $self;
    }
  }
}

%extend IOSParameters {
  ~IOSParameters() {
    if ($self) {
      delete[] ((char*)$self->bundle_id);
      delete[] ((char*)$self->fallback_url);
      delete[] ((char*)$self->custom_scheme);
      delete[] ((char*)$self->ipad_fallback_url);
      delete[] ((char*)$self->ipad_bundle_id);
      delete[] ((char*)$self->app_store_id);
      delete[] ((char*)$self->minimum_version);
      delete $self;
    }
  }
}

%extend ITunesConnectAnalyticsParameters {
  ~ITunesConnectAnalyticsParameters() {
    if ($self) {
      delete[] ((char*)($self->provider_token));
      delete[] ((char*)($self->affiliate_token));
      delete[] ((char*)($self->campaign_token));
      delete $self;
    }
  }
}

%extend SocialMetaTagParameters {
  ~SocialMetaTagParameters() {
    if ($self) {
      delete[] ((char*)($self->title));
      delete[] ((char*)($self->description));
      delete[] ((char*)($self->image_url));
      delete $self;
    }
  }
}

}  // namespace dynamic_links
}  // namespace firebase

// Generate a Task wrapper around Future<GeneratedDynamicLink>.
%SWIG_FUTURE(Future_GeneratedDynamicLinkInternal, GeneratedDynamicLinkInternal,
             internal, firebase::dynamic_links::GeneratedDynamicLink,
             FirebaseException)

%include "dynamic_links/src/include/firebase/dynamic_links/components.h"

%pragma(csharp) modulecode=%{
  internal class Listener : System.IDisposable {
    internal delegate void DynamicLinkReceivedDelegate(
        System.IntPtr dynamic_link_ptr);

    private DynamicLinkReceivedDelegate dynamicLinkReceivedDelegate =
        new DynamicLinkReceivedDelegate(DynamicLinkReceivedMethod);
    // Hold a reference to the default app until this object is finalized.
    private FirebaseApp app = Firebase.FirebaseApp.DefaultInstance;

    // Listener singleton.
    private static Listener listener;

    // Create the listener singleton.
    internal static Listener Create() {
      lock (typeof(Listener)) {
        if (listener != null) return listener;
        listener = new Listener();
        Platform.FirebaseHandler.DefaultInstance.ApplicationFocusChanged +=
            OnApplicationFocus;
        return listener;
      }
    }

    // Destroy the listener singleton.
    internal static void Destroy() {
      lock (typeof(Listener)) {
        if (listener == null) return;
        Platform.FirebaseHandler.DefaultInstance.ApplicationFocusChanged -=
            OnApplicationFocus;
        listener.Dispose();
        listener = null;
      }
    }

    // Fetch invites when the application focus changes.
    internal static void OnApplicationFocus(
        object sender,
        Platform.FirebaseHandler.ApplicationFocusChangedEventArgs
            eventArgs) {
      if (eventArgs.HasFocus) {
        LogUtil.LogMessage(Firebase.LogLevel.Debug,
                               "Fetch Pending Dynamic Links");
        FirebaseDynamicLinks.Fetch();
      }
    }

    // Setup callbacks from the ListenerImplC ++ class to this object.
    private Listener() {
      FirebaseDynamicLinks.SetListenerCallbacks(dynamicLinkReceivedDelegate);
    }

    // Disconnect callbacks from ListenerImpl.
    public void Dispose() {
      lock (typeof(Listener)) {
        if (listener == this) {
          System.Diagnostics.Debug.Assert(app != null);
          app = null;
          FirebaseDynamicLinks.SetListenerCallbacks(null);
          listener = null;
        }
      }
    }

    // Called from ListenerImpl::OnDynamicLinkReceived() via the
    // dynamicLinkReceivedDelegate.
    [MonoPInvokeCallback(typeof(DynamicLinkReceivedDelegate))]
    private static void DynamicLinkReceivedMethod(
        System.IntPtr dynamic_link_ptr) {
      ExceptionAggregator.Wrap(() => {
          Firebase.DynamicLinks.DynamicLinks.NotifyDynamicLinkReceived(
              new ReceivedDynamicLink {
                Url = Firebase.FirebaseApp.UrlStringToUri(
                    DynamicLinkGetUrl(dynamic_link_ptr)),
                MatchStrength = DynamicLinkGetMatchStrength(dynamic_link_ptr)
                });
        });
    }
  }
%}

%{
namespace firebase {
namespace dynamic_links {

// C++ implementation of the Listener interface that forwards links to the
// C# layer via the main thread.
class ListenerImpl : public Listener {
 public:
  // Function which is used to reference a C# delegate called from the
  // OnDynamicLinkReceived() method of this class.  SWIGSTDCALL is used here as
  // C# delegates *must* be called using the stdcall calling convention rather
  // than whatever the compiler defines.
  typedef void (SWIGSTDCALL *DynamicLinkReceivedCallback)(
      const DynamicLink* dynamic_link);

  // Called when a dynamic link arrives.
  void OnDynamicLinkReceived(const DynamicLink* dynamic_link) override {
    if (g_dynamic_link_received_callback) {
      callback::AddCallback(new callback::Callback1<DynamicLink>(
          *dynamic_link, DynamicLinkReceived));
    }
  }

  // Set C# delegates that should be called by this class.
  // This should only be called on the main thread.
  static void SetCallbacks(DynamicLinkReceivedCallback dynamic_link_received) {
    g_dynamic_link_received_callback = dynamic_link_received;
    auto *previous_listener = firebase::dynamic_links::SetListener(
        g_dynamic_link_received_callback ? new ListenerImpl : nullptr);
    if (previous_listener) delete previous_listener;
  }

 private:
  // Called on the main thread.  This method is called prior to the C# delegate
  // as we need to convert the calling convention to stdcall.
  static void DynamicLinkReceived(void* dynamic_link) {
    if (g_dynamic_link_received_callback) {
      g_dynamic_link_received_callback(
          reinterpret_cast<const DynamicLink*>(dynamic_link));
    }
  }
 private:
  // C# delegate.
  static DynamicLinkReceivedCallback g_dynamic_link_received_callback;
};

ListenerImpl::DynamicLinkReceivedCallback
    ListenerImpl::g_dynamic_link_received_callback = nullptr;

// Accessors for the DynamicLink struct.
// These methods are used in preference to generating a complete proxy object
// to just read the fields of the struct.
static std::string DynamicLinkGetUrl(const void* dynamic_link) {
  return reinterpret_cast<const DynamicLink*>(dynamic_link)->url;
}

static dynamic_links::LinkMatchStrength DynamicLinkGetMatchStrength(const void* dynamic_link) {
  return reinterpret_cast<const DynamicLink*>(dynamic_link)->match_strength;
}

static void SetListenerCallbacks(
    firebase::dynamic_links::ListenerImpl::DynamicLinkReceivedCallback
        received_callback) {
  ListenerImpl::SetCallbacks(received_callback);
}

}  // namespace dynamic_links
}  // namespace firebase
%}

// Tell swig that the callbacks and delegates are equivalent.
SWIG_MAP_CFUNC_TO_CSDELEGATE(
    firebase::dynamic_links::ListenerImpl::DynamicLinkReceivedCallback,
    Firebase.DynamicLinks.FirebaseDynamicLinks.Listener.DynamicLinkReceivedDelegate)

// All classes and methods of the dynamic links API do not need to be exposed to
// the end user.
%ignore firebase::dynamic_links::DynamicLink;
%ignore firebase::dynamic_links::Listener;
%ignore firebase::dynamic_links::Initialize;
%ignore firebase::dynamic_links::Terminate;
%ignore firebase::dynamic_links::SetListener;

namespace firebase {
namespace dynamic_links {
// Expose this via FirebaseDynamicLinks.Fetch().
%csmethodmodifiers Fetch "internal";

%csmethodmodifiers SetListenerCallbacks "internal";
void SetListenerCallbacks(
    firebase::dynamic_links::ListenerImpl::DynamicLinkReceivedCallback
        received_callback);

%csmethodmodifiers DynamicLinkGetUrl "internal";
static std::string DynamicLinkGetUrl(const void* dynamic_link);

%csmethodmodifiers DynamicLinkGetMatchStrength "internal";
static dynamic_links::LinkMatchStrength DynamicLinkGetMatchStrength(const void* dynamic_link);
}  // namespace invites
}  // namespace firebase

%include "dynamic_links/src/include/firebase/dynamic_links.h"
