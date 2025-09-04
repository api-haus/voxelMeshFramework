#!/usr/bin/env bash
set -euo pipefail

# Defaults from .cursor/mcp.json
UNITY_DEFAULT="/Volumes/Archive2TB/UNITY/Installs/6000.0.56f1/Unity.app/Contents/MacOS/Unity"
PROJECT_DEFAULT="/Volumes/Archive2TB/UNITY/Projects/voxelMeshFramework"

# Allow override via env vars
UNITY_PATH="${UNITY_PATH:-$UNITY_DEFAULT}"
PROJECT_PATH="${PROJECT_PATH:-$PROJECT_DEFAULT}"

# Parameters
PLATFORM="EditMode"
TEST_FILTER="Voxels.Tests.Editor.NaiveSurfaceNetsPerformanceTests"
RESULTS_XML="${PROJECT_PATH}/results.xml"
PERF_RESULTS_JSON="${PROJECT_PATH}/perfResults.json"
LOG_DIR="${PROJECT_PATH}/Logs"
mkdir -p "${LOG_DIR}"
TIMESTAMP=$(date +%Y-%m-%d_%H-%M-%S)
LOG_FILE="${LOG_DIR}/perf_${PLATFORM}_${TIMESTAMP}.log"

usage() {
  echo "Usage: $0 [-p EditMode|PlayMode] [-f testFilter] [-o resultsXml] [-j perfResultsJson]"
  echo "Env overrides: UNITY_PATH, PROJECT_PATH"
}

while getopts ":p:f:o:j:h" opt; do
  case $opt in
    p) PLATFORM="$OPTARG" ;;
    f) TEST_FILTER="$OPTARG" ;;
    o) RESULTS_XML="$OPTARG" ;;
    j) PERF_RESULTS_JSON="$OPTARG" ;;
    h) usage; exit 0 ;;
    \?) echo "Invalid option: -$OPTARG"; usage; exit 2 ;;
  esac
done

echo "UNITY_PATH=${UNITY_PATH}"
echo "PROJECT_PATH=${PROJECT_PATH}"
echo "PLATFORM=${PLATFORM}"
echo "TEST_FILTER=${TEST_FILTER}"
echo "RESULTS_XML=${RESULTS_XML}"
echo "PERF_RESULTS_JSON=${PERF_RESULTS_JSON}"
echo "LOG_FILE=${LOG_FILE}"

"${UNITY_PATH}" \
  -batchmode \
  -nographics \
  -quit \
  -runTests \
  -projectPath "${PROJECT_PATH}" \
  -testPlatform "${PLATFORM}" \
  -testFilter "${TEST_FILTER}" \
  -testResults "${RESULTS_XML}" \
  -perfTestResults "${PERF_RESULTS_JSON}" \
  -logFile "${LOG_FILE}"

echo "Done. Results: ${RESULTS_XML}, Perf: ${PERF_RESULTS_JSON}"

