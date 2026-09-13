# Privacy Policy — CVAT Duplicate Checker

CVAT Duplicate Checker is a Windows desktop application (WinUI 3) for inspecting CVAT
annotations and detecting duplicate bounding boxes. This policy explains what data the
app accesses and where that data goes.

## Data the app accesses

- **Annotation files you open.** Local XML/ZIP annotation files are parsed entirely on
  your device.
- **Your CVAT server.** When you connect to a CVAT server, the app talks only to the
  server URL you entered (or picked from the list) using the Personal Access Token (PAT)
  you provide. It reads tasks/jobs/annotations and frame images, and — only after you
  explicitly confirm — deletes the specific shapes you marked.

## Credentials

- The PAT is kept in memory while the app runs.
- The PAT is stored on your device in the Windows Credential Locker **only if** you check
  "Ghi nhớ PAT" (Remember PAT). It is encrypted by Windows for your account.
- Credentials are never sent anywhere except to the CVAT server you configured, for the
  purpose of authenticating API requests.
- You can remove a stored PAT at any time from the Windows Credential Manager.

## Local backups

Before deleting shapes on CVAT, the app writes a backup copy of the current annotation
XML to `%LOCALAPPDATA%\CvatDuplicateChecker\backups\` on your device. These backups stay
on your device until you delete them.

## What the app does NOT do

- No telemetry, no analytics, no crash reporting, no advertising.
- No data is shared with third parties, including the app developer.
- No network calls other than to the CVAT server URL you configured.
- All duplicate detection and analysis runs locally on your device.

## Children's privacy

The app is a professional annotation-QA tool and is not directed at children under 13.

## Changes to this policy

Updates to this policy will be published in this repository
(https://github.com/NDCLI/duplicatebox).

## Contact

Open an issue at https://github.com/NDCLI/duplicatebox for privacy questions.
