# Azunt.Editor 0.8.10 — Shared Document Styles

## What changed

- Browser Print Preview and Print default to **Match document**, reflecting the live Standard, Browser Default, Compact Report, Business Document or Minimal setting.
- The native print window reuses document CSS from the loaded editor stylesheet, not the host application's CSS or editing overlays. No new asset or host registration is required.
- Existing Standard, Compact and Comfortable print profiles remain as explicit print-only alternatives.
- Page size, orientation, margins, repeating table headers, row fragmentation protection, code wrapping and image loading warnings remain in effect. Inline HTML styles and saved percentage table widths are preserved within the existing sanitizer policy.
- The source editor's HTML, selection of document style and history are not modified by opening the preview or choosing a print profile.

## Compatibility and limits

The editor itself and its default content rendering are unchanged. Native printing now uses the active document style by default, even for Standard; select the old Standard print profile to recover the previous print typography. The paper margins replace canvas padding, and print uses light colors rather than the dark application theme. The preview is continuous; browser pagination and print settings may affect the final output. Code blocks omit editing decorations. Host CSS is not transferred. A missing, inaccessible or incompatible stylesheet lacking the required rules produces a warning and offers the legacy Standard print profile; direct printing does not automatically proceed in that case. Keep JS and CSS assets from the same release, loaded as readable top-level CSS rules.

This release does not change the server PDF, Word, Excel or import engines, and does not persist the chosen style into HTML. The host can persist the existing `ContentRendering` enum separately. Custom style registration and server export alignment remain future work.

## Verification

Run `python tests/editor-shared-document-styles-0810-browser.py` for actual Chromium comparison of five styles across Edit, View, Preview and print media, saved table widths, inline formatting, repeated headers and 80-row PDF data completeness. Run `python tests/editor-print-086-browser.py` for the three legacy profiles and paper/image handling. These browser fixtures do not require the .NET host. Run the normal .NET build and NuGet packaging checks separately.
