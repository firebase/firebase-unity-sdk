// Copyright 2020 Google LLC
//

// This file contains macros to deal with the generated C# proxy classes in
// a uniform way.



// Begins a new mapping declaration for a public Firestore class. This mapping
// appends `Proxy` suffix to the given `classname` and ignores all its members
// (except the destructor). After calling this macro, list out the class members
// that should be kept in the generated code explicitly, e.g.
//
//   SWIG_CREATE_PROXY(firebase::firestore::Foo)
//   %rename("%s") firebase::firestore::Foo::Bar;
//   %rename("%s") firebase::firestore::Foo::Baz;
%define SWIG_CREATE_PROXY(classname)

%rename("%sProxy") #classname ;

// The regular expression means "ignore all members that don't start with `~`",
// i.e. ignore everything except the destructor. Ignoring the destructor would
// make the resulting class leak memory (see
// https://docs.google.com/document/d/1qqYXGzbB-01l00Mv-dGbGRXZn9XCAHjGtwstgj31R1Q/edit#heading=h.zgrg0ghm4bhl
// ).
%rename("$ignore", regextarget=1, fullname=1) #classname "::(?!~).*";

// Makes sure enumeration members are not ignored (the alternative is to list
// each one explicitly, which is tedious). The enumeration itself still has to
// be unignored manually.
%rename("%s", %$isenumitem) "";

%enddef

// # LINT.ThenChange(//depot/google3/firebase/firestore/client/unity/generated/src/last-updated.txt)
