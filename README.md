# Azunt.Editor
A lightweight rich-text editor for ASP.NET Core MVC, Razor Pages, and Blazor Server.

Version: **0.5.1** · .NET 10

## Features
- Table cell merging and splitting
- Signature drawing, dates and templates

## Projects
- `src/Azunt.Editor` — editor components
- `src/Azunt.Editor.Export` — PDF, Word and Excel export
- `samples/Azunt.Web` — example application
- `tests` — smoke and browser tests

## Run
```powershell
dotnet restore Azunt.Editor.sln
dotnet build Azunt.Editor.sln -c Release
dotnet run --project samples/Azunt.Web
```

## License
MIT. See [LICENSE](LICENSE).
