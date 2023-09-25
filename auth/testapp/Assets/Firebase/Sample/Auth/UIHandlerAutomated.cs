namespace Firebase.Sample.Auth {
  using Firebase.Extensions;
  using System;
  using System.Collections.Generic;
  using System.Threading;
  using System.Threading.Tasks;

  // An automated version of the UIHandler that runs tests on Firebase Auth.
  public class UIHandlerAutomated : UIHandler {
    // Default settings used for testing.
    private const string AutoTestEmail = "emailtest{0}@autotest.com";
    private const string AutoTestPassword = "password";
    private const string AutoTestDisplayName = "AutoTester";

    private Firebase.Sample.AutomatedTestRunner testRunner;

    // We need a random generator to prevent overlaps on the email test names.
    // TODO(amaurice): Remove this when the automated tests are set up to use a
    // fake server to handle the auth calls, as the overlap problem won't occur.
    private System.Random random;

    // Checks the given Task for an exception.
    // If one is found, it is passed to the TaskCompletionSource, and true is returned.
    // Otherwise, false is returned.
    bool ForwardTaskException<T>(TaskCompletionSource<T> tcs, Task toCheck) {
      if (toCheck.IsFaulted) {
        tcs.TrySetException(toCheck.Exception);
        return true;
      }
      return false;
    }

    public override void Start() {
      // Set the list of tests to run, note this is done at Start since they are
      // non-static.
      Func<Task>[] tests = {
        TestCreateDestroy,
        TestSignInAnonymouslyAsync,
        TestSignInEmailAsync,
        TestSignInCredentialAsync,
        TestUpdateUserProfileAsync,
        TestSignInAnonymouslyAsync_DEPRECATED,
        TestSignInEmailAsync_DEPRECATED,
        TestSignInCredentialAsync_DEPRECATED,
        TestCachingUser,
        // TODO(b/132083720) This test is currently broken, so disable it until it is fixed.
        // TestSignInAnonymouslyWithExceptionsInEventHandlersAsync,
        // TODO(b/281153256): Add more test cases
      };
      testRunner = AutomatedTestRunner.CreateTestRunner(
        testsToRun: tests,
        logFunc: DebugLog
      );
      // TestCreateDestroy can take longer than 60s
      testRunner.TestTimeoutSeconds = 120.0f;

      base.Start();

      random = new System.Random();
    }

    // Passes along the update call to automated test runner.
    protected override void Update() {
      base.Update();

      // Firebase initialization can be delayed by CheckAndFixDependenciesAsync,
      // so wait until the auth object has been created.
      if (auth != null) {
        testRunner.Update();
      }
    }

    // Validate it's possible to create and destroy auth objects.
    Task TestCreateDestroy() {
        TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
        // Cleanup auth objects referenced by the sample application.
        auth = null;
        otherAuth = null;
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        // Kick off test on another thread.
        (new Thread(() => {
          Exception caughtException = null;
          try {
            // Create a named app using the default app options.
            Func<int, Firebase.FirebaseApp> createAnotherApp = (int appId) => {
              return Firebase.FirebaseApp.Create(
                Firebase.FirebaseApp.DefaultInstance.Options,
                String.Format("anotherapp_{0}", appId));
            };

            int currentAppId = 0;
            var CREATE_DESTROY_ITERATIONS = 100;
            // Dispose of an App object that is in use by an auth object.
            for (int i = 0; i < CREATE_DESTROY_ITERATIONS; ++i) {
              UnityEngine.Debug.Log(String.Format("Dispose app {0}/{1}", i + 1,
                                                  CREATE_DESTROY_ITERATIONS));
              var anotherApp = createAnotherApp(currentAppId++);
              var anotherAuth = Firebase.Auth.FirebaseAuth.GetAuth(anotherApp);
              // Dispose of the app which should dispose the associated auth.
              anotherApp.Dispose();
              try {
                var user = anotherAuth.CurrentUser;
                // The exception can happen, but is not guaranteed, but we do
                // this to make sure it doesn't crash.
              } catch (NullReferenceException) {
                  // Do nothing here.
              }
              anotherApp = null;
              anotherAuth = null;
              System.GC.Collect();
              System.GC.WaitForPendingFinalizers();
            }

            // Ensure finalization of auth and app objects does not result in a crash due to out of
            // order destruction of native objects.
            for (int i = 0; i < CREATE_DESTROY_ITERATIONS; ++i) {
              UnityEngine.Debug.Log(String.Format("Finalize app and auth {0}/{1}", i + 1,
                                                  CREATE_DESTROY_ITERATIONS));
              var anotherApp = createAnotherApp(currentAppId++);
              var anotherAuth = Firebase.Auth.FirebaseAuth.GetAuth(anotherApp);
              UnityEngine.Debug.Log(String.Format("Created auth {0} for app {1}", anotherAuth,
                                                  anotherApp.Name));
              anotherApp = null;
              anotherAuth = null;
              System.GC.Collect();
              System.GC.WaitForPendingFinalizers();
            }
          } catch (Exception exception) {
            caughtException = exception;
          } finally {
            // Try to restore UIHandler's initial state.
            base.InitializeFirebase();
            // Ensure the initial state is restored before completing the Task
            if (caughtException != null) {
              tcs.SetException(caughtException);
            } else {
              tcs.SetResult(true);
            }
          }
        })).Start();
        return tcs.Task;
    }

    // Confirms that the default Auth instance does not have a user set.
    // If one is found, an exception is set on the given Task, and false is returned.
    bool ConfirmNoCurrentUser<T>(TaskCompletionSource<T> tcs, string extraMessage = null) {
      if (auth.CurrentUser != null) {
        string message = "FirebaseAuth.DefaultInstance.CurrentUser is not null when expected." +
            (string.IsNullOrEmpty(extraMessage) ? "" : (" " + extraMessage));
        tcs.TrySetException(new Exception(message));
        return false;
      }
      return true;
    }

    // Sets the UI fields to defaults used for testing.
    void SetDefaultUIFields() {
      // We want to use a random number with the email,
      // to prevent possible collisions with other runs.
      int randomInt = random.Next(int.MaxValue);
      email = string.Format(AutoTestEmail, randomInt);
      password = AutoTestPassword;
      displayName = AutoTestDisplayName;
    }

    // Confirms that the current user is set as an anonymous user.
    // If a problem is found, an exception is set on the given Task, and false is returned.
    bool ConfirmAnonymousCurrentUser<T>(TaskCompletionSource<T> tcs) {
      if (auth.CurrentUser == null) {
        tcs.TrySetException(new Exception(
          "SignIn Anonymously failed to set the User on the Auth instance"));
      } else if (!auth.CurrentUser.IsAnonymous) {
        tcs.TrySetException(new Exception(
          "SignIn Anonymously set a non-anonymous User on the Auth instance"));
      } else {
        // Everything is good.
        return true;
      }
      return false;
    }

    // Confirms that the current user is set with the default information.
    // If a problem is found, an exception is set on the given Task, and false is returned.
    bool ConfirmDefaultCurrentUser<T>(TaskCompletionSource<T> tcs) {
      if (auth.CurrentUser == null) {
        tcs.TrySetException(new Exception("No CurrentUser found when expected."));
      } else if (auth.CurrentUser.Email != email) {
        tcs.TrySetException(new Exception("CurrentUser email (" + auth.CurrentUser.Email +
          ") does not match expected (" + email + ")"));
#if !UNITY_EDITOR
      } else if (auth.CurrentUser.DisplayName != AutoTestDisplayName) {
        tcs.TrySetException(new Exception("CurrentUser display name (" +
          auth.CurrentUser.DisplayName + ") does not match expected (" + AutoTestDisplayName + ")"));
#endif
      } else {
        // Everything is good.
        return true;
      }
      return false;
    }

    // Clears the user from the default Auth instance, if one is found.
    // This is useful at the beginning of a test, to make sure it begins from a clean slate.
    // If if fails to clear the user, an exception is set on the given Task, and false is returned.
    bool TestSetupClearUser<T>(TaskCompletionSource<T> tcs) {
      if (auth.CurrentUser != null) {
        auth.SignOut();
        if (!ConfirmNoCurrentUser(tcs, "Failed to clear user prior to test.")) {
            return false;
        }
      }
      return true;
    }

    // Perform the standard sign in flow with an Anonymous account.
    // Tests: SignInAnonymouslyAsync_DEPRECATED, DeleteUserAsync.
    Task TestSignInAnonymouslyAsync_DEPRECATED() {
      TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();

      // We don't want to be signed in at the start of this test.
      if (!TestSetupClearUser(tcs)) {
          return tcs.Task;
      }

      // First, sign in anonymously.
      SigninAnonymouslyAsync_DEPRECATED().ContinueWithOnMainThread(t1 => {
        if (ForwardTaskException(tcs, t1)) return;
        // Confirm that the current user is correct.
        if (!ConfirmAnonymousCurrentUser(tcs)) return;

        // Delete the user, as we are done.
        DeleteUserAsync().ContinueWithOnMainThread(t2 => {
            if (ForwardTaskException(tcs, t2)) return;
            // Confirm that there is no user set anymore.
            ConfirmNoCurrentUser(tcs);
            // The tests are done
            tcs.TrySetResult(0);
        });
      });

      return tcs.Task;
    }

    // Perform the standard sign in flow with an Anonymous account.
    // Tests: SignInAnonymouslyAsync, DeleteUserAsync.
    Task TestSignInAnonymouslyAsync() {
      TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();

      // We don't want to be signed in at the start of this test.
      if (!TestSetupClearUser(tcs)) {
          return tcs.Task;
      }

      // First, sign in anonymously.
      SigninAnonymouslyAsync().ContinueWithOnMainThread(t1 => {
        if (ForwardTaskException(tcs, t1)) return;
        // Confirm that the current user is correct.
        if (!ConfirmAnonymousCurrentUser(tcs)) return;

        // Delete the user, as we are done.
        DeleteUserAsync().ContinueWithOnMainThread(t2 => {
            if (ForwardTaskException(tcs, t2)) return;
            // Confirm that there is no user set anymore.
            ConfirmNoCurrentUser(tcs);
            // The tests are done
            tcs.TrySetResult(0);
        });
      });

      return tcs.Task;
    }

    // Goes over the standard create/signout/signin flow, using the provided function to sign in.
    // Tests: CreateUserWithEmailAndPasswordAsync_DEPRECATED, SignOut, the given signin function,
    // and DeleteUserAsync.
    Task TestSignInFlowAsync_DEPRECATED(Func<Task> signInFunc) {
      TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();

      // We don't want to be signed in at the start of this test.
      if (!TestSetupClearUser(tcs)) {
        return tcs.Task;
      }

      // Set up the test email/password/etc fields.
      SetDefaultUIFields();

      CreateUserWithEmailAsync_DEPRECATED().ContinueWithOnMainThread(createTask => {
        // Confirm that the current user is correct
        if (!ConfirmDefaultCurrentUser(tcs)) return;
        // Sign out of the user
        SignOut();
        // Confirm no user
        if (!ConfirmNoCurrentUser(tcs)) return;
        // Sign back in
        signInFunc().ContinueWithOnMainThread(signinTask => {
          // Confirm that the current user is correct
          if (!ConfirmDefaultCurrentUser(tcs)) return;
          // Delete the user
          DeleteUserAsync().ContinueWithOnMainThread(deleteTask => {
            // Confirm no user
            ConfirmNoCurrentUser(tcs);
            // Tests are done.
            tcs.TrySetResult(0);
          });
        });
      });

      return tcs.Task;
    }

    // Goes over the standard create/signout/signin flow, using the provided function to sign in.
    // Tests: CreateUserWithEmailAndPasswordAsync, SignOut, the given signin function,
    // and DeleteUserAsync.
    Task TestSignInFlowAsync(Func<Task> signInFunc) {
      TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();

      // We don't want to be signed in at the start of this test.
      if (!TestSetupClearUser(tcs)) {
        return tcs.Task;
      }

      // Set up the test email/password/etc fields.
      SetDefaultUIFields();

      CreateUserWithEmailAsync().ContinueWithOnMainThread(createTask => {
        // Confirm that the current user is correct
        if (!ConfirmDefaultCurrentUser(tcs)) return;
        // Sign out of the user
        SignOut();
        // Confirm no user
        if (!ConfirmNoCurrentUser(tcs)) return;
        // Sign back in
        signInFunc().ContinueWithOnMainThread(signinTask => {
          // Confirm that the current user is correct
          if (!ConfirmDefaultCurrentUser(tcs)) return;
          // Delete the user
          DeleteUserAsync().ContinueWithOnMainThread(deleteTask => {
            // Confirm no user
            ConfirmNoCurrentUser(tcs);
            // Tests are done.
            tcs.TrySetResult(0);
          });
        });
      });

      return tcs.Task;
    }

    // Perform the standard sign in flow, using Email/Password.
    // Tests: SignInWithEmailAndPasswordAsync_DEPRECATED.
    Task TestSignInEmailAsync_DEPRECATED() {
      return TestSignInFlowAsync_DEPRECATED(SigninWithEmailAsync_DEPRECATED);
    }

    // Perform the standard sign in flow, using Email/Password.
    // Tests: SignInWithEmailAndPasswordAsync.
    Task TestSignInEmailAsync() {
      return TestSignInFlowAsync(SigninWithEmailAsync);
    }

    // Perform the standard sign in flow, using a credential generated from the Email/Password.
    // Tests: SignInWithCredentialAsync_DEPRECATED (Email credential).
    Task TestSignInCredentialAsync_DEPRECATED() {
      return TestSignInFlowAsync_DEPRECATED(SigninWithEmailCredentialAsync_DEPRECATED);
    }

    // Perform the standard sign in flow, using a credential generated from the Email/Password.
    // Tests: SignInWithCredentialAsync (Email credential).
    Task TestSignInCredentialAsync() {
      return TestSignInFlowAsync(SigninWithEmailCredentialAsync);
    }

    // Update the user profile.
    Task TestUpdateUserProfileAsync() {
      TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
      SigninAnonymouslyAsync().ContinueWithOnMainThread(t1 => {
        if (ForwardTaskException(tcs, t1)) return;
        const string ExpectedDisplayName = "Test Name";
        const string ExpectedPhotoUrl = "http://test.com/image.jpg";
        auth.CurrentUser.UpdateUserProfileAsync(new Firebase.Auth.UserProfile {
          DisplayName = ExpectedDisplayName,
          PhotoUrl = new Uri(ExpectedPhotoUrl),
        }).ContinueWithOnMainThread(t2 => {
            if (ForwardTaskException(tcs, t2)) return;
            var user = auth.CurrentUser;
            DisplayDetailedUserInfo(user, 0);
            if (user.DisplayName != ExpectedDisplayName) {
              tcs.SetException(new Exception(String.Format(
                "Unexpected display name '{0}' vs '{1}'",
                user.DisplayName, ExpectedDisplayName)));
            } if (user.PhotoUrl.ToString() != ExpectedPhotoUrl) {
              tcs.SetException(new Exception(String.Format(
                "Unexpected photo URL '{0}' vs '{1}'",
                user.PhotoUrl.ToString(), ExpectedPhotoUrl)));
            }
            tcs.SetResult(true);
        });
      });
      return tcs.Task;
    }

    // Anonymous sign-in with exceptions being thrown by auth state and token event handlers.
    // The sign-in process should continue uninterrupted.
    Task TestSignInAnonymouslyWithExceptionsInEventHandlersAsync_DEPRECATED() {
      SignOut();

      var exceptions = new List<Exception>();
      TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
      EventHandler stateChangedThrowException = (object sender, EventArgs e) => {
        var exception = new Exception("State changed");
        exceptions.Add(exception);
        throw exception;
      };
      EventHandler idTokenChangedThrowException = (object sender, EventArgs e) => {
        var exception = new Exception("ID token changed");
        exceptions.Add(exception);
        throw exception;
      };
      auth.StateChanged += stateChangedThrowException;
      auth.IdTokenChanged += idTokenChangedThrowException;

      SigninAnonymouslyAsync_DEPRECATED().ContinueWithOnMainThread(t => {
          auth.StateChanged -= stateChangedThrowException;
          auth.IdTokenChanged -= idTokenChangedThrowException;
          var exceptionMessages = new HashSet<string>();
          foreach (var exception in exceptions) {
            exceptionMessages.Add(exception.Message);
          }
          if (exceptionMessages.Count == 2) {
            var missingExceptions = new List<string>();
            foreach (var expectedMessage in new [] { "State changed", "ID token changed" }) {
              if (!exceptionMessages.Contains(expectedMessage)) {
                missingExceptions.Add(expectedMessage);
              }
            }
            if (missingExceptions.Count > 0) {
              tcs.SetException(new Exception(String.Format(
                  "The following expected exceptions were not thrown: {0}",
                  String.Join(", ", missingExceptions.ToArray()))));
            } else {
              tcs.SetResult(true);
            }
          } else {
            tcs.SetException(new Exception(String.Format(
                "Unexpected number of exceptions thrown {0} vs. 2 ({1})",
                exceptionMessages.Count,
                String.Join(", ", (new List<string>(exceptionMessages)).ToArray()))));
          }
        });
      return tcs.Task;
    }

    // Test if caching the FirebaseUser object, and then deleting some of the C++ objects, will work.
    Task TestCachingUser() {
      SignOut();
      TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

      auth.SignInAnonymouslyAsync().ContinueWithOnMainThread(t => {
        if (t.IsFaulted) {
          tcs.SetException(t.Exception);
          return;
        } else if (!t.Result.User.IsValid()) {
          tcs.SetException(new Exception("User wasn't valid after sign in"));
          return;
        }

        // Cache the user, and then Dispose the AuthResult, to delete the underlying
        // C++ AuthResult object.
        Firebase.Auth.FirebaseUser user = t.Result.User;
        t.Result.Dispose();

        // Check if the User is still valid, which is should be
        if (!user.IsValid()) {
          tcs.SetException(new Exception("User should still be valid after deleting the AuthResult"));
        }

        user.DeleteAsync().ContinueWithOnMainThread(t2 => {
          tcs.SetResult(true);
        });
      });

      return tcs.Task;
    }
  }
}
