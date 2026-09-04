#!/usr/bin/env sh
# Bumps the package version across MSBuild and Cargo sources.
# POSIX equivalent of eng/bump-version.ps1.
set -eu

if [ "$#" -ne 1 ]; then
    echo "Usage: $0 <version>" >&2
    exit 2
fi

version=$1
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)

if ! printf '%s' "$version" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.+-]+)?(\+[0-9A-Za-z.+-]+)?$'; then
    echo "Invalid version '$version'. Expected SemVer (major.minor.patch with optional -prerelease/+build)." >&2
    exit 2
fi

props="$root/Directory.Build.props"
if ! grep -q '<Version>[^<]*</Version>' "$props"; then
    echo 'Directory.Build.props does not contain <Version>...</Version>.' >&2
    exit 1
fi
sed "s|<Version>[^<]*</Version>|<Version>$version</Version>|" "$props" > "$props.tmp"
mv "$props.tmp" "$props"

cargo_toml="$root/crates/gpui-dotnet/Cargo.toml"
if ! grep -q '^\[workspace\.package\]' "$cargo_toml"; then
    echo 'crates/gpui-dotnet/Cargo.toml does not contain [workspace.package]. Run check-version for details.' >&2
    exit 1
fi
sed "/^\\[workspace\\.package\\]/,/^\\[/ s|^version[[:space:]]*=.*|version = \"$version\"|" "$cargo_toml" > "$cargo_toml.tmp"
mv "$cargo_toml.tmp" "$cargo_toml"

echo "Updated Directory.Build.props and Cargo workspace version to $version."

cargo update --manifest-path "$root/crates/gpui-dotnet/Cargo.toml" -p gpui-dotnet -p gpui-dotnet-default-host -p gpui-dotnet-editor-host

"$root/eng/check-version.sh"
echo "Bumped to $version. Verify with 'dotnet run --project tools/Gpui.Bindings.Generator -- verify' and commit the change before tagging."
