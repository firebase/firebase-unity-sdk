/*
 * Copyright 2018 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Text;
using Firebase;
using Firebase.Storage;

// Use the Unity SDK on desktop to download and display a file.
public class MonoTest {
    public static void Main() {
        FirebaseApp.LogLevel = Firebase.LogLevel.Info;

        var storageReference =
          FirebaseStorage.DefaultInstance.GetReference("File.txt");
        var task = storageReference.GetBytesAsync(
            0,
            new StorageProgress<DownloadState>((downloadState) => {
                Console.WriteLine("Downloading {0}: {1} out of {2}",
                                  downloadState.Reference.Name,
                                  downloadState.BytesTransferred,
                                  downloadState.TotalByteCount);
              }));
        task.Wait();
        if (!(task.IsFaulted || task.IsCanceled)) {
          Console.WriteLine("Complete");
          Console.WriteLine("----");
          Console.WriteLine(Encoding.Default.GetString(task.Result));
        } else {
          Console.WriteLine("Failed");
        }
    }
}
