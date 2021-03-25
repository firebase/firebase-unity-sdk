// Copyright 2016 Google Inc. All Rights Reserved.
//
// This file contains typemaps that handle firebase::InitResult function output
// parameters such that...
//
// int foo(InitResult* result);
//
// generates the C# method:
//
// int foo(out InitResult result);

// InitResult is used as a output parameter, so add the typemaps for that case.
%typemap(ctype, out="void *") firebase::InitResult* "int *"
%typemap(imtype, out="global::System.IntPtr") firebase::InitResult* "out int"
%typemap(cstype, out="$csclassname") firebase::InitResult* "out InitResult"
%typemap(csin,
         pre="    int temp$csinput = 0;",
         post="      $csinput = (InitResult)temp$csinput;",
         cshin="out $csinput"
        ) firebase::InitResult* "out temp$csinput"
