---
name: firebase-unity-coding-standards
description:
  Coding conventions for the Firebase Unity SDK: C# style and XML doc
  comments, the SWIG `.i` binding layer and how C++ docs flow into C#,
  source-shipped (`.asmdef`) products and their `.meta` files, release notes
  in `docs/readme.md`, and PR hygiene. Use when writing or reviewing C#,
  SWIG, or CMake changes in this repository.
---

# Firebase Unity SDK Coding Standards

## Repository layout (per product)

```
<product>/
├── CMakeLists.txt        # builds Firebase.<Product>.dll (or packs sources)
├── src/*.cs              # hand-written public C# API
├── src/swig/<product>.i  # SWIG bindings over the C++ SDK (not for pure C# products)
├── src/Internal/ or src/internal/   # non-public helpers (pure C# products)
└── testapp/              # integration testapp (see firebase-unity-test-runner)
```

Cross-cutting: `app/` (FirebaseApp, platform dispatcher, `app/src/swig/*.i`
helpers such as `future.i`, `null_check_this.i`, `serial_dispose.i`),
`editor/` (Editor-only plugin code), `cmake/`, `unity_packer/`
(packaging manifest + GUID cache), `docs/readme.md` (release notes),
`scripts/`.

## C# style

- Follow the [.NET C# style guide](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions)
  as referenced by `CONTRIBUTING.md`, matching the surrounding file: 2-space
  indentation, opening brace on the same line, `PascalCase` public members,
  `camelCase` locals/fields, `namespace Firebase.<Product> { ... }` wrapping
  the file.
- Start every file with the Apache 2.0 header (`Copyright <year> Google LLC`).
- Public API is `Task`-based (`...Async` methods returning `Task`/`Task<T>`).
  Marshal results back onto the Unity main thread via the existing
  `ExceptionAggregator` / `FirebaseHandler` patterns rather than ad-hoc
  threads. Dispose native handles deterministically; keep `IDisposable`
  wrappers alive across P/Invoke calls (see 14.0.0 Remote Config notes).
- Internal types that must be visible to generated code or other Firebase
  assemblies use `internal` plus `InternalsVisibleTo` in `AssemblyInfo.cs`.
- Do not add third-party NuGet/UPM dependencies; the SDK ships only what is
  in the repo and EDM4U-resolved native packages.

### XML documentation

Every public type and member gets `///` XML docs: `<summary>` always,
`<param name="...">` for each parameter, `<returns>` for non-void,
`<exception>` where relevant, `<remarks>` for longer notes.

```csharp
/// <summary>
/// Asynchronously uploads a byte array to this reference.
/// </summary>
/// <param name="bytes">The data to upload.</param>
/// <param name="customMetadata">Optional metadata to associate with the object.</param>
/// <returns>A task that completes with the uploaded object's metadata.</returns>
public Task<StorageMetadata> PutBytesAsync(byte[] bytes, MetadataChange customMetadata = null)
```

## SWIG bindings (`<product>/src/swig/<product>.i`)

Most products are thin C# wrappers over the C++ SDK; SWIG generates the
`*Internal` classes.

- Module header pattern: `%module <Product>Internal`,
  `%pragma(csharp) moduleclassmodifiers="internal sealed class"`,
  `%feature("flatnested")`, `%import "app/src/swig/app.i"`, then the shared
  `%include`s (`future.i`, `null_check_this.i`, `init_result.i`,
  `serial_dispose.i`).
- Hide C++ surface you re-expose by hand: `%ignore`, `%rename("...Internal")`,
  and `%csmethodmodifiers Foo(...) "internal";`. The hand-written C# in
  `src/*.cs` is the public API; generated code should stay `internal`.
- `Future<T>` return values are wrapped by `future.i` into `Task<T>`; use
  the `%SWIG_FUTURE(...)` macros already in use for new future types.
- Docs: C++ Doxygen comments are copied into the generated C# by
  `swig_commenter.py` (wired in `cmake/firebase_swig.cmake`). In C++ headers,
  wrap Unity-specific wording in `<SWIG> @if swig_examples ... @endif </SWIG>`
  blocks (see the C++ repo's `firebase-cpp-commenting-standards`).
- To preview generated code without a full build, use
  `build_zips.py --gen_swig_only` or the **Generate SWIG files** GitHub
  workflow (see `firebase-unity-builder`).

## Source-shipped products (`.asmdef`)

Firebase AI (`firebaseai/src`), Functions (`functions/src`) and the shared
core `app/src/internal` are shipped as **C# source** with a
`Firebase.<Product>.asmdef`, packed by `unity_pack_folder(...)`. Rules:

- Every `.cs`, `.asmdef`, `.xml`, and sub-folder has a committed
  **Unity-generated** `.meta` next to it. Generate new ones with a Unity
  Editor; never author or edit `guid:` lines by hand, and never touch
  `unity_packer/guids.json` (see `firebase-unity-guid-policy`).
- Code must compile under Unity 2022.3's C# level without preprocessor
  tricks that depend on project settings; keep `link.xml` updated for types
  reached only via reflection (IL2CPP stripping).
- Keep `src/Internal/` (or `internal/`) for non-public helpers and exclude it
  from documentation globs in `CMakeLists.txt`.

## Release notes

`docs/readme.md` -> "Release Notes" -> top `### <next version>` section. One
bullet per change under `-   Changes`, prefixed with the product and optional
platform, e.g.

```
    - Storage: Added `StorageReference.ListAsync` to enumerate objects.
    - General (iOS): Update to Firebase Apple SDK version 13.1.1.
    - Auth: **Breaking Change** `UserMetadata.CreationTimestamp` now returns `System.DateTime`.
```

Add a bullet for any user-visible change in the same PR. Version bumps
(`cmake/firebase_unity_version.cmake`, Android/iOS pins, EDM4U) are done by
`scripts/update_versions.py --unity_sdk_version=<ver>` and the
`update_versions.yml` workflow during release prep, not in feature PRs.

## PR hygiene

- Use `pull_request_template.md` (Description / Testing / Type of Change).
- Build the affected product locally where possible (`firebase-unity-builder`)
  and run the testapp (`firebase-unity-test-runner`); say how you tested.
- Keep the diff to source changes: no `unity_packer/guids.json`, no
  `google-services.json` / `GoogleService-Info.plist`, no `*_unity/` build
  output, no generated SWIG files.
- CI runs the full build + integration matrix on every PR; failures in
  unrelated platforms are often infra flakes, but confirm by reading the
  summarized log before re-running.
