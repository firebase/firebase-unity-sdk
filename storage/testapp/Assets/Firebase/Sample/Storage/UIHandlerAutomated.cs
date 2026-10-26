namespace Firebase.Sample.Storage {
  using Firebase;
  using Firebase.Extensions;
  using Firebase.Storage;
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Text;
  using System.Threading.Tasks;
  using UnityEngine;

  // An automated version of the UIHandler that runs tests on Firebase Storage.
  public class UIHandlerAutomated : UIHandler {
    // Delegate which validates a completed task.
    delegate Task TaskValidationDelegate(Task task);

    private Firebase.Sample.AutomatedTestRunner testRunner;

#if !(UNITY_IOS || UNITY_TVOS || UNITY_ANDROID) || UNITY_EDITOR
    // Storage bucket (without the scheme) extracted from MyStorageBucket.
    private string storageBucket;
    // Manually create the default app on desktop so that it's possible to specify the storage bucket.
    private FirebaseApp defaultApp;
#endif  // !(UNITY_IOS || UNITY_TVOS || UNITY_ANDROID) || UNITY_EDITOR

    // Metadata to upload to the test file.
    private string METADATA_STRING_NON_CUSTOM_ONLY =
      "CacheControl=no-cache\n" +
      "ContentLanguage=en\n";

    // Expected metadata if expectedMetadataTestMode is set to MetadataTestMode.Both and
    // MetadataTestMode.NonCustomOnly
    private string EXPECTED_METADATA_CACHE_CONTROL = "no-cache";
    private string EXPECTED_METADATA_CONTENT_LANGUAGE = "en";

    private string METADATA_STRING_CUSTOM_ONLY =
      "this=is\n" +
      "just=a\n" +
      "set=of\n" +
      "test=metadata\n";

    // Expected custom metadata, if expectedMetadataTestMode is set to MetadataTestMode.Both and
    // MetadataTestMode.CustomOnly
    private Dictionary<string, string> EXPECTED_CUSTOM_METADATA = new Dictionary<string, string> {
      {"this", "is"},
      {"just", "a"},
      {"set", "of"},
      {"test", "metadata"}
    };

    private enum MetadataTestMode : byte {
      Both,             // Change both custom and non-custom metadata
      CustomOnly,       // Change only custom metadata
      NonCustomOnly,    // Change only non-custom metadata
      None,             // Change no metadata
    };

    // X is replaced in this string with a different character per line.
    private string FILE_LINE = "X: a relatively large file with lots of evil exs\n";
    // File to upload (created from FILE_LINE).
    private string LARGE_FILE_CONTENTS;
    // Path to the file.
    private string LARGE_FILE_PATH = "this_is_a/test_path/to_a/large_text_file.txt";
    // Small file contents.
    private string SMALL_FILE_CONTENTS = "this is a small text file";
    // Path to a small file.
    private string SMALL_FILE_PATH = "this_is_a/test_path/to_a/small_text_file.txt";
    // Path to a metadata testing file.
    private string METADATA_TEST_FILE_PATH = "this_is_a/test_path/to_a/metadata_test_file.txt";
    // Path to non-existant file.
    private string NON_EXISTANT_FILE_PATH = "this_is_a/path_to_a/non_existant_file.txt";
    // Time to wait before canceling an operation.
    private float CANCELATION_DELAY_SECONDS = 0.05f;

    // Content type for text file uploads
    const string ContentTypePlainText = "text/plain";

    // Expected total file size in bytes during upload / download.
    private int expectedFileSize;
    // Expected storage reference during upload / download.
    private StorageReference expectedStorageReference;
    // Expected metadata test mode (none, non-customized only, customized only or both)
    private MetadataTestMode expectedMetadataTestMode;
    // Expected metadata value for CacheControl
    private string expectedMetadataCacheControlValue;
    // Expected metadata value for ContentLanguage
    private string expectedMetadataCacheContentLanguageValue;
    // Expected custom metadata
    private Dictionary<string, string> expectedCustomMetadata;

    // Number of upload / download progress updates.
    private int progressUpdateCount;
    // Class for forcing code to run on the main thread.
    private MainThreadDispatcher mainThreadDispatcher;

    // When set to true, each download or upload callback will throw an exception.
    private bool throwExceptionsInProgressCallbacks = false;

    protected override void Start() {
      // Set the list of tests to run, note this is done at Start since they are
      // non-static.
      Func<Task>[] tests = {
        TestCreateDestroy,
        TestCreateDestroyRace,
        TestStorageReferenceNavigation,
        TestUrl,
        TestGetReference,
        TestGetStorageInvalidUris,
        TestGetStorageWrongBucket,
        TestUploadBytesLargeFile,
        TestUploadBytesSmallFile,
        TestUploadBytesSmallFileWithNoMetadata,
        TestUploadBytesSmallFileWithNonCustomOnlyMetadata,
        TestUploadBytesSmallFileWithCustomOnlyMetadata,
        TestUploadBytesSmallFileWithBothMetadata,
        TestUploadBytesSmallFileThenUpdateMetadata,
        TestUploadStreamLargeFile,
        TestUploadStreamSmallFile,
        TestUploadFromFileLargeFile,
        TestUploadFromFileSmallFile,
        TestUploadFromNonExistantFile,
        TestUploadBytesWithCancelation,
        TestUploadStreamWithCancelation,
        TestUploadFromFileWithCancelation,
        TestUploadSmallFileGetDownloadUrl,
        TestGetDownloadUrlNonExistantFile,
        TestUploadSmallFileGetMetadata,
        TestGetMetadataNonExistantFile,
        TestUploadSmallFileAndDelete,
        TestDeleteNonExistantFile,
        TestDownloadNonExistantFile,
        TestUploadSmallFileAndDownload,
        TestUploadSmallFileAndDownloadWithProgressExceptions,
        TestUploadLargeFileAndDownload,
        TestUploadLargeFileAndDownloadWithCancelation,
        TestUploadSmallFileAndDownloadUsingStreamCallback,
        TestUploadLargeFileAndDownloadUsingStreamCallback,
        TestUploadLargeFileAndDownloadUsingStreamCallbackWithCancelation,
        TestUploadSmallFileAndDownloadToFile,
        TestUploadLargeFileAndDownloadToFile,
        TestUploadLargeFileAndDownloadToFileWithCancelation,
        TestList,
        TestUploadSessionUriAndResume,
        TestUploadPartialSessionResume,
        TestDownloadIntoSmallBuffer,
        TestGetStreamAsyncDirect,
        TestRealMidTransferCancelation,
        TestConcurrentTransfersThrottling,
        TestRootReferenceRejected,
        TestDeleteCustomMetadataAndClearStandardFields,
        TestUndisposedGetStreamAsyncDoesNotStarveDownloads,
        TestUploadSeekableStreamFromNonZeroPosition,
        TestGetFileAsyncPreservesExistingFileOnFailure,
      };

      testRunner = AutomatedTestRunner.CreateTestRunner(
        testsToRun: tests,
        logFunc: DebugLog,
        maxAttempts: 3
      );

      mainThreadDispatcher = gameObject.AddComponent<MainThreadDispatcher>();
      if (mainThreadDispatcher == null) {
        Debug.LogError("Could not create MainThreadDispatcher component!");
        return;
      }

      Debug.Log("NOTE: Some API calls report failures using UnityEngine.Debug.LogError which will " +
                "pause execution in the editor when 'Error Pause' in the console window is " +
                "enabled.  `Error Pause` should be disabled to execute this test.");

      var largeFileSize = 512 * 1024;
      int repeatedLines = largeFileSize / FILE_LINE.Length;
      int partialLineLength = largeFileSize % FILE_LINE.Length;
      var builder = new StringBuilder();
      for (int i = 0; i < repeatedLines; ++i) {
        builder.Append(FILE_LINE.Replace('X', (i % 10).ToString()[0]));
      }
      builder.Append(FILE_LINE.Substring(0, partialLineLength));
      LARGE_FILE_CONTENTS = builder.ToString();

      UIEnabled = false;
      base.Start();
    }

    // Create the default FirebaseApp on non-mobile platforms.
    private void CreateDefaultApp() {
#if !(UNITY_IOS || UNITY_TVOS || UNITY_ANDROID) || UNITY_EDITOR
      defaultApp = FirebaseApp.Create(new AppOptions { StorageBucket = storageBucket });
      Debug.Log(String.Format("Default app created with storage bucket {0}",
                              defaultApp.Options.StorageBucket));
#endif  // !(UNITY_IOS || UNITY_TVOS || UNITY_ANDROID) || UNITY_EDITOR
    }

    // Remove all reference to the default FirebaseApp.
    private void DestroyDefaultApp() {
#if !(UNITY_IOS || UNITY_TVOS || UNITY_ANDROID) || UNITY_EDITOR
      defaultApp = null;
#endif  // !(UNITY_IOS || UNITY_TVOS || UNITY_ANDROID) || UNITY_EDITOR
    }

    protected override void InitializeFirebase() {
#if !(UNITY_IOS || UNITY_TVOS || UNITY_ANDROID) || UNITY_EDITOR
      storageBucket = (new Uri(MyStorageBucket)).Host;
#endif
      CreateDefaultApp();
      base.InitializeFirebase();
    }

    // Passes along the update call to automated test runner.
    protected override void Update() {
      base.Update();
      if (testRunner != null && isFirebaseInitialized) {
        testRunner.Update();
      }
    }

    // Throw when condition is false.
    private void Assert(string message, bool condition) {
      if (!condition)
        throw new Exception(String.Format("Assertion failed ({0}): {1}",
                                          testRunner.CurrentTestDescription, message));
    }

    // Throw when value1 != value2.
    private void AssertEq<T>(string message, T value1, T value2) {
      if (!(object.Equals(value1, value2))) {
        throw new Exception(String.Format("Assertion failed ({0}): {1} != {2} ({3})",
                                          testRunner.CurrentTestDescription, value1, value2,
                                          message));
      }
    }

    // Returns a completed task.
    private Task CompletedTask(Exception exception = null) {
      TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();
      if (exception == null) {
        taskCompletionSource.SetResult(true);
      } else {
        taskCompletionSource.SetException(exception);
      }
      return taskCompletionSource.Task;
    }

    // Make sure it's possible to create and tear down storage instances.
    // There's some complexity here, because System.GC.WaitForPendingFinalizers()
    // can't be called on the main thread, because it blocks.  (And by blocking,
    // prevents garbage collection from happening, since that ALSO occurs on the
    // main thread, resulting in a deadlock.)
    // CreateDefaultApp(), on the other hand, HAS to run on the main thread,
    // because it internally depends on several main-thread-specific function
    // calls.  (get_IsEditor and get_IsPlaying)
    Task TestCreateDestroy() {
      return Task.Run(() => {
        storage = FirebaseStorage.DefaultInstance;
        try {
          for (int i = 0; i < 100; ++i) {
            // Dereference the default storage objects.
            storage = null;
            DestroyDefaultApp();
            // Dereference the default storage and app objects.
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();

            var task = mainThreadDispatcher.RunOnMainThread(() => {
              CreateDefaultApp();
              storage = FirebaseStorage.DefaultInstance;
            });
            task.Wait();
          }
        } finally {
          System.GC.WaitForPendingFinalizers();

          // Recreate App and Storage instance just in case.
          var task = mainThreadDispatcher.RunOnMainThread(() => {
            CreateDefaultApp();
            storage = FirebaseStorage.DefaultInstance;
          });
          task.Wait();
        }
      });
    }

    Task TestCreateDestroyRace() {
      return Task.Run(() => {
        // Dereference the default storage objects.
        storage = null;
        DestroyDefaultApp();
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();

        try {
          for (int i = 0; i < 1000; ++i) {
            {
              // Get a reference to storage and app and immediately deference both
              FirebaseStorage.DefaultInstance.GetReference("RaceTest");
            }

            System.GC.Collect();
          }
        } finally {
          System.GC.WaitForPendingFinalizers();

          // Recreate App and Storage instance
          var task = mainThreadDispatcher.RunOnMainThread(() => {
            CreateDefaultApp();
            storage = FirebaseStorage.DefaultInstance;
          });
          task.Wait();
        }
      });
    }

    // Make sure it's possible to navigate storage references.
    Task TestStorageReferenceNavigation() {
      DebugLog("TestStorageReferenceNavigation");
      var reference = FirebaseStorage.DefaultInstance.GetReference(LARGE_FILE_PATH);
      AssertEq("reference.Bucket", reference.Bucket, FirebaseApp.DefaultInstance.Options.StorageBucket);
      AssertEq("reference.Name", reference.Name, "large_text_file.txt");
      AssertEq("reference.Path", reference.Path, "/" + LARGE_FILE_PATH);
      AssertEq("reference.Root", reference.Root, FirebaseStorage.DefaultInstance.RootReference);
      AssertEq("reference.Storage", reference.Storage, FirebaseStorage.DefaultInstance);

      var parentReference = reference.Parent;
      AssertEq("parentReference.Bucket", parentReference.Bucket,
               FirebaseApp.DefaultInstance.Options.StorageBucket);
      AssertEq("parentReference.Name", parentReference.Name, "to_a");
      AssertEq("parentReference.Path", parentReference.Path,
               "/this_is_a/test_path/to_a");
      AssertEq("parentReference.Root", parentReference.Root,
               FirebaseStorage.DefaultInstance.RootReference);
      AssertEq("parentReference.Storage", parentReference.Storage, FirebaseStorage.DefaultInstance);

      var childReference = parentReference.Child("small_text_file.txt");
      AssertEq("childReference.Bucket", childReference.Bucket,
               FirebaseApp.DefaultInstance.Options.StorageBucket);
      AssertEq("childReference.Name", childReference.Name, "small_text_file.txt");
      AssertEq("childReference.Path", childReference.Path,
               "/this_is_a/test_path/to_a/small_text_file.txt");
      AssertEq("childReference.Root", childReference.Root,
               FirebaseStorage.DefaultInstance.RootReference);
      AssertEq("childReference.Storage", childReference.Storage, FirebaseStorage.DefaultInstance);

      return CompletedTask();
    }

    // Validate returning url for default and non-default storage objects.
    Task TestUrl() {
      DebugLog("TestUrl");
      // Get the storage URL on the default app.
      var expectedDefaultUrl = String.Format("gs://{0}", FirebaseApp.DefaultInstance.Options.StorageBucket);
      var defaultUrl = FirebaseStorage.DefaultInstance.Url();
      AssertEq("defaultUrl", defaultUrl, expectedDefaultUrl);

      // Get the storage URL on a custom app.
      var customApp = FirebaseApp.Create(new AppOptions { StorageBucket = "somebucket" }, "emptyapp");
      var customStorage = FirebaseStorage.GetInstance(customApp, "gs://somebucket");
      var customStorageUrl = customStorage.Url();
      AssertEq("customStorageUrl", customStorageUrl, "gs://somebucket");
      return CompletedTask();
    }

    // Validate retrieving references to different buckets and non-default storage objects.
    Task TestGetReference() {
      DebugLog("TestGetReference");
      // Get references using a path vs. URL on the default app.
      var defaultReference = FirebaseStorage.DefaultInstance.GetReference(LARGE_FILE_PATH);
      var defaultReferenceFromUrl = FirebaseStorage.DefaultInstance.GetReferenceFromUrl(
          String.Format("gs://{0}/{1}", FirebaseApp.DefaultInstance.Options.StorageBucket,
                        LARGE_FILE_PATH));
      AssertEq("defaultReference", defaultReference, defaultReferenceFromUrl);

      // Get references using a path vs. URL on a custom app.
      var customApp = FirebaseApp.Create(new AppOptions { StorageBucket = "somebucket" }, "emptyapp");
      var customStorage = FirebaseStorage.GetInstance(customApp, "gs://somebucket");
      var anotherReferenceFromUrl = customStorage.GetReferenceFromUrl(
          "gs://somebucket/somefile/path.txt");
      var anotherReference = FirebaseStorage.GetInstance(customApp, "gs://somebucket").GetReference(
          "somefile/path.txt");
      AssertEq("anotherReferenceFromUrl", anotherReferenceFromUrl, anotherReference);
      return CompletedTask();
    }

    // Should fail when using invalid storage URIs.
    Task TestGetStorageInvalidUris() {
      DebugLog("TestGetStorageInvalidUris");
      try {
        var brokenStorage = FirebaseStorage.GetInstance(
          String.Format("gs://{0}/a/b/c/d", FirebaseApp.DefaultInstance.Options.StorageBucket));
        Assert("brokenStorage == null", brokenStorage == null);
      } catch (ArgumentException) {
        // Drop through.
      }
      return CompletedTask();
    }

    // Should fail when trying to get a storage object from an app with a mismatched bucket name.
    Task TestGetStorageWrongBucket() {
      DebugLog("TestGetStorageWrongBucket");
      try {
        var reference = FirebaseStorage.DefaultInstance.GetReferenceFromUrl(
            "gs://somebucket/somefile/path.txt");
        Assert("reference == null", reference == null);
      } catch (ArgumentException) {
        // Drop through.
      }
      return CompletedTask();
    }

    // Validate metadata contains the specifeid expected custom metadata.
    private void ValidateCustomMetadata(StorageMetadata metadata,
                                        Dictionary<string, string> expected) {
      foreach (var kv in expected) {
        AssertEq(String.Format("GetCustomMetadata {0}", kv.Key), metadata.GetCustomMetadata(kv.Key),
                 kv.Value);
      }
      AssertEq("CustomMetadataKeys.Count", (new List<string>(metadata.CustomMetadataKeys)).Count,
               expected.Count);
      foreach (var key in metadata.CustomMetadataKeys) {
        Assert(String.Format("Contains key {0}", key), expected.ContainsKey(key));
      }
    }

    // Current time in the time zone reported by the StorageMetadata object.
    private DateTime MetadataTimeZoneNow() {
      return DateTime.UtcNow;
    }

    // Ensure the uploaded / downloaded file metadata matches expectations.
    private void ValidateMetadata(StorageMetadata metadata, bool uploadFromFile,
                                  string contentType) {
      var expectedName = GetStorageReference().Name;
      Assert("metadata not null", metadata != null);
      AssertEq("metadata.ContentDisposition", metadata.ContentDisposition,
               "inline; filename*=utf-8''" + expectedName);
      AssertEq("metadata.ContentEncoding", metadata.ContentEncoding, "identity");
      if (contentType != null) {
        AssertEq("metadata.ContentType", metadata.ContentType, contentType);
      }
      AssertEq("metadata.Reference", metadata.Reference, GetStorageReference());
      AssertEq("metadata.Bucket", metadata.Bucket, GetStorageReference().Bucket);
      AssertEq("metadata.Name", metadata.Name, expectedName);
      Assert("metadata.CreationTimeMillis",
             Math.Abs(MetadataTimeZoneNow().Subtract(metadata.CreationTimeMillis).TotalSeconds) > 0);
      Assert("metadata.UpdatedTimeMillis",
             Math.Abs(MetadataTimeZoneNow().Subtract(metadata.UpdatedTimeMillis).TotalMinutes) < 5);
      Assert("metadata.Generation", Int64.Parse(metadata.Generation) > 0);
      Assert("metadata.MetadataGeneration", Int64.Parse(metadata.MetadataGeneration) > 0);
      AssertEq("metadata.SizeBytes", metadata.SizeBytes, expectedFileSize);

      // The following metadata may varies based on the MetadataTestMode
      AssertEq("metadata.CacheControl", metadata.CacheControl, expectedMetadataCacheControlValue);
      AssertEq("metadata.ContentLanguage", metadata.ContentLanguage,
        expectedMetadataCacheContentLanguageValue);
      ValidateCustomMetadata(metadata, expectedCustomMetadata);
    }

    // Track upload progress.
    protected override void DisplayUploadState(UploadState uploadState) {
      base.DisplayUploadState(uploadState);
      Assert("uploadState.BytesTransferred",
             uploadState.BytesTransferred <= expectedFileSize ||
             uploadState.BytesTransferred <= uploadState.TotalByteCount);
      if (uploadState.TotalByteCount > 0) {
        // Mobile SDKs include the metadata as part of the upload size.
        Assert("uploadState.TotalByteCount", uploadState.TotalByteCount >= expectedFileSize);
      } else {
        // When uploading a stream the C# SDK does not know the complete file size so it reports -1
        // instead.
        AssertEq("uploadState.TotalByteCount", uploadState.TotalByteCount, -1);
      }
      AssertEq("uploadState.TotalByteCount", uploadState.Reference.Path,
               expectedStorageReference.Path);
      // Assert(uploadState.Metadata != null);  // TODO: Write validator for this.
      // TODO: This is supported in the C# build, need this in the C++ version.
      //AssertEq(uploadState.UploadSessionUri != null);
      progressUpdateCount++;
      if (throwExceptionsInProgressCallbacks) {
        throw new Exception(String.Format("Upload state {0}", progressUpdateCount));
      }
    }

    // Validate an upload completed with a report of the expected metadata.
    Task ValidateUploadSuccessful(Task task, bool uploadFromFile, string contentType) {
      var storageMetadataTask = task as Task<StorageMetadata>;
      Assert("storageMetadataTask != null", storageMetadataTask != null);
      if (!(storageMetadataTask.IsFaulted || storageMetadataTask.IsCanceled)) {
        ValidateMetadata(storageMetadataTask.Result, uploadFromFile, contentType);
        // Make sure progress was reported.
        Assert("progressCount > 0", progressUpdateCount > 0);
      }
      return storageMetadataTask;
    }

    // Validate an upload completed with a report of the expected metadata when uploading from a
    // byte array or stream.
    Task ValidateUploadSuccessfulNotFile(Task task) {
      return ValidateUploadSuccessful(task, false, null);
    }

    // Validate an upload completed with a report of the expected metadata when uploading from a
    // local file.
    Task ValidateUploadSuccessfulFile(Task task) {
      return ValidateUploadSuccessful(task, true, ContentTypePlainText);
    }

    // Set the metadata when uploading a file and the expected values for validation later.
    void SetMetadataForTest(MetadataTestMode mode, String contentType) {
      switch (mode) {
        case MetadataTestMode.Both:
          fileMetadataChangeString = METADATA_STRING_NON_CUSTOM_ONLY +
                                     METADATA_STRING_CUSTOM_ONLY;

          expectedMetadataCacheControlValue = EXPECTED_METADATA_CACHE_CONTROL;
          expectedMetadataCacheContentLanguageValue = EXPECTED_METADATA_CONTENT_LANGUAGE;
          expectedCustomMetadata = EXPECTED_CUSTOM_METADATA;
          break;
        case MetadataTestMode.CustomOnly:
          fileMetadataChangeString = METADATA_STRING_CUSTOM_ONLY;

          expectedMetadataCacheControlValue = "";
          expectedMetadataCacheContentLanguageValue = "";
          expectedCustomMetadata = EXPECTED_CUSTOM_METADATA;
          break;
        case MetadataTestMode.NonCustomOnly:
          fileMetadataChangeString = METADATA_STRING_NON_CUSTOM_ONLY;

          expectedMetadataCacheControlValue = EXPECTED_METADATA_CACHE_CONTROL;
          expectedMetadataCacheContentLanguageValue = EXPECTED_METADATA_CONTENT_LANGUAGE;
          expectedCustomMetadata = new Dictionary<string, string>();
          break;
        case MetadataTestMode.None:
          fileMetadataChangeString = "";

          expectedMetadataCacheControlValue = "";
          expectedMetadataCacheContentLanguageValue = "";
          expectedCustomMetadata = new Dictionary<string, string>();
          break;
      }

      if (contentType != null) {
        fileMetadataChangeString += "ContentType="+contentType+"\n";
      }

      expectedMetadataTestMode = mode;
    }

    Task UploadToPath(string path, string contents, MetadataTestMode metadata_mode,
                      Func<Task<StorageMetadata>> uploadFunc,
                      TaskValidationDelegate taskValidationDelegate) {
      progressUpdateCount = 0;
      storageLocation = path;
      fileContents = contents;
      SetMetadataForTest(metadata_mode, null);
      expectedFileSize = contents.Length;
      expectedStorageReference = GetStorageReference();
      return uploadFunc().ContinueWithOnMainThread(task => {
        return taskValidationDelegate(task);
      });
    }

    // Upload large file and ensure returned metadata is valid after upload.
    Task TestUploadBytesLargeFile() {
      return UploadToPath(LARGE_FILE_PATH, LARGE_FILE_CONTENTS, MetadataTestMode.Both,
                          UploadBytesAsync, ValidateUploadSuccessfulNotFile);
    }

    // Upload small file and ensure returned metadata is valid after upload.
    Task TestUploadBytesSmallFile() {
      return UploadToPath(SMALL_FILE_PATH, SMALL_FILE_CONTENTS, MetadataTestMode.Both,
                          UploadBytesAsync, ValidateUploadSuccessfulNotFile);
    }

    Task TestUploadBytesSmallFileWithNoMetadata() {
      return UploadToPath(METADATA_TEST_FILE_PATH, SMALL_FILE_CONTENTS,
                          MetadataTestMode.None, UploadBytesAsync,
                          ValidateUploadSuccessfulNotFile);
    }

    Task TestUploadBytesSmallFileWithNonCustomOnlyMetadata() {
      return UploadToPath(METADATA_TEST_FILE_PATH, SMALL_FILE_CONTENTS,
                          MetadataTestMode.NonCustomOnly, UploadBytesAsync,
                          ValidateUploadSuccessfulNotFile);
    }

    Task TestUploadBytesSmallFileWithCustomOnlyMetadata() {
      return UploadToPath(METADATA_TEST_FILE_PATH, SMALL_FILE_CONTENTS,
                          MetadataTestMode.CustomOnly, UploadBytesAsync,
                          ValidateUploadSuccessfulNotFile);
    }

    Task TestUploadBytesSmallFileWithBothMetadata() {
      return UploadToPath(METADATA_TEST_FILE_PATH, SMALL_FILE_CONTENTS,
                          MetadataTestMode.Both, UploadBytesAsync,
                          ValidateUploadSuccessfulNotFile);
    }

    // Upload small file and update metadata after upload.
    Task TestUploadBytesSmallFileThenUpdateMetadata() {
      return TestUploadBytesSmallFile().ContinueWithOnMainThread((task) => {
        var metadataChange = new MetadataChange {
          CacheControl = "no-transform",
          ContentDisposition = "attachment; filename=\"helloworld.txt\"",
          ContentEncoding = "gzip",
          ContentLanguage = "es",
          ContentType = "text/html; charset=utf-8",
          CustomMetadata = new Dictionary<string, string> {
            {"its_different", "metadata"},
            {"this", "isnt"},
          }
        };
        return GetStorageReference().UpdateMetadataAsync(metadataChange).ContinueWithOnMainThread(
              (metadataTask) => {
                if (!(metadataTask.IsCanceled || metadataTask.IsFaulted)) {
                  var metadata = metadataTask.Result;
                  Assert("metadata != null", metadata != null);
                  AssertEq("metadata.CacheControl", metadata.CacheControl, "no-transform");
                  AssertEq("metadata.CacheControl", metadata.ContentDisposition,
                           "attachment; filename=\"helloworld.txt\"");
                  AssertEq("metadata.CacheControl", metadata.ContentEncoding, "gzip");
                  AssertEq("metadata.ContentLanguage", metadata.ContentLanguage, "es");
                  AssertEq("metadata.ContentType", metadata.ContentType, "text/html; charset=utf-8");
                  AssertEq("metadata.Reference", metadata.Reference, GetStorageReference());
                  AssertEq("metadata.Bucket", metadata.Bucket, GetStorageReference().Bucket);
                  AssertEq("metadata.Name", metadata.Name, "small_text_file.txt");
                  Assert("metadata.CreationTimeMillis",
                         Math.Abs(MetadataTimeZoneNow().Subtract(
                          metadata.CreationTimeMillis).TotalSeconds) > 0);
                  Assert("metadata.UpdatedTimeMillis",
                         Math.Abs(MetadataTimeZoneNow().Subtract(
                          metadata.UpdatedTimeMillis).TotalMinutes) < 5);
                  Assert("metadata.Generation", Int64.Parse(metadata.Generation) > 0);
                  Assert("metadata.MetadataGeneration", Int64.Parse(metadata.MetadataGeneration) > 0);
                  AssertEq("metadata.SizeBytes", metadata.SizeBytes, expectedFileSize);
                  ValidateCustomMetadata(metadata, new Dictionary<string, string> {
                  {"this", "isnt"},
                  {"just", "a"},
                  {"set", "of"},
                  {"test", "metadata"},
                  {"its_different", "metadata"}
                });
                }
                return metadataTask;
              }).Unwrap();
      }).Unwrap();
    }

    // Upload large file using stream and ensure returned metadata is valid after upload.
    Task TestUploadStreamLargeFile() {
      return UploadToPath(LARGE_FILE_PATH, LARGE_FILE_CONTENTS, MetadataTestMode.Both,
                          UploadStreamAsync, ValidateUploadSuccessfulNotFile);
    }

    // Upload small file using stream and ensure returned metadata is valid after upload.
    Task TestUploadStreamSmallFile() {
      return UploadToPath(SMALL_FILE_PATH, SMALL_FILE_CONTENTS, MetadataTestMode.Both,
                          UploadStreamAsync, ValidateUploadSuccessfulNotFile);
    }

    // Write contents to a local file relative to the persistent data path.
    void WriteFile(string pathRelativeToPersistentDataPath, string contents) {
      var localDirectory = persistentDataPath;
      var localPath = Path.Combine(localDirectory, pathRelativeToPersistentDataPath);
      if (!Directory.Exists(localDirectory))
        Directory.CreateDirectory(localDirectory);
      if (File.Exists(localPath))
        File.Delete(localPath);
      File.WriteAllText(localPath, contents);
    }

    // Upload from file and ensure returned storage metadata is valid after upload.
    Task UploadFromFileToPath(string path, string contents,
                              TaskValidationDelegate taskValidationDelegate) {
      var filename = Path.GetFileName(path);
      WriteFile(filename, contents);
      // Initialize UIHandler parameters.
      localFilename = filename;
      progressUpdateCount = 0;
      storageLocation = path;
      fileContents = "";
      SetMetadataForTest(MetadataTestMode.Both, ContentTypePlainText);
      expectedFileSize = contents.Length;
      expectedStorageReference = GetStorageReference();
      return UploadFromFileAsync().ContinueWithOnMainThread((task) => {
        return taskValidationDelegate(task);
      }).Unwrap();
    }

    // Upload from large file and ensure returned metadata is valid after upload.
    Task TestUploadFromFileLargeFile() {
      return UploadFromFileToPath(LARGE_FILE_PATH, LARGE_FILE_CONTENTS,
                                  ValidateUploadSuccessfulFile);
    }

    // Upload from small file and ensure returned metadata is valid after upload.
    Task TestUploadFromFileSmallFile() {
      return UploadFromFileToPath(SMALL_FILE_PATH, SMALL_FILE_CONTENTS,
                                  ValidateUploadSuccessfulFile);
    }

    // Try uploading from a file that doesn't exist.
    // The old C# implementation throws an exception on an internal thread when presented with an
    // invalid filename which can't be caught by the application or in this case, this test case.
    Task TestUploadFromNonExistantFile() {
      storageLocation = SMALL_FILE_PATH;
      expectedStorageReference = GetStorageReference();
      localFilename = Path.GetFileName(NON_EXISTANT_FILE_PATH);
      var expectedLocalPath = PathToPersistentDataPathUriString(localFilename);
      return UploadFromFileAsync().ContinueWithOnMainThread((uploadTask) => {
        Assert("uploadTask.IsFaulted", uploadTask.IsFaulted);
        var fileNotFoundException =
          (new List<Exception>(uploadTask.Exception.InnerExceptions))[0] as FileNotFoundException;
        Assert("fileNotFoundException", fileNotFoundException != null);
        AssertEq("fileNotFoundException.FileName", fileNotFoundException.FileName,
                  expectedLocalPath);
        return CompletedTask();
      }).Unwrap();
    }

    // Coroutine method which cancels an operation after waiting cancelationDelay seconds or until the
    // first progress update from the transfer.
    IEnumerator CancelAfterDelayInSecondsCoroutine(float cancelationDelay) {
      float startTime = UnityEngine.Time.realtimeSinceStartup;
      yield return new WaitWhile(
          () => {
            return (UnityEngine.Time.realtimeSinceStartup - startTime) < cancelationDelay &&
                progressUpdateCount == 0;
          });
      // Since it's possible for no progress updates occur before cancelation, fake one here so
      // that ValidateTaskCanceled passes.
      progressUpdateCount = 1;
      CancelOperation();
      // TODO: If a task fails prematurely or runs very quickly this could signal cancelation after
      // the task is complete.
    }

    // Cancel an operation after waiting cancelationDelay seconds.
    void CancelAfterDelayInSeconds(float cancelationDelay) {
      mainThreadDispatcher.RunOnMainThread(() => {
        StartCoroutine(CancelAfterDelayInSecondsCoroutine(cancelationDelay));
      });
    }

    // Validate a task was canceled.
    Task ValidateTaskCanceled(Task task) {
      Assert("task.IsCompleted", task.IsCompleted);
      Assert("!task.IsFaulted", !task.IsFaulted);
      if (!task.IsCanceled) {
        DebugLog("WARNING: Expected task to be canceled, but it finished before it could.");
      }
      return task;
    }

    // Start uploading from a byte array and cancel the upload.
    Task TestUploadBytesWithCancelation() {
      var task = UploadToPath(LARGE_FILE_PATH, LARGE_FILE_CONTENTS,
                              MetadataTestMode.Both, UploadBytesAsync, ValidateTaskCanceled);
      CancelAfterDelayInSeconds(CANCELATION_DELAY_SECONDS);
      return task;
    }

    // Start uploading with a stream and cancel the upload.
    Task TestUploadStreamWithCancelation() {
      var task = UploadToPath(LARGE_FILE_PATH, LARGE_FILE_CONTENTS,
                              MetadataTestMode.Both, UploadStreamAsync, ValidateTaskCanceled);
      CancelAfterDelayInSeconds(CANCELATION_DELAY_SECONDS);
      return task;
    }

    // Start uploading from a file and cancel the upload.
    Task TestUploadFromFileWithCancelation() {
      var task = UploadFromFileToPath(LARGE_FILE_PATH, LARGE_FILE_CONTENTS, ValidateTaskCanceled);
      CancelAfterDelayInSeconds(CANCELATION_DELAY_SECONDS);
      return task;
    }

    // Upload small file and retrieve a download URL.
    Task TestUploadSmallFileGetDownloadUrl() {
      return TestUploadBytesSmallFile().ContinueWithOnMainThread((task) => {
        return GetStorageReference().GetDownloadUrlAsync().ContinueWithOnMainThread(
          (downloadUrlTask) => {
            if (downloadUrlTask.IsCanceled || downloadUrlTask.IsFaulted)
              return downloadUrlTask;
            var url = downloadUrlTask.Result;
            AssertEq("url.Host", url.Host, "firebasestorage.googleapis.com");
            AssertEq("url.AbsolutePath", Uri.UnescapeDataString(url.AbsolutePath),
                   String.Format("/v0/b/{0}/o/{1}",
                                 FirebaseApp.DefaultInstance.Options.StorageBucket,
                                 SMALL_FILE_PATH));
            return CompletedTask();
          }
        );
      }).Unwrap();
    }

    // Get download URL from non-existant file.
    Task TestGetDownloadUrlNonExistantFile() {
      storageLocation = NON_EXISTANT_FILE_PATH;
      return GetStorageReference().GetDownloadUrlAsync().ContinueWithOnMainThread((task) => {
        Assert("task.IsFaulted", task.IsFaulted);
        StorageException exception =
          (StorageException)(new List<Exception>(task.Exception.InnerExceptions))[0];
        AssertEq("exception.ErrorCode", exception.ErrorCode, StorageException.ErrorObjectNotFound);
        AssertEq("exception.HttpResultCode", exception.HttpResultCode, 404);
        return CompletedTask();
      }).Unwrap();
    }

    // Upload small file and retrieve metadata.
    Task TestUploadSmallFileGetMetadata() {
      return TestUploadBytesSmallFile().ContinueWithOnMainThread((task) => {
        return GetMetadataAsync().ContinueWithOnMainThread((metadataTask) => {
          if (metadataTask.IsCanceled || metadataTask.IsFaulted)
            return metadataTask;
          ValidateMetadata(metadataTask.Result, false, null);
          return CompletedTask();
        });
      }).Unwrap();
    }

    // Get metadata from a known missing file.
    Task GetMetadataNonExistantFile(string path) {
      return FirebaseStorage.DefaultInstance.GetReference(
                path).GetMetadataAsync().ContinueWithOnMainThread((task) => {
                  TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
                  tcs.SetResult(task.IsFaulted);
                  return tcs.Task;
                }).Unwrap();
    }

    // Get metadata from a non-existant file.
    Task TestGetMetadataNonExistantFile() {
      return GetMetadataNonExistantFile(NON_EXISTANT_FILE_PATH);
    }

    // Upload small file, delete and validate the file is inaccessible after deletion.
    Task TestUploadSmallFileAndDelete() {
      return TestUploadBytesSmallFile().ContinueWithOnMainThread((task) => {
        return DeleteAsync().ContinueWithOnMainThread((deleteTask) => {
          if (deleteTask.IsCanceled || deleteTask.IsFaulted)
            return deleteTask;
          // Try getting metadata from the reference, this should fail as the file has been
          // deleted.
          return GetMetadataNonExistantFile(SMALL_FILE_PATH);
        });
      }).Unwrap();
    }

    // Try to delete non-existant file.
    Task TestDeleteNonExistantFile() {
      storageLocation = NON_EXISTANT_FILE_PATH;
      return DeleteAsync().ContinueWithOnMainThread((task) => {
        Assert("task.IsFaulted", task.IsFaulted);
        StorageException exception =
          (StorageException)(new List<Exception>(task.Exception.InnerExceptions))[0];
        AssertEq("exception.ErrorCode", exception.ErrorCode,
                 Firebase.Storage.StorageException.ErrorObjectNotFound);
        AssertEq("exception.HttpResultCode", exception.HttpResultCode, 404);
        return CompletedTask();
      }).Unwrap();
    }

    // Try to download non-existant file.
    Task TestDownloadNonExistantFile() {
      storageLocation = NON_EXISTANT_FILE_PATH;
      return DownloadBytesAsync().ContinueWithOnMainThread((task) => {
        Assert("task.IsFaulted", task.IsFaulted);
        StorageException exception =
          (StorageException)(new List<Exception>(task.Exception.InnerExceptions))[0];
        AssertEq("exception.ErrorCode", exception.ErrorCode,
                 Firebase.Storage.StorageException.ErrorObjectNotFound);
        AssertEq("exception.ErrorCode", exception.HttpResultCode, 404);
        return CompletedTask();
      }).Unwrap();
    }


    // Track download progress.
    protected override void DisplayDownloadState(DownloadState downloadState) {
      base.DisplayDownloadState(downloadState);
      Assert("downloadState.BytesTransferred",
             downloadState.BytesTransferred <= expectedFileSize ||
             downloadState.BytesTransferred <= downloadState.TotalByteCount ||
             expectedFileSize == 0);
      AssertEq("downloadState.Reference.Path",
               downloadState.Reference.Path, expectedStorageReference.Path);
      // In mobile clients the reported total byte count includes metadata.
      // Also, if the total download size isn't yet known it will be reported as -1.
      Assert("downloadState.TotalByteCount",
             downloadState.TotalByteCount < 0 ||
             downloadState.TotalByteCount >= expectedFileSize);
      progressUpdateCount++;
      if (throwExceptionsInProgressCallbacks) {
        throw new Exception(String.Format("Download state {0}", progressUpdateCount));
      }
    }

    // Validate downloading a byte array matches the specified expected file contents.
    Task ValidateDownloadedBytes(Task downloadTask, string contents) {
      if (downloadTask.IsFaulted || downloadTask.IsCanceled)
        return downloadTask;
      var downloadTaskWithResult = downloadTask as Task<byte[]>;
      Assert("downloadTaskWithResult != null", downloadTaskWithResult != null);
      // Validate the downloaded byte array matches the expected file contents.
      var downloadedBytes = downloadTaskWithResult.Result;
      var expectedFileContents = System.Text.Encoding.ASCII.GetBytes(contents);
      AssertEq("expectedFileContents.Length", expectedFileContents.Length, downloadedBytes.Length);
      for (int i = 0; i < expectedFileContents.Length; ++i) {
        AssertEq(String.Format("expectedFileContents[{0}]", i),
                 expectedFileContents[i], downloadedBytes[i]);
      }
      // Downloading small files does not report status updates.
      if (contents.Length == LARGE_FILE_CONTENTS.Length) {
        // The large file might have downloaded too fast, so only warn.
        if (progressUpdateCount == 0) {
          DebugLog("WARNING: Expected a progress update, but none happened.");
        }
      }
      return downloadTask;
    }

    // Upload file and download as byte array.
    Task UploadAndDownloadAsByteArray(string path, string contents,
                                      TaskValidationDelegate downloadTaskValidationDelegate,
                                      Action predownloadOperation = null) {
      return UploadToPath(path, contents, MetadataTestMode.Both, UploadBytesAsync,
                          ValidateUploadSuccessfulNotFile).ContinueWithOnMainThread(
                            (task) => {
                              if (task.IsFaulted || task.IsCanceled)
                                return task;
                              expectedStorageReference = GetStorageReference();
                              expectedFileSize = contents.Length;
                              progressUpdateCount = 0;
                              if (predownloadOperation != null)
                                predownloadOperation();
                              return DownloadBytesAsync().ContinueWithOnMainThread(
                                (downloadTask) => {
                                  return downloadTaskValidationDelegate(downloadTask);
                                }
                              ).Unwrap();
                            }
                          ).Unwrap();
    }

    // Upload a small file and download.
    Task TestUploadSmallFileAndDownload() {
      return UploadAndDownloadAsByteArray(
          SMALL_FILE_PATH, SMALL_FILE_CONTENTS,
          (task) => { return ValidateDownloadedBytes(task, SMALL_FILE_CONTENTS); });
    }

    // Upload a small file and download while throwing exceptions in the progress callbacks.
    Task TestUploadSmallFileAndDownloadWithProgressExceptions() {
      throwExceptionsInProgressCallbacks = true;
      return UploadAndDownloadAsByteArray(
          SMALL_FILE_PATH, SMALL_FILE_CONTENTS,
          (task) => {
            throwExceptionsInProgressCallbacks = false;
            return ValidateDownloadedBytes(task, SMALL_FILE_CONTENTS);
          });
    }

    // Upload a large file and download.
    Task TestUploadLargeFileAndDownload() {
      return UploadAndDownloadAsByteArray(
          LARGE_FILE_PATH, LARGE_FILE_CONTENTS,
          (task) => { return ValidateDownloadedBytes(task, LARGE_FILE_CONTENTS); });
    }

    // Upload a large file, start downloading then cancel.
    Task TestUploadLargeFileAndDownloadWithCancelation() {
      return UploadAndDownloadAsByteArray(
          LARGE_FILE_PATH, LARGE_FILE_CONTENTS, ValidateTaskCanceled,
          predownloadOperation: () => { CancelAfterDelayInSeconds(CANCELATION_DELAY_SECONDS); });
    }

    // Validate the result of a stream download operation.
    Task ValidateDownloadedStream(Task downloadTask, string contents) {
      AssertEq("fileContents.Length", fileContents.Length, contents.Length);
      AssertEq("fileContents", fileContents, contents);
      // Downloading small files does not report status updates.
      if (contents.Length == LARGE_FILE_CONTENTS.Length) {
        // The large file might have downloaded too fast, so only warn.
        if (progressUpdateCount == 0) {
          DebugLog("WARNING: Expected a progress update, but none happened.");
        }
      }
      return downloadTask;
    }

    // Upload file and download using a stream callback.
    Task UploadAndDownloadUsingStreamCallback(string path, string contents,
                                              TaskValidationDelegate downloadTaskValidationDelegate,
                                              Action predownloadOperation = null) {
      return UploadToPath(path, contents, MetadataTestMode.Both, UploadBytesAsync,
                          ValidateUploadSuccessfulNotFile).ContinueWithOnMainThread(
                            (task) => {
                              if (task.IsFaulted || task.IsCanceled)
                                return task;
                              expectedStorageReference = GetStorageReference();
                              expectedFileSize = contents.Length;
                              progressUpdateCount = 0;
                              if (predownloadOperation != null)
                                predownloadOperation();
                              return
                                DownloadStreamAsync().ContinueWithOnMainThread(
                                  (downloadTask) => {
                                    return downloadTaskValidationDelegate(task);
                                  }
                                ).Unwrap();
                            }
                          ).Unwrap();
    }

    // Upload a small file and download using a stream callback.
    Task TestUploadSmallFileAndDownloadUsingStreamCallback() {
      return UploadAndDownloadUsingStreamCallback(
          SMALL_FILE_PATH, SMALL_FILE_CONTENTS,
          (task) => { return ValidateDownloadedStream(task, SMALL_FILE_CONTENTS); });
    }

    // Upload a large file and download using a stream callback.
    Task TestUploadLargeFileAndDownloadUsingStreamCallback() {
      return UploadAndDownloadUsingStreamCallback(
          LARGE_FILE_PATH, LARGE_FILE_CONTENTS,
          (task) => { return ValidateDownloadedStream(task, LARGE_FILE_CONTENTS); });
    }

    // Upload a large file, start downloading using a stream callback then cancel.
    Task TestUploadLargeFileAndDownloadUsingStreamCallbackWithCancelation() {
      return UploadAndDownloadUsingStreamCallback(
          LARGE_FILE_PATH, LARGE_FILE_CONTENTS, ValidateTaskCanceled,
          predownloadOperation: () => { CancelAfterDelayInSeconds(CANCELATION_DELAY_SECONDS); });
    }

    // Validate the result of a file download operation.
    Task ValidateDownloadedFile(Task downloadTask, string contents) {
      var filename = FileUriStringToPath(PathToPersistentDataPathUriString(localFilename));
      Assert(String.Format("{0} exists", filename), File.Exists(filename));
      var readFileContents = File.ReadAllText(filename);
      AssertEq(String.Format("{0} Length", filename), readFileContents.Length, contents.Length);
      AssertEq(String.Format("{0} contents", filename), readFileContents, contents);
      // Validate UIHandler has read the file contents correctly.
      AssertEq("fileContents.Length", fileContents.Length, contents.Length);
      AssertEq("fileContents", fileContents, contents);
      // Downloading small files does not report status updates.
      if (contents.Length == LARGE_FILE_CONTENTS.Length) {
        // The large file might have downloaded too fast, so only warn.
        if (progressUpdateCount == 0) {
          DebugLog("WARNING: Expected a progress update, but none happened.");
        }
      }
      return downloadTask;
    }

    // Upload file and download to a local file.
    Task UploadAndDownloadToFile(string path, string contents,
                                 TaskValidationDelegate downloadTaskValidationDelegate,
                                 Action predownloadOperation = null) {
      localFilename = Path.GetFileName(path);
      var downloadFilePath = FileUriStringToPath(PathToPersistentDataPathUriString(localFilename));
      if (File.Exists(downloadFilePath))
        File.Delete(downloadFilePath);
      return UploadToPath(path, contents, MetadataTestMode.Both, UploadBytesAsync,
                          ValidateUploadSuccessfulNotFile).ContinueWithOnMainThread(
                            (task) => {
                              if (task.IsFaulted || task.IsCanceled)
                                return task;
                              expectedStorageReference = GetStorageReference();
                              expectedFileSize = contents.Length;
                              progressUpdateCount = 0;
                              localFilename = Path.GetFileName(path);
                              if (predownloadOperation != null)
                                predownloadOperation();
                              return
                                DownloadToFileAsync().ContinueWithOnMainThread(
                                  (downloadTask) => {
                                    return downloadTaskValidationDelegate(downloadTask);
                                  }
                                ).Unwrap();
                            }
                          ).Unwrap();
    }

    // Upload a small file and download to a file.
    Task TestUploadSmallFileAndDownloadToFile() {
      return UploadAndDownloadToFile(
          SMALL_FILE_PATH, SMALL_FILE_CONTENTS,
          (task) => { return ValidateDownloadedFile(task, SMALL_FILE_CONTENTS); });
    }

    // Upload a large file and download to a file.
    Task TestUploadLargeFileAndDownloadToFile() {
      return UploadAndDownloadToFile(
          LARGE_FILE_PATH, LARGE_FILE_CONTENTS,
          (task) => { return ValidateDownloadedFile(task, LARGE_FILE_CONTENTS); });
    }

    // Upload a large file, start to download to a file then cancel.
    Task TestUploadLargeFileAndDownloadToFileWithCancelation() {
      return UploadAndDownloadToFile(
          LARGE_FILE_PATH, LARGE_FILE_CONTENTS, ValidateTaskCanceled,
          predownloadOperation: () => { CancelAfterDelayInSeconds(CANCELATION_DELAY_SECONDS); });
    }

    async Task TestList() {
      string baseFolder = "test_list_" + Guid.NewGuid().ToString() + "/";
      string file1Path = baseFolder + "file1.txt";
      string file2Path = baseFolder + "file2.txt";
      string file3Path = baseFolder + "file3.txt";
      string file4Path = baseFolder + "prefix/file4.txt";

      // Upload files
      await UploadToPath(file1Path, SMALL_FILE_CONTENTS, MetadataTestMode.None, UploadBytesAsync, ValidateUploadSuccessfulNotFile);
      await UploadToPath(file2Path, SMALL_FILE_CONTENTS, MetadataTestMode.None, UploadBytesAsync, ValidateUploadSuccessfulNotFile);
      await UploadToPath(file3Path, SMALL_FILE_CONTENTS, MetadataTestMode.None, UploadBytesAsync, ValidateUploadSuccessfulNotFile);
      await UploadToPath(file4Path, SMALL_FILE_CONTENTS, MetadataTestMode.None, UploadBytesAsync, ValidateUploadSuccessfulNotFile);

      // List with maxResultsPerPage: 2 to test pagination
      var storageRef = FirebaseStorage.DefaultInstance.GetReference(baseFolder);
      var result = await storageRef.ListAsync(maxResults: 2);
      Assert("result != null", result != null);
      
      // We should get 2 items (order is not guaranteed, but we should have exactly 2)
      AssertEq("result.Items.Count + result.Prefixes.Count", result.Items.Count + result.Prefixes.Count, 2);
      Assert("result.NextPageToken != null", !string.IsNullOrEmpty(result.NextPageToken));

      // Fetch page 2
      var result2 = await storageRef.ListAsync(maxResults: 2, pageToken: result.NextPageToken);
      AssertEq("result2.Items.Count + result2.Prefixes.Count", result2.Items.Count + result2.Prefixes.Count, 2); // 1 item remaining + 1 prefix
      
      // Verify we can find the prefix
      bool foundPrefix = false;
      foreach (var prefix in result2.Prefixes) {
        if (prefix.Name == "prefix") foundPrefix = true;
      }
      Assert("foundPrefix", foundPrefix);
    }

    // Verify resumable multi-chunk upload surfaces a non-null UploadSessionUri and can resume from it.
    async Task TestUploadSessionUriAndResume() {
      var targetRef = FirebaseStorage.DefaultInstance.GetReference(LARGE_FILE_PATH);
      var payload = Encoding.UTF8.GetBytes(LARGE_FILE_CONTENTS);
      Uri capturedSessionUri = null;
      var metadata = await targetRef.PutBytesAsync(
          payload,
          null,
          new StorageProgress<UploadState>(state => {
            if (state.UploadSessionUri != null) {
              capturedSessionUri = state.UploadSessionUri;
            }
          }));
      Assert("capturedSessionUri != null", capturedSessionUri != null);
      AssertEq("metadata.SizeBytes", metadata.SizeBytes, payload.Length);

      var resumedMetadata = await targetRef.PutBytesAsync(
          payload,
          null,
          null,
          default,
          capturedSessionUri);
      Assert("resumedMetadata != null", resumedMetadata != null);
      AssertEq("resumedMetadata.SizeBytes", resumedMetadata.SizeBytes, payload.Length);
    }

    // Non-seekable read-only stream over a byte array that throws an IOException once
    // failAfterBytes have been read. Used to simulate an upload interrupted mid-transfer.
    private sealed class FaultingNonSeekableStream : Stream {
      private readonly byte[] data;
      private readonly long failAfterBytes;
      private long position;

      public FaultingNonSeekableStream(byte[] data, long failAfterBytes = -1) {
        this.data = data;
        this.failAfterBytes = failAfterBytes;
      }

      public override bool CanRead => true;
      public override bool CanSeek => false;
      public override bool CanWrite => false;
      public override long Length => throw new NotSupportedException();
      public override long Position {
        get => position;
        set => throw new NotSupportedException();
      }
      public override void Flush() { }
      public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

      public override int Read(byte[] buffer, int offset, int count) {
        if (failAfterBytes >= 0 && position >= failAfterBytes) {
          throw new IOException("Simulated upload stream failure.");
        }
        int available = (int)Math.Min(count, data.Length - position);
        if (failAfterBytes >= 0) {
          available = (int)Math.Min(available, failAfterBytes - position);
        }
        if (available <= 0) return 0;
        Array.Copy(data, position, buffer, offset, available);
        position += available;
        return available;
      }
    }

    // Interrupt a multi-chunk upload after the first chunk is committed (without canceling the
    // session), then resume from the captured UploadSessionUri using a non-seekable stream and
    // verify the final object matches the original payload byte-for-byte.
    async Task TestUploadPartialSessionResume() {
      var targetRef = FirebaseStorage.DefaultInstance.GetReference(LARGE_FILE_PATH);
      var payload = Encoding.UTF8.GetBytes(LARGE_FILE_CONTENTS);
      // The first chunk is 256 KB; fail while reading the second chunk.
      long failAfter = 256 * 1024 + 4096;
      Assert("payload larger than first chunk", payload.Length > failAfter);

      Uri capturedSessionUri = null;
      long lastBytesTransferred = 0;
      bool faulted = false;
      try {
        await targetRef.PutStreamAsync(
            new FaultingNonSeekableStream(payload, failAfter),
            null,
            new StorageProgress<UploadState>(state => {
              if (state.UploadSessionUri != null) {
                capturedSessionUri = state.UploadSessionUri;
              }
              lastBytesTransferred = Math.Max(lastBytesTransferred, state.BytesTransferred);
            }));
      } catch (IOException) {
        faulted = true;
      }
      Assert("interrupted upload faulted", faulted);
      Assert("capturedSessionUri != null", capturedSessionUri != null);
      Assert("first chunk committed before fault", lastBytesTransferred >= 256 * 1024);

      long firstResumedProgress = -1;
      var resumedMetadata = await targetRef.PutStreamAsync(
          new FaultingNonSeekableStream(payload),
          null,
          new StorageProgress<UploadState>(state => {
            if (firstResumedProgress < 0 && state.BytesTransferred > 0) {
              firstResumedProgress = state.BytesTransferred;
            }
          }),
          default,
          capturedSessionUri);
      Assert("resumedMetadata != null", resumedMetadata != null);
      AssertEq("resumedMetadata.SizeBytes", resumedMetadata.SizeBytes, payload.Length);
      Assert("resume started from server offset",
             firstResumedProgress >= 256 * 1024);

      var downloaded = await targetRef.GetBytesAsync(0);
      AssertEq("downloaded.Length", downloaded.Length, payload.Length);
      for (int i = 0; i < payload.Length; ++i) {
        if (downloaded[i] != payload[i]) {
          Assert(String.Format("downloaded[{0}] matches payload", i), false);
        }
      }
    }

    // Verify GetBytesAsync faults with IndexOutOfRangeException when maxDownloadSizeBytes is exceeded.
    Task TestDownloadIntoSmallBuffer() {
      return TestUploadBytesSmallFile().ContinueWithOnMainThread((uploadTask) => {
        if (uploadTask.IsFaulted || uploadTask.IsCanceled) return uploadTask;
        return GetStorageReference().GetBytesAsync(SMALL_FILE_CONTENTS.Length - 1)
            .ContinueWithOnMainThread((downloadTask) => {
              Assert("downloadTask.IsFaulted", downloadTask.IsFaulted);
              var innerException = (new List<Exception>(downloadTask.Exception.InnerExceptions))[0];
              Assert("innerException is IndexOutOfRangeException",
                     innerException is IndexOutOfRangeException);
              return CompletedTask();
            }).Unwrap();
      }).Unwrap();
    }

    // Verify the zero-copy Task<Stream> GetStreamAsync() overload streams object contents directly.
    async Task TestGetStreamAsyncDirect() {
      await TestUploadBytesSmallFile();
      var targetRef = GetStorageReference();
      using (var stream = await targetRef.GetStreamAsync()) {
        using (var reader = new StreamReader(stream, Encoding.UTF8)) {
          string downloaded = await reader.ReadToEndAsync();
          AssertEq("downloaded", downloaded, SMALL_FILE_CONTENTS);
        }
      }
    }

    // Verify mid-transfer CancellationToken cancellation transitions the task to IsCanceled (!IsFaulted).
    async Task TestRealMidTransferCancelation() {
      var targetRef = FirebaseStorage.DefaultInstance.GetReference(LARGE_FILE_PATH);
      var payload = Encoding.UTF8.GetBytes(LARGE_FILE_CONTENTS);
      using (var cts = new System.Threading.CancellationTokenSource()) {
        var uploadTask = targetRef.PutBytesAsync(
            payload,
            null,
            new StorageProgress<UploadState>(state => {
              cts.Cancel();
            }),
            cts.Token);
        try {
          await uploadTask;
          Assert("Expected OperationCanceledException", false);
        } catch (OperationCanceledException) {
          Assert("uploadTask.IsCanceled", uploadTask.IsCanceled);
          Assert("!uploadTask.IsFaulted", !uploadTask.IsFaulted);
        }
      }
    }

    // Verify concurrency throttling gates parallel uploads, downloads, and metadata commands cleanly.
    async Task TestConcurrentTransfersThrottling() {
      string baseFolder = "test_concurrency_" + Guid.NewGuid().ToString() + "/";
      var payload = Encoding.UTF8.GetBytes(SMALL_FILE_CONTENTS);
      var uploadTasks = new List<Task<StorageMetadata>>();
      var refs = new List<StorageReference>();

      for (int i = 0; i < 4; i++) {
        var itemRef = FirebaseStorage.DefaultInstance.GetReference(baseFolder + "item_" + i + ".txt");
        refs.Add(itemRef);
        uploadTasks.Add(itemRef.PutBytesAsync(payload));
      }
      await Task.WhenAll(uploadTasks);

      var downloadTasks = new List<Task<byte[]>>();
      foreach (var itemRef in refs) {
        downloadTasks.Add(itemRef.GetBytesAsync(1024));
      }
      var downloadedResults = await Task.WhenAll(downloadTasks);
      foreach (var resultBytes in downloadedResults) {
        AssertEq("resultBytes.Length", resultBytes.Length, payload.Length);
      }

      var deleteTasks = new List<Task>();
      foreach (var itemRef in refs) {
        deleteTasks.Add(itemRef.DeleteAsync());
      }
      await Task.WhenAll(deleteTasks);
    }

    // Verify that operations targeting the bucket root (empty object path) fail fast with
    // StorageException rather than issuing malformed HTTP requests to ".../o?name=".
    async Task TestRootReferenceRejected() {
      var root = FirebaseStorage.DefaultInstance.RootReference;
      Func<Func<Task>, string, Task> assertRootRejected = async (op, label) => {
        bool caughtStorageException = false;
        try {
          await op();
        } catch (StorageException ex) {
          caughtStorageException = true;
          AssertEq(label + " ErrorCode", ex.ErrorCode, StorageException.ErrorUnknown);
        }
        Assert(label + " threw StorageException", caughtStorageException);
      };

      await assertRootRejected(() => root.GetBytesAsync(1024), "Root.GetBytesAsync");
      await assertRootRejected(() => root.GetMetadataAsync(), "Root.GetMetadataAsync");
      await assertRootRejected(() => root.GetDownloadUrlAsync(), "Root.GetDownloadUrlAsync");
      await assertRootRejected(() => root.DeleteAsync(), "Root.DeleteAsync");
      await assertRootRejected(() => root.PutBytesAsync(new byte[] { 1, 2, 3 }), "Root.PutBytesAsync");
      await assertRootRejected(() => root.GetStreamAsync(), "Root.GetStreamAsync");
    }

    // Verify that passing null for standard metadata fields or custom metadata keys in
    // UpdateMetadataAsync serializes JSON null and clears/deletes those fields on the server.
    async Task TestDeleteCustomMetadataAndClearStandardFields() {
      var targetRef = FirebaseStorage.DefaultInstance.GetReference(METADATA_TEST_FILE_PATH);
      var payload = Encoding.UTF8.GetBytes(SMALL_FILE_CONTENTS);
      var initialChange = new MetadataChange {
        CacheControl = "no-cache",
        ContentLanguage = "en",
        CustomMetadata = new Dictionary<string, string> {
          { "keep_key", "keep_val" },
          { "delete_key", "delete_val" }
        }
      };
      var uploaded = await targetRef.PutBytesAsync(payload, initialChange);
      AssertEq("initial CacheControl", uploaded.CacheControl, "no-cache");
      AssertEq("initial ContentLanguage", uploaded.ContentLanguage, "en");
      AssertEq("initial keep_key", uploaded.GetCustomMetadata("keep_key"), "keep_val");
      AssertEq("initial delete_key", uploaded.GetCustomMetadata("delete_key"), "delete_val");

      var clearChange = new MetadataChange {
        CacheControl = null,
        ContentLanguage = null,
        CustomMetadata = new Dictionary<string, string> {
          { "delete_key", null }
        }
      };
      var updated = await targetRef.UpdateMetadataAsync(clearChange);
      AssertEq("cleared CacheControl", updated.CacheControl, "");
      AssertEq("cleared ContentLanguage", updated.ContentLanguage, "");
      AssertEq("preserved keep_key", updated.GetCustomMetadata("keep_key"), "keep_val");
      Assert("deleted delete_key is null", updated.GetCustomMetadata("delete_key") == null);
    }

    // Verify that opening more than MaxConcurrentDownloads (3) streams via GetStreamAsync()
    // without disposing them does not starve subsequent downloads.
    async Task TestUndisposedGetStreamAsyncDoesNotStarveDownloads() {
      await TestUploadBytesSmallFile();
      var targetRef = GetStorageReference();
      var leakedStreams = new List<Stream>();
      try {
        for (int i = 0; i < 4; i++) {
          leakedStreams.Add(await targetRef.GetStreamAsync());
        }
        var downloaded = await targetRef.GetBytesAsync(1024);
        AssertEq("downloaded.Length", downloaded.Length, SMALL_FILE_CONTENTS.Length);
      } finally {
        foreach (var s in leakedStreams) {
          s.Dispose();
        }
      }
    }

    // Verify PutStreamAsync from a seekable stream with a non-zero initial Position uploads only
    // the remaining slice starting at stream.Position.
    async Task TestUploadSeekableStreamFromNonZeroPosition() {
      var targetRef = FirebaseStorage.DefaultInstance.GetReference(SMALL_FILE_PATH);
      byte[] prefix = Encoding.UTF8.GetBytes("0123456789ABCDEF");
      byte[] body = Encoding.UTF8.GetBytes(SMALL_FILE_CONTENTS);
      byte[] combined = new byte[prefix.Length + body.Length];
      Buffer.BlockCopy(prefix, 0, combined, 0, prefix.Length);
      Buffer.BlockCopy(body, 0, combined, prefix.Length, body.Length);

      using (var ms = new MemoryStream(combined, writable: false)) {
        ms.Position = prefix.Length;
        var metadata = await targetRef.PutStreamAsync(ms);
        AssertEq("metadata.SizeBytes", metadata.SizeBytes, body.Length);
      }

      var downloaded = await targetRef.GetBytesAsync(1024);
      AssertEq("downloaded", Encoding.UTF8.GetString(downloaded), SMALL_FILE_CONTENTS);
    }

    // Verify GetFileAsync downloads to a temporary file first and does not clobber a pre-existing
    // destination file if the download fails.
    async Task TestGetFileAsyncPreservesExistingFileOnFailure() {
      string tempFile = Path.Combine(Path.GetTempPath(), "fb_storage_preserve_" + Guid.NewGuid().ToString("N") + ".txt");
      const string originalContent = "original pre-existing file content";
      try {
        File.WriteAllText(tempFile, originalContent);
        var missingRef = FirebaseStorage.DefaultInstance.GetReference(NON_EXISTANT_FILE_PATH);
        bool faulted = false;
        try {
          await missingRef.GetFileAsync(tempFile);
        } catch (StorageException) {
          faulted = true;
        }
        Assert("missingRef.GetFileAsync faulted", faulted);
        Assert("pre-existing destination file still exists", File.Exists(tempFile));
        AssertEq("pre-existing destination file contents preserved",
                 File.ReadAllText(tempFile), originalContent);
        Assert("temp .fbdownload file cleaned up", !File.Exists(tempFile + ".fbdownload"));
      } finally {
        if (File.Exists(tempFile)) {
          File.Delete(tempFile);
        }
      }
    }
  }
}
