# Building Snaploop for Android

Snaploop builds in the cloud on GitHub Actions, so no dedicated build machine is needed. Every push to `main` produces a debug APK you can download from the run page and sideload onto a phone. You can also build locally from the Unity Editor whenever you have Unity installed.

- Engine: Unity 6 LTS. The exact version is whatever `ProjectSettings/ProjectVersion.txt` says; CI reads it from there.
- Android package id: `com.snaploop.game` (placeholder, set in Player Settings).
- CI: [GameCI `unity-builder`](https://game.ci/docs/github/builder) running the official `unityci/editor` Docker image on GitHub-hosted Ubuntu runners. The repository is public, so these runners and artifact storage cost nothing.
- Build script: `Assets/Editor/Build/SnaploopBuild.cs`, used by both the Editor menu and CI.

## One-time setup (Lance)

### 1. Unity account for CI

CI activates a free Unity Personal seat with a Unity account email and password at the start of each build and returns it at the end. The account needs:

- **An email-and-password Unity ID.** Accounts that sign in only with Google, Apple or Facebook have no password, so CI can't use them. If yours is a social login, set a password at [id.unity.com](https://id.unity.com) or create a separate Unity ID just for CI.
- **Two-factor authentication turned off.** A headless build can't type in a 2FA code. If you want to keep 2FA on your main account, use a separate CI-only Unity ID; GameCI recommends this anyway.
- **Personal plan terms accepted.** Signing in once at [id.unity.com](https://id.unity.com) (or in Unity Hub) is enough. Unity Personal is free while revenue/funding is under Unity's Personal threshold.
- **A password made of letters and digits only.** GameCI reports problems with some special characters.

### 2. GitHub secrets

Add these under **GitHub > SnapLoop > Settings > Secrets and variables > Actions > New repository secret**:

| Secret | Required | Value |
| --- | --- | --- |
| `UNITY_EMAIL` | Yes | The email of the Unity ID above. |
| `UNITY_PASSWORD` | Yes | That Unity ID's password. |
| `UNITY_LICENSE` | No | Contents of a `Unity_lic.ulf` file. Leave unset: CI activates from the email and password. Only add it if GameCI support or a failing build tells you to (see Troubleshooting). |
| `ANDROID_KEYSTORE_BASE64` | For Play uploads | The release keystore file, base64-encoded (see [Release keystore](#release-keystore)). |
| `ANDROID_KEYSTORE_PASS` | With the keystore | The keystore password. |
| `ANDROID_KEYALIAS_NAME` | With the keystore | The key alias inside the keystore, for example `snaploop`. |
| `ANDROID_KEYALIAS_PASS` | With the keystore | The key password. If you made it the same as the keystore password, enter it again. |

Until `UNITY_EMAIL` and `UNITY_PASSWORD` exist, the workflow's preflight job prints a warning and skips the build instead of failing. Pull requests from forks never receive secrets, so they skip the same way.

The four `ANDROID_*` secrets are only needed when you want builds that Google Play will accept. Without them, release builds are signed with Unity's debug key, which is fine for sideloading and testing.

## CI builds (GitHub Actions)

Workflow: `.github/workflows/android-build.yml` ("Android build").

| Trigger | What it builds |
| --- | --- |
| Push to `main` | Debug APK |
| Pull request | Debug APK (checks the PR still builds) |
| Manual: **Actions > Android build > Run workflow** | Your choice of `debug` or `release`, as `apk` or `aab` |

**Downloading the APK:** open the run under the **Actions** tab, scroll to **Artifacts**, and download `Snaploop-debug-apk-0.1.<run>`. GitHub delivers it as a zip; unzip it to get the `.apk`. Artifacts are kept for 30 days.

**Installing on a phone:** copy the APK to the phone and open it (allow "install unknown apps" for your file manager or browser when Android asks), or run `adb install -r Snaploop-debug-0.1.<run>.apk` with USB debugging on. A debug build and a release build signed with different keys can't replace each other: uninstall the old one first.

**Variants:**

- `debug`: Development build (shows the "Development Build" watermark and allows the Profiler to attach), signed with Unity's debug keystore, uses the project's scripting backend and architecture settings.
- `release`: Non-development, IL2CPP, ARM64 only (Google Play requires 64-bit). Signed with the release keystore when the `ANDROID_*` secrets are set, otherwise with the debug keystore plus a warning in the log.
- `aab`: Android App Bundle, the format Google Play requires for new apps. Use `release` + `aab` with the release keystore for store uploads.

**Versioning:** CI sets the version name to `0.1.<run number>` and the Android `versionCode` to the run number, so every CI build installs over the previous one and Play always sees an increasing code. Change the `APP_VERSION_PREFIX` value at the top of the workflow to bump the marketing version (for example to `1.0`).

**What the workflow does:** frees disk space on the runner (the Unity Android image is about 22 GB unpacked), checks out the repo, restores cached Git LFS objects and pulls the rest (so the video clips don't use LFS bandwidth on every run), restores the cached Unity `Library` folder, builds with GameCI, and uploads the result. Only one build runs at a time, so the Personal license seat is never used twice at once. The first build on a fresh cache is the slowest; later builds reuse the cached `Library`.

**Unity version upgrades:** upgrade the project in the Editor and commit `ProjectSettings/ProjectVersion.txt`. CI picks the matching `unityci/editor:ubuntu-<version>-android-3` image automatically. GameCI usually publishes images within hours of a Unity release; check [Docker Hub](https://hub.docker.com/r/unityci/editor/tags?name=android) if a brand-new patch fails to pull.

## Local build in the Unity Editor

1. Install [Unity Hub](https://unity.com/download) and sign in with your Unity ID.
2. In Unity Hub, install the Unity version listed in `ProjectSettings/ProjectVersion.txt` with **Android Build Support**, including **OpenJDK** and **Android SDK & NDK Tools**.
3. Clone the repository with Git LFS installed (`git lfs install` once, then `git clone`), so the video clips download as real files rather than small pointer files.
4. Open the project folder in Unity Hub.
5. Use the menu **Snaploop > Build**:
   - **Android Debug APK**: development build, debug-signed.
   - **Android Release APK**: release settings; signed with your release keystore if configured (see below), otherwise debug-signed.
   - **Android Release AAB (Google Play)**: release App Bundle.

   Builds are written to `Builds/Android/Snaploop-<variant>.<apk|aab>` and the folder opens when the build finishes. The first build switches the project to the Android platform, which reimports assets and takes a while.

`File > Build Profiles > Android > Build` also works, but it uses whatever is in Player Settings as-is, without the variant settings above.

The menu builds restore every Player Setting they change afterwards, so building never leaves `ProjectSettings` modified and never writes passwords to disk.

**Signing local release builds:** either enter the keystore in **Edit > Project Settings > Player > Android > Publishing Settings** (Unity asks for the passwords once per Editor session and never saves them), or set the same environment variables CI uses before launching Unity: `ANDROID_KEYSTORE_NAME` (path to the `.keystore`, absolute or relative to the project folder), `ANDROID_KEYSTORE_PASS`, `ANDROID_KEYALIAS_NAME`, `ANDROID_KEYALIAS_PASS`.

### Command line (any machine with Unity installed)

```bash
<path-to-Unity> -batchmode -quit -nographics -projectPath . -buildTarget Android \
  -executeMethod Snaploop.Build.SnaploopBuild.BuildFromCommandLine \
  -variant release -format apk -logFile -
```

Options (a command-line flag wins over its environment variable):

| Flag | Environment variable | Default |
| --- | --- | --- |
| `-variant debug\|release` | `SNAPLOOP_BUILD_VARIANT` | `debug` |
| `-format apk\|aab` | `SNAPLOOP_BUILD_FORMAT` | `apk` |
| `-customBuildPath <file>` | `SNAPLOOP_BUILD_PATH` | `Builds/Android/Snaploop-<variant>.<ext>` |
| `-buildVersion 0.1.7` | `SNAPLOOP_VERSION` | Player Settings |
| `-androidVersionCode 7` | `SNAPLOOP_ANDROID_VERSION_CODE` | Player Settings |
| `-androidKeystoreName <file>` | `ANDROID_KEYSTORE_NAME` | none (debug keystore) |
| `-androidKeystorePass` | `ANDROID_KEYSTORE_PASS` | |
| `-androidKeyaliasName` | `ANDROID_KEYALIAS_NAME` | |
| `-androidKeyaliasPass` | `ANDROID_KEYALIAS_PASS` | keystore password |
| `-requireReleaseKeystore` | `SNAPLOOP_REQUIRE_RELEASE_KEYSTORE=true` | off: release falls back to the debug keystore with a warning |

The process exits with code 1 if the build fails.

## Release keystore

Android requires every APK/AAB to be signed. The debug keystore Unity generates is fine for testing but Google Play rejects it, and every update to a published app must be signed with the same key. Create the release keystore once, when you're getting ready for a Play upload.

**Create it** (pick one):

- In Unity: **Edit > Project Settings > Player > Android > Publishing Settings > Keystore Manager > Keystore... > Create New**. Choose a location **outside the repository**, set a password, then add a key with alias `snaploop`, a password, a validity of 50 years, and your name or studio name.
- With the JDK's `keytool` (Unity bundles one under `Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin/`):

  ```bash
  keytool -genkeypair -v -keystore snaploop-release.keystore -alias snaploop \
    -keyalg RSA -keysize 2048 -validity 18250
  ```

**Add it to GitHub:** base64-encode the file and paste the output into the `ANDROID_KEYSTORE_BASE64` secret, then add `ANDROID_KEYSTORE_PASS`, `ANDROID_KEYALIAS_NAME` (`snaploop`) and `ANDROID_KEYALIAS_PASS`.

```bash
# macOS
base64 -i snaploop-release.keystore | pbcopy
# Linux
base64 -w0 snaploop-release.keystore
```

```powershell
# Windows PowerShell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("snaploop-release.keystore")) | Set-Clipboard
```

**Keep it safe:**

- Never commit the keystore. Keep it outside the repo folder, or make sure `*.keystore` and `*.jks` are in `.gitignore`.
- Back up the file and both passwords somewhere durable, such as a password manager.
- When you create the Play Console app, enroll in **Play App Signing** (the default for new apps). Google then holds the real app-signing key and this keystore becomes the *upload key*, which Google can reset if it is ever lost. Without Play App Signing, losing this keystore means you can never update the app.

## Troubleshooting

- **Build skipped with "Secrets UNITY_EMAIL and UNITY_PASSWORD are not available":** add the two secrets, then re-run the workflow. Fork pull requests always skip.
- **"Unity rejected the account credentials, or wants a second factor":** check the email and password, make sure 2FA is off, and that it isn't a Google/Apple-only login. Unity sometimes emails a "new device" confirmation the first time it sees a GitHub runner's IP address: check the account's inbox, confirm, and re-run.
- **"Unity reports no available Personal seats":** a previous run was killed before it could return its seat. Sign in at [id.unity.com](https://id.unity.com), release the stale activation, and re-run.
- **"Unity did not grant a Personal seat for this editor version":** re-run once. If it keeps failing, sign in to Unity Hub on any computer, go to **Preferences > Licenses > Add > Get a free personal license**, and paste the contents of the resulting `Unity_lic.ulf` into the `UNITY_LICENSE` secret (`C:\ProgramData\Unity\Unity_lic.ulf` on Windows, `/Library/Application Support/Unity/Unity_lic.ulf` on macOS, `~/.local/share/unity3d/Unity/Unity_lic.ulf` on Linux). GameCI tries that file first and falls back to the email and password.
- **"No enabled scenes":** add the game scenes under **File > Build Profiles > Scene List**.
- **Out of disk space on the runner:** the workflow's "Free disk space" step should prevent this; if it still happens, set `tool-cache: true` on that step.
- **The APK won't install over an older one:** it was signed with a different key (debug versus release). Uninstall the old app first.

## Notes

- Build artifacts on a public repository can be downloaded by any signed-in GitHub user who finds the run. If unreleased builds must stay private, make the repository private; Actions minutes are then metered against the account's free monthly allowance.
- APK size grows with the video clips in `Assets/StreamingAssets/Levels/`. Google Play caps the base module of an AAB at 200 MB; if the clip set outgrows that, move clips into Play Asset Delivery packs.
