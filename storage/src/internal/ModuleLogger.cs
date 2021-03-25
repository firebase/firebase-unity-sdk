/*
 * Copyright 2017 Google LLC
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
using System.Collections.Generic;
using Firebase;

namespace Firebase.Storage.Internal {
  /// <summary>
  /// Module specific logger.
  /// </summary>
  /// Each module or class can instance their own logger which logs context and allows parent
  /// loggers to control the log level of this instance.
  // TODO(smiles): This should be backed with a C++ implementation so that we can mirror the
  // behavior C++ and C# implementations.
  internal class ModuleLogger {
    // Lock for roots, parent and children members of this class.
    private static object lockObject = new object();

    // List of all root logger instances.
    // This is a set of weak references so that each module that creates a logger can own the
    // lifetime of the object.
    private static List<WeakReference> roots = new List<WeakReference>();

    // Parent of this logger.
    // Logger instances are arranged in a tree such that a change to the log level of a parent
    // changes the log level of all children.
    private ModuleLogger parent;

    // Children of this logger.
    private List<ModuleLogger> children = new List<ModuleLogger>();

    // Tag for log messages.
    private string tag;

    // Log level filter for this instance.
    // TODO(smiles): Remove reference to FirebaseApp.LogLevel if / when core logged is changed to
    // ModuleLogger.
    private LogLevel logLevel = Firebase.FirebaseApp.LogLevel;

    /// <summary>
    /// Construct a logger for a Firebase module.
    /// </summary>
    /// <param name="parentLogger">Parent of this logger.  This is used to filter log messages and
    /// optionally supplies the tag for messages.</param>
    public ModuleLogger(ModuleLogger parentLogger = null) {
      lock (lockObject) {
        if (parentLogger == null) {
          roots.Add(new WeakReference(this, false));
        } else {
          parent = parentLogger;
          parent.children.Add(this);
        }
      }
    }

    /// <summary>
    /// Finalize the logger, removing references to it.
    /// </summary>
    ~ModuleLogger() {
      lock (lockObject) {
        if (parent == null) {
          foreach (var reference in roots) {
            var logger = Firebase.FirebaseApp.WeakReferenceGetTarget(reference) as ModuleLogger;
            if (logger == this) {
              roots.Remove(reference);
              break;
            }
          }
        } else {
          parent.children.Remove(this);
          parent = null;
        }
      }
    }

    /// <summary>
    /// Minimum visible log level for this module.
    /// All messages below this level will not be logged.
    /// If this logger has parents, the minumum log level across all parents will be used.
    /// </summary>
    public LogLevel Level {
      get {
        LogLevel minimumLevel = logLevel;
        lock (lockObject) {
          if (parent != null) {
            var parentLevel = parent.Level;
            if (parentLevel < minimumLevel) minimumLevel = parentLevel;
          }
        }
        return minimumLevel;
      }
      set { logLevel = value; }
    }

    /// <summary>
    /// Tag prefixed to log messages reported by this logger.
    /// </summary>
    /// <remarks>
    /// This is used to prefix each log message so they can be filtered in developer's logs.
    /// If no tag is specified the first tag found in the set of parent loggers is used.
    /// </remarks>
    public string Tag {
      get {
        if (tag != null) return tag;
        lock (lockObject) {
          if (parent != null) return parent.Tag;
        }
        return null;
      }

      set {
        tag = value;
      }
    }

    /// <summary>
    /// Log a message.
    /// </summary>
    /// <param name="level">Level of the message.</param>
    /// <param name="message">Message to log.</param>
    public void LogMessage(LogLevel level, string message) {
      if (level >= Level) {
        LogUtil.LogMessage(level, String.Format("{0}{1}", Tag != null ? Tag + " " : "",
                                                    message));
      }
    }
  }
}
