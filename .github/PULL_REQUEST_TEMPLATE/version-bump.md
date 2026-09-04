# Version bump to `<version>`

> Replace `<version>` with the new version.
> See [Versioning](../../docs/PACKAGING.md#versioning).

## Checklist

- [ ] Bumped with `./eng/bump-version.sh <version>` (`./eng/bump-version.ps1 -Version <version>` on Windows).
- [ ] `./eng/check-version.sh` passes.
- [ ] `dotnet run --project tools/Gpui.Bindings.Generator -- verify` passes.
- [ ] `cargo test --manifest-path crates/gpui-dotnet/Cargo.toml` passes.
- [ ] `dotnet test Gpui.slnx --no-restore` passes.
- [ ] No version literals added outside `Directory.Build.props`, the Cargo workspace
      version, and generated `Cargo.lock` (`README.md` and tag/doc examples stay
      version-agnostic).

## After merge

Tag the release commit (the `Release` workflow requires the tag on `main`):

```sh
version=$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' Directory.Build.props)
git tag "v$version"
git push origin "v$version"
```
