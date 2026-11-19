# Azunt.Editor
A lightweight rich-text editor for ASP.NET Core MVC, Razor Pages, and Blazor Server.

Version: **0.5.0** · .NET 10

## Features
- Paste cleanup and find/replace
- Page breaks, special characters and fullscreen
- Word and character counts

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
