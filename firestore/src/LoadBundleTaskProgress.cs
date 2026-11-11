// Copyright 2021 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;

namespace Firebase.Firestore {
  /// <summary>
  /// Represents a progress update or a final state from loading bundles.
  /// </summary>
  public sealed class LoadBundleTaskProgress : EventArgs {
    /// <summary>
    /// Creates a new instance of the class. This is to support testing against progress
    /// updates.
    /// </summary>
    public LoadBundleTaskProgress(
        int documentsLoaded,
        int totalDocuments,
        long bytesLoaded,
        long totalBytes,
        LoadBundleTaskState state) {
      DocumentsLoaded = documentsLoaded;
      TotalDocuments = totalDocuments;
      BytesLoaded = bytesLoaded;
      TotalBytes = totalBytes;
      State = state;
    }

    /// <summary>
    /// Represents the state of bundle loading tasks.
    /// </summary>
    /// <remarks>
    /// Both <see cref="LoadBundleTaskState.Success"/> and <see cref="LoadBundleTaskState.Error"/>
    /// are final states: the task will fail or complete and there will be no more updates after
    /// they are reported.
    /// </remarks>
    public enum LoadBundleTaskState {
      /// <summary>Bundle loading encountered an error.</summary>
      Error,
      /// <summary>Bundle loading is in progress.</summary>
      InProgress,
      /// <summary>Bundle loading succeeded.</summary>
      Success,
    }

    /// <summary>
    /// The number of documents that have been loaded.
    /// </summary>
    public int DocumentsLoaded { get; private set; }

    /// <summary>
    /// The total number of documents in the bundle. Zero if the bundle failed to parse.
    /// </summary>
    public int TotalDocuments { get; private set; }

    /// <summary>
    /// The number of bytes that have been loaded.
    /// </summary>
    public long BytesLoaded { get; private set; }

    /// <summary>
    /// The total number of bytes in the bundle. Zero if the bundle failed to parse.
    /// </summary>
    public long TotalBytes { get; private set; }

    /// <summary>
    /// The current state of the loading progress.
    /// </summary>
    public LoadBundleTaskState State { get; private set; } = LoadBundleTaskState.InProgress;

    /// <inheritdoc />
    public override bool Equals(object obj) {
      var other = obj as LoadBundleTaskProgress;
      if (other == null) return false;
      return DocumentsLoaded == other.DocumentsLoaded &&
             TotalDocuments == other.TotalDocuments &&
             BytesLoaded == other.BytesLoaded &&
             TotalBytes == other.TotalBytes &&
             State == other.State;
    }

    /// <inheritdoc />
    public override int GetHashCode() =>
        DocumentsLoaded.GetHashCode() * 31 +
        TotalDocuments.GetHashCode() * 23 +
        BytesLoaded.GetHashCode() * 17 +
        TotalBytes.GetHashCode() * 13 +
        State.GetHashCode();
  }
}
