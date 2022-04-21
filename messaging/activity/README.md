The classes.jar file is a combination of the following:
 - The MessagingUnityPlayerActivity.java file
 - The firebase_messaging_cpp.aar file from the Firebase C++ SDK
 - The java source files from Flatbuffers
And it is compiled by linking against the UnityPlayerActivity and the classes.jar provided by Unity.

This gets included in the firebase-messaging-unity-{version}.srcaar that is built. For now, we have this file checked in and used directly, but the plan is to switch to generating the classes.jar file as part of the open source build process.