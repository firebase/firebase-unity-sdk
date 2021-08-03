using System;
using UnityEngine;

public sealed class SceneLoader : MonoBehaviour {
  [SerializeField] GUISkin skin;

  const string MANUAL = "MainScene";
  const string AUTOMATIC = "MainSceneAutomated";

  // After loading this scene, the automated scene will be loaded by default
  // after this many seconds.
  const float TIMEOUT = 6f;

  float secondsUntilTimeout;

  void Start() {
    secondsUntilTimeout = TIMEOUT;
  }

  void Update() {
    secondsUntilTimeout -= Time.deltaTime;
    if (secondsUntilTimeout <= 0) {
       UnityEngine.SceneManagement.SceneManager.LoadScene(AUTOMATIC);
    }
  }

  // Draw two buttons to load each scene. For manual use.
  void OnGUI() {
    GUI.skin = skin;
    float width = Screen.width;
    float height = Screen.height;
    GUI.Label(
      new Rect(0.05f * width, 0.05f * height, 0.9f * width, 0.2f * height),
      string.Format("Automatic scene will auto-select in {0}", secondsUntilTimeout)
    );
    // Place the controls in a box between 25% and 75% of the screen's space.
    Rect controlArea = new Rect(0.25f * width, 0.25f * height, 0.5f * width, 0.5f * height);
    GUILayout.BeginArea(controlArea);
    GUILayout.BeginVertical();
    SceneLoadButton("Manual Scene", MANUAL);
    SceneLoadButton("Automated Scene", AUTOMATIC);
    GUILayout.EndVertical();
    GUILayout.EndArea();
  }

  void SceneLoadButton(string text, string sceneToLoad) {
    if (GUILayout.Button(text)) {
      UnityEngine.SceneManagement.SceneManager.LoadScene(sceneToLoad);
    }
  }
}
