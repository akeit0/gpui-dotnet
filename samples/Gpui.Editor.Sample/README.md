# Editor extension sample

Run from the repository root:

```sh
dotnet run --project samples/Gpui.Editor.Sample/Gpui.Editor.Sample.csproj
```

The sample project builds `gpui-dotnet-components-host` with `--features editor` and copies the
native library beside the executable. The application selects that library through
`NativeRuntimeOptions.LibraryPath` and requires `EditorExtension.Requirement`. The editor provider
retains document text, selection, undo history, parsing, scrolling, focus, and IME state. The sample
bootstraps a document once, then uses typed commands and revisioned edit events.
