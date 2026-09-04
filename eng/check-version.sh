#!/usr/bin/env sh
# Verifies the package version is consistent across MSBuild and Cargo sources.
# POSIX equivalent of eng/check-version.ps1.
set -eu

root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
failed=0

fail() {
    echo "check-version: $1" >&2
    failed=1
}

version=$(sed -n 's/^[[:space:]]*<Version>\([^<]*\)<\/Version>.*/\1/p' "$root/Directory.Build.props" | head -n 1)
if [ -z "${version:-}" ]; then
    fail 'Directory.Build.props does not define /Project/PropertyGroup/Version.'
    version=''
fi

workspace_version=$(awk '/^\[workspace\.package\]/{flag=1;next} /^\[/{flag=0} flag && /^version[[:space:]]*=/ {line=$0; sub(/^[^"]*"/, "", line); sub(/".*$/, "", line); print line; exit}' "$root/crates/gpui-dotnet/Cargo.toml")
if [ -z "${workspace_version:-}" ]; then
    fail 'crates/gpui-dotnet/Cargo.toml does not define [workspace.package] version.'
elif [ "$workspace_version" != "$version" ]; then
    fail "Cargo workspace version '$workspace_version' does not match Directory.Build.props '$version'."
fi

for member in 'hosts/default' 'extensions/editor-host'; do
    member_toml="$root/crates/gpui-dotnet/$member/Cargo.toml"
    if grep -q '^version[[:space:]]*=' "$member_toml"; then
        fail "crates/gpui-dotnet/$member/Cargo.toml sets an explicit version; use 'version.workspace = true'."
    fi
    if ! grep -q '^version\.workspace[[:space:]]*=[[:space:]]*true' "$member_toml"; then
        fail "crates/gpui-dotnet/$member/Cargo.toml does not inherit 'version.workspace = true'."
    fi
done

if ! grep -q '^version\.workspace[[:space:]]*=[[:space:]]*true' "$root/crates/gpui-dotnet/Cargo.toml"; then
    fail "crates/gpui-dotnet/Cargo.toml root package does not inherit 'version.workspace = true'."
fi

lock="$root/crates/gpui-dotnet/Cargo.lock"
for name in 'gpui-dotnet' 'gpui-dotnet-default-host' 'gpui-dotnet-editor-host'; do
    lock_version=$(grep -A2 -F "name = \"$name\"" "$lock" | sed -n 's/^version = "\(.*\)"/\1/p' | head -n 1)
    if [ -z "${lock_version:-}" ]; then
        fail "Cargo.lock has no entry for '$name'."
    elif [ "$lock_version" != "$version" ]; then
        fail "Cargo.lock '$name' is '$lock_version', expected '$version'. Run eng/bump-version.sh or 'cargo update -p $name'."
    fi
done

if grep -qE 'dotnet add package GPUI\.NET --version [0-9]|current package line is `' "$root/README.md"; then
    fail 'README.md must not pin a version literal; use version-agnostic install text.'
fi

if [ "$failed" -ne 0 ]; then
    echo 'Version check failed. Directory.Build.props is authoritative; sync with eng/bump-version.sh <version>.' >&2
    exit 1
fi

echo "Versions consistent at $version."
