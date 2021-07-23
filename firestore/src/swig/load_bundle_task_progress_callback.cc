#include "firestore/src/swig/load_bundle_task_progress_callback.h"

#include <memory>
#include <utility>

#include "app/src/callback.h"

namespace firebase {
namespace firestore {
namespace csharp {

namespace {

class ProgressCallback {
 public:
  ProgressCallback(LoadBundleTaskProgressCallback callback, int32_t callback_id,
                   std::unique_ptr<LoadBundleTaskProgress> progress)
      : callback_(callback),
        callback_id_(callback_id),
        progress_(std::move(progress)) {}

  static void Run(ProgressCallback* callback) { callback->Run(); }

 private:
  void Run() {
    // Ownership of the progress pointer is passed to C#.
    callback_(callback_id_, progress_.release());
  }

  LoadBundleTaskProgressCallback callback_ = nullptr;
  int32_t callback_id_ = -1;
  std::unique_ptr<LoadBundleTaskProgress> progress_;
};

}  // namespace

void LoadBundleWithCallback(Firestore* firestore,
                            const std::string& bundle_data, int32_t callback_id,
                            LoadBundleTaskProgressCallback callback) {
  auto progress_listener =
      [callback, callback_id](const LoadBundleTaskProgress& progress) {
        // NOLINTNEXTLINE(modernize-make-unique)
        std::unique_ptr<LoadBundleTaskProgress> progress_ptr(
            new LoadBundleTaskProgress(progress));
        ProgressCallback progress_callback(callback, callback_id,
                                           std::move(progress_ptr));
        auto* callback = new callback::CallbackMoveValue1<ProgressCallback>(
            std::move(progress_callback), ProgressCallback::Run);
        callback::AddCallback(callback);
      };
  firestore->LoadBundle(bundle_data, progress_listener);
}

}  // namespace csharp
}  // namespace firestore
}  // namespace firebase
