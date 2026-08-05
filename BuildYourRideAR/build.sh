#!/usr/bin/env bash
# Headless Android build for Build Your Ride AR, for Ubuntu.
#
#   ./build.sh              # normal build (repairs scene wiring, then builds)
#   ./build.sh --full       # bootstrap: regenerates the scene and re-imports
#                           # every roster FBX. Slow, and wipes saved builds.
#
# Requires the Unity editor version in ProjectSettings/ProjectVersion.txt with
# the Android Build Support module (incl. OpenJDK + Android SDK/NDK) installed.

set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VERSION="$(sed -n 's/^m_EditorVersion: //p' "$PROJECT_DIR/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
METHOD="BatchBuild.QuickBuild"
[[ "${1:-}" == "--full" ]] && METHOD="BatchBuild.ReleaseBuild"

# Unity Hub's default install root on Linux, then a few common alternatives.
UNITY=""
for candidate in \
    "$HOME/Unity/Hub/Editor/$VERSION/Editor/Unity" \
    "/opt/unity/editors/$VERSION/Editor/Unity" \
    "/opt/Unity/Hub/Editor/$VERSION/Editor/Unity" \
    "$HOME/.local/share/unity3d/Hub/Editor/$VERSION/Editor/Unity"
do
    [[ -x "$candidate" ]] && { UNITY="$candidate"; break; }
done

if [[ -z "$UNITY" ]]; then
    echo "Unity $VERSION not found. Searched:" >&2
    echo "  ~/Unity/Hub/Editor/$VERSION/Editor/Unity" >&2
    echo "  /opt/unity/editors/$VERSION/Editor/Unity" >&2
    echo "Install it via Unity Hub, or set UNITY_PATH and re-run:" >&2
    echo "  UNITY_PATH=/path/to/Unity ./build.sh" >&2
    [[ -n "${UNITY_PATH:-}" ]] && UNITY="$UNITY_PATH" || exit 1
fi

LOG="$PROJECT_DIR/Builds/build.log"
mkdir -p "$PROJECT_DIR/Builds"

echo "Unity   : $UNITY"
echo "Project : $PROJECT_DIR"
echo "Method  : $METHOD"
echo "Log     : $LOG"
echo

# -nographics is deliberately NOT passed: this build compiles shader variants,
# and some drivers need a GL context to do that reliably in batch mode.
set +e
"$UNITY" \
    -batchmode \
    -quit \
    -projectPath "$PROJECT_DIR" \
    -buildTarget Android \
    -executeMethod "$METHOD" \
    -logFile "$LOG"
STATUS=$?
set -e

echo
if [[ $STATUS -eq 0 ]]; then
    APK="$PROJECT_DIR/Builds/BuildYourRideAR.apk"
    if [[ -f "$APK" ]]; then
        echo "BUILD OK  ->  $APK  ($(du -h "$APK" | cut -f1))"
        echo "Install with:  adb install -r \"$APK\""
    else
        echo "Unity exited 0 but no APK was produced. Check: $LOG" >&2
        exit 1
    fi
else
    echo "BUILD FAILED (exit $STATUS). Last errors:" >&2
    grep -nE "error CS|BuildFailedException|Exception:|error:" "$LOG" | tail -25 >&2 || true
    echo >&2
    echo "Full log: $LOG" >&2
    exit $STATUS
fi
