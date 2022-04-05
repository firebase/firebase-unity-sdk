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

#include "app/src/cpp_instance_manager.h"

#include <string>
#include <thread>  //NOLINT

#include "testing/base/public/gmock.h"
#include "testing/base/public/gunit.h"

namespace firebase {

TEST(CppInstanceManagerTest, DefaultConstructor) {
  { CppInstanceManager<int> manager; }
  { CppInstanceManager<std::string> manager; }
  { CppInstanceManager<std::map<int, bool>> manager; }
}

TEST(CppInstanceManagerTest, AddOnceNoRelease) {
  CppInstanceManager<std::string> manager;

  std::string* instance = new std::string("A");

  EXPECT_EQ(manager.AddReference(instance), 1);
}

TEST(CppInstanceManagerTest, AddOnceReleaseOnce) {
  CppInstanceManager<std::string> manager;

  std::string* instance = new std::string("A");

  EXPECT_EQ(manager.AddReference(instance), 1);

  EXPECT_EQ(manager.ReleaseReference(instance), 0);
}

TEST(CppInstanceManagerTest, AddOnceReleaseMultiple) {
  CppInstanceManager<std::string> manager;

  std::string* instance = new std::string("A");

  EXPECT_EQ(manager.AddReference(instance), 1);

  EXPECT_EQ(manager.ReleaseReference(instance), 0);

  EXPECT_EQ(manager.ReleaseReference(instance), -1);

  EXPECT_EQ(manager.ReleaseReference(instance), -1);
}

TEST(CppInstanceManagerTest, AddMultipleReleaseMultiple) {
  CppInstanceManager<std::string> manager;

  std::string* instance = new std::string("A");

  EXPECT_EQ(manager.AddReference(instance), 1);

  EXPECT_EQ(manager.AddReference(instance), 2);

  EXPECT_EQ(manager.ReleaseReference(instance), 1);

  EXPECT_EQ(manager.ReleaseReference(instance), 0);

  EXPECT_EQ(manager.ReleaseReference(instance), -1);
}

TEST(CppInstanceManagerTest, AddAndReleaseTwice) {
  CppInstanceManager<std::string> manager;

  {
    std::string* instance = new std::string("A");

    EXPECT_EQ(manager.AddReference(instance), 1);

    EXPECT_EQ(manager.ReleaseReference(instance), 0);
  }

  {
    std::string* instance = new std::string("A");

    EXPECT_EQ(manager.AddReference(instance), 1);

    EXPECT_EQ(manager.ReleaseReference(instance), 0);
  }
}

}  // namespace firebase
