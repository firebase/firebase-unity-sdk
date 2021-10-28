/*
 * Copyright 2021 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

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
%include "firebase/firestore/geo_point.h"

// Generate a C# wrapper for Timestamp.
SWIG_CREATE_PROXY(firebase::Timestamp)
%rename("%s") firebase::Timestamp::Timestamp;
%rename("%s") firebase::Timestamp::Now;
%rename("%s") firebase::Timestamp::seconds;
%rename("%s") firebase::Timestamp::nanoseconds;
%rename("%s") firebase::Timestamp::ToString;
%csmethodmodifiers ToString "public override";
%include "firebase/firestore/timestamp.h"

// Generate Error enum.
SWIG_CREATE_PROXY(firebase::firestore::Error)
%rename("%(regex:/Error(.*)/\\1/)s", %$isenumitem) "";
// Remove the `Error` prefix from enum members.
%include "firebase/firestore/firestore_errors.h"

// # LINT.ThenChange(//depot/google3/firebase/firestore/client/unity/generated/src/last-updated.txt)
