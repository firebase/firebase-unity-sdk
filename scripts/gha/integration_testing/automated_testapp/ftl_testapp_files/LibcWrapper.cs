// Copyright 2019 Google Inc. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Runtime.InteropServices;

namespace Firebase.TestLab {
  /// <summary>
  /// Provides an interface for working with file descriptors. Used by the Android implementation
  /// of Game Loops to write to a results file provided through the intent.
  /// </summary>
  class LibcWrapper {
    [DllImport("c")]
    public static extern int dup(int fd);

    [DllImport("c")]
    public static extern int write(int fd, string buf, int count);

    public static int WriteToFileDescriptor(int fileDescriptor, string message) {
      return write(fileDescriptor, message, message.Length);
    }
  }
}
