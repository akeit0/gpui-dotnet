# Editor extension sample

Run from the repository root:

```sh
dotnet run --project samples/Gpui.Editor.Sample/Gpui.Editor.Sample.csproj
```

The project builds the combined component host and copies its native library beside the
executable. The application selects that library and lists `EditorExtension.Requirement`.
The sample bootstraps one document, then demonstrates revisioned editing commands and events.
