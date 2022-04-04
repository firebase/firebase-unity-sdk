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

# Handles packing all the static libs as one on ios

set(ar_tool ${CMAKE_AR})
if (CMAKE_INTERPROCEDURAL_OPTIMIZATION)
  set(ar_tool ${CMAKE_CXX_COMPILER_AR})
endif()

if(NOT CMAKE_LIBTOOL)
  find_program(CMAKE_LIBTOOL NAMES libtool)
endif()
if(CMAKE_LIBTOOL)
  set(CMAKE_LIBTOOL ${CMAKE_LIBTOOL} CACHE PATH "libtool executable")  
endif()

# Packs static libraries into one static library for ios and sets up the correct
# install call for it
#
# ios_pack(<name>
#          <output_name>
#          [DEPS <deps>]
# )
#
# Args:
#  name: Target name
#  output_name: Library file name (without extension)
#  deps: List of dependent static libraries
#
macro(ios_pack name output_name)
  set(multi DEPS)
  # Parse the arguments into UNITY_MONO_SOURCES and UNITY_MONO_DEPENDS.
  cmake_parse_arguments(UNITY_PACK "" "" "${multi}" ${ARGN})

  set(IOS_PACK_SCRIPT "${CMAKE_CURRENT_BINARY_DIR}/generated/${output_name}.sh")
  SET(TARGET_FILE "${output_name}.a")
  set(content "#!/bin/bash\n")

  string(APPEND content "rm -f ${TARGET_FILE}\n")

  set(first true)
  foreach(dep ${UNITY_PACK_DEPS})
    if(first)
      set(first false)
      string(APPEND content "cp $<TARGET_FILE:${dep}> ${TARGET_FILE}\n")
    else()
      string(APPEND content "${CMAKE_LIBTOOL} -static -o ${TARGET_FILE} ${TARGET_FILE} $<TARGET_FILE:${dep}>\n")
    endif()
  endforeach()

  file(WRITE ${IOS_PACK_SCRIPT}.in ${content})

  file(GENERATE
    OUTPUT
      ${IOS_PACK_SCRIPT}
    INPUT
      ${IOS_PACK_SCRIPT}.in
  )

  add_custom_command(
    COMMAND chmod +x ${IOS_PACK_SCRIPT} && ${IOS_PACK_SCRIPT}
    OUTPUT ${TARGET_FILE}
    COMMENT "Bundling ${TARGET_FILE}"
    VERBATIM
  )

  add_custom_target(${name} ALL DEPENDS ${TARGET_FILE})
  add_dependencies(${name} ${UNITY_PACK_DEPS})

  unity_pack_program(${CMAKE_CURRENT_BINARY_DIR}/${TARGET_FILE})
endmacro()
