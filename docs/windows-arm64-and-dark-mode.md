# Windows ARM64 and dark-mode troubleshooting (0.6.4)

## Dark-mode controls

Azunt.Editor uses a scoped `az-` CSS namespace. The editor chrome follows the
operating system `prefers-color-scheme: dark` preference, without changing
host application styles. Version 0.6.4 fills gaps where writing panels,
buttons, select boxes and numeric inputs retained light-mode backgrounds
while their text inherited dark-mode foregrounds. It does not add a new
JavaScript or .NET public API. Host applications can still apply their own
CSS customizations.

### Document text contrast inside the editor

The editor's canvas uses `--azunt-text` as its default foreground color
(`#1e293b` in light mode and `#f1f5f9` in dark mode). A hosting site may
also contain global rules such as `p { color: #516076; }`. Because a color
explicitly assigned to a paragraph beats a color inherited from its parent,
those rules previously caused gray-on-dark text inside the editor.

The editor now scopes the unformatted text color for ordinary document blocks
(paragraphs, headings, list items, quotes, and table cells) to inherit from
the editor canvas. User-authored inline foreground colors and the editor's
link color are preserved. This is a rendering-only CSS change; saved HTML,
export content, and .NET/JavaScript APIs are unchanged.

The regression check `python tests/editor-content-contrast-browser.py` loads
this repository's real sample `site.css` under both light and dark preferences
and verifies that the canvas contrast remains correct regardless of stylesheet
load order.

If the appearance looks stale after updating assets, hard-refresh the page
and clear any stale `_content/Azunt.Editor/css/azunt-editor.css` cache.
Inspect the computed styles of a problematic control, and emulate dark mode
using browser Developer Tools. If the host uses a separate manual dark-mode
toggle instead of an OS preference, it should pass the appropriate theme
through its own CSS styling rules; this editor does not infer arbitrary host
theme classes.

## PDF export on Windows ARM64

PDF export runs **server side** using `Microsoft.Playwright`, not the user's
browser. It requires an installed and launchable browser on the machine
hosting ASP.NET Core. A working Azure deployment does not prove that a local
Windows ARM64 environment has the same browser installation.

Playwright does not currently ship a native Windows ARM64 Chromium browser;
its bundled Windows browser depends on x64 emulation, and compatibility
varies by Windows, runtime, and browser versions. Do not assume a
Windows ARM64 browser launch is guaranteed.

The demo project's `appsettings.Development.json` selects the `chrome`
channel. On Windows ARM64 in **Development only**, the demo uses `msedge`
instead if no `ExecutablePath` was explicitly specified. This fallback
is sample-only, not part of the reusable editor library. It does not
install any browser or change Azure production behavior.

If the demo still cannot generate PDFs, explicitly configure the host:

```json
{
  "Azunt": {
    "Pdf": {
      "BrowserChannel": "msedge"
    }
  }
}
```

Or set `Azunt:Pdf:ExecutablePath` to a valid **server-side** executable path
for an installed compatible browser. An executable path takes priority over
browser channel during browser launch. For projects using Playwright's
bundled Chromium, install the browser for the exact package version using
`pwsh bin/Debug/net10.0/playwright.ps1 install chromium` after building
the actual host; the script path depends on the host's output directory.
The browser cache and installation are host/deployment responsibilities and
must not run automatically when the editor starts.

### Troubleshooting order

1. Check if Word and Excel downloads work. If they do, investigate the
   Playwright browser installation/launch first, rather than the toolbar.
2. Inspect application logs for `Azunt.Editor PDF browser could not...`.
   They now include OS architecture, process architecture, browser channel,
   plus the original Playwright error as the inner exception.
3. Confirm the selected browser is installed for the **account running the
   server**, not merely for the logged-in desktop account.
4. On Windows ARM64, try `msedge` and check that Edge is installed. On
   systems where this fails, consider running the PDF service on a supported
   x64 environment. Do not rely on automatic browser installation.
5. If a PDF is generated in logs but the download never starts, look for a
   client-side JavaScript error or blocked download, which is a separate
   failure from browser launch.

Do not modify `Microsoft.Playwright` package versions or deploy scripts
without validating browser compatibility for your target environment.
