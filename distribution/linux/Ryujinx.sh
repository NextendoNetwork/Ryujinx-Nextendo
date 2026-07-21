#!/usr/bin/env sh

SCRIPT_DIR=$(dirname "$(realpath "$0")")

COMMAND="env LANG=C.UTF-8 DOTNET_EnableAlternateStackCheck=1"

if command -v gamemoderun > /dev/null 2>&1; then
    COMMAND="$COMMAND gamemoderun"
fi

exec $COMMAND "$SCRIPT_DIR/Ryujinx" "$@"
