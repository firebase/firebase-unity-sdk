---
name: firebase-unity-guid-policy
description:
  Rules for Unity asset GUIDs in the Firebase Unity SDK. Use this skill BEFORE
  touching `unity_packer/guids.json`, any `.meta` file, `gen_guids.py`, or
  `build_package.py`, and whenever a packaging run reports
  "There were asset paths without a known guid" (MissingGuidsError). Agents
  must never generate, invent, copy, or commit GUIDs in a feature PR; the
  release workflow owns `guids.json`.
---

# Unity GUID Policy: do not generate or commit GUIDs

> [!CAUTION]
> **Feature/bug-fix PRs must not add, change, or remove GUIDs.** Do not edit
> `unity_packer/guids.json`, do not run `scripts/build_scripts/gen_guids.py`,
> and do not write or alter the `guid:` line of any `.meta` file by hand.
> `guids.json` is populated by the release process, not by contributors.

## Why GUIDs matter

Unity identifies every asset by the GUID in its `.meta` file. When a customer
upgrades the Firebase package, Unity treats an asset whose GUID changed as a
brand-new asset (broken references, duplicated folders, orphaned files). The SDK
keeps GUIDs stable across releases with a checked-in cache,
`unity_packer/guids.json`, keyed by SDK version then asset path:

```json
{
  "14.1.0": {
    "Firebase/Plugins/x86_64/FirebaseCppApp-14_1_0.dll": "<32 hex chars>",
    "com.google.firebase.storage/Firebase/FirebaseStorage": "<32 hex chars>"
  }
}
```

Older versions' entries are inherited by newer versions, so an asset only needs
an entry in the version where it first appeared.

## How GUIDs are resolved at package time

`scripts/build_scripts/export_unity_package.py` (invoked by
`scripts/build_scripts/build_package.py`) picks a GUID for each packaged asset in
this order:

1. The `guid:` from a `.meta` file shipped next to the asset inside the build
   zip (this is how source-shipped C# products such as `firebaseai/src`,
   `functions/src`, and `app/src/internal` get their file GUIDs). Folder
   `.meta` files are **not** consulted; folder GUIDs always come from step 2.
2. The entry in `unity_packer/guids.json` for the current or any earlier
   version.
3. Otherwise it raises `MissingGuidsError`:
   `There were asset paths without a known guid. generate guids for these assets: ...`

`build_package.py` (full run, no `--apis`) catches that error, runs
`gen_guids.py --generate_new_guids=True` for the missing paths, **writes the
new entries into `unity_packer/guids.json` in your working tree**, and re-runs
the packager. Full packaging therefore always self-heals; a missing GUID never
blocks a release build. (The `--apis=<subset>` debug path skips the self-heal
and may simply fail with `MissingGuidsError`; that is acceptable for a local
check and still not a reason to add GUIDs.)

## Who commits `guids.json`

The release workflow. `.github/workflows/package.yml` runs `build_package.py`
and, when invoked with `working_branch` (or `create_new_branch=1`) by
`build_starter.yml` / `update_versions.yml`, commits whatever changed as
`"Update Guids ID"` / `"Update Unity SDK dependencies - <date>"` under the
`firebase-workflow-trigger[bot]` identity. That release-prep run is the single
place where permanent GUIDs are minted and recorded. Until then, pre-release CI
builds just mint throwaway GUIDs on the fly, which nobody depends on.

## Rules for agents

1. **Never edit `unity_packer/guids.json` in a PR.** Not to add entries for new
   files, not to add folder entries for a new UPM package folder, not to
   "fix" a missing one. Leave it identical to `main`.
2. **Never run `gen_guids.py` directly**, and never pass `--generate_new_guids`.
3. **If you run `build_package.py` locally**, it may modify `guids.json` as a
   side effect. Before committing, discard that change:

   ```bash
   git checkout -- unity_packer/guids.json
   ```

   and confirm nothing GUID-related is staged:

   ```bash
   git diff --cached --name-only | grep -E 'unity_packer/guids\.json|\.meta$' && \
     echo "GUID-bearing files are staged; review against firebase-unity-guid-policy"
   ```

4. **Never hand-write or hand-edit a `.meta` file.** In particular, never copy
   a GUID from `guids.json` (or from another `.meta`) into a `.meta` so that
   "source import" and "package import" match; the packer ignores folder
   `.meta` files anyway, so this buys nothing and leaves a hand-minted GUID in
   history.
5. **New files in source-shipped C# products** (`firebaseai/src/**`,
   `functions/src/**`, `app/src/internal/**`, and any product packed with
   `unity_pack_folder(...)`) follow the convention that every `.cs`, `.asmdef`,
   `.xml`, and sub-folder has a committed, **Unity-generated** `.meta`.
   Produce it by letting a Unity Editor (2022.3 LTS or newer) import the file
   (e.g., drop it into `<product>/testapp/Assets/` or any scratch Unity
   project, then copy the generated `.meta` back next to the source file).
   If no Unity Editor is available, leave the `.meta` out, say so in the PR
   description, and let a maintainer add it; packaging will still self-heal.
6. **Renames and moves** of packaged assets are the only situation where a
   deliberate `guids.json` edit can be justified (carrying the *existing* GUID
   over to the new path so customers' projects don't see a new asset). Even
   then: reuse the old value, never mint a new one, and only do it when the
   user explicitly asks for it. Call it out in the PR description.
7. When reviewing a PR (yours or someone else's), flag any diff hunk in
   `unity_packer/guids.json` or any `guid:` change in a `.meta` file that is not
   part of a release-prep bot commit.

## What to do when you see `MissingGuidsError`

Nothing. It is expected for any newly added packaged file or folder in a
pre-release build. `build_package.py` already handles it, and the release-prep
`package.yml` run will commit the permanent entries. Do not "pre-empt" the bot
by committing the generated entries yourself.

## Checklist before opening a PR

- [ ] `git diff main -- unity_packer/guids.json` is empty.
- [ ] No `.meta` file in the diff has a hand-written or edited `guid:` line.
- [ ] Any new `.meta` files were generated by the Unity Editor, not authored.
- [ ] If a packaged asset was renamed/moved, the user explicitly approved
      carrying its existing GUID forward and the PR description says so.
