/*
 * Copyright 2019 Google LLC
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

namespace Firebase.Crashlytics.Editor {
    using System;
    using UnityEngine;
    /// <summary>
    /// Unity does not support serializing Dictionary objects,
    /// C# doesn't either to be fair, but to facilitate a similar
    /// interface, these association classes allow one to
    /// pair a key and value in a list format.
    /// </summary>
    public class Associations {

        /// <summary>
        /// A base class for holding a Key, Value pair
        /// where the Key is a string and the Value
        /// is determined by the implementing class.
        ///
        /// Implementing classes must also be marked as
        /// [Serializable] for Unity to properly format/save them
        /// in a .asset file.
        ///
        /// WARNING (and another interesting thing about Unity serialization):
        /// Unity leverages reflection to set and get fields off of a
        /// class. Unity will not serialize out properties, only fields. It
        /// will by default serialize any public fields but will also
        /// serialize any private fields marked with [SerializeField].
        /// </summary>
        /// <typeparam name="V">The type of value.</typeparam>
        [Serializable]
        public class Association<V> {

            [SerializeField] private string _key;

            [SerializeField] private V _value;

            /// <summary>
            /// The Key for this association. Used to determine
            /// uniqueness.
            /// </summary>
            public string Key {
                get { return _key; }
                set { _key = value; }
            }

            /// <summary>
            /// The Value for this association.
            /// </summary>
            public V Value {
                get { return _value; }
                set { _value = value; }
            }

            /// <summary>
            /// The hash code for this object is overriden
            /// and determined solely based on the key.
            /// </summary>
            /// <returns></returns>
            public override int GetHashCode() {
                return (_key != null ? _key.GetHashCode() : 0);
            }

            /// <summary>
            /// Equality for this object is determined solely
            /// based on the key's value.
            /// </summary>
            /// <param name="other">Another Association object</param>
            /// <returns>true if the objects are of the
            /// same type and have the same key, false otherwise.
            /// </returns>
            protected bool Equals(Association<V> other) {
                return string.Equals(_key, other._key);
            }

            /// <summary>
            /// Equality for this object is determined solely
            /// based on the key's value.
            /// </summary>
            /// <param name="obj">Another object</param>
            /// <returns>true if the objects are of the
            /// same type and have the same key, false otherwise.
            /// </returns>
            public override bool Equals(object obj) {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((Association<V>) obj);
            }

            /// <summary>
            /// Outputs the key and value in a well formatted string.
            /// </summary>
            /// <returns>A string representation of this Association</returns>
            public override string ToString() {
                return "{ " + Key + ": " + Value + " }";
            }

        }

        /// <summary>
        /// A String implementation of the base Association class.
        /// </summary>
        [Serializable]
        public class StringAssociation : Association<string> {}

        /// <summary>
        /// An Int implementation of the base Association class.
        /// </summary>
        [Serializable]
        public class IntAssociation : Association<int> {}

        /// <summary>
        /// A Float implementation of the base Association class.
        /// </summary>
        [Serializable]
        public class FloatAssociation : Association<float> {}

        /// <summary>
        /// A Boolean implemenation of the base Association class.
        /// </summary>
        [Serializable]
        public class BoolAssociation : Association<bool> {}
    }
}
