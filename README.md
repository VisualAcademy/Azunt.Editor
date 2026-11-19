# Azunt.Editor
A lightweight rich-text editor for ASP.NET Core MVC, Razor Pages, and Blazor Server.

Version: **0.3.0** · .NET 10

## Features
- PDF, Word and Excel export
- Embedded images in exported documents

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
