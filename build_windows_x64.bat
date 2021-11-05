@echo off

REM !/bin/bash
REM
REM Copyright 2019 Google LLC
REM
REM Licensed under the Apache License, Version 2.0 (the "License");
REM you may not use this file except in compliance with the License.
REM You may obtain a copy of the License at
REM
REM     http://www.apache.org/licenses/LICENSE-2.0
REM
REM Unless required by applicable law or agreed to in writing, software
REM distributed under the License is distributed on an "AS IS" BASIS,
REM WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
REM See the License for the specific language governing permissions and
REM limitations under the License.
REM
REM Builds and packs firebase unity meant to be used on a Windows environment.

SETLOCAL

SET status=0
SET CPP_DIR=

SET PATH=%PROTOBUF_SRC_ROOT_FOLDER%\vsprojects\x64\Release;%PATH%

set UNITY_ROOT_DIR=%UNITY_ROOT_DIR:\=/%
set PROTOBUF_SRC_ROOT_FOLDER=%PROTOBUF_SRC_ROOT_FOLDER:\=/%

IF EXIST ..\firebase-cpp-sdk (
  SET CPP_DIR=-DFIREBASE_CPP_SDK_DIR^=../../firebase-cpp-sdk
)

IF EXIST "C:\Program Files\Mono\bin" (
  SET MONO_DIR="C:/Program Files/Mono/bin"
) ELSE (
  ECHO ERROR: Cant find mono in program files
  EXIT /B -1
)

IF EXIST "C:\Program Files\OpenSSL-Win64" (
  SET OPENSSL_x64="C:/Program Files/OpenSSL-Win64"
) ELSE (
  ECHO ERROR: Cant find open ssl x64
  EXIT /B -1
)

CALL :BUILD unity, "-DFIREBASE_INCLUDE_UNITY=ON"
if %errorlevel% neq 0 (SET status=%errorlevel%)

:: TODO: Fix mono build to not need unity deps
:: CALL :BUILD mono, "-DFIREBASE_INCLUDE_MONO=ON"
:: if %errorlevel% neq 0 (SET status=%errorlevel%)

CALL :BUILD unity_separate, "-DFIREBASE_INCLUDE_UNITY=ON -DFIREBASE_UNI_LIBRARY=OFF"
if %errorlevel% neq 0 (SET status=%errorlevel%)

:: TODO: Fix mono build to not need unity deps
:: CALL :BUILD mono_separate, "-DFIREBASE_INCLUDE_MONO=ON -DFIREBASE_UNI_LIBRARY=OFF"
:: if %errorlevel% neq 0 (SET status=%errorlevel%)

EXIT /B %status%

:BUILD
  ECHO #################################################################
  DATE /T
  TIME /T
  ECHO Building config '%~1' with option '%~2'.
  ECHO #################################################################

  @echo on

  mkdir windows_%~1
  pushd windows_%~1

  SET CMAKE_ARGS=-DPROTOBUF_SRC_ROOT_FOLDER=%PROTOBUF_SRC_ROOT_FOLDER%
  SET CMAKE_ARGS=%CMAKE_ARGS% %CPP_DIR% -DMONO_DIR=%MONO_DIR%
  SET CMAKE_ARGS=%CMAKE_ARGS% -DUNITY_ROOT_DIR=%UNITY_ROOT_DIR%
  SET CMAKE_ARGS=%CMAKE_ARGS% -DOPENSSL_ROOT_DIR=%OPENSSL_x64%
  SET CMAKE_ARGS=%CMAKE_ARGS% -DFIREBASE_UNITY_BUILD_TESTS=ON
  SET CMAKE_ARGS=%CMAKE_ARGS% -DCMAKE_CONFIGURATION_TYPES=RelWithDebInfo

  cmake .. -A x64 %CMAKE_ARGS% %~2

  :: Check for errors, and return if there were any
  if %errorlevel% neq 0 (
    popd
    @echo off
    exit /b %errorlevel%
  )

  cmake --build . --config RelWithDebInfo

  :: Again, check for errors, and return if there were any
  if %errorlevel% neq 0 (
    popd
    @echo off
    exit /b %errorlevel%
  )

  cpack .

  popd
  @echo off
EXIT /B %errorlevel%