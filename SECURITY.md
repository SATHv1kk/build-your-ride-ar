# Security Policy

## Scope

This is an academic/personal Android AR application. It is **not production software** and
carries no security guarantees.

## Known characteristics

| | Characteristic |
|---|---|
| 🔑 | **Release signing key is not in this repository** (by design — `buildyourride.keystore` and `keystore_credentials.txt` are git-ignored). A fresh clone builds debug-signed. Never commit either file. |
| 📁 | **No network calls, no backend, no accounts.** All state (`PlayerPrefs`) is local to the device. There is no data collection or transmission in this app. |
| 🎨 | **3D car models are third-party community assets**, used for a non-commercial academic project — see the README's closing note. They are not this project's own IP. |
| 📷 | **Camera permission** is required for AR functionality (ARCore) and is requested via the standard Android permission flow; the camera feed is never transmitted anywhere. |

## Reporting a vulnerability

If you find a security issue in this code, please open an issue describing it. This is an
educational/personal repository with a single maintainer — there's no formal disclosure SLA, but
I'll look at it.

## Supported versions

There are no tagged releases; only the current state of the default branch is maintained.

This code is provided for **educational purposes**, without warranty, under the [MIT License](LICENSE).
