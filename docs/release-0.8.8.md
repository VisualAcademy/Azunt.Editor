# Azunt.Editor 0.8.8 — Native HTML rendering (step 1)

- Opt-in `BrowserDefault` rendering for the Edit and View canvas, with existing Standard unchanged.
- Shared Blazor, Tag Helper and JavaScript API; server prerender support.
- Browser-like paragraph, heading, table, link and image presentation, scoped to the document.
- Existing HTML and sanitization, toolbar, extensions and export/print engines are unchanged.
- Demo: `/editor/blazor` (selector), `/editor/mvc?rendering=browser`, `/editor/razor-pages?rendering=browser`.

The mode is not a saved document style. It approximates browser defaults on a light document canvas, not an isolated iframe; host CSS outside the reset properties can still affect it. Inline styles supported by the existing allowlist remain authoritative. Unstyled native tables have no visible borders. Fixed-layout tables remain full width after resizing; wide images remain bounded. The native canvas can scroll horizontally. Preview/Print/PDF/Word/Excel styles are not synchronized in this release. Built-in document style selector, persistence and custom styles are future work.

Includes the 0.8.7 table resize hover hotfix in the baseline.
