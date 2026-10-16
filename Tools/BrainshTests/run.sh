#!/usr/bin/env bash
# Runs the Brainsh test suite.
#
# The reference interpreter is compiled from source (needs g++). Every program is run
# with a hard timeout, so a runaway Brainfuck loop can never hang the machine.
#
#   ./run.sh
#
set -euo pipefail

cd "$(dirname "$0")"

if [ ! -x ref/bsh ]; then
    echo "building the reference interpreter..."
    g++ -O2 -std=c++17 -o ref/bsh ref/bsh.cpp
fi

exec dotnet run -c Release --project bshtests.csproj