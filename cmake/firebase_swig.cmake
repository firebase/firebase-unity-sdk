# Copyright 2019 Google
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#      http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

# File defines macros and functions to help setup swig rules for Firebase

include(FindSWIG)

# 3.0.6 has support for -outfile command line option, min version request 3.0.6
find_package(SWIG 3.0.6)

include(${SWIG_USE_FILE})

set(FIREBASE_SWIG_FIX_PY ${CMAKE_CURRENT_LIST_DIR}/swig_fix.py)

if(FIREBASE_UNI_LIBRARY)
  set(FIREBASE_SWIG_LIBRARY_TYPE STATIC)
else()
  set(FIREBASE_SWIG_LIBRARY_TYPE SHARED)
endif()

function(firebase_copy_property src dest property)
  get_property(temp SOURCE ${src} PROPERTY ${property})
  set_property(SOURCE ${dest} PROPERTY ${property} ${temp})
endfunction()

# Define a native shared library that contains the swig generated c++ and also
# output csharp code::
#
#  firebase_swig_add_library(<name>
#                     NAMESPACE <namespace>
#                     MODULE <module>
#                     SOURCES <src_files>...
#                     [DEPENDS <dep_target>...]
#                     [MODULE_OUT <module_out>]
#                     [TYPE <type>]
#                     [SYSTEM_DEPS <system_deps>...]
#                    )
# Args:
#   name: Target name
#   namespace: CSharp Namespace i.e. Firebase.App
#   module: C++ Shared Object name i.e. FirebaseCppApp
#   src_files: Swig input files
#   dep_target: Targets this target depends on and need to be linked/built first
#   module_out: Module output name for generated files. Defaults to namespace
#   type: Library output type (see FIREBASE_SWIG_LIBRARY_TYPE for default)
#   system_deps : extra dependency libraries required for certain platform.
#
# Notes:
#  * Generated CSharp file path is set into variable ${name}_gen_src
#  * Swig input files need to have the .i extension to work correctly
#  * All include paths should be based from the root directoy of unity sdk or
#    cpp sdk
#
macro(firebase_swig_add_library name)

  set(multi TYPE NAMESPACE MODULE SOURCES DEPENDS MODULE_OUT SYSTEM_DEPS)
  # Parse the arguments into UNITY_SWIG_SOURCES and UNITY_SWIG_DEPENDS.
  cmake_parse_arguments(UNITY_SWIG "" "" "${multi}" ${ARGN})

  foreach(source in ${UNITY_SWIG_SOURCES})
    set_property(SOURCE ${source} PROPERTY CPLUSPLUS ON)
  endforeach()

  if ("${UNITY_SWIG_MODULE_OUT}" STREQUAL "")
    set(UNITY_SWIG_MODULE_OUT ${UNITY_SWIG_NAMESPACE})
  endif()

  if ("${UNITY_SWIG_TYPE}" STREQUAL "")
    set(UNITY_SWIG_TYPE ${FIREBASE_SWIG_LIBRARY_TYPE})
  endif()

  set(SWIG_MODULE_${name}_EXTRA_FLAGS
    "-dllimport" "${UNITY_SWIG_MODULE}"
    "-outfile" "${UNITY_SWIG_MODULE_OUT}.cs"
    "-namespace" "${UNITY_SWIG_NAMESPACE}"
  )

  swig_add_library(${name}
    TYPE
      ${UNITY_SWIG_TYPE}
    LANGUAGE
      csharp
    SOURCES
      ${UNITY_SWIG_SOURCES}
    OUTPUT_DIR
      ${CMAKE_CURRENT_BINARY_DIR}/swig/
    OUTFILE_DIR
      ${CMAKE_CURRENT_BINARY_DIR}/swig/
  )

  set(UNITY_SWIG_PUBLIC_DEP_INCLUDE "")

  if(UNITY_SWIG_DEPENDS)

    # UseSwig.cmake creates a fake target for running swig when using make. We
    # need to make sure that target depends on passed in deps as those can
    # generate files swig needs to compile the interfaces
    if(CMAKE_GENERATOR MATCHES "Make")
      add_dependencies(${name}_swig_compilation ${UNITY_SWIG_DEPENDS})
    endif()

    target_link_libraries(${name} ${UNITY_SWIG_DEPENDS})

    foreach(dep ${UNITY_SWIG_DEPENDS})
      get_property(public_include_dirs
        TARGET ${dep}
        PROPERTY INTERFACE_INCLUDE_DIRECTORIES
      )
      list(APPEND UNITY_SWIG_PUBLIC_DEP_INCLUDE ${public_include_dirs})
    endforeach()
  endif()

  if(UNIX AND NOT ANDROID)
    target_link_libraries(${name} "pthread")
  endif()

  target_link_libraries(${name} ${FIREBASE_SYSTEM_DEPS})

  if(UNITY_SWIG_SYSTEM_DEPS)
    target_link_libraries(${name} ${UNITY_SWIG_SYSTEM_DEPS})
  endif()

  set_property(TARGET ${name} PROPERTY SWIG_INCLUDE_DIRECTORIES
    ${FIREBASE_CPP_SDK_DIR}
    ${FIREBASE_UNITY_DIR}
    ${UNITY_SWIG_PUBLIC_DEP_INCLUDE}
  )

  set(all_cpp_header_files)
  foreach (directory ${UNITY_SWIG_PUBLIC_DEP_INCLUDE})
    file(GLOB_RECURSE cpp_header_files "${directory}/*.h")
    list(APPEND all_cpp_header_files ${cpp_header_files})
  endforeach()

  set_property(TARGET ${name} PROPERTY SWIG_GENERATED_INCLUDE_DIRECTORIES
    ${FIREBASE_CPP_SDK_DIR}
    ${FIREBASE_UNITY_DIR}
  )

  set_property(TARGET ${name} PROPERTY SWIG_COMPILE_DEFINITIONS
    # Note: SWIG doesn't support C++11 `final` specifier, using `-Dfinal=`
    # workaround as suggested in
    # https://github.com/swig/swig/issues/672#issuecomment-400577864
    final=
    USE_EXPORT_FIX
  )

  set_property(TARGET ${name} PROPERTY SWIG_GENERATED_COMPILE_DEFINITIONS
    INTERNAL_EXPERIMENTAL=0
  )

  if("${UNITY_SWIG_TYPE}" STREQUAL "SHARED")
    set_target_properties(${name} PROPERTIES OUTPUT_NAME ${UNITY_SWIG_MODULE})
    set_property(TARGET ${name} PROPERTY FOLDER "Swig Dll")
  else()
    set_property(TARGET ${name} PROPERTY FOLDER "Swig Lib")
  endif()

  set(unity_swig_compile_options
    -fexceptions
  )

  set_target_properties(${name} PROPERTIES SWIG_GENERATED_COMPILE_OPTIONS
    "${unity_swig_compile_options}"
  )

  get_target_property(outdir ${name} SWIG_SUPPORT_FILES_DIRECTORY)

  set(UNITY_SWIG_CS_GEN_FILE ${outdir}${UNITY_SWIG_MODULE_OUT}.cs)
  set(UNITY_SWIG_CS_FIX_FILE ${outdir}${UNITY_SWIG_MODULE_OUT}_fixed.cs)

  # If we build all swig c++ code as one shared object, we need to change the
  # import module in the generated CSharp code
  if(FIREBASE_IOS_BUILD)
    # Because iOS uses a static library, the DllImport needs to be changed.
    set(import_module_override "--import_module=__Internal")
  elseif(NOT "${FIREBASE_SWIG_OVERRIDE_IMPORT_MODULE}" STREQUAL "")
    set(import_module_override
      "--import_module=${FIREBASE_SWIG_OVERRIDE_IMPORT_MODULE}"
    )
  else()
    set(import_module_override "")
  endif()

  set(static_k_removal_override "")
  if("${name}" STREQUAL "firebase_analytics_swig") # hack for now, need to find better way to do special cases
    set(static_k_removal_override "--static_k_removal=True")
  endif()

  add_custom_command(
    OUTPUT ${UNITY_SWIG_CS_FIX_FILE}
    DEPENDS ${UNITY_SWIG_CS_GEN_FILE}
    COMMAND
      ${FIREBASE_PYTHON_EXECUTABLE}
        ${CMAKE_CURRENT_LIST_DIR}/../swig_commenter.py
        --input=\"${all_cpp_header_files}\"
        --output=\"${UNITY_SWIG_CS_GEN_FILE}\"
        --namespace_prefix=\"Firebase\"
    COMMAND
      ${FIREBASE_PYTHON_EXECUTABLE}
        ${FIREBASE_SWIG_FIX_PY}
        --language=csharp
        --in_file=\"${UNITY_SWIG_CS_GEN_FILE}\"
        --out_file=\"${UNITY_SWIG_CS_FIX_FILE}\"
        --namespace=${UNITY_SWIG_MODULE_OUT}
        ${import_module_override}
        ${static_k_removal_override}
  )

  set(UNITY_SWIG_CPP_GEN_FILE ${swig_generated_file_fullname})
  set(UNITY_SWIG_CPP_FIX_FILE ${swig_generated_file_fullname})

  # Make fix file unique
  string(REPLACE "_wrap." "_wrap_fix."
    UNITY_SWIG_CPP_FIX_FILE
    ${UNITY_SWIG_CPP_FIX_FILE}
  )

  # Fix the c++ file and clear the old one as ${name} will still try and compile
  # it
  add_custom_command(
    OUTPUT ${UNITY_SWIG_CPP_FIX_FILE}
    DEPENDS ${UNITY_SWIG_CPP_GEN_FILE}
    COMMAND
      ${FIREBASE_PYTHON_EXECUTABLE}
        ${FIREBASE_SWIG_FIX_PY}
        --language=cpp
        --in_file=\"${UNITY_SWIG_CPP_GEN_FILE}\"
        --out_file=\"${UNITY_SWIG_CPP_FIX_FILE}\"
        --namespace="${UNITY_SWIG_MODULE_OUT}"
        --clear_in_file
  )

  firebase_copy_property(${UNITY_SWIG_CPP_GEN_FILE}
    ${UNITY_SWIG_CPP_FIX_FILE}
    INCLUDE_DIRECTORIES
  )

  firebase_copy_property(${UNITY_SWIG_CPP_GEN_FILE}
    ${UNITY_SWIG_CPP_FIX_FILE}
    COMPILE_DEFINITIONS
  )

  firebase_copy_property(${UNITY_SWIG_CPP_GEN_FILE}
    ${UNITY_SWIG_CPP_FIX_FILE}
    COMPILE_OPTIONS
  )

  firebase_copy_property(${UNITY_SWIG_CPP_GEN_FILE}
    ${UNITY_SWIG_CPP_FIX_FILE}
    GENERATED
  )

  # Add fixed files to target
  target_sources(${name}
    PRIVATE
      ${UNITY_SWIG_CPP_FIX_FILE}
      ${UNITY_SWIG_CS_FIX_FILE}
  )

  set(${name}_gen_src ${UNITY_SWIG_CS_FIX_FILE})
  # Set a variable so that the C++ file can be referenced elsewhere
  set(${name}_gen_cpp_src ${UNITY_SWIG_CPP_FIX_FILE})
  set(${name}_gen_cpp_src ${UNITY_SWIG_CPP_FIX_FILE} PARENT_SCOPE)

endmacro()
