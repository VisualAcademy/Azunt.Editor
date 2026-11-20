# Programming code blocks (0.7.1)

Azunt.Editor provides a small dependency-free, presentation-only syntax highlighter
for **C#**, **JavaScript**, **SQL**, **HTML**, and **CSS**. No code is executed or evaluated.

## Usage

1. Position the cursor in Edit mode, choose the **Code** split button or the Insert menu.
2. Use **Code** for C# or **▾** to pick a language, enter literal code in the textarea,
   then select **Apply code**. Tab inserts four spaces; Escape closes the editor.
3. Edit a saved code block by double-clicking it or selecting its **Edit** action.
4. **Copy** copies the literal code with no HTML or token markup (Clipboard API
   requires a secure context); View mode exposes Copy but no Edit button.

## Stored HTML

```html
<pre class="az-code-block" data-language="csharp"><code>Console.WriteLine(&quot;Hello&quot;);</code></pre>
```

`data-language` values are restricted to `csharp`, `javascript`, `sql`, `html`, `css`.
Original code is escaped plain text. The runtime adds `az-code-token-*` spans and
Copy/Edit UI for presentation; browser `getHtml()`, server Viewer sanitization,
and Export sanitization discard those transient elements. No language flag or
arbitrary CSS class is accepted from untrusted markup unless it matches the
code-block allowlist. Each inserted block is limited to 100,000 characters.

## Compatibility

- Edit/View and Undo/Redo preserve code and language metadata.
- Static SSR shows correctly encoded, monospaced code without JavaScript; token
  coloring and Copy button appear after JavaScript initialization.
- PDF output receives a printable code block style, but server-side PDF/Word/Excel
  exports do not embed syntax-token color spans. Word/Excel text layout may differ.
- Code blocks are intentionally atomic within the contenteditable canvas. Use the
  code textarea to change their text; the main editor does not tokenize text on
  every keystroke.
- No third-party JS syntax highlighting/editor packages were added.

## Tests

```shell
python tests/editor-code-blocks-071-browser.py
# On a machine with .NET 10 SDK:
dotnet test Azunt.Editor.sln
# Run the two console smoke-test projects as well:
dotnet run --project tests/Azunt.Editor.SmokeTests
dotnet run --project tests/Azunt.Editor.Export.SmokeTests
```
