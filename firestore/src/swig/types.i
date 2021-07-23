// Copyright 2019 Google Inc. All Rights Reserved.
//
// The SWIG interface to wrap Firestore types that come from `third_party`.



%include "stdint.i"
%include "std_string.i"

%import "firestore/src/swig/proxy_helpers.i"
%include "app/src/swig/null_check_this.i"

// Generate a C# wrapper for GeoPoint.
SWIG_CREATE_PROXY(firebase::firestore::GeoPoint)
%rename("%s") firebase::firestore::GeoPoint::GeoPoint(double, double);
%rename("%s") firebase::firestore::GeoPoint::latitude;
%rename("%s") firebase::firestore::GeoPoint::longitude;
%include \firebase/firestore/geo_point.h"

// Generate a C# wrapper for Timestamp.
SWIG_CREATE_PROXY(firebase::Timestamp)
%rename("%s") firebase::Timestamp::Timestamp;
%rename("%s") firebase::Timestamp::Now;
%rename("%s") firebase::Timestamp::seconds;
%rename("%s") firebase::Timestamp::nanoseconds;
%rename("%s") firebase::Timestamp::ToString;
%csmethodmodifiers ToString "public override";
%include \firebase/firestore/timestamp.h"

// Generate Error enum.
%rename("FirestoreError") firebase::firestore::Error;
%rename("%(regex:/Error(.*)/\\1/)s", %$isenumitem) "";
%include \firebase/firestore/firestore_errors.h"

// # LINT.ThenChange(//depot/google3/firebase/firestore/client/unity/generated/src/last-updated.txt)
