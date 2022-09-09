# `unity-setup` GitHub Action

## Usage
-   Install and Activate Unity
```yml
jobs:
  unity_integration_tests:
    # ...

    steps:
      # ...
      - id: unity_setup
        uses: firebase/firebase-unity-sdk/unity@unity-gha
        with:
          version: ${{ matrix.unity_version }}
          platforms: ${{ matrix.platform }}
          username: ${{ secrets.UNITY_USERNAME }}
          password: ${{ secrets.UNITY_PASSWORD }}
          serial_ids: ${{ secrets.SERIAL_ID }}
```


-   Return Unity License
```yml
jobs:
  unity_integration_tests:
    # ...

    steps:
      # ...
      - id: unity_setup
        uses: firebase/firebase-unity-sdk/unity@unity-gha
        with:
          version: ${{ matrix.unity_version }}
          release_license: "true"
```
