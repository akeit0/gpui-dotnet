#!/usr/bin/env sh

set -eu

if [ "$#" -lt 1 ] || [ "$#" -gt 2 ]; then
    echo "Usage: $0 <Cargo.toml> [debug|release]" >&2
    exit 2
fi

manifest_path=$1
configuration=${2:-debug}

case "$configuration" in
    debug)
        ;;
    release)
        ;;
    *)
        echo "Configuration must be debug or release." >&2
        exit 2
        ;;
esac

if [ "$configuration" = release ]; then
    cargo build --locked --manifest-path "$manifest_path" --release
else
    cargo build --locked --manifest-path "$manifest_path"
fi
