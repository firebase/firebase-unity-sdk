// Copyright 2019 Google Inc. All Rights Reserved.

// Makes sure any reference to self / this that references nullptr will throw
// an exception.
%typemap(check) SWIGTYPE *self %{
#ifndef FIREBASE_TO_STRING
#define FIREBASE_TO_STRING2(x) #x
#define FIREBASE_TO_STRING(x) FIREBASE_TO_STRING2(#x)
#endif  // FIREBASE_TO_STRING
  if (!$1) {
    SWIG_CSharpSetPendingExceptionArgument(
        SWIG_CSharpArgumentNullException,
        FIREBASE_TO_STRING($1_mangle) " has been disposed", 0);
    return $null;
  }
#undef FIREBASE_TO_STRING
#undef FIREBASE_TO_STRING2
%}
