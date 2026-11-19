# Azunt.Editor
A lightweight rich-text editor for ASP.NET Core MVC, Razor Pages, and Blazor Server.

Version: **0.1.0** · .NET 10

## Features
- Basic formatting and image insertion
- MVC, Razor Pages and Blazor Server integration
- HTML save and restore

## Projects
- `src/Azunt.Editor` — editor components
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
