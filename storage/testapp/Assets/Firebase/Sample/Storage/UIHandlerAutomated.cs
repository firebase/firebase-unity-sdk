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

    // TODO: Upload and attempt to partially download a file.
  }
}
