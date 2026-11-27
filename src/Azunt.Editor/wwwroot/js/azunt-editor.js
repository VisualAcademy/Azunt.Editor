/* Azunt.Editor shared editing engine, no third-party JavaScript dependencies. */
(function () {
  "use strict";

  const instances = new Map();
  const globalCommands = new Map();
  const commandsByEditor = new Map();
  let selectionSequence = 0;
  // Built-in templates are safe, deliberately small HTML examples. Host code can
  // register additional content via AzuntEditor.registerTemplate (no CDN/network).
  const templates = new Map([
    ["blank", { title: "Blank document", html: "<p><br></p>" }],
    ["meeting", { title: "Meeting notes", html: "<h2>Meeting notes</h2><p>Date: </p><p>Attendees: </p><h3>Agenda</h3><ul><li>Agenda item</li></ul><h3>Decisions</h3><p><br></p><h3>Action items</h3><table><tbody><tr><th>Action</th><th>Owner</th><th>Due</th></tr><tr><td><br></td><td><br></td><td><br></td></tr></tbody></table>" }],
    ["report", { title: "Simple report", html: "<h2>Report title</h2><p>Date: </p><h3>Summary</h3><p><br></p><h3>Details</h3><table><tbody><tr><th>Item</th><th>Notes</th></tr><tr><td><br></td><td><br></td></tr></tbody></table><h3>Conclusion</h3><p><br></p>" }],
    ["letter", { title: "Letter", html: "<p>Date: </p><p>To: </p><p>Subject: </p><p>Dear recipient,</p><p><br></p><p>Sincerely,</p><p>Your name</p>" }]
  ]);
  const allowedTags = new Set([
    "p", "div", "h1", "h2", "h3", "h4", "strong", "b", "em", "i", "u", "s", "del", "strike", "ul", "ol", "li",
    "blockquote", "pre", "code", "sup", "sub", "table", "thead", "tbody", "tfoot", "tr", "td", "th", "br", "hr", "img", "a", "span", "input"
  ]);
  const dropWithChildren = new Set([
    "script", "style", "iframe", "frame", "frameset", "object", "embed", "svg", "math", "template",
    "form", "textarea", "button", "select", "option", "link", "meta", "base", "video", "audio",
    "head", "title", "noscript", "noembed", "noframes", "plaintext", "xmp"
  ]);
  const imageDataUrlPattern = /^data:image\/(?:png|jpeg|gif|webp);base64,[A-Za-z0-9+/]+={0,2}$/i;
  const supportedTypes = new Set(["image/png", "image/jpeg", "image/gif", "image/webp"]);
  const maxImageBytes = 2 * 1024 * 1024;
  const maxInlineImageBytes = 8 * 1024 * 1024;
  // Keep regular JS -> .NET interop well below Blazor Server's default 32 KiB
  // SignalR message limit. Large documents use Microsoft's JS stream references.
  const inlineInteropMaxBytes = 16 * 1024;
  const maxDocumentBytes = 16 * 1024 * 1024;
  // Base64 images can make each undo snapshot several MB. Bound the total
  // browser history instead of retaining 60 full copies of large documents.
  const maxHistoryBytes = 32 * 1024 * 1024;
  const maxHistoryEntries = 60;
  const allowedFonts = new Set(["Arial", "Georgia", "Times New Roman", "Verdana", "Segoe UI", "Noto Sans KR", "Malgun Gothic"]);
  const hexColor = /^#[0-9a-f]{6}$/i;
  const codeLanguages = Object.freeze(["csharp", "javascript", "sql", "html", "css"]);
  const maxCodeCharacters = 100000;
  const codeKeywords = {
    csharp: new Set("abstract as async await base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params partial private protected public readonly record ref return sbyte sealed short sizeof static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void volatile while yield get set init required global file".split(" ")),
    javascript: new Set("async await break case catch class const continue debugger default delete do else export extends false finally for from function get if import in instanceof let new null of return set static super switch this throw true try typeof undefined var void while with yield".split(" ")),
    sql: new Set("ADD ALL ALTER AND AS ASC BEGIN BETWEEN BY CASE CAST CHECK COLUMN COMMIT CONSTRAINT COUNT CREATE CROSS DATABASE DEFAULT DELETE DESC DISTINCT DROP ELSE END EXISTS FALSE FOREIGN FROM FULL GROUP HAVING IN INDEX INNER INSERT INTO IS JOIN KEY LEFT LIKE LIMIT NOT NULL ON OR ORDER OUTER PRIMARY REFERENCES RIGHT ROLLBACK SELECT SET TABLE THEN TOP TRUE UNION UNIQUE UPDATE VALUES VIEW WHEN WHERE WITH".split(" ")),
    css: new Set("inherit initial unset revert important auto none block flex grid inline solid absolute relative fixed sticky var calc rgb rgba url display position color background border margin padding width height font transform transition content".split(" ")),
    html: new Set()
  };
  const allowedPageSizes = new Set(["A4", "Letter", "Legal"]);
  const allowedOrientations = new Set(["portrait", "landscape"]);
  const allowedMargins = { normal: "16mm", narrow: "8mm", wide: "25mm" };

  function cleanColor(value) {
    const v = String(value || "").trim().toLowerCase();
    if (hexColor.test(v)) return v;
    const match = /^rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})(?:\s*,\s*1(?:\.0+)?)?\s*\)$/i.exec(v);
    if (match) {
      const numbers = match.slice(1, 4).map(Number);
      if (numbers.every(n => n >= 0 && n <= 255)) return "#" + numbers.map(n => n.toString(16).padStart(2, "0")).join("");
    }
    return null;
  }


  function safeResourceUrl(raw, isImage) {
    if (!raw || typeof raw !== "string") return null;
    const value = raw.trim();
    // A protocol-relative URL bypassed the local-path check and was previously
    // accepted after URL() resolved it against the host. Reject this ambiguous
    // form (and backslashes/control characters) consistently with server View.
    if (value.startsWith("//") || value.includes("\\") || /[\u0000-\u001f\u007f]/.test(value)) return null;
    if (isImage && imageDataUrlPattern.test(value)) return value;
    if (/^\/(?!\/)[^\\\r\n]*$/.test(value) || /^(?!\/)[A-Za-z0-9][^:\\]*$/.test(value) || /^#[A-Za-z][A-Za-z0-9_-]*$/.test(value)) {
      return value;
    }
    try {
      const url = new URL(value, document.baseURI);
      if (url.protocol === "https:" || url.protocol === "http:" || (!isImage && url.protocol === "mailto:")) {
        if (url.username || url.password) return null;
        return value;
      }
    } catch (_) { /* Invalid URL is excluded. */ }
    return null;
  }

  // This is editor-side cleanup, not a substitute for server-side validation of untrusted HTML.
  function sanitizeHtml(html) {
    const inert = document.createElement("template");
    inert.innerHTML = typeof html === "string" ? html : "";
    const output = document.createElement("div");

    function visit(node, destination) {
      if (node.nodeType === Node.TEXT_NODE) {
        destination.appendChild(document.createTextNode(node.textContent || ""));
        return;
      }
      if (node.nodeType !== Node.ELEMENT_NODE) return;
      const tag = node.localName.toLowerCase();
      if (node.namespaceURI !== "http://www.w3.org/1999/xhtml" || dropWithChildren.has(tag)) return;
      if (!allowedTags.has(tag)) {
        for (const child of Array.from(node.childNodes)) visit(child, destination);
        return;
      }
      // Form controls are intentionally a tiny whitelist. A saved document must
      // not introduce text/password/file inputs or submit host application forms.
      if (tag === "input" && !["checkbox", "radio"].includes((node.getAttribute("type") || "").toLowerCase())) return;
      const clean = document.createElement(tag);
      if (tag === "pre" && codeLanguages.includes(node.getAttribute("data-language"))) {
        const source = Array.from(node.children).find(child => child.localName === "code");
        if (source) {
          const raw = source.textContent || "";
          if (raw.length <= maxCodeCharacters) {
            clean.className = "az-code-block";
            clean.setAttribute("data-language", node.getAttribute("data-language"));
            const code = document.createElement("code");
            code.textContent = raw;
            clean.appendChild(code);
            destination.appendChild(clean);
            return;
          }
        }
      }
      if (tag === "input") {
        clean.setAttribute("type", node.getAttribute("type").toLowerCase());
        // Read the live property so user toggles survive HTML serialization,
        // even if the browser has not changed the checked HTML attribute.
        if (node.checked) clean.setAttribute("checked", "");
        destination.appendChild(clean);
        return;
      }
      if (tag === "img") {
        const src = safeResourceUrl(node.getAttribute("src"), true);
        if (!src) return;
        clean.setAttribute("src", src);
        clean.setAttribute("alt", (node.getAttribute("alt") || "").slice(0, 250));
        for (const dimension of ["width", "height"]) {
          const raw = node.getAttribute(dimension);
          const numeric = Number(raw);
          if (raw !== null && Number.isInteger(numeric) && numeric >= 1 && numeric <= 2500) {
            clean.setAttribute(dimension, String(numeric));
          }
        }
      }
      if ((tag === "td" || tag === "th") && node.hasAttribute("colspan")) {
        const span = Number(node.getAttribute("colspan"));
        if (Number.isInteger(span) && span >= 2 && span <= 12) clean.setAttribute("colspan", String(span));
      }
      if (tag === "hr" && node.getAttribute("data-azunt-page-break") === "true")
        clean.setAttribute("data-azunt-page-break", "true");
      if (tag === "a") {
        const href = safeResourceUrl(node.getAttribute("href"), false);
        if (href) {
          clean.setAttribute("href", href);
          clean.setAttribute("rel", "noopener noreferrer");
        }
      }
      // Only allow narrowly-validated formatting properties. Never carry arbitrary CSS.
      if (tag === "span" || ["p", "div", "h1", "h2", "h3", "h4", "li", "td", "th"].includes(tag)) {
        const family = (node.style.fontFamily || "").replace(/^['"]|['"]$/g, "");
        if (allowedFonts.has(family)) clean.style.fontFamily = family.includes(" ") ? `"${family}"` : family;
        const size = /^(\d{1,2})px$/.exec(node.style.fontSize || "");
        if (size && Number(size[1]) >= 8 && Number(size[1]) <= 72) clean.style.fontSize = `${Number(size[1])}px`;
        const color = cleanColor(node.style.color);
        const background = cleanColor(node.style.backgroundColor);
        if (color) clean.style.color = color;
        if (background) clean.style.backgroundColor = background;
      }
      if (["p", "div", "h1", "h2", "h3", "h4", "li", "td", "th", "blockquote"].includes(tag)) {
        const align = (node.style.textAlign || "").toLowerCase();
        if (["left", "center", "right", "justify"].includes(align)) clean.style.textAlign = align;
        const margin = /^(\d{1,3})px$/.exec(node.style.marginLeft || "");
        if (margin && Number(margin[1]) <= 240 && Number(margin[1]) % 24 === 0)
          clean.style.marginLeft = `${Number(margin[1])}px`;
      }
      if (tag === "tr" && /^(?:[1-9]\d{0,2}|1000)px$/.test(node.style.height))
        clean.style.height = node.style.height;
      if (["table", "td", "th"].includes(tag)) {
        const borderColor = cleanColor(node.style.borderColor);
        const borderWidth = /^([1-4])px$/.exec(node.style.borderWidth || "");
        if (borderColor) clean.style.borderColor = borderColor;
        if (borderWidth) clean.style.borderWidth = `${borderWidth[1]}px`;
        if (borderColor || borderWidth) clean.style.borderStyle = "solid";
        // Percentage column widths are generated by the controlled resize grip.
        // Keep only finite, bounded values; arbitrary CSS still cannot persist.
        const percent = (node.style.width || "").trim();
        if ((tag === "td" || tag === "th") && /^(?:100|[1-9]?\d(?:\.\d{1,3})?)%$/.test(percent))
          clean.style.width = percent;
        // A newly inserted table fills the content area in every document style.
        if (tag === "table" && /^(?:100|[1-9]\d?(?:\.\d{1,3})?)%$/.test(percent)) clean.style.width = percent;
        if (tag === "table" && node.style.tableLayout === "fixed")
          clean.style.tableLayout = "fixed";
      }
      for (const child of Array.from(node.childNodes)) visit(child, clean);
      destination.appendChild(clean);
    }

    for (const node of Array.from(inert.content.childNodes)) visit(node, output);
    return output.innerHTML;
  }

  // Status notifications stay outside document HTML and do not change undo history.
  const statusDuration = Object.freeze({ success: 3000, info: 4000, warning: 5000, error: 0 });

  function clearMessage(instance) {
    window.clearTimeout(instance.messageTimer);
    instance.messageTimer = null;
    instance.messageVersion = (instance.messageVersion || 0) + 1;
    if (!instance.status) return;
    instance.status.textContent = "";
    delete instance.status.dataset.statusKind;
    instance.status.setAttribute("role", "status");
    instance.status.setAttribute("aria-live", "polite");
  }

  function message(instance, text, kind = "info") {
    if (!instance.status) return;
    clearMessage(instance);
    const value = String(text ?? "");
    if (!value) return;
    const level = Object.hasOwn(statusDuration, kind) ? kind : "info";
    instance.status.dataset.statusKind = level;
    instance.status.setAttribute("role", level === "error" ? "alert" : "status");
    instance.status.setAttribute("aria-live", level === "error" ? "assertive" : "polite");
    instance.status.textContent = value;
    if (statusDuration[level] > 0) {
      const version = instance.messageVersion;
      instance.messageTimer = window.setTimeout(() => {
        // A newer message or a remounted editor must not be cleared by this timer.
        if (instances.get(instance.element.id) === instance && instance.messageVersion === version)
          clearMessage(instance);
      }, statusDuration[level]);
    }
  }

  function showStatus(id, text, kind = "info") {
    message(getInstance(id), text, kind);
  }


  // Small dependency-free lexer: color only, never executes user-provided code.
  // All token content enters through textContent, never through innerHTML.
  // The lexer is presentation-only. Match complete strings/comments first so
  // words inside them are never recolored as keywords or HTML attributes.
  // No eval/innerHTML or external syntax highlighter is used.
  const codePatterns = {
    csharp: /(?:\/\/[^\n]*|\/\*[\s\S]*?\*\/|@"(?:[^"]|"")*"|\$?"(?:\\[\s\S]|[^"\\])*"|'(?:\\.|[^'\\])*'|\b0x[0-9a-fA-F_]+\b|\b\d[\d_]*(?:\.[\d_]+)?\b|\b[A-Za-z_][\w]*\b)/g,
    javascript: /(?:\/\/[^\n]*|\/\*[\s\S]*?\*\/|"(?:\\[\s\S]|[^"\\])*"|'(?:\\[\s\S]|[^'\\])*'|`(?:\\[\s\S]|[^`\\])*`|\b0x[0-9a-fA-F_]+\b|\b\d[\d_]*(?:\.[\d_]+)?\b|\b[A-Za-z_$][\w$]*\b)/g,
    sql: /(?:--[^\n]*|\/\*[\s\S]*?\*\/|'(?:''|[^'])*'|"(?:""|[^"])*"|\b\d+(?:\.\d+)?\b|\b[A-Za-z_][\w]*\b)/g,
    html: /(?:<!--[\s\S]*?-->|<\/?[A-Za-z][\w:-]*|\b[A-Za-z_:][\w:.-]*(?=\s*=)|"[^"]*"|'[^']*'|&(?:#[0-9]+|#x[0-9a-fA-F]+|[A-Za-z]+);)/g,
    css: /(?:\/\*[\s\S]*?\*\/|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|#[0-9a-fA-F]{3,8}\b|@[A-Za-z-]+|--[\w-]+|[A-Za-z-]+(?=\s*:)|\b\d+(?:\.\d+)?(?:px|rem|em|%|vh|vw|pt|s|ms)?\b|\b[A-Za-z_][\w-]*\b)/g
  };
  function codeTokenType(language, word, matchIndex, source) {
    if ((language === "sql" && /^--/.test(word)) ||
        (language !== "sql" && language !== "html" && /^\/\//.test(word)) ||
        /^\/\*/.test(word) || /^<!--/.test(word)) return "comment";
    if (/^(?:['"`]|@"|\$")/.test(word)) return "string";
    if (/^\d/.test(word) || (language === "css" && /^#[0-9a-f]{3,8}$/i.test(word))) return "number";
    if (language === "html") {
      if (/^<\/?[A-Za-z]/.test(word)) return "tag";
      if (/^[A-Za-z_:][\w:.-]*$/.test(word)) return "property";
      if (/^&/.test(word)) return "number";
      return "plain";
    }
    if (language === "css") {
      if (/^@/.test(word)) return "keyword";
      if (/^--/.test(word) || /^[A-Za-z-]+$/.test(word) && /\s*:/.test(source.slice(matchIndex + word.length))) return "property";
    }
    if (codeKeywords[language]?.has(language === "sql" ? word.toUpperCase() : word)) return "keyword";
    if (language === "csharp" && /^[A-Z][A-Za-z0-9_]*$/.test(word)) return "type";
    if (language === "javascript" && /^(?:console|document|window|Promise|Array|Object|Math|JSON)$/.test(word)) return "type";
    return "plain";
  }

  function highlightCodeText(code, language, source) {
    const fragment = document.createDocumentFragment();
    const tokens = codePatterns[language];
    if (!tokens) { code.textContent = source; return; }
    let offset = 0;
    for (const match of source.matchAll(tokens)) {
      if (match.index > offset) fragment.appendChild(document.createTextNode(source.slice(offset, match.index)));
      const type = codeTokenType(language, match[0], match.index, source);
      if (type === "plain") fragment.appendChild(document.createTextNode(match[0]));
      else {
        const span = document.createElement("span");
        span.className = `az-code-token-${type}`;
        span.textContent = match[0];
        fragment.appendChild(span);
      }
      offset = match.index + match[0].length;
    }
    if (offset < source.length) fragment.appendChild(document.createTextNode(source.slice(offset)));
    code.replaceChildren(fragment);
  }

  // Line numbers are view-only siblings of <code>. Neither the stored document
  // nor the Copy action ever includes them. Cap the gutter for very long files.
  function updateCodeLineNumbers(block, source) {
    const lines = source.split("\n").length;
    const previous = block.querySelector(":scope > .az-code-line-numbers");
    if (lines > 5000) {
      previous?.remove();
      block.dataset.codeLinesHidden = "true";
      block.azCodeLineCount = lines;
      return;
    }
    delete block.dataset.codeLinesHidden;
    if (previous && block.azCodeLineCount === lines) return;
    const gutter = previous || document.createElement("span");
    gutter.className = "az-code-line-numbers";
    gutter.setAttribute("aria-hidden", "true");
    gutter.contentEditable = "false";
    gutter.textContent = Array.from({ length: lines }, (_, index) => index + 1).join("\n");
    gutter.style.minWidth = `${Math.max(3, String(lines).length + 1)}ch`;
    if (!previous) block.insertBefore(gutter, block.querySelector("code"));
    block.azCodeLineCount = lines;
  }

  function editCodeIndent(textarea, outdent) {
    const value = textarea.value;
    const start = textarea.selectionStart;
    const end = textarea.selectionEnd;
    const firstLineStart = value.lastIndexOf("\n", Math.max(0, start - 1)) + 1;
    const hasMultipleLines = value.slice(start, end).includes("\n");
    if (!outdent && !hasMultipleLines) {
      textarea.setRangeText("    ", start, end, "end");
      return;
    }
    // Exclude a trailing selected line when selection ends at column zero.
    const effectiveEnd = end > firstLineStart && value[end - 1] === "\n" ? end - 1 : end;
    const lastLineEnd = value.indexOf("\n", effectiveEnd);
    const rangeEnd = lastLineEnd === -1 ? value.length : lastLineEnd;
    const lines = value.slice(firstLineStart, rangeEnd).split("\n");
    const changed = lines.map(line => outdent ? line.replace(/^(?: {1,4}|\t)/, "") : "    " + line);
    const replacement = changed.join("\n");
    const firstDifference = changed[0].length - lines[0].length;
    const totalDifference = replacement.length - (rangeEnd - firstLineStart);
    textarea.setRangeText(replacement, firstLineStart, rangeEnd, "preserve");
    if (hasMultipleLines) textarea.setSelectionRange(firstLineStart, rangeEnd + totalDifference);
    else textarea.setSelectionRange(Math.max(firstLineStart, start + firstDifference), Math.max(firstLineStart, start + firstDifference));
  }

  function insertCodeNewline(textarea) {
    const start = textarea.selectionStart;
    const end = textarea.selectionEnd;
    const before = textarea.value.slice(0, start);
    const currentLine = before.slice(before.lastIndexOf("\n") + 1);
    const whitespace = /^\s*/.exec(currentLine)?.[0] || "";
    const increase = /[{[(]\s*$/.test(currentLine) ? "    " : "";
    textarea.setRangeText("\n" + whitespace + increase, start, end, "end");
  }

  function decorateCodeBlocks(instance) {
    instance.canvas.querySelectorAll('pre[data-language]').forEach(block => {
      const language = block.getAttribute("data-language");
      if (!codeLanguages.includes(language)) return;
      const code = Array.from(block.children).find(child => child.localName === "code");
      if (!code) return;
      block.classList.add("az-code-block");
      // Atomic in Edit: double-click or Edit button opens the accessible textarea.
      block.contentEditable = "false";
      const value = code.textContent || "";
      if (value.length > maxCodeCharacters) return;
      if (block.azCodeRendered !== value || block.azCodeLanguage !== language) {
        highlightCodeText(code, language, value);
        block.azCodeRendered = value;
        block.azCodeLanguage = language;
      }
      updateCodeLineNumbers(block, value);
      if (!block.querySelector('.az-code-actions')) {
        const actions = document.createElement("span");
        actions.className = "az-code-actions";
        actions.contentEditable = "false";
        const label = document.createElement("span");
        label.className = "az-code-language";
        label.textContent = language === "csharp" ? "C#" : language === "javascript" ? "JavaScript" : language.toUpperCase();
        actions.appendChild(label);
        if (!instance.readOnly) {
          const edit = document.createElement("button");
          edit.type = "button";
          edit.dataset.codeAction = "edit";
          edit.textContent = "Edit";
          edit.setAttribute("aria-label", "Edit code block");
          actions.appendChild(edit);
        }
        const copy = document.createElement("button");
        copy.type = "button";
        copy.dataset.codeAction = "copy";
        copy.textContent = "Copy";
        copy.setAttribute("aria-label", "Copy code block");
        actions.appendChild(copy);
        block.insertBefore(actions, code);
      }
    });
  }

  function openCodePanel(instance, language = "csharp", block = null) {
    if (instance.readOnly || instance.sourceMode || !instance.codePanel) return;
    const validLanguage = codeLanguages.includes(language) ? language : "csharp";
    instance.editingCodeBlock = block && instance.canvas.contains(block) ? block : null;
    instance.codeLanguage.value = validLanguage;
    instance.codeText.value = instance.editingCodeBlock?.querySelector("code")?.textContent || "";
    instance.codePanel.hidden = false;
    instance.codeText.focus();
  }

  function closeCodePanel(instance) {
    if (!instance.codePanel) return;
    instance.codePanel.hidden = true;
    instance.editingCodeBlock = null;
  }

  function applyCodePanel(instance) {
    const text = instance.codeText.value;
    if (text.length > maxCodeCharacters) return message(instance, "Code block exceeds 100,000 characters.", "warning");
    const language = instance.codeLanguage.value;
    if (!codeLanguages.includes(language)) return;
    const existing = instance.editingCodeBlock;
    closeCodePanel(instance);
    if (existing && instance.canvas.contains(existing)) {
      existing.setAttribute("data-language", language);
      const code = existing.querySelector("code");
      code.textContent = text;
      existing.azCodeRendered = null;
      changed(instance);
      decorateCodeBlocks(instance);
    } else {
      const pre = document.createElement("pre");
      pre.setAttribute("data-language", language);
      pre.className = "az-code-block";
      const code = document.createElement("code");
      code.textContent = text || "// Enter code here";
      pre.appendChild(code);
      const fragment = document.createDocumentFragment();
      fragment.appendChild(pre);
      insertClipboardFragment(instance, fragment);
      decorateCodeBlocks(instance);
    }
  }

  function handleCodeAction(instance, event) {
    const button = event.target.closest('button[data-code-action]');
    if (!button) return false;
    const block = button.closest('pre[data-language]');
    if (!block || !instance.canvas.contains(block)) return false;
    event.preventDefault();
    event.stopPropagation();
    const code = block.querySelector('code');
    if (!code) return true;
    if (button.dataset.codeAction === "edit") openCodePanel(instance, block.dataset.language, block);
    else if (button.dataset.codeAction === "copy") {
      const text = code.textContent || "";
      if (navigator.clipboard?.writeText) navigator.clipboard.writeText(text)
        .then(() => message(instance, "Code copied.", "success"))
        .catch(() => message(instance, "Copy unavailable. Select and copy the code manually.", "warning"));
      else message(instance, "Clipboard unavailable in this browser context.", "warning");
    }
    return true;
  }

  function readHtml(instance) {
    return sanitizeHtml(instance.sourceMode ? instance.source.value : instance.canvas.innerHTML);
  }

  function createHtmlBlob(html) {
    const blob = new Blob([html], { type: "text/html;charset=utf-8" });
    if (blob.size > maxDocumentBytes) {
      throw new Error("This document exceeds the 16 MB Blazor transfer limit. Use image uploads instead of inline Base64.");
    }
    return blob;
  }

  // JS -> .NET callback arguments must be explicitly wrapped as JS stream references.
  function createHtmlStream(html) {
    if (!window.DotNet || typeof window.DotNet.createJSStreamReference !== "function") {
      throw new Error("Blazor streaming is unavailable. Check blazor.web.js.");
    }
    return window.DotNet.createJSStreamReference(createHtmlBlob(html));
  }

  function emit(instance) {
    const html = readHtml(instance);
    instance.value.value = html;
    if (!instance.dotNetReference) return;

    const revision = ++instance.revision;
    // Measuring UTF-8 bytes (not JS string length) protects Unicode documents.
    const size = new Blob([html]).size;
    try {
      const operation = size <= inlineInteropMaxBytes
        ? instance.dotNetReference.invokeMethodAsync("OnHtmlChanged", html, revision)
        : instance.dotNetReference.invokeMethodAsync("OnHtmlStreamChanged", createHtmlStream(html), revision);
      operation.catch(error => {
        console.warn("Azunt.Editor Blazor synchronization failed:", error);
        message(instance, "Blazor synchronization failed. Check the connection and browser console.", "error");
      });
    } catch (error) {
      console.warn("Azunt.Editor Blazor synchronization failed:", error);
      message(instance, String(error.message || error), "error");
    }
  }

  function queueEmit(instance) {
    window.clearTimeout(instance.emitTimer);
    instance.emitTimer = window.setTimeout(() => emit(instance), 200);
  }

  // Store DOM-relative endpoints separately from the HTML snapshots. A live
  // Range cannot survive innerHTML replacement (Undo, View -> Edit, or paste).
  // Paths are best-effort: sanitizing HTML may remove nodes, in which case we
  // safely collapse at the end of the restored document instead of throwing.
  function selectionEndpoint(instance, node, offset) {
    const path = [];
    for (let current = node; current && current !== instance.canvas; current = current.parentNode) {
      const parent = current.parentNode;
      if (!parent) return null;
      path.unshift(Array.prototype.indexOf.call(parent.childNodes, current));
    }
    if (!node || (node !== instance.canvas && !instance.canvas.contains(node))) return null;
    return { path, offset };
  }

  function captureHistorySelection(instance) {
    const selection = window.getSelection();
    let range = selection?.rangeCount ? selection.getRangeAt(0) : null;
    if (!range || !instance.canvas.contains(range.commonAncestorContainer)) {
      range = instance.lastRange && instance.canvas.contains(instance.lastRange.commonAncestorContainer)
        ? instance.lastRange : null;
    }
    if (!range) return null;
    const start = selectionEndpoint(instance, range.startContainer, range.startOffset);
    const end = selectionEndpoint(instance, range.endContainer, range.endOffset);
    return start && end ? { start, end } : null;
  }

  function rememberHistorySelection(instance) {
    const position = captureHistorySelection(instance);
    if (position) instance.historySelections[instance.historyIndex] = position;
  }

  function restoreHistorySelection(instance) {
    const saved = instance.historySelections[instance.historyIndex];
    const resolve = endpoint => {
      let node = instance.canvas;
      for (const index of endpoint.path) {
        if (!node.childNodes || index >= node.childNodes.length) return null;
        node = node.childNodes[index];
      }
      const limit = node.nodeType === Node.TEXT_NODE ? (node.textContent || "").length : node.childNodes.length;
      return { node, offset: Math.max(0, Math.min(endpoint.offset, limit)) };
    };
    const range = document.createRange();
    const start = saved && resolve(saved.start);
    const end = saved && resolve(saved.end);
    try {
      if (start) {
        range.setStart(start.node, start.offset);
        if (end) range.setEnd(end.node, end.offset);
        else range.collapse(true);
      } else {
        range.selectNodeContents(instance.canvas);
        range.collapse(false);
      }
    } catch (_) {
      range.selectNodeContents(instance.canvas);
      range.collapse(false);
    }
    const selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
    instance.lastRange = range.cloneRange();
  }

  function remember(instance) {
    const html = readHtml(instance);
    if (instance.history[instance.historyIndex] === html) {
      rememberHistorySelection(instance);
      return;
    }
    instance.history = instance.history.slice(0, instance.historyIndex + 1);
    instance.historySelections = instance.historySelections.slice(0, instance.historyIndex + 1);
    instance.history.push(html);
    instance.historySelections.push(captureHistorySelection(instance));
    // Always retain at least the current snapshot, even if it alone exceeds
    // the history budget. Large image-rich documents intentionally have fewer
    // undo steps to avoid exhausting browser memory.
    let totalBytes = instance.history.reduce((total, value) => total + new Blob([value]).size, 0);
    while (instance.history.length > 1 &&
      (instance.history.length > maxHistoryEntries || totalBytes > maxHistoryBytes)) {
      totalBytes -= new Blob([instance.history.shift()]).size;
      instance.historySelections.shift();
    }
    instance.historyIndex = instance.history.length - 1;
  }

  function updateStats(instance) {
    if (!instance.stats) return;
    // textContent excludes image Base64 and HTML markup. Do not treat code or
    // attributes as words, and do not persist view-only counters into HTML.
    let text = instance.sourceMode
      ? (() => { const node = document.createElement("div"); node.innerHTML = sanitizeHtml(instance.source.value); return node.textContent || ""; })()
      : instance.canvas.innerText || instance.canvas.textContent || "";
    // Code Copy/Edit UI is not part of the document and must not affect counts.
    if (!instance.sourceMode && instance.canvas.querySelector('.az-code-actions')) {
      const clone = instance.canvas.cloneNode(true);
      clone.querySelectorAll('.az-code-actions, .az-code-line-numbers').forEach(node => node.remove());
      text = clone.textContent || "";
    }
    // Chromium reports the placeholder <br> in an empty editable paragraph
    // as a line break in innerText. It is a caret placeholder, not a character.
    if (!instance.sourceMode && !instance.canvas.textContent &&
        !instance.canvas.querySelector("img,table,input,hr")) text = "";
    const count = (text.match(/[\p{L}\p{N}]+(?:['’‐-―-][\p{L}\p{N}]+)*/gu) || []).length;
    instance.stats.textContent = `${count} word${count === 1 ? "" : "s"} · ${Array.from(text).length} characters`;
  }

  function changed(instance) {
    clearSelectionBookmark(instance);
    remember(instance);
    decorateCodeBlocks(instance);
    updateStats(instance);
    updateResizeHandle(instance);
    updateFormattingState(instance);
    queueEmit(instance);
  }

  // Ctrl+A in a contenteditable region often selects the text inside the first
  // and last block, not the canvas element itself. Compare the selected document
  // content rather than requiring Range boundaries at the canvas edges.
  function selectionCoversDocument(instance) {
    const selection = window.getSelection();
    if (!selection?.rangeCount || selection.isCollapsed) return false;
    const range = selection.getRangeAt(0);
    if (!instance.canvas.contains(range.startContainer) ||
        !instance.canvas.contains(range.endContainer)) return false;
    const fragment = range.cloneContents();
    if (fragment.textContent !== instance.canvas.textContent) return false;
    // Text-only comparison must not erase an unselected image, table, form
    // control or horizontal rule (which may have no textContent at all).
    for (const selector of ["img", "table", "input", "hr"]) {
      if (fragment.querySelectorAll(selector).length !== instance.canvas.querySelectorAll(selector).length)
        return false;
    }
    return true;
  }

  // A paste is a whole-document replacement only when the selection includes
  // every root block, not merely all visible text. An unselected empty heading
  // or paragraph has no textContent but must remain in a partial replacement.
  function selectionReplacesDocument(instance) {
    if (!selectionCoversDocument(instance)) return false;
    const range = window.getSelection().getRangeAt(0);
    return Array.from(instance.canvas.children).every(child => range.intersectsNode(child));
  }

  function resetSelectedDocument(instance) {
    // Commit the pre-delete HTML before replacing the document, otherwise
    // Ctrl+Z could skip directly over an unsnapshotted editing change.
    remember(instance);
    const paragraph = document.createElement("p");
    paragraph.appendChild(document.createElement("br"));
    instance.canvas.replaceChildren(paragraph);
    const range = document.createRange();
    range.setStart(paragraph, 0);
    range.collapse(true);
    const selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
    instance.lastRange = range.cloneRange();
    instance.activeCell = null;
    setImageSelection(instance, null);
    selectTableControls(instance, null);
    updateContextTools(instance);
    changed(instance);
  }

  function selectedRange(instance) {
    const selection = window.getSelection();
    if (selection && selection.rangeCount) {
      const range = selection.getRangeAt(0);
      if (instance.canvas.contains(range.commonAncestorContainer)) {
        instance.lastRange = range.cloneRange();
        rememberHistorySelection(instance);
        return range;
      }
    }
    if (instance.lastRange && instance.canvas.contains(instance.lastRange.commonAncestorContainer)) {
      selection.removeAllRanges();
      selection.addRange(instance.lastRange);
      rememberHistorySelection(instance);
      return instance.lastRange;
    }
    const range = document.createRange();
    range.selectNodeContents(instance.canvas);
    range.collapse(false);
    selection.removeAllRanges();
    selection.addRange(range);
    rememberHistorySelection(instance);
    return range;
  }

  function selectAfter(node) {
    const range = document.createRange();
    range.setStartAfter(node);
    range.collapse(true);
    const selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
  }

  // Form inserts are purely document elements, not host-application form fields:
  // they have no name/value, handlers, or arbitrary attributes.
  const formControlKinds = new Set([
    "form-checkbox", "form-checkbox-checked", "form-radio", "form-radio-checked"
  ]);

  function insertFormControl(instance, command) {
    if (!formControlKinds.has(command)) return;
    const input = document.createElement("input");
    input.type = command.startsWith("form-radio") ? "radio" : "checkbox";
    input.checked = command.endsWith("-checked");
    if (input.checked) input.setAttribute("checked", "");
    insertAtSelection(instance, input);
    message(instance, `${input.type === "radio" ? "Radio button" : "Checkbox"} inserted.`, "success");
  }

  function setDocumentControlsReadOnly(instance) {
    // Disabled is view-only state; sanitizeHtml intentionally never persists it.
    instance.canvas.querySelectorAll('input[type="checkbox"],input[type="radio"]').forEach(control => {
      control.disabled = instance.readOnly;
      if (instance.readOnly) control.tabIndex = -1;
      else control.removeAttribute("tabindex");
    });
  }

  function insertAtSelection(instance, node) {
    instance.canvas.focus();
    const range = selectedRange(instance);
    range.deleteContents();
    range.insertNode(node);
    selectAfter(node);
    changed(instance);
  }

  function inlineStyle(instance, tag) {
    const range = selectedRange(instance);
    if (range.collapsed) {
      message(instance, "Select text before applying a style.", "warning");
      return;
    }
    const wrapper = document.createElement(tag);
    wrapper.appendChild(range.extractContents());
    range.insertNode(wrapper);
    selectAfter(wrapper);
    changed(instance);
  }

  function formatSpan(instance, property, value) {
    const range = selectedRange(instance);
    if (range.collapsed) {
      message(instance, "Select text before applying this formatting.", "warning");
      return;
    }
    const wrapper = document.createElement("span");
    wrapper.style[property] = value;
    wrapper.appendChild(range.extractContents());
    range.insertNode(wrapper);
    selectAfter(wrapper);
    changed(instance);
  }

  function adjustIndent(instance, amount) {
    const block = nearestBlock(instance, selectedRange(instance));
    if (!block) return message(instance, "Place the cursor inside a paragraph first.", "warning");
    const old = parseInt(block.style.marginLeft || "0", 10) || 0;
    const next = Math.max(0, Math.min(240, old + amount));
    block.style.marginLeft = next ? `${next}px` : "";
    changed(instance);
  }

  function editableAncestor(instance, tagNames, origin) {
    const range = instance.lastRange;
    let node = origin || range?.startContainer || null;
    if (node?.nodeType !== Node.ELEMENT_NODE) node = node?.parentElement;
    while (node && node !== instance.canvas) {
      if (tagNames.includes(node.tagName)) return node;
      node = node.parentElement;
    }
    return null;
  }

  function openLinkPanel(instance) {
    const range = selectedRange(instance);
    const active = editableAncestor(instance, ["A"], range.startContainer);
    instance.editingLink = active;
    instance.linkText.value = active ? active.textContent || "" : range.toString().slice(0, 250);
    instance.linkUrl.value = active ? active.getAttribute("href") || "" : "";
    instance.linkPanel.hidden = false;
    instance.linkUrl.focus();
  }

  function applyLink(instance) {
    const rawUrl = instance.linkUrl.value.trim();
    const url = safeResourceUrl(rawUrl, false);
    if (!url) return message(instance, "Enter a valid HTTPS, HTTP, mailto, or relative URL.", "warning");
    const label = instance.linkText.value.trim();
    const active = instance.editingLink && instance.canvas.contains(instance.editingLink) ? instance.editingLink : null;
    if (active) {
      active.setAttribute("href", url);
      active.textContent = label || url;
    } else {
      const range = selectedRange(instance);
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.rel = "noopener noreferrer";
      anchor.textContent = label || range.toString() || url;
      range.deleteContents();
      range.insertNode(anchor);
      selectAfter(anchor);
    }
    instance.linkPanel.hidden = true;
    instance.editingLink = null;
    changed(instance);
  }

  function removeLink(instance) {
    const active = editableAncestor(instance, ["A"]);
    if (!active) return message(instance, "Place the cursor inside a link to remove it.", "warning");
    const parent = active.parentNode;
    while (active.firstChild) parent.insertBefore(active.firstChild, active);
    active.remove();
    changed(instance);
  }

  function toggleSource(instance) {
    closeCodePanel(instance);
    if (instance.readOnly) return;
    clearSelectionBookmark(instance);
    // Mode transitions must never persist a half-completed pointer gesture.
    finishTableMove(instance, null, true);
    finishTableColumnResize(instance, null, true);
    selectTableControls(instance, null);
    // A pending image should never appear after source HTML has been edited.
    instance.imageInsertGeneration++;
    if (!instance.sourceMode) {
      instance.source.value = sanitizeHtml(instance.canvas.innerHTML);
      instance.source.hidden = false;
      instance.canvas.hidden = true;
      instance.sourceMode = true;
      updateResizeHandle(instance);
      instance.source.focus();
      updateStats(instance);
      message(instance, "HTML source mode. Select </> again to apply permitted markup.");
    } else {
      const clean = sanitizeHtml(instance.source.value);
      instance.canvas.innerHTML = clean;
      decorateCodeBlocks(instance);
      instance.sourceMode = false;
      instance.source.hidden = true;
      instance.canvas.hidden = false;
      instance.lastRange = null;
      changed(instance);
      message(instance, "HTML source applied; unsupported markup was removed.", "success");
    }
  }

  function selectedImage(instance) {
    return instance.selectedImage && instance.canvas.contains(instance.selectedImage) ? instance.selectedImage : null;
  }

  function updateResizeHandle(instance) {
    const image = selectedImage(instance);
    const handle = instance.imageResizeHandle;
    if (!handle) return;
    if (!image || instance.sourceMode || instance.readOnly || !instance.element.isConnected) {
      handle.hidden = true;
      return;
    }
    const imageRect = image.getBoundingClientRect();
    const bounds = instance.canvas.getBoundingClientRect();
    const editorRect = instance.element.getBoundingClientRect();
    // The handle is outside contenteditable: it is never included in saved HTML.
    if (!imageRect.width || !imageRect.height || imageRect.bottom < 0 || imageRect.top > window.innerHeight || imageRect.bottom > bounds.bottom || imageRect.bottom < bounds.top || imageRect.right > bounds.right || imageRect.right < bounds.left) {
      handle.hidden = true;
      return;
    }
    handle.style.left = `${imageRect.right - editorRect.left - 9}px`;
    handle.style.top = `${imageRect.bottom - editorRect.top - 9}px`;
    handle.hidden = false;
  }

  function setImageSelection(instance, image) {
    if (instance.selectedImage) instance.selectedImage.classList.remove("az-azunt-editor-image-selected");
    instance.selectedImage = image && instance.canvas.contains(image) ? image : null;
    if (instance.selectedImage) {
      instance.selectedImage.classList.add("az-azunt-editor-image-selected");
      // An absent width attribute represents the full native pixel dimensions.
      instance.imageWidth.value = instance.selectedImage.getAttribute("width") || "";
      if (instance.imageAlt) instance.imageAlt.value = instance.selectedImage.getAttribute("alt") || "";
      updateResizeHandle(instance);
      if (!instance.selectedImage.complete) instance.selectedImage.addEventListener("load", () => {
        if (selectedImage(instance) === instance.selectedImage) updateResizeHandle(instance);
      }, { once: true });
    } else {
      instance.imageWidth.value = "";
      if (instance.imageAlt) instance.imageAlt.value = "";
      updateResizeHandle(instance);
    }
  }

  function setImageAlt(instance) {
    const image = selectedImage(instance);
    if (!image || instance.readOnly || !instance.imageAlt) return;
    image.setAttribute("alt", instance.imageAlt.value.slice(0, 200));
    changed(instance);
  }

  function setImagePixelWidth(instance, image, width) {
    image.setAttribute("width", String(width));
    image.removeAttribute("height");
    image.style.removeProperty("width");
    image.style.removeProperty("height");
    instance.imageWidth.value = String(width);
    updateResizeHandle(instance);
  }

  function restoreOriginalImageSize(instance) {
    const image = selectedImage(instance);
    if (!image) return message(instance, "Click an image to select it first.", "warning");
    image.removeAttribute("width");
    image.removeAttribute("height");
    image.style.removeProperty("width");
    image.style.removeProperty("height");
    instance.imageWidth.value = "";
    updateResizeHandle(instance);
    changed(instance);
    message(instance, "Original image dimensions restored. Large images fit the editor width on screen.", "success");
  }

  function resizeImage(instance) {
    const image = selectedImage(instance);
    if (!image) return message(instance, "Click an image to select it first.", "warning");
    if (instance.imageWidth.value.trim() === "") return restoreOriginalImageSize(instance);
    const width = Number(instance.imageWidth.value);
    if (!Number.isInteger(width) || width < 24 || width > 2500)
      return message(instance, "Image width must be between 24 and 2500 pixels.", "warning");
    setImagePixelWidth(instance, image, width);
    changed(instance);
  }

  function beginImageResize(instance, event) {
    const image = selectedImage(instance);
    if (!image || instance.readOnly || instance.sourceMode || event.button !== 0) return;
    event.preventDefault();
    event.stopPropagation();
    const handle = instance.imageResizeHandle;
    instance.imageResize = {
      image,
      pointerId: event.pointerId,
      startX: event.clientX,
      originalWidth: image.getAttribute("width"),
      originalHeight: image.getAttribute("height"),
      originalStyle: image.getAttribute("style"),
      displayWidth: image.getBoundingClientRect().width / viewScale(instance),
      moved: false
    };
    handle.classList.add("az-is-resizing");
    try { handle.setPointerCapture(event.pointerId); } catch (_) { /* Pointer capture is optional. */ }
  }

  function dragImageResize(instance, event) {
    const state = instance.imageResize;
    if (!state || event.pointerId !== state.pointerId || !instance.canvas.contains(state.image)) return;
    event.preventDefault();
    // Limit the size to the editable area; export preserves the chosen width.
    const maxVisible = Math.min(2500, Math.max(24, instance.canvas.clientWidth - 48));
    const width = Math.round(Math.max(24, Math.min(maxVisible, state.displayWidth + (event.clientX - state.startX) / viewScale(instance))));
    if (Math.abs(width - state.displayWidth) >= 1) state.moved = true;
    setImagePixelWidth(instance, state.image, width);
  }

  function finishImageResize(instance, event, cancelled = false) {
    const state = instance.imageResize;
    if (!state || (event?.pointerId !== undefined && event.pointerId !== state.pointerId)) return;
    instance.imageResize = null;
    instance.imageResizeHandle?.classList.remove("az-is-resizing");
    if (cancelled && instance.canvas.contains(state.image)) {
      if (state.originalWidth === null) state.image.removeAttribute("width");
      else state.image.setAttribute("width", state.originalWidth);
      if (state.originalHeight === null) state.image.removeAttribute("height");
      else state.image.setAttribute("height", state.originalHeight);
      if (state.originalStyle === null) state.image.removeAttribute("style");
      else state.image.setAttribute("style", state.originalStyle);
      instance.imageWidth.value = state.originalWidth || "";
    } else if (state.moved) {
      changed(instance); // Exactly one history snapshot and Blazor sync per drag.
      message(instance, `Image resized to ${state.image.getAttribute("width")} px wide (aspect ratio preserved).`, "success");
    }
    updateResizeHandle(instance);
  }

  function alignImage(instance, alignment) {
    const image = selectedImage(instance);
    if (!image) return message(instance, "Click an image to select it first.", "warning");
    let block = image.closest("p,div");
    if (!block || !instance.canvas.contains(block)) {
      block = document.createElement("p");
      image.replaceWith(block);
      block.appendChild(image);
    }
    block.style.textAlign = alignment;
    changed(instance);
  }

  function deleteImage(instance) {
    const image = selectedImage(instance);
    if (!image) return message(instance, "Click an image to select it first.", "warning");
    const parent = image.parentElement;
    setImageSelection(instance, null);
    image.remove();
    if (parent && parent !== instance.canvas && parent.tagName === "P" && !parent.textContent?.trim() && !parent.querySelector("img")) parent.remove();
    changed(instance);
  }

  // Table controls live OUTSIDE the editable canvas, so handles and drop markers
  // cannot leak into saved HTML, Undo, View, or document exports.
  function ensureTableControls(instance) {
    // The .NET renderer supplies the grips in MVC, Razor Pages and Blazor. Use
    // them instead of injecting sibling DOM nodes into a Blazor-owned subtree.
    const existingMove = instance.element.querySelector("[data-table-move-handle]");
    const existingResize = instance.element.querySelector("[data-table-column-handle]");
    const existingMarker = instance.element.querySelector("[data-table-drop-marker]");
    if (existingMove && existingResize && existingMarker) {
      existingMove.title = "Drag to move; Alt+Up/Down to move one block; arrow keys to resize selected row or column";
      instance.tableMoveHandle = existingMove;
      instance.tableColumnHandle = existingResize;
      instance.tableDropMarker = existingMarker;
      instance.tableControlsOwned = false;
      return;
    }
    // Compatible fallback for manually-authored older host HTML/test harnesses.
    instance.tableControlsOwned = true;
    const move = document.createElement("button");
    move.type = "button";
    move.dataset.tableMoveHandle = "";
    move.className = "az-azunt-editor-table-move-handle";
    move.textContent = "\u2725";
    move.title = "Drag to move; Alt+Up/Down to move one block; arrow keys to resize selected row or column";
    move.setAttribute("aria-label", "Move table");
    move.hidden = true;
    const resize = document.createElement("button");
    resize.type = "button";
    resize.dataset.tableColumnHandle = "";
    resize.className = "az-azunt-editor-table-column-handle";
    resize.title = "Drag to resize adjacent table columns";
    resize.setAttribute("aria-label", "Resize table column");
    resize.hidden = true;
    const marker = document.createElement("div");
    marker.dataset.tableDropMarker = "";
    marker.className = "az-azunt-editor-table-drop-marker";
    marker.setAttribute("aria-hidden", "true");
    marker.hidden = true;
    instance.element.append(move, resize, marker);
    instance.tableMoveHandle = move;
    instance.tableColumnHandle = resize;
    instance.tableDropMarker = marker;
  }

  function editableTable(instance, table) {
    // Tables inserted within existing paragraphs or loaded from older HTML can
    // have a wrapping block. A handle must still be usable for those tables.
    // Only reject a table nested inside ANOTHER table (moving it could alter
    // its parent's cell structure).
    return table && table.tagName === "TABLE" && instance.canvas.contains(table) &&
      !table.parentElement?.closest("table") &&
      !instance.readOnly && !instance.sourceMode && !instance.imageResize;
  }

  function clearTableHandles(instance) {
    if (instance.tableMoveHandle) instance.tableMoveHandle.hidden = true;
    if (instance.tableColumnHandle) instance.tableColumnHandle.hidden = true;
    instance.tableResizeTarget = null;
    if (instance.tableDropMarker) instance.tableDropMarker.hidden = true;
  }

  function rectangularTable(table, minColumns = 2) {
    const rows = Array.from(table?.rows || []);
    if (!rows.length || rows[0].cells.length < minColumns || rows[0].cells.length > 10) return false;
    return rows.every(row => row.cells.length === rows[0].cells.length &&
      Array.from(row.cells).every(cell => cell.colSpan === 1 && cell.rowSpan === 1));
  }

  function positionTableControls(instance) {
    if (instance.tableMove || instance.tableColumnResize) return;
    const table = instance.selectedTable;
    if (!editableTable(instance, table)) {
      clearTableHandles(instance);
      if (instance.tableHoverPoint) updateTableColumnHover(instance);
      return;
    }
    const host = instance.element.getBoundingClientRect();
    const rect = table.getBoundingClientRect();
    const canvasBounds = instance.canvas.getBoundingClientRect();
    if (!rect.width || rect.bottom < canvasBounds.top || rect.top > canvasBounds.bottom) {
      clearTableHandles(instance); return;
    }
    const move = instance.tableMoveHandle;
    move.hidden = false;
    // Never clamp the grip INTO a cell: narrow document padding is valid.
    // Prefer the outside left gutter; use the space above the table when a
    // viewport/overflow ancestor would clip that gutter.
    const gap = 4;
    const width = move.offsetWidth, height = move.offsetHeight;
    let clipLeft = 0;
    for (let parent = instance.element; parent; parent = parent.parentElement) {
      if (/^(hidden|clip|auto|scroll)$/.test(getComputedStyle(parent).overflowX))
        clipLeft = Math.max(clipLeft, parent.getBoundingClientRect().left + parent.clientLeft);
    }
    const left = rect.left - width - gap;
    const originX = host.left + instance.element.clientLeft;
    const originY = host.top + instance.element.clientTop;
    if (left >= clipLeft + 1) {
      move.style.left = `${left - originX}px`;
      move.style.top = `${rect.top - originY + 2}px`;
    } else {
      move.style.left = `${Math.max(clipLeft + 1, rect.left) - originX}px`;
      move.style.top = `${rect.top - originY - height - gap}px`;
    }
    if (rect.top < canvasBounds.top || rect.top > canvasBounds.bottom) move.hidden = true;
    // Resize hit targets are independent of the selected cell/table.
    if (instance.tableHoverPoint) updateTableColumnHover(instance);
  }

  function selectTableControls(instance, table, column = 0) {
    if (!editableTable(instance, table)) {
      instance.selectedTable = null;
      clearTableHandles(instance);
      return;
    }
    instance.selectedTable = table;
    const count = table.rows[0]?.cells.length || 0;
    instance.tableColumnIndex = rectangularTable(table)
      ? Math.min(Math.max(0, column), count - 2) : column;
    positionTableControls(instance);
  }

  // A transparent hit area is only exposed within 5 CSS pixels of an edge.
  // Hover never changes the active cell or selects another table.
  function tableResizeTargetAt(instance, x, y) {
    const front = document.elementFromPoint(x, y);
    if (!front || (front !== instance.tableColumnHandle && !instance.canvas.contains(front))) return null;
    const cellAt = (px) => document.elementsFromPoint(px, y)
      .filter(el => el !== instance.tableColumnHandle && instance.canvas.contains(el))
      .map(el => el.closest("td,th")).find(Boolean);
    // The right edge is also reachable from the small gutter outside a table.
    const directCell = cellAt(x);
    const cell = directCell || cellAt(x - 5);
    const table = cell?.closest("table");
    if (!editableTable(instance, table)) return null;
    const rect = cell.getBoundingClientRect();
    const row = cell.parentElement;
    const rowRect = row.getBoundingClientRect();
    const candidates = [];
    const add = (axis, index, distance) => {
      if (index >= 0 && distance <= 5) candidates.push({ table, axis, index, distance });
    };
    const col = cell.cellIndex;
    const tableRect = table.getBoundingClientRect();
    if (rectangularTable(table, 1) && y >= tableRect.top && y <= tableRect.bottom)
      add("outer", 0, Math.abs(x - tableRect.right));
    if (directCell && (Math.abs(x - rect.left) <= 5 || Math.abs(x - rect.right) <= 5) && rectangularTable(table)) {
      if (col > 0) add("column", col - 1, Math.abs(x - rect.left));
      if (col < row.cells.length - 1) add("column", col, Math.abs(x - rect.right));
    }
    // Row heights are safe for horizontal merges, but not vertical spans.
    if (directCell && (Math.abs(y - rowRect.top) <= 5 || Math.abs(y - rowRect.bottom) <= 5) &&
        Array.from(table.rows).every(r => Array.from(r.cells).every(c => c.rowSpan === 1))) {
      if (row.rowIndex > 0) add("row", row.rowIndex - 1, Math.abs(y - rowRect.top));
      add("row", row.rowIndex, Math.abs(y - rowRect.bottom));
    }
    return candidates.sort((a, b) => a.distance - b.distance)[0] || null;
  }

  function positionTableResizeHandle(instance, target) {
    const handle = instance.tableColumnHandle;
    const { table, axis, index } = target;
    const rect = table.getBoundingClientRect();
    const host = instance.element.getBoundingClientRect();
    const canvas = instance.canvas.getBoundingClientRect();
    const left = Math.max(rect.left, canvas.left), right = Math.min(rect.right, canvas.right);
    const top = Math.max(rect.top, canvas.top), bottom = Math.min(rect.bottom, canvas.bottom);
    handle.dataset.resizeAxis = axis;
    handle.setAttribute("aria-label", axis === "row" ? "Resize table row" : axis === "outer" ? "Resize table width" : "Resize table column");
    handle.title = "";
    if (axis === "row") {
      const edge = table.rows[index].getBoundingClientRect().bottom;
      handle.style.left = `${left - host.left}px`;
      handle.style.top = `${edge - host.top - 5}px`;
      handle.style.width = `${Math.max(0, right - left)}px`;
      handle.style.height = "10px";
    } else {
      const edge = axis === "outer" ? rect.right : table.rows[0].cells[index].getBoundingClientRect().right;
      handle.style.left = `${edge - host.left - 5}px`;
      handle.style.top = `${top - host.top}px`;
      handle.style.width = "10px";
      handle.style.height = `${Math.max(0, bottom - top)}px`;
    }
    handle.hidden = false;
  }

  function updateTableColumnHover(instance, target) {
    if (instance.tableMove || instance.tableColumnResize || !instance.tableColumnHandle) return;
    const point = instance.tableHoverPoint;
    const hit = point && target !== null ? tableResizeTargetAt(instance, point.x, point.y) : null;
    instance.tableResizeTarget = hit;
    if (hit) positionTableResizeHandle(instance, hit);
    else instance.tableColumnHandle.hidden = true;
  }

  // Only direct children of the canvas are valid table drop destinations.
  // Never move a table into another table/cell or split a paragraph's text.
  function tableDropTarget(instance, x, y) {
    const canvas = instance.canvas;
    const bounds = canvas.getBoundingClientRect();
    if (x < bounds.left || x > bounds.right || y < bounds.top || y > bounds.bottom) return null;
    const element = document.elementFromPoint(x, y);
    if (!element || (element !== canvas && !canvas.contains(element))) return null;
    let block = element;
    while (block && block.parentElement !== canvas) block = block.parentElement;
    const before = block ? (y < block.getBoundingClientRect().top + block.getBoundingClientRect().height / 2
      ? block : block.nextSibling) : null;
    return { before, reference: block };
  }

  function positionTableDropMarker(instance, target) {
    const canvasRect = instance.canvas.getBoundingClientRect();
    const hostRect = instance.element.getBoundingClientRect();
    const before = target.before;
    const reference = before?.nodeType === Node.ELEMENT_NODE ? before.getBoundingClientRect() : null;
    const y = reference ? reference.top : (target.reference
      ? target.reference.getBoundingClientRect().bottom : canvasRect.bottom - 12);
    const marker = instance.tableDropMarker;
    marker.hidden = false;
    marker.style.left = `${canvasRect.left - hostRect.left + 8}px`;
    marker.style.width = `${Math.max(20, canvasRect.width - 16)}px`;
    marker.style.top = `${y - hostRect.top}px`;
  }

  function startTableMove(instance, event) {
    if (event.button !== 0 || !editableTable(instance, instance.selectedTable)) return;
    event.preventDefault();
    // The floating table command bar may overlap the drop destination; keep it
    // hidden for the entire gesture so elementFromPoint() sees the document.
    if (instance.contextTools) instance.contextTools.hidden = true;
    remember(instance);
    instance.tableMove = { table: instance.selectedTable, pointerId: event.pointerId,
      startX: event.clientX, startY: event.clientY, target: null, moved: false };
    instance.tableColumnHandle.hidden = true;
    try { instance.tableMoveHandle.setPointerCapture(event.pointerId); } catch (_) { /* optional */ }
  }

  function dragTableMove(instance, event) {
    const state = instance.tableMove;
    if (!state || event.pointerId !== state.pointerId) return;
    event.preventDefault();
    if (Math.hypot(event.clientX - state.startX, event.clientY - state.startY) < 6 && !state.moved) return;
    state.moved = true;
    instance.tableMoveHandle.classList.add("az-is-dragging");
    // Ignore the handle itself when resolving the destination underneath it.
    instance.tableMoveHandle.style.pointerEvents = "none";
    state.target = tableDropTarget(instance, event.clientX, event.clientY);
    if (state.target) positionTableDropMarker(instance, state.target);
    else instance.tableDropMarker.hidden = true;
  }

  function finishTableMove(instance, event, cancel = false) {
    const state = instance.tableMove;
    if (!state || (event && event.pointerId !== undefined && event.pointerId !== state.pointerId)) return;
    instance.tableMove = null;
    instance.tableMoveHandle.style.pointerEvents = "";
    instance.tableMoveHandle.classList.remove("az-is-dragging");
    instance.tableDropMarker.hidden = true;
    const before = state.target?.before || null;
    if (!cancel && state.moved && state.target && editableTable(instance, state.table) &&
        before !== state.table && before !== state.table.nextSibling) {
      instance.canvas.insertBefore(state.table, before);
      instance.activeCell = state.table.rows[0]?.cells[0] || null;
      changed(instance);
      message(instance, "Table moved. Use Undo to restore its previous position.", "success");
    }
    positionTableControls(instance);
    updateContextTools(instance);
  }

  function moveTableByKeyboard(instance, direction) {
    const table = instance.selectedTable;
    if (!editableTable(instance, table)) return;
    // A historical document may wrap the selected table inside a paragraph
    // or div. Move only the table, but calculate destination using root-level
    // blocks; insertBefore() requires a direct child of the canvas.
    let rootBlock = table;
    while (rootBlock.parentElement && rootBlock.parentElement !== instance.canvas)
      rootBlock = rootBlock.parentElement;
    const sibling = direction < 0 ? rootBlock.previousElementSibling : rootBlock.nextElementSibling;
    if (!sibling) return;
    remember(instance);
    instance.canvas.insertBefore(table, direction < 0 ? sibling : sibling.nextSibling);
    changed(instance);
    positionTableControls(instance);
    instance.tableMoveHandle.focus({ preventScroll: true });
  }

  function startTableColumnResize(instance, event, explicitTarget = null) {
    const target = explicitTarget || instance.tableResizeTarget;
    const table = target?.table;
    if (event.button !== 0 || !editableTable(instance, table) || instance.tableMove || instance.tableColumnResize) return;
    const { axis, index } = target;
    if (axis === "column" && (!rectangularTable(table) || index < 0 || index >= table.rows[0].cells.length - 1)) return;
    if (axis === "outer" && !rectangularTable(table, 1)) return;
    if (axis === "row" && (!table.rows[index] || Array.from(table.rows).some(r => Array.from(r.cells).some(c => c.rowSpan !== 1)))) return;
    event.preventDefault();
    instance.canvas.focus({ preventScroll: true });
    if (instance.contextTools) instance.contextTools.hidden = true;
    remember(instance);
    const scale = viewScale(instance);
    const widths = Array.from(table.rows[0].cells, c => c.getBoundingClientRect().width / scale);
    const total = widths.reduce((a, b) => a + b, 0);
    if ((axis === "column" || axis === "outer") && total <= 0) return;
    const parent = table.parentElement;
    const parentStyle = getComputedStyle(parent);
    const availableWidth = parent.clientWidth - parseFloat(parentStyle.paddingLeft) - parseFloat(parentStyle.paddingRight);
    if (axis === "outer" && availableWidth <= 0) return;
    const tableWidth = table.getBoundingClientRect().width / scale;
    // Respect both the column ratios and minimum CSS cell sizes. Browser
    // content constraints can still impose a larger intrinsic minimum.
    const minTableWidth = axis === "outer" ? Math.min(availableWidth, Math.max(24,
      ...widths.map((width, col) => {
        const css = getComputedStyle(table.rows[0].cells[col]);
        const minimum = Math.max(12, parseFloat(css.minWidth) || 0,
          parseFloat(css.paddingLeft) + parseFloat(css.paddingRight) + 2);
        return minimum * total / Math.max(1, width);
      }))) : 0;
    const row = axis === "row" ? table.rows[index] : null;
    instance.tableColumnResize = { table, axis, index, pointerId: event.pointerId,
      startX: event.clientX, startY: event.clientY, widths, total, changed: false,
      row, rowHeight: row ? row.getBoundingClientRect().height / scale : undefined, savedHeight: row?.style.height,
      savedLayout: table.style.tableLayout, savedTableWidth: table.style.width,
      availableWidth, tableWidth, minTableWidth,
      savedWidths: axis !== "row" ? Array.from(table.rows, r => Array.from(r.cells, cell => cell.style.width)) : null };
    instance.tableColumnHandle.classList.add("az-is-dragging");
    positionTableResizeHandle(instance, target);
    try { instance.tableColumnHandle.setPointerCapture(event.pointerId); } catch (_) { /* optional */ }
  }

  function applyTableColumnWidths(table, widths, total) {
    table.style.tableLayout = "fixed";
    for (const row of Array.from(table.rows)) {
      for (let col = 0; col < widths.length; col++) {
        const percent = (widths[col] * 100 / total).toFixed(3);
        row.cells[col].style.width = percent === "100.000" ? "100%" : `${percent}%`;
      }
    }
  }

  function dragTableColumnResize(instance, event) {
    const state = instance.tableColumnResize;
    if (!state || event.pointerId !== state.pointerId || !instance.canvas.contains(state.table)) return;
    event.preventDefault();
    const index = state.index;
    if (state.axis === "row") {
      const height = Math.max(12, Math.min(1000, Math.round(state.rowHeight + (event.clientY - state.startY) / viewScale(instance))));
      state.row.style.height = Math.abs(height - state.rowHeight) >= 1 ? `${height}px` : state.savedHeight;
      state.changed = state.row.style.height !== state.savedHeight;
      positionTableResizeHandle(instance, state);
      return;
    }
    const delta = (event.clientX - state.startX) / viewScale(instance);
    if (state.axis === "outer") {
      const width = Math.max(state.minTableWidth, Math.min(state.availableWidth, state.tableWidth + delta));
      const changedWidth = Math.abs(width - state.tableWidth) >= 1 && Math.abs(delta) >= 1;
      if (!changedWidth && !state.changed) return;
      if (changedWidth) {
        const percent = Math.max(1, Math.min(100, width * 100 / state.availableWidth));
        state.table.style.width = `${Number(percent.toFixed(3))}%`;
        applyTableColumnWidths(state.table, state.widths, state.total);
      } else {
        restoreTableResizeStyles(state);
      }
      state.changed = changedWidth;
      positionTableResizeHandle(instance, state);
      return;
    }
    const minWidth = Math.min(50, (state.widths[index] + state.widths[index + 1]) / 2);
    const left = Math.max(minWidth, Math.min(state.widths[index] + state.widths[index + 1] - minWidth, state.widths[index] + delta));
    if (Math.abs(left - state.widths[index]) < 1 && !state.changed) return;
    const next = state.widths.slice();
    next[index] = left;
    next[index + 1] = state.widths[index] + state.widths[index + 1] - left;
    applyTableColumnWidths(state.table, next, state.total);
    state.changed = Math.abs(left - state.widths[index]) >= 1;
    positionTableResizeHandle(instance, state);
  }

  function restoreTableResizeStyles(state) {
    state.table.style.tableLayout = state.savedLayout;
    if (state.axis === "outer") state.table.style.width = state.savedTableWidth;
    for (const [r, row] of Array.from(state.table.rows).entries())
      for (const [c, cell] of Array.from(row.cells).entries()) cell.style.width = state.savedWidths[r][c];
  }

  function finishTableColumnResize(instance, event, cancel = false) {
    const state = instance.tableColumnResize;
    if (!state || (event && event.pointerId !== undefined && event.pointerId !== state.pointerId)) return;
    instance.tableColumnResize = null;
    const handle = instance.tableColumnHandle;
    handle.classList.remove("az-is-dragging");
    try {
      if (handle.hasPointerCapture(state.pointerId)) handle.releasePointerCapture(state.pointerId);
    } catch (_) { /* capture may already be lost */ }
    if ((cancel || !state.changed) && instance.canvas.contains(state.table)) {
      if (state.axis === "row") state.row.style.height = state.savedHeight;
      else restoreTableResizeStyles(state);
    } else if (state.changed && instance.canvas.contains(state.table)) {
      changed(instance);
      message(instance, state.axis === "row" ? "Row height updated. Use Undo to restore it." : state.axis === "outer" ? "Table width updated. Use Undo to restore it." : "Column widths updated. Use Undo to restore them.", "success");
    }
    if (event?.pointerType === "mouse" || event?.pointerType === "pen")
      instance.tableHoverPoint = { x: event.clientX, y: event.clientY };
    else if (cancel) instance.tableHoverPoint = null;
    positionTableControls(instance);
    updateContextTools(instance);
    updateTableColumnHover(instance);
  }

  function cellAtNode(instance, node) {
    let current = node?.nodeType === Node.ELEMENT_NODE ? node : node?.parentElement;
    while (current && current !== instance.canvas) {
      if ((current.tagName === "TD" || current.tagName === "TH") &&
          current.closest("table")?.closest(".az-azunt-editor-canvas") === instance.canvas)
        return current;
      current = current.parentElement;
    }
    return null;
  }

  function activeTableCell(instance) {
    // Keyboard movement can change the caret without firing a click. Prefer
    // the live in-editor selection, but retain the clicked cell when the
    // toolbar has taken focus and the browser has cleared its selection.
    const selection = window.getSelection();
    if (selection?.rangeCount) {
      const range = selection.getRangeAt(0);
      if (instance.canvas.contains(range.startContainer)) {
        const cell = cellAtNode(instance, range.startContainer);
        if (cell) return cell;
        // A selection outside a table must not mutate a previously clicked table.
        if (range.startContainer !== instance.canvas) return null;
      }
    }
    if (instance.activeCell && instance.canvas.contains(instance.activeCell)) return instance.activeCell;
    const cell = cellAtNode(instance, instance.lastRange?.startContainer);
    return cell && instance.canvas.contains(cell) ? cell : null;
  }

  function selectTableCell(instance, cell) {
    if (!cell || !instance.canvas.contains(cell)) return;
    const range = document.createRange();
    range.selectNodeContents(cell);
    range.collapse(false);
    instance.canvas.focus();
    const selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
    instance.lastRange = range.cloneRange();
    instance.activeCell = cell;
    updateContextTools(instance);
  }

  function selectAfterTableRemoval(instance, table) {
    const parent = table.parentNode;
    if (!parent || !instance.canvas.contains(parent) && parent !== instance.canvas) return;
    const offset = Array.from(parent.childNodes).indexOf(table);
    table.remove();
    const range = document.createRange();
    range.setStart(parent, Math.min(offset, parent.childNodes.length));
    range.collapse(true);
    instance.canvas.focus();
    const selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
    instance.lastRange = range.cloneRange();
    instance.activeCell = null;
    updateContextTools(instance);
  }

  function isPlaceholderCell(cell) {
    // A solitary <br> is the editor's empty-cell placeholder. Everything else
    // (including input, hr, and inline images) is document content.
    const nodes = Array.from(cell.childNodes).filter(node =>
      node.nodeType !== Node.TEXT_NODE || !!node.textContent.trim());
    return nodes.length === 0 || (nodes.length === 1 && nodes[0].nodeName === "BR");
  }

  function mergeTableRight(instance) {
    const cell = activeTableCell(instance);
    if (!cell) return message(instance, "Click a table cell first.", "warning");
    const right = cell.nextElementSibling;
    if (!right || !["TD", "TH"].includes(right.tagName) || cell.rowSpan !== 1 || right.rowSpan !== 1)
      return message(instance, "No adjacent cell available for merging.", "warning");
    const width = cell.colSpan + right.colSpan;
    if (width > 12) return message(instance, "Merged cell cannot exceed 12 columns.", "warning");
    if (!isPlaceholderCell(right)) {
      if (isPlaceholderCell(cell)) cell.replaceChildren();
      else if (cell.lastChild?.nodeName !== "BR") cell.appendChild(document.createElement("br"));
      while (right.firstChild) cell.appendChild(right.firstChild);
    }
    cell.colSpan = width;
    right.remove();
    selectTableCell(instance, cell);
    changed(instance);
    message(instance, `Merged ${width} columns. Split before adding/deleting rows or columns.`, "success");
  }

  function splitTableCell(instance) {
    const cell = activeTableCell(instance);
    if (!cell) return message(instance, "Click a merged table cell first.", "warning");
    const count = cell.colSpan;
    if (count < 2) return message(instance, "This cell is not merged.", "warning");
    cell.removeAttribute("colspan");
    let after = cell;
    for (let i = 1; i < count; i++) {
      const sibling = document.createElement(cell.tagName.toLowerCase());
      sibling.innerHTML = "<br>";
      sibling.style.borderColor = cell.style.borderColor;
      sibling.style.borderWidth = cell.style.borderWidth;
      sibling.style.borderStyle = cell.style.borderStyle;
      after.after(sibling);
      after = sibling;
    }
    selectTableCell(instance, cell);
    changed(instance);
    message(instance, `Split into ${count} cells. Content remains in the first cell.`, "success");
  }

  function updateTableBorders(instance, field, value) {
    const cell = activeTableCell(instance);
    if (!cell) return message(instance, "Click a table cell before setting its table border.", "warning");
    const table = cell.closest("table");
    if (!table) return;
    if (field === "tableBorderColor" && !cleanColor(value)) return;
    if (field === "tableBorderWidth" && !/^[1-4]$/.test(String(value))) return;
    const borderColor = field === "tableBorderColor" ? cleanColor(value) :
      cleanColor(table.style.borderColor) || "#aeb8c6";
    const borderWidth = field === "tableBorderWidth" ? `${value}px` :
      (/^[1-4]px$/.test(table.style.borderWidth) ? table.style.borderWidth : "1px");
    for (const element of [table, ...table.querySelectorAll("td,th")]) {
      element.style.borderColor = borderColor;
      element.style.borderWidth = borderWidth;
      element.style.borderStyle = "solid";
    }
    changed(instance);
  }

  function insertDateTime(instance, format) {
    const now = new Date();
    const locale = document.documentElement.lang || navigator.language || "en-US";
    const formats = {
      date: { year: "numeric", month: "2-digit", day: "2-digit" },
      time: { hour: "2-digit", minute: "2-digit" },
      datetime: { year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" }
    };
    if (!formats[format]) return;
    const text = new Intl.DateTimeFormat(locale, formats[format]).format(now);
    insertAtSelection(instance, document.createTextNode(text));
    instance.datePanel.hidden = true;
  }

  function updateSignaturePen(instance) {
    const context = instance.signatureContext;
    if (!context) return;
    const color = cleanColor(instance.signatureColor?.value) || "#1e293b";
    const width = Number(instance.signatureWidth?.value);
    context.strokeStyle = color;
    context.lineWidth = [2, 3, 5].includes(width) ? width : 3;
    context.lineCap = "round";
    context.lineJoin = "round";
  }

  function showSignature(instance) {
    if (!instance.signaturePanel || instance.readOnly) return;
    instance.signaturePanel.hidden = false;
    if (!instance.signatureContext) {
      const context = instance.signatureCanvas.getContext("2d", { willReadFrequently: true });
      if (!context) return message(instance, "Signature canvas is unavailable.", "error");
      instance.signatureContext = context;
    }
    updateSignaturePen(instance);
    instance.signatureCanvas.focus();
  }

  function clearSignature(instance) {
    instance.signatureContext?.clearRect(0, 0, instance.signatureCanvas.width, instance.signatureCanvas.height);
    instance.signatureHasInk = false;
    instance.signatureBounds = null;
    instance.signatureDrawing = false;
  }

  function signaturePoint(instance, event) {
    const bounds = instance.signatureCanvas.getBoundingClientRect();
    return { x: Math.max(0, Math.min(instance.signatureCanvas.width, (event.clientX - bounds.left) * instance.signatureCanvas.width / bounds.width)),
      y: Math.max(0, Math.min(instance.signatureCanvas.height, (event.clientY - bounds.top) * instance.signatureCanvas.height / bounds.height)) };
  }

  function recordSignaturePoint(instance, point) {
    if (!instance.signatureBounds) {
      instance.signatureBounds = { left: point.x, top: point.y, right: point.x, bottom: point.y };
    } else {
      instance.signatureBounds.left = Math.min(instance.signatureBounds.left, point.x);
      instance.signatureBounds.top = Math.min(instance.signatureBounds.top, point.y);
      instance.signatureBounds.right = Math.max(instance.signatureBounds.right, point.x);
      instance.signatureBounds.bottom = Math.max(instance.signatureBounds.bottom, point.y);
    }
  }

  // Trim transparent whitespace from the canvas before embedding the PNG.
  // This keeps signatures usable in PDF/DOCX/XLSX without oversized white margins.
  function signatureImage(instance) {
    const canvas = instance.signatureCanvas;
    const bounds = instance.signatureBounds;
    if (!bounds) return null;
    const padding = Math.max(12, Math.ceil(instance.signatureContext.lineWidth * 3));
    const left = Math.max(0, Math.floor(bounds.left - padding));
    const top = Math.max(0, Math.floor(bounds.top - padding));
    const right = Math.min(canvas.width, Math.ceil(bounds.right + padding));
    const bottom = Math.min(canvas.height, Math.ceil(bounds.bottom + padding));
    const width = Math.max(1, right - left);
    const height = Math.max(1, bottom - top);
    const cropped = document.createElement("canvas");
    cropped.width = width;
    cropped.height = height;
    cropped.getContext("2d").drawImage(canvas, left, top, width, height, 0, 0, width, height);
    return { dataUrl: cropped.toDataURL("image/png"), width, height };
  }

  function insertSignature(instance) {
    if (!instance.signatureHasInk) return message(instance, "Draw a signature before inserting it.", "warning");
    const result = signatureImage(instance);
    if (!result || !imageDataUrlPattern.test(result.dataUrl) || result.dataUrl.length > 2 * 1024 * 1024)
      return message(instance, "Signature image is too large.", "warning");
    const image = document.createElement("img");
    image.src = result.dataUrl;
    image.alt = "Signature";
    // Insert the tightly cropped signature at its natural pixel size.
    insertAtSelection(instance, image);
    instance.signaturePanel.hidden = true;
    clearSignature(instance);
    message(instance, "Signature inserted.", "success");
  }

  function refreshTemplateOptions(instance) {
    if (!instance.templateList) return;
    const existing = instance.templateList.value;
    instance.templateList.replaceChildren();
    for (const [key, item] of templates) {
      const option = document.createElement("option");
      option.value = key;
      option.textContent = item.title;
      instance.templateList.appendChild(option);
    }
    if (templates.has(existing)) instance.templateList.value = existing;
  }

  function applyTemplate(instance) {
    const selected = templates.get(instance.templateList?.value);
    if (!selected) return message(instance, "Choose a template first.", "warning");
    const existingText = (instance.canvas.textContent || "").trim();
    const hasContent = existingText.length > 0 || !!instance.canvas.querySelector("img,table,hr");
    if (hasContent && !window.confirm("Replace the current document with this template? Existing edits may be lost.")) return;
    instance.sourceMode = false;
    instance.source.hidden = true;
    instance.canvas.hidden = false;
    instance.canvas.innerHTML = sanitizeHtml(selected.html);
    instance.lastRange = null;
    instance.activeCell = null;
    setImageSelection(instance, null);
    instance.templatePanel.hidden = true;
    changed(instance);
    message(instance, `Loaded template: ${selected.title}`, "success");
  }

  function registerTemplate(key, title, html) {
    if (typeof key !== "string" || !/^[a-z][a-z0-9_-]{1,40}$/.test(key) ||
        typeof title !== "string" || !title.trim() || title.length > 100 ||
        typeof html !== "string" || html.length > 200000)
      throw new Error("Invalid template key, title or HTML length.");
    const clean = sanitizeHtml(html);
    templates.set(key, { title: title.trim(), html: clean });
    for (const instance of instances.values()) refreshTemplateOptions(instance);
  }

  function modifyTable(instance, operation) {
    if (operation === "table-merge-right") return mergeTableRight(instance);
    if (operation === "table-split") return splitTableCell(instance);
    const cell = activeTableCell(instance);
    if (!cell) return message(instance, "Click a table cell first.", "warning");
    const row = cell.closest("tr");
    const table = cell.closest("table");
    if (!row || !table) return;
    const rows = Array.from(table.rows);
    const rowIndex = rows.indexOf(row);
    const colIndex = Array.from(row.cells).indexOf(cell);
    if (rows.some(r => r.cells.length !== row.cells.length || Array.from(r.cells).some(c => c.colSpan !== 1 || c.rowSpan !== 1))) {
      return message(instance, "Split merged cells before changing rows, columns, or header status.", "warning");
    }
    const emptyCell = source => {
      const result = document.createElement(source.tagName.toLowerCase());
      // Retain the appearance of borders, heading and text while creating an
      // empty cell; never copy its data, event handlers or arbitrary attributes.
      for (const style of ["borderColor", "borderWidth", "borderStyle", "backgroundColor", "color",
        "textAlign", "fontFamily", "fontSize", "width"]) {
        if (source.style[style]) result.style[style] = source.style[style];
      }
      result.appendChild(document.createElement("br"));
      return result;
    };
    let destination = null;
    if (operation === "table-delete") {
      selectAfterTableRemoval(instance, table);
    } else if (operation === "table-row-before" || operation === "table-row-after") {
      const added = document.createElement("tr");
      for (const existing of Array.from(row.cells)) added.appendChild(emptyCell(existing));
      if (operation === "table-row-before") row.before(added); else row.after(added);
      destination = added.cells[colIndex];
    } else if (operation === "table-row-delete") {
      if (rows.length === 1) selectAfterTableRemoval(instance, table);
      else {
        // Prefer the following row, falling back to the preceding row.
        destination = rows[Math.min(rowIndex + 1, rows.length - 1)];
        if (destination === row) destination = rows[rowIndex - 1];
        destination = destination?.cells[colIndex] || null;
        row.remove();
      }
    } else if (operation === "table-col-before" || operation === "table-col-after") {
      const insertAt = colIndex + (operation === "table-col-after" ? 1 : 0);
      for (const r of rows) {
        const reference = r.cells[insertAt] || null;
        const source = r.cells[colIndex];
        const inserted = emptyCell(source);
        r.insertBefore(inserted, reference);
        if (r === row) destination = inserted;
      }
    } else if (operation === "table-col-delete") {
      if (row.cells.length === 1) selectAfterTableRemoval(instance, table);
      else {
        for (const r of rows) r.deleteCell(colIndex);
        destination = row.cells[Math.min(colIndex, row.cells.length - 1)];
      }
    } else if (operation === "table-header-toggle") {
      const nextTag = Array.from(row.cells).every(c => c.tagName === "TH") ? "td" : "th";
      for (const oldCell of Array.from(row.cells)) {
        const replacement = document.createElement(nextTag);
        for (const attribute of Array.from(oldCell.attributes)) replacement.setAttribute(attribute.name, attribute.value);
        while (oldCell.firstChild) replacement.appendChild(oldCell.firstChild);
        if (oldCell === cell) destination = replacement;
        oldCell.replaceWith(replacement);
      }
      message(instance, nextTag === "th" ? "Header row enabled." : "Header row removed.", "success");
    } else {
      return;
    }
    // Column insertion/deletion changes the number of columns. Normalize
    // percentages once so previously resized tables remain valid at 100%.
    if ((operation === "table-col-before" || operation === "table-col-after" || operation === "table-col-delete") &&
        table.isConnected && table.style.tableLayout === "fixed" && rectangularTable(table)) {
      const widths = Array.from(table.rows[0].cells, c => c.getBoundingClientRect().width);
      const sum = widths.reduce((a, b) => a + b, 0);
      if (sum > 0) applyTableColumnWidths(table, widths, sum);
    }
    if (destination) selectTableCell(instance, destination);
    selectTableControls(instance, destination?.closest("table") || null, colIndex);
    changed(instance);
  }

  function nearestBlock(instance, range) {
    let current = range.startContainer.nodeType === Node.ELEMENT_NODE ? range.startContainer : range.startContainer.parentElement;
    while (current && current !== instance.canvas) {
      if (["P", "DIV", "H1", "H2", "H3", "H4", "LI", "BLOCKQUOTE"].includes(current.tagName)) return current;
      current = current.parentElement;
    }
    return null;
  }

  function changeBlock(instance, tag) {
    const range = selectedRange(instance);
    const block = nearestBlock(instance, range);
    const replacement = document.createElement(tag);
    if (block && block.parentElement === instance.canvas) {
      while (block.firstChild) replacement.appendChild(block.firstChild);
      block.replaceWith(replacement);
      selectAfter(replacement);
      changed(instance);
    } else {
      replacement.textContent = range.collapsed ? "Heading" : range.toString();
      insertAtSelection(instance, replacement);
    }
  }

  function applyList(instance, kind) {
    const range = selectedRange(instance);
    const block = nearestBlock(instance, range);
    const list = document.createElement(kind);
    const item = document.createElement("li");
    if (block && block.parentElement === instance.canvas) {
      while (block.firstChild) item.appendChild(block.firstChild);
      list.appendChild(item);
      block.replaceWith(list);
      selectAfter(list);
      changed(instance);
    } else {
      item.textContent = range.collapsed ? "List item" : range.toString();
      list.appendChild(item);
      insertAtSelection(instance, list);
    }
  }

  function alignBlock(instance, alignment) {
    const block = nearestBlock(instance, selectedRange(instance));
    if (!block) {
      message(instance, "Place the cursor in a paragraph to change alignment.", "warning");
      return;
    }
    block.style.textAlign = alignment;
    changed(instance);
  }

  function insertTable(instance, rows = 2, columns = 2) {
    if (instance.readOnly || instance.sourceMode || !Number.isInteger(rows) || !Number.isInteger(columns) ||
        rows < 1 || columns < 1 || rows > 10 || columns > 10) return;
    const table = document.createElement("table");
    table.style.width = "100%";
    const body = table.createTBody();
    for (let rowNumber = 0; rowNumber < rows; rowNumber++) {
      const row = body.insertRow();
      for (let columnNumber = 0; columnNumber < columns; columnNumber++) {
        const cell = row.insertCell();
        cell.appendChild(document.createElement("br"));
      }
    }
    // Reuse the block-aware paste insertion path. insertAtSelection() would
    // put a table *inside* the current <p>, which is invalid HTML and made the
    // move/resize grips disappear until a later save/reload normalized it.
    const fragment = document.createDocumentFragment();
    fragment.appendChild(table);
    insertClipboardFragment(instance, fragment);
    instance.activeCell = table.rows[0].cells[0];
    selectTableControls(instance, table, 0);
  }

  function undoOrRedo(instance, delta) {
    if (instance.readOnly || instance.sourceMode || instance.isComposing) return;
    // Preserve the caret in the snapshot we are leaving. An unrecorded edit
    // is committed first, so Undo never silently loses the latest keystroke.
    if (delta < 0) remember(instance);
    else if (readHtml(instance) !== instance.history[instance.historyIndex]) {
      remember(instance); // New content invalidates the previous Redo branch.
      return;
    }
    rememberHistorySelection(instance);
    const next = instance.historyIndex + delta;
    if (next < 0 || next >= instance.history.length) return;
    clearSelectionBookmark(instance);
    instance.historyIndex = next;
    instance.canvas.innerHTML = instance.history[next];
    decorateCodeBlocks(instance);
    setImageSelection(instance, null);
    instance.lastRange = null;
    instance.activeCell = null;
    selectTableControls(instance, null);
    setDocumentControlsReadOnly(instance);
    instance.canvas.focus();
    restoreHistorySelection(instance);
    updateStats(instance);
    updateFormattingState(instance);
    updateContextTools(instance);
    queueEmit(instance);
  }

  function countInlineBytes(instance) {
    return Array.from(instance.canvas.querySelectorAll("img[src^='data:']"))
      .reduce((sum, image) => {
        const src = image.getAttribute("src") || "";
        const comma = src.indexOf(",");
        if (comma < 0) return sum;
        // Count decoded raster bytes, not the data: URL header or Base64 padding.
        // The saved-image budget is defined in bytes, not HTML character count.
        const length = src.length - comma - 1;
        const padding = src.endsWith("==") ? 2 : src.endsWith("=") ? 1 : 0;
        return sum + Math.max(0, Math.floor(length * 3 / 4) - padding);
      }, 0);
  }

  function asDataUrl(file, signal) {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      const cleanup = () => signal?.removeEventListener("abort", onAbort);
      const onAbort = () => {
        // Do not allow a stale FileReader to insert content after View/unmount.
        if (reader.readyState === FileReader.LOADING) reader.abort();
        cleanup();
        reject(new DOMException("Image insertion canceled.", "AbortError"));
      };
      if (signal?.aborted) { onAbort(); return; }
      signal?.addEventListener("abort", onAbort, { once: true });
      reader.onload = () => { cleanup(); resolve(String(reader.result || "")); };
      reader.onerror = () => { cleanup(); reject(new Error("Image could not be read.")); };
      reader.onabort = () => { cleanup(); reject(new DOMException("Image insertion canceled.", "AbortError")); };
      try { reader.readAsDataURL(file); }
      catch (error) { cleanup(); reject(error); }
    });
  }

  async function imageUrl(instance, file) {
    if (!instance.uploadUrl) {
      if (countInlineBytes(instance) + file.size > maxInlineImageBytes) {
        throw new Error("The inline image limit is 8 MB per document. Configure an upload URL for larger documents.");
      }
      return asDataUrl(file, instance.controller?.signal);
    }
    const form = new FormData();
    form.append("file", file, file.name);
    const headers = {};
    const uploadTarget = new URL(instance.uploadUrl, document.baseURI);
    if (uploadTarget.origin !== location.origin) throw new Error("Image upload must use the same origin.");
    const requestToken = instance.element.closest("form")?.querySelector('input[name="__RequestVerificationToken"]');
    if (requestToken) {
      headers.RequestVerificationToken = requestToken.value;
    } else {
      // A standalone Blazor component has no enclosing MVC/Razor form token.
      // The host's opt-in endpoint issues an antiforgery token using the same
      // origin and sets the matching cookie. No upload happens without a token.
      if (!instance.uploadToken) {
        const tokenResponse = await fetch(uploadTarget.href.replace(/\/$/, "") + "/token", {
          credentials: "same-origin", cache: "no-store", signal: instance.controller?.signal
        });
        if (!tokenResponse.ok) throw new Error("The image upload token could not be obtained.");
        const token = await tokenResponse.json();
        if (!token.requestToken) throw new Error("Image upload antiforgery token is missing.");
        instance.uploadToken = token.requestToken;
      }
      headers.RequestVerificationToken = instance.uploadToken;
    }
    const response = await fetch(uploadTarget.href, { method: "POST", body: form, headers,
      credentials: "same-origin", signal: instance.controller?.signal });
    if (!response.ok) throw new Error(`Image upload failed (${response.status}).`);
    const result = await response.json();
    const url = safeResourceUrl(result.url, true);
    if (!url || new URL(url, document.baseURI).origin !== location.origin)
      throw new Error("Image upload response must contain a same-origin URL.");
    return url;
  }

  // External drag-and-drop must use the same validated insertion path as the
  // file chooser and clipboard. Do not let the browser insert raw dropped HTML.
  function hasTransferType(dataTransfer, type) {
    return Array.from(dataTransfer?.types || []).includes(type);
  }

  function isSupportedDrop(dataTransfer) {
    return hasTransferType(dataTransfer, "Files") ||
      hasTransferType(dataTransfer, "text/html") ||
      hasTransferType(dataTransfer, "text/plain");
  }

  function placeDropCaret(instance, x, y) {
    let range = null;
    if (typeof document.caretRangeFromPoint === "function") {
      range = document.caretRangeFromPoint(x, y);
    } else if (typeof document.caretPositionFromPoint === "function") {
      const position = document.caretPositionFromPoint(x, y);
      if (position) {
        range = document.createRange();
        range.setStart(position.offsetNode, position.offset);
        range.collapse(true);
      }
    }
    if (!range || !instance.canvas.contains(range.startContainer)) return;
    range.collapse(true);
    const selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
    instance.lastRange = range.cloneRange();
  }

  function insertImageParagraph(instance, image) {
    const paragraph = document.createElement("p");
    paragraph.appendChild(image);
    const range = selectedRange(instance);
    const block = nearestBlock(instance, range);
    if (block && block.parentElement === instance.canvas &&
        ["P", "H1", "H2", "H3", "H4"].includes(block.tagName)) {
      // A paragraph cannot legally contain another paragraph. Split it at
      // the caret so save/restore and DOCX export preserve the image position.
      range.deleteContents();
      const tail = document.createRange();
      tail.setStart(range.startContainer, range.startOffset);
      tail.setEnd(block, block.childNodes.length);
      const right = block.cloneNode(false);
      right.appendChild(tail.extractContents());
      if (!block.hasChildNodes()) block.appendChild(document.createElement("br"));
      if (!right.hasChildNodes()) right.appendChild(document.createElement("br"));
      block.after(paragraph, right);
      selectAfter(paragraph);
      changed(instance);
    } else {
      insertAtSelection(instance, paragraph);
    }
  }

  function isCurrentImageInsert(instance, generation) {
    return instances.get(instance.element.id) === instance && instance.element.isConnected &&
      !instance.controller.signal.aborted && !instance.readOnly && !instance.sourceMode &&
      instance.imageInsertGeneration === generation;
  }

  // Keep the insertion caret from the initial user action. A file reader or
  // upload can resolve after the user focuses another editor or changes selection.
  function restoreImageInsertRange(instance, range) {
    if (!range || !instance.canvas.contains(range.commonAncestorContainer)) return false;
    instance.canvas.focus();
    const selection = window.getSelection();
    if (!selection) return false;
    selection.removeAllRanges();
    selection.addRange(range.cloneRange());
    instance.lastRange = range.cloneRange();
    return true;
  }

  async function addImages(instance, files) {
    if (instance.readOnly || instance.sourceMode) return;
    const images = Array.from(files).slice(0, 10);
    if (!images.length) return;
    const generation = instance.imageInsertGeneration;
    // Capture before the first await; preserve the order of multi-file insertion.
    let insertionRange = selectedRange(instance).cloneRange();
    const insertSize = instance.imageInsertSize?.value;
    let inserted = 0;
    for (const file of images) {
      if (!isCurrentImageInsert(instance, generation)) return;
      if (!supportedTypes.has(file.type) || file.size > maxImageBytes) {
        message(instance, `Skipped ${file.name}: only PNG/JPEG/GIF/WebP up to 2 MB are supported.`, "warning");
        continue;
      }
      try {
        const url = await imageUrl(instance, file);
        // A mode change, unmount or programmatic setHtml invalidates this job.
        if (!isCurrentImageInsert(instance, generation)) return;
        if (!restoreImageInsertRange(instance, insertionRange)) {
          message(instance, "The insertion position changed. Select a location and insert the image again.", "warning");
          return;
        }
        const image = document.createElement("img");
        image.setAttribute("src", url);
        image.setAttribute("alt", file.name);
        // Original is the default. CSS max-width:100% constrains display size.
        if (insertSize === "480") image.setAttribute("width", "480");
        insertImageParagraph(instance, image);
        setImageSelection(instance, image);
        const selection = window.getSelection();
        if (selection?.rangeCount) insertionRange = selection.getRangeAt(0).cloneRange();
        inserted++;
      } catch (error) {
        if (!isCurrentImageInsert(instance, generation) || error?.name === "AbortError") return;
        message(instance, String(error.message || error), "error");
      }
    }
    if (inserted && isCurrentImageInsert(instance, generation))
      message(instance, `${inserted} image(s) inserted.`, "success");
  }

  // Full-document deletion leaves a caret-ready <p><br></p>. Treat this as
  // an editor placeholder, not as content to keep before/after the next paste.
  // Do not remove genuine empty paragraphs elsewhere in the document.
  function emptyDocumentParagraph(instance, range) {
    const paragraph = instance.canvas.firstElementChild;
    return range.collapsed && instance.canvas.childNodes.length === 1 &&
      paragraph?.tagName === "P" && !paragraph.attributes.length &&
      paragraph.childNodes.length === 1 && paragraph.firstChild.tagName === "BR" &&
      (paragraph.contains(range.startContainer) || range.startContainer === instance.canvas)
        ? paragraph : null;
  }

  // Rich block pastes replace the entire placeholder paragraph. Put the caret
  // at the end of the pasted content, not at the canvas root (which could make
  // the next typed character an orphan text node outside any paragraph).
  function selectEndOfPastedContent(instance, last) {
    let block = last?.nodeType === Node.ELEMENT_NODE ? last : null;
    if (block?.matches("ul,ol")) block = block.querySelector("li:last-child");
    else if (block?.matches("table")) {
      const cells = block.querySelectorAll("td,th");
      block = cells[cells.length - 1] || null;
    } else if (block?.matches('blockquote')) block = block.lastElementChild || block;
    if (block && block.isContentEditable &&
        block.matches("p,div,h1,h2,h3,h4,li,td,th,blockquote")) {
      const range = document.createRange();
      range.selectNodeContents(block);
      if (block.childNodes.length === 1 && block.firstChild.tagName === "BR")
        range.setStart(block, 0);
      else range.collapse(false);
      const selection = window.getSelection();
      selection.removeAllRanges();
      selection.addRange(range);
    } else if (last) {
      selectAfter(last);
    }
    const selection = window.getSelection();
    if (selection?.rangeCount) instance.lastRange = selection.getRangeAt(0).cloneRange();
  }

  function insertPlainText(instance, text) {
    const fragment = document.createDocumentFragment();
    const lines = text.split(/\r\n|\r|\n/);
    lines.forEach((line, index) => {
      if (index) fragment.appendChild(document.createElement("br"));
      fragment.appendChild(document.createTextNode(line));
    });
    // Use a temporary marker because a DocumentFragment is empty after insertion.
    const marker = document.createTextNode("");
    fragment.appendChild(marker);
    instance.canvas.focus();
    const replaceDocument = selectionReplacesDocument(instance);
    const range = selectedRange(instance);
    const placeholder = text ? emptyDocumentParagraph(instance, range) : null;
    if (replaceDocument) {
      // A paste over Ctrl+A replaces the document, not just the selected text.
      // Range.deleteContents() leaves empty heading/paragraph wrappers behind.
      remember(instance);
      const paragraph = document.createElement("p");
      paragraph.appendChild(fragment);
      instance.canvas.replaceChildren(paragraph);
    } else if (placeholder) placeholder.replaceChildren(fragment);
    else {
      range.deleteContents();
      range.insertNode(fragment);
    }
    selectAfter(marker);
    changed(instance);
  }

  // Word, Google Docs and web-page HTML can be entire documents or Windows
  // CF_HTML data with StartFragment markers. Only the selected fragment belongs
  // in the editor: never paste header metadata or a source page's <head>.
  function clipboardFragmentHtml(html) {
    if (typeof html !== "string" || html.length > maxDocumentBytes) return "";
    const start = /<!--\s*StartFragment\s*-->/i.exec(html);
    const end = /<!--\s*EndFragment\s*-->/i.exec(html);
    if (start && end && end.index >= start.index + start[0].length)
      return html.slice(start.index + start[0].length, end.index);
    // Some native CF_HTML providers omit fragment markers but keep the header.
    if (/^Version:\s*\d/i.test(html)) {
      const firstTag = html.search(/<(?:html|body|p|div|table|h[1-6]|ul|ol|pre)\b/i);
      return firstTag >= 0 ? html.slice(firstTag) : "";
    }
    return html;
  }

  // Translate only a few standard clipboard formatting declarations into
  // semantic elements supported by the existing editor/viewer/export policies.
  // No Office mso-* styles, classes, handlers, or arbitrary CSS are retained.
  function normalizeClipboardFormatting(html) {
    // Ordinary HTML without inline styles needs only the final sanitizer pass.
    if (!/<font\b|\bstyle\s*=/i.test(html)) return html;
    const template = document.createElement("template");
    template.innerHTML = html;
    for (const element of Array.from(template.content.querySelectorAll("font"))) {
      const span = document.createElement("span");
      const face = (element.getAttribute("face") || "").replace(/^['"]|['"]$/g, "");
      if (allowedFonts.has(face)) span.style.fontFamily = face;
      const color = cleanColor(element.getAttribute("color"));
      if (color) span.style.color = color;
      const legacySize = Number(element.getAttribute("size"));
      const sizes = [0, 10, 13, 16, 18, 24, 32, 48];
      if (Number.isInteger(legacySize) && legacySize >= 1 && legacySize <= 7)
        span.style.fontSize = `${sizes[legacySize]}px`;
      while (element.firstChild) span.appendChild(element.firstChild);
      element.replaceWith(span);
    }
    for (const element of Array.from(template.content.querySelectorAll("[style]"))) {
      // Re-wrap children rather than carrying CSS properties through the
      // sanitizer. It also works for Google Docs <span> and Word <p>.
      const weight = element.style.fontWeight.trim().toLowerCase();
      const italic = element.style.fontStyle.trim().toLowerCase();
      const decoration = (element.style.textDecorationLine || element.style.textDecoration || "").toLowerCase();
      const wrappers = [];
      if (weight === "bold" || weight === "bolder" || /^(?:[6-9]00)$/.test(weight)) wrappers.push("strong");
      if (italic === "italic" || italic === "oblique") wrappers.push("em");
      if (/(?:^|\s)underline(?:\s|$)/.test(decoration)) wrappers.push("u");
      if (/(?:^|\s)line-through(?:\s|$)/.test(decoration)) wrappers.push("s");
      if (!wrappers.length) continue;
      let content = document.createDocumentFragment();
      while (element.firstChild) content.appendChild(element.firstChild);
      for (const tag of wrappers) {
        const wrapper = document.createElement(tag);
        wrapper.appendChild(content);
        content = document.createDocumentFragment();
        content.appendChild(wrapper);
      }
      element.appendChild(content);
    }
    return template.innerHTML;
  }

  const clipboardBlockTags = new Set(["P", "DIV", "H1", "H2", "H3", "H4", "BLOCKQUOTE", "PRE", "UL", "OL", "TABLE", "HR"]);

  // A rich fragment inserted into the middle of <p> must be a sibling of
  // that paragraph, not a nested <p>/<table>. Keep surrounding text intact.
  // Restrict splitting to a root-level text block; within tables and lists the
  // browser's existing insertion behavior is retained for compatibility.
  function insertClipboardFragment(instance, fragment) {
    const marker = document.createTextNode("");
    fragment.appendChild(marker);
    instance.canvas.focus();
    const replaceDocument = selectionReplacesDocument(instance);
    const range = selectedRange(instance);
    const block = nearestBlock(instance, range);
    const hasBlock = Array.from(fragment.childNodes).some(node =>
      node.nodeType === Node.ELEMENT_NODE && clipboardBlockTags.has(node.tagName));
    if (replaceDocument) {
      // Replacing the entire selection must also remove the old block wrappers.
      // Otherwise Range.deleteContents() leaves an empty <h2> and/or <p>.
      remember(instance);
      if (hasBlock) {
        marker.remove();
        const last = Array.from(fragment.childNodes).reverse().find(node =>
          node.nodeType === Node.ELEMENT_NODE ||
          (node.nodeType === Node.TEXT_NODE && node.textContent.trim()));
        instance.canvas.replaceChildren(fragment);
        selectEndOfPastedContent(instance, last);
      } else {
        const paragraph = document.createElement("p");
        paragraph.appendChild(fragment);
        instance.canvas.replaceChildren(paragraph);
        selectAfter(marker);
      }
      instance.activeCell = null;
      setImageSelection(instance, null);
      selectTableControls(instance, null);
      updateContextTools(instance);
      changed(instance);
      return;
    }
    const placeholder = emptyDocumentParagraph(instance, range);
    if (placeholder) {
      if (hasBlock) {
        // Keep the actual clipboard paragraphs (including intentional blank
        // lines), but remove the one placeholder introduced by Reset/Delete.
        marker.remove();
        const last = Array.from(fragment.childNodes).reverse().find(node =>
          node.nodeType === Node.ELEMENT_NODE ||
          (node.nodeType === Node.TEXT_NODE && node.textContent.trim()));
        placeholder.replaceWith(fragment);
        selectEndOfPastedContent(instance, last);
      } else {
        // Inline rich HTML belongs *inside* the original empty paragraph.
        // Replace its <br> rather than leaving an extra line at the end.
        placeholder.replaceChildren(fragment);
        selectAfter(marker);
      }
      changed(instance);
      return;
    }
    const canSplit = hasBlock && block?.parentElement === instance.canvas &&
      ["P", "DIV", "H1", "H2", "H3", "H4"].includes(block.tagName) &&
      block.contains(range.endContainer);
    range.deleteContents();
    if (canSplit && block.isConnected && block.contains(range.startContainer)) {
      const tailRange = document.createRange();
      tailRange.setStart(range.startContainer, range.startOffset);
      tailRange.setEnd(block, block.childNodes.length);
      const right = block.cloneNode(false);
      right.appendChild(tailRange.extractContents());
      if (!right.hasChildNodes()) right.appendChild(document.createElement("br"));
      if (!block.hasChildNodes()) block.appendChild(document.createElement("br"));
      block.after(fragment, right);
    } else {
      range.insertNode(fragment);
    }
    selectAfter(marker);
    changed(instance);
  }

  // Paste only approved HTML. Plain text is the safe fallback for empty or
  // unsupported rich fragments; do not drop clipboard content silently.
  function pasteCleanContent(instance, html, plainText) {
    if (typeof plainText === "string" && new Blob([plainText]).size > maxDocumentBytes) {
      message(instance, "The pasted text exceeds the 16 MB document limit.", "warning");
      return;
    }
    if (typeof html === "string" && new Blob([html]).size > maxDocumentBytes) {
      if (plainText) {
        message(instance, "Rich clipboard content is too large; inserting plain text.", "warning");
        return insertPlainText(instance, plainText);
      }
      message(instance, "The pasted HTML exceeds the 16 MB document limit.", "warning");
      return;
    }
    const fragmentHtml = clipboardFragmentHtml(html);
    const clean = fragmentHtml ? sanitizeHtml(normalizeClipboardFormatting(fragmentHtml)) : "";
    if (!clean || !clean.trim()) return insertPlainText(instance, plainText || "");
    const template = document.createElement("template");
    template.innerHTML = clean;
    // A rich clipboard fragment containing only stripped active content must
    // never execute; if ordinary plain text is available, use that instead.
    if (plainText && !template.content.textContent?.trim() &&
        !template.content.querySelector("img,table,input,hr"))
      return insertPlainText(instance, plainText);
    insertClipboardFragment(instance, template.content);
  }

  function clearFormatting(instance) {
    const range = selectedRange(instance);
    if (range.collapsed) return message(instance, "Select the text whose formatting you want to clear.", "warning");
    const text = range.toString();
    range.deleteContents();
    const node = document.createTextNode(text);
    range.insertNode(node);
    // A plain text node still inherits styles from surrounding <strong>,
    // <span>, etc. Split each inline formatting ancestor around the selection.
    const formatted = new Set(["STRONG", "B", "EM", "I", "U", "S", "SUP", "SUB", "SPAN", "A", "CODE"]);
    let parent = node.parentElement;
    while (parent && parent !== instance.canvas && formatted.has(parent.tagName)) {
      const left = parent.cloneNode(false);
      const right = parent.cloneNode(false);
      while (parent.firstChild && parent.firstChild !== node) left.appendChild(parent.firstChild);
      while (node.nextSibling) right.appendChild(node.nextSibling);
      if (left.childNodes.length) parent.before(left);
      parent.before(node);
      if (right.childNodes.length) parent.before(right);
      const outer = parent.parentElement;
      parent.remove();
      parent = outer;
    }
    selectAfter(node);
    changed(instance);
  }

  function insertSpecial(instance, character) {
    // Only characters listed in the toolbar can be inserted through this action.
    if (!instance.characterPanel?.querySelector(`button[data-insert-character="${CSS.escape(character)}"]`)) return;
    insertAtSelection(instance, document.createTextNode(character));
    instance.characterPanel.hidden = true;
  }

  function insertRule(instance, pageBreak) {
    const rule = document.createElement("hr");
    if (pageBreak) rule.setAttribute("data-azunt-page-break", "true");
    const range = selectedRange(instance);
    const block = nearestBlock(instance, range);
    if (block && block.parentElement === instance.canvas && ["P", "DIV", "H1", "H2", "H3", "H4"].includes(block.tagName)) {
      // Split a root-level paragraph around the caret. A page break is a
      // separate block, never an invalid <p><hr></p> nested structure.
      range.deleteContents();
      const tailRange = document.createRange();
      tailRange.setStart(range.startContainer, range.startOffset);
      tailRange.setEnd(block, block.childNodes.length);
      const right = block.cloneNode(false);
      right.appendChild(tailRange.extractContents());
      if (!right.hasChildNodes()) right.appendChild(document.createElement("br"));
      if (!block.hasChildNodes()) block.appendChild(document.createElement("br"));
      block.after(rule, right);
      const caret = document.createRange();
      caret.selectNodeContents(right);
      caret.collapse(true);
      const selection = window.getSelection();
      selection.removeAllRanges(); selection.addRange(caret);
      instance.lastRange = caret.cloneRange();
      changed(instance);
    } else {
      // A rule must not be nested in a table cell or a list item. When the
      // cursor is inside a complex root-level structure, insert after it.
      let root = range.startContainer.nodeType === Node.ELEMENT_NODE
        ? range.startContainer : range.startContainer.parentElement;
      while (root && root.parentElement !== instance.canvas) root = root.parentElement;
      if (root && ["TABLE", "UL", "OL"].includes(root.tagName)) {
        const next = document.createElement("p");
        next.appendChild(document.createElement("br"));
        root.after(rule, next);
        const caret = document.createRange(); caret.selectNodeContents(next); caret.collapse(true);
        const selection = window.getSelection(); selection.removeAllRanges(); selection.addRange(caret);
        instance.lastRange = caret.cloneRange();
        changed(instance);
      } else {
        insertAtSelection(instance, rule);
      }
    }
    message(instance, pageBreak ? "Page break inserted." : "Horizontal line inserted.", "success");
  }

  function toggleFullscreen(instance, enable = !instance.element.classList.contains("az-azunt-editor-fullscreen")) {
    // Keep Compact's persistent control and Classic's grouped button in sync.
    // Preserve the control to return keyboard focus after Esc exits fullscreen.
    if (enable) {
      // Only one fullscreen editor may own the viewport at a time.
      for (const other of instances.values()) {
        if (other !== instance && other.element.classList.contains("az-azunt-editor-fullscreen"))
          toggleFullscreen(other, false);
      }
      const active = document.activeElement;
      const fallback = instance.element.dataset.toolbarMode === "classic"
        ? instance.toolbar.querySelector('button[data-command="fullscreen"]')
        : instance.toolbar.querySelector('button[data-quick-command="fullscreen"]');
      instance.fullscreenReturnFocus = instance.element.contains(active) && active?.matches?.('button[data-command="fullscreen"],button[data-quick-command="fullscreen"]')
        ? active : fallback;
    }
    instance.element.classList.toggle("az-azunt-editor-fullscreen", enable);
    instance.toolbar.querySelectorAll('button[data-command="fullscreen"],button[data-quick-command="fullscreen"]').forEach(button => {
      button.setAttribute("aria-pressed", String(enable));
      button.setAttribute("aria-label", enable ? "Exit fullscreen" : "Enter fullscreen");
      button.title = enable ? "Exit fullscreen (Esc)" : "Fullscreen (Esc to exit)";
    });
    if (enable) instance.canvas.focus();
    else {
      if (instance.fullscreenReturnFocus?.isConnected) instance.fullscreenReturnFocus.focus();
      instance.fullscreenReturnFocus = null;
    }
  }

  function findNodes(instance) {
    const walker = document.createTreeWalker(instance.canvas, NodeFilter.SHOW_TEXT);
    const nodes = [];
    while (walker.nextNode()) if (walker.currentNode.textContent) nodes.push(walker.currentNode);
    return nodes;
  }

  function findMatches(instance, query) {
    if (!query) return [];
    const needle = query.toLocaleLowerCase();
    const matches = [];
    for (const node of findNodes(instance)) {
      const text = node.textContent.toLocaleLowerCase();
      let pos = 0;
      while (pos <= text.length - needle.length) {
        const start = text.indexOf(needle, pos);
        if (start < 0) break;
        matches.push({ node, start, end: start + needle.length });
        pos = start + Math.max(needle.length, 1);
        if (matches.length > 5000) break;
      }
      if (matches.length > 5000) break;
    }
    return matches;
  }

  function selectMatch(instance, match) {
    const range = document.createRange();
    range.setStart(match.node, match.start);
    range.setEnd(match.node, match.end);
    const selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
    instance.lastRange = range.cloneRange();
    match.node.parentElement?.scrollIntoView({ block: "nearest" });
  }

  function findNext(instance) {
    if (instance.sourceMode) return message(instance, "Apply HTML source before searching text.", "warning");
    const query = instance.findQuery?.value || "";
    if (!query) return message(instance, "Enter a search term.", "warning");
    const matches = findMatches(instance, query);
    const selection = window.getSelection();
    const active = selection?.rangeCount ? selection.getRangeAt(0) : null;
    let chosen = 0;
    if (active && instance.canvas.contains(active.endContainer)) {
      const after = matches.findIndex(m => {
        const range = document.createRange();
        range.setStart(m.node, m.start);
        return range.compareBoundaryPoints(Range.START_TO_END, active) > 0;
      });
      chosen = after < 0 ? 0 : after;
    }
    if (!matches.length) {
      instance.findStatus.textContent = "No matches";
      return;
    }
    selectMatch(instance, matches[chosen]);
    instance.findStatus.textContent = `${chosen + 1} of ${matches.length}`;
  }

  function selectionMatches(instance, query) {
    const selection = window.getSelection();
    if (!selection?.rangeCount || !instance.canvas.contains(selection.anchorNode)) return null;
    const range = selection.getRangeAt(0);
    if (range.collapsed || range.toString().toLocaleLowerCase() !== query.toLocaleLowerCase()) return null;
    return range;
  }

  function replaceMatch(instance) {
    if (instance.readOnly || instance.sourceMode) return;
    const query = instance.findQuery?.value || "";
    if (!query) return;
    let range = selectionMatches(instance, query);
    if (!range) { findNext(instance); range = selectionMatches(instance, query); }
    if (!range) return;
    const replacement = instance.findReplacement.value;
    range.deleteContents();
    const node = document.createTextNode(replacement);
    range.insertNode(node);
    selectAfter(node);
    changed(instance);
    findNext(instance);
  }

  function replaceAllMatches(instance) {
    if (instance.readOnly || instance.sourceMode) return;
    const query = instance.findQuery?.value || "";
    if (!query) return;
    const replacement = instance.findReplacement.value;
    // Only replace within individual text nodes: preserve surrounding links,
    // table cells and inline formatting. This intentionally does not cross tags.
    let count = 0;
    for (const node of findNodes(instance)) {
      const source = node.textContent;
      const lower = source.toLocaleLowerCase();
      const needle = query.toLocaleLowerCase();
      let cursor = 0;
      let result = "";
      while (cursor < source.length) {
        const index = lower.indexOf(needle, cursor);
        if (index < 0) break;
        result += source.slice(cursor, index) + replacement;
        cursor = index + needle.length;
        count++;
        if (count > 5000) break;
      }
      if (cursor) node.textContent = result + source.slice(cursor);
      if (count > 5000) break;
    }
    if (count) { instance.lastRange = null; changed(instance); }
    instance.findStatus.textContent = `${count} replaced`;
  }

  // Reuse the actual built-in document CSS instead of maintaining a second
  // set of font, spacing and table presets for printing. Only the editor's
  // document rules are copied, not host CSS, toolbar rules or dark UI media.
  function sharedPrintDocumentCss() {
    const baseSelectors = new Set([
      ".az-azunt-editor", ".az-azunt-editor *", ".az-azunt-editor [hidden]",
      ".az-azunt-editor-canvas", ".az-azunt-editor-canvas > :first-child",
      ".az-azunt-editor-canvas h2", ".az-azunt-editor-canvas p",
      ".az-azunt-editor .az-azunt-editor-canvas :is(p, h1, h2, h3, h4, h5, h6, li, blockquote, th, td)",
      ".az-azunt-editor-canvas a", ".az-azunt-editor-canvas img",
      ".az-azunt-editor-canvas table", ".az-azunt-editor-canvas th, .az-azunt-editor-canvas td",
      ".az-azunt-editor-canvas th",
      '.az-azunt-editor-canvas input[type="checkbox"], .az-azunt-editor-canvas input[type="radio"]',
      '.az-azunt-editor-canvas input[type="checkbox"]:disabled, .az-azunt-editor-canvas input[type="radio"]:disabled',
      ".az-azunt-editor .az-code-block", ".az-azunt-editor .az-code-block > code"
    ]);
    for (const sheet of Array.from(document.styleSheets)) {
      let rules;
      try { rules = Array.from(sheet.cssRules); } catch { continue; }
      // Identify the bundled stylesheet (also works with inline test fixtures
      // or same-origin bundlers). Do not read unrelated application sheets.
      if (!rules.some(rule => rule.selectorText === '.az-azunt-editor[data-content-rendering="compact-report"]'
          && rule.style?.getPropertyValue("--azunt-document-size"))) continue;
      if (!rules.some(rule => rule.selectorText === ".az-azunt-editor-canvas")) continue;
      return rules.filter(rule => {
        const selector = rule.selectorText || "";
        return baseSelectors.has(selector) ||
          (selector.startsWith(".az-azunt-editor[data-content-rendering=") ||
           selector.startsWith(".az-azunt-editor:is([data-content-rendering="));
      }).map(rule => rule.cssText).join("\n");
    }
    return "";
  }

  function printDocument(instance, previewOnly = false) {
    const html = readHtml(instance);
    const sharedCss = sharedPrintDocumentCss();
    const rendering = getContentRendering(instance.element.id);
    const custom = instance.documentStyles.get(instance.customDocumentStyle);
    const renderingLabel = custom?.name || { Standard: "Standard", BrowserDefault: "Browser Default",
      CompactReport: "Compact Report", BusinessDocument: "Business Document", Minimal: "Minimal" }[rendering];
    const paper = allowedPageSizes.has(instance.page.paper) ? instance.page.paper : "A4";
    const orientation = allowedOrientations.has(instance.page.orientation) ? instance.page.orientation : "portrait";
    const margin = allowedMargins[instance.page.margin] || allowedMargins.normal;
    // Open synchronously as part of the user's click to avoid popup blockers.
    const win = window.open("", "_blank", "width=1000,height=850");
    if (!win) return message(instance, "Please allow popups to preview or print.", "warning");
    win.opener = null;
    const [width, height] = { A4: [210, 297], Letter: [215.9, 279.4], Legal: [215.9, 355.6] }[paper];
    const [pageWidth, pageHeight] = orientation === "landscape" ? [height, width] : [width, height];
    const style = `@page{size:${paper} ${orientation};margin:${margin}}` +
      `*{box-sizing:border-box}body{font:12pt/1.5 Arial,sans-serif;color:#1e293b;margin:0;background:#e9edf2}` +
      `#azunt-print-controls{position:sticky;top:0;padding:12px;background:#e2e8f0;z-index:10;display:flex;align-items:center;gap:12px;flex-wrap:wrap}` +
      `#azunt-print-controls button,#azunt-print-controls select{padding:8px 12px;cursor:pointer;font:inherit}` +
      `.az-azunt-print-page{margin:15px auto;padding:${margin};background:#fff;width:${pageWidth}mm;min-height:${pageHeight}mm;box-shadow:0 1px 12px #ccc;overflow-wrap:break-word}` +
      `.az-azunt-print-page:not([data-print-profile="document"]) img{max-width:100%;height:auto}` +
      `.az-azunt-print-page table{break-inside:auto}` +
      `.az-azunt-print-page:not([data-print-profile="document"]) table{border-collapse:collapse;width:100%}` +
      `.az-azunt-print-page thead{display:table-header-group}.az-azunt-print-page tfoot{display:table-footer-group}` +
      `.az-azunt-print-page tr{break-inside:avoid;page-break-inside:avoid}` +
      `.az-azunt-print-page hr[data-azunt-page-break="true"]{break-before:page;page-break-before:always;border:0;margin:0;height:0}` +
      `.az-azunt-print-page:not([data-print-profile="document"]) :is(th,td){border:1px solid #aaa;padding:6px}` +
      `.az-azunt-print-page[data-print-profile="compact"]{font-size:10pt;line-height:1.2}` +
      `.az-azunt-print-page[data-print-profile="compact"] :is(td,th){font-size:9pt;padding:3px;border-width:.5px}` +
      `.az-azunt-print-page[data-print-profile="comfortable"]{font-size:14pt;line-height:1.6}` +
      `.az-azunt-print-page[data-print-profile="comfortable"] :is(td,th){font-size:12pt;padding:8px}` +
      sharedCss +
      // Paper margins replace the editor's canvas padding. Exclude editor UI
      // chrome and scrolling, but retain the selected document typography.
      `\n.az-azunt-print-page .az-azunt-editor{border:0;border-radius:0;max-width:none}` +
      `.az-azunt-print-page .az-azunt-editor > .az-azunt-editor-canvas{padding:0;overflow:visible;min-height:0;display:flow-root}` +
      `.az-azunt-print-page pre{white-space:pre-wrap;overflow-wrap:anywhere}` +
      `.az-azunt-print-page .az-azunt-editor .az-code-block{display:block;padding:15px;overflow:visible;white-space:pre-wrap;overflow-wrap:anywhere}` +
      `.az-azunt-print-page .az-azunt-editor .az-code-block > code{display:block;min-width:0;padding:0;white-space:pre-wrap;overflow-wrap:anywhere}` +
      `.az-azunt-print-page hr[data-azunt-page-break="true"]::after{content:none}` +
      `@media print{body{background:#fff}#azunt-print-controls{display:none}.az-azunt-print-page{margin:0;padding:0;width:auto;min-height:0;box-shadow:none}}`;
    win.document.open();
    win.document.write("<!doctype html><html><head><meta charset='utf-8'><title>Azunt Editor — Print Preview</title></head><body></body></html>");
    win.document.close();
    const styles = win.document.createElement("style");
    styles.textContent = style;
    win.document.head.appendChild(styles);
    const controls = win.document.createElement("header");
    controls.id = "azunt-print-controls";
    const button = win.document.createElement("button");
    button.type = "button";
    button.textContent = "Print / Save as PDF";
    button.disabled = true;
    controls.appendChild(button);
    const label = win.document.createElement("label");
    label.textContent = "Layout: ";
    const select = win.document.createElement("select");
    select.setAttribute("aria-label", "Print layout");
    if (sharedCss) {
      const option = win.document.createElement("option");
      option.value = "document"; option.textContent = `Match document (${renderingLabel})`;
      select.appendChild(option);
    }
    for (const name of ["Standard", "Compact", "Comfortable"]) {
      const option = win.document.createElement("option");
      option.value = name.toLowerCase(); option.textContent = name;
      select.appendChild(option);
    }
    label.appendChild(select); controls.appendChild(label);
    controls.appendChild(win.document.createTextNode(`${paper} · ${orientation} · ${instance.page.margin} margins`));
    const status = win.document.createElement("span");
    status.setAttribute("role", "status"); status.textContent = "Preparing images…";
    controls.appendChild(status);
    const page = win.document.createElement("main");
    page.className = "az-azunt-print-page";
    const documentRoot = win.document.createElement("div");
    const content = win.document.createElement("div");
    content.innerHTML = html; // Already restricted by sanitizeHtml; do not inject raw user HTML.
    documentRoot.appendChild(content);
    page.appendChild(documentRoot);
    const applyProfile = () => {
      const match = select.value === "document" && !!sharedCss;
      page.dataset.printProfile = match ? "document" : select.value;
      documentRoot.className = match ? "az-azunt-editor" : "";
      content.className = match ? "az-azunt-editor-canvas" : "";
      if (match && custom) documentRoot.dataset.contentRendering = "custom-document";
      else if (match && contentRenderings[rendering]) documentRoot.dataset.contentRendering = contentRenderings[rendering];
      else delete documentRoot.dataset.contentRendering;
      applyCustomStyle(documentRoot, match ? custom : null);
    };
    select.value = sharedCss ? "document" : "standard";
    applyProfile();
    win.document.body.append(controls, page);
    select.addEventListener("change", applyProfile);
    const images = Array.from(page.querySelectorAll("img"));
    const ready = images.map(image => image.complete ? Promise.resolve() :
      new Promise(resolve => {
        image.addEventListener("load", resolve, { once: true });
        image.addEventListener("error", resolve, { once: true });
      }));
    if (win.document.fonts) ready.push(win.document.fonts.ready.catch(() => {}));
    // A stalled remote image must not leave Print permanently disabled.
    let timer;
    const timeout = new Promise(resolve => { timer = window.setTimeout(resolve, 10000); });
    const prepared = Promise.race([Promise.all(ready), timeout]).then(() => {
      window.clearTimeout(timer);
      if (win.closed) return false;
      const missing = images.some(image => !image.complete || !image.naturalWidth);
      status.textContent = [missing ? "Some images could not load. Check the preview before printing." : "",
        !sharedCss ? "Document styles unavailable. Review the Standard print layout before printing." : ""].filter(Boolean).join(" ");
      button.disabled = false;
      return !missing && !!sharedCss;
    });
    button.addEventListener("click", () => { if (!win.closed) { win.focus(); win.print(); } });
    // On image failure show the preview instead of silently printing incomplete content.
    if (!previewOnly) prepared.then(ok => { if (ok && !win.closed) { win.focus(); win.print(); } });
  }

  function runSelect(instance, field, value) {
    if (field === "viewHeightMode" || field === "viewZoom") {
      const id = instance.element.id, before = getViewOptions(id);
      setViewOptions(id, field === "viewHeightMode" ? value : before.heightMode, before.height,
        field === "viewZoom" ? Number(value) : before.zoomPercent);
      const options = getViewOptions(id);
      if (before.heightMode !== options.heightMode || before.zoomPercent !== options.zoomPercent) {
        instance.element.dispatchEvent(new CustomEvent("azunt:view-options-changed", { bubbles: true, detail: options }));
        instance.dotNetReference?.invokeMethodAsync("OnViewOptionsChanged", options.heightMode, options.zoomPercent)
          .catch(() => message(instance, "Could not notify the host of the view change.", "error"));
      }
      return;
    }
    if (field === "documentStyle") {
      const id = instance.element.id;
      const before = getDocumentStyleId(id);
      setDocumentStyle(id, value);
      const styleId = getDocumentStyleId(id);
      const rendering = getContentRendering(id);
      if (before !== styleId) {
        instance.element.dispatchEvent(new CustomEvent("azunt:document-style-changed", {
          bubbles: true, detail: { styleId, rendering }
        }));
        if (!styleId.startsWith("custom:")) instance.element.dispatchEvent(new CustomEvent("azunt:content-rendering-changed", {
          bubbles: true, detail: { rendering }
        }));
        if (instance.dotNetReference) {
          const customApi = instance.documentStyles.size || before.startsWith("custom:") || instance.element.hasAttribute("data-document-style-id");
          const call = customApi ? instance.dotNetReference.invokeMethodAsync("OnDocumentStyleChanged", styleId, rendering)
            : instance.dotNetReference.invokeMethodAsync("OnContentRenderingChanged", rendering);
          call.catch(() => message(instance, "Could not notify the host of the document style change.", "error"));
        }
      }
      return;
    }
    if (["paper", "orientation", "margin"].includes(field)) {
      instance.page[field] = value;
      return;
    }
    if (instance.readOnly) return;
    if (instance.sourceMode) return message(instance, "Apply HTML source before using formatting tools.", "warning");
    if (field === "tableBorderColor" || field === "tableBorderWidth") {
      if (value) updateTableBorders(instance, field, value);
      return;
    }
    if (field === "imageInsertSize") return;
    if (field === "imageAlign") {
      if (value) alignImage(instance, value);
      return;
    }
    if (!value) return;
    if (field === "fontFamily" && allowedFonts.has(value)) return formatSpan(instance, "fontFamily", value);
    if (field === "fontSize" && /^\d{1,2}$/.test(value) && Number(value) >= 8 && Number(value) <= 72)
      return formatSpan(instance, "fontSize", `${value}px`);
    if (["color", "backgroundColor"].includes(field) && cleanColor(value))
      return formatSpan(instance, field, cleanColor(value));
  }

  function runCommand(instance, command) {
    if (command === "fullscreen") return toggleFullscreen(instance);
    if (command === "find") {
      if (!instance.findPanel) return;
      instance.findPanel.hidden = false;
      instance.findQuery.focus();
      instance.findQuery.select();
      return;
    }
    if (command === "preview") return printDocument(instance, true);
    if (command === "print") return printDocument(instance, false);
    if (instance.readOnly) return;
    if (command === "source") return toggleSource(instance);
    if (command === "special-chars") {
      if (instance.characterPanel) instance.characterPanel.hidden = !instance.characterPanel.hidden;
      return;
    }
    if (instance.sourceMode) return message(instance, "Apply HTML source before using formatting tools.", "warning");
    if (formControlKinds.has(command)) return insertFormControl(instance, command);
    if (command === "clear-format") return clearFormatting(instance);
    if (command === "page-break") return insertRule(instance, true);
    if (command === "horizontal-rule") return insertRule(instance, false);
    if (command === "datetime") {
      if (instance.datePanel) instance.datePanel.hidden = !instance.datePanel.hidden;
      return;
    }
    if (command === "code-block") return openCodePanel(instance);
    if (command.startsWith("code-block-") && codeLanguages.includes(command.slice("code-block-".length)))
      return openCodePanel(instance, command.slice("code-block-".length));
    if (command === "signature") return showSignature(instance);
    if (command === "templates") {
      refreshTemplateOptions(instance);
      if (instance.templatePanel) instance.templatePanel.hidden = !instance.templatePanel.hidden;
      return;
    }
    if (command === "image-delete") return deleteImage(instance);
    if (command === "image-original") return restoreOriginalImageSize(instance);
    if (command.startsWith("table-") && command !== "table") return modifyTable(instance, command);
    if (command === "link") return openLinkPanel(instance);
    if (command === "unlink") return removeLink(instance);
    instance.canvas.focus();
    if (["bold", "italic", "underline", "strike", "sup", "sub"].includes(command)) {
      return inlineStyle(instance, { bold: "strong", italic: "em", underline: "u", strike: "s", sup: "sup", sub: "sub" }[command]);
    }
    if (command === "h2" || command === "p") return changeBlock(instance, command);
    if (command === "ul" || command === "ol") return applyList(instance, command);
    if (["left", "center", "right", "justify"].includes(command)) return alignBlock(instance, command);
    if (command === "indent") return adjustIndent(instance, 24);
    if (command === "outdent") return adjustIndent(instance, -24);
    if (command === "table") return insertTable(instance);
    if (command === "image") return instance.images.click();
    if (command === "undo") return undoOrRedo(instance, -1);
    if (command === "redo") return undoOrRedo(instance, 1);
  }

  // Compact toolbar. The original controls are moved into the open menu,
  // rather than copied, so selects and color inputs retain their existing state.
  const menuGroups = Object.freeze({
    file: ["Document"], edit: ["Writing tools"], view: ["View options", "Document"],
    insert: ["Link", "Image"], format: ["Document style", "Text formatting", "Font and color", "Paragraph"],
    table: ["Table"], tablepicker: [], image: ["Image"], link: ["Link"], controls: [], codepicker: [],
    tools: ["Writing tools"], export: [],
    more: ["View options", "Document style", "Text formatting", "Font and color", "Paragraph", "Link", "Table", "Image", "Writing tools", "Document"]
  });
  const menuNames = new Set(Object.keys(menuGroups));
  const toggleCommands = new Set(["bold", "italic", "underline", "strike", "sup", "sub", "ul", "ol"]);

  // Read the caret's formatting without modifying the document or HTML value.
  // Keep the last editor selection while the keyboard focus is on a toolbar control.
  function updateFormattingState(instance) {
    if (!instance.toolbar || !instance.canvas) return;
    const selection = window.getSelection();
    const inCanvas = selection?.rangeCount && instance.canvas.contains(selection.anchorNode);
    let node = inCanvas ? selection.anchorNode : null;
    if (!node && instance.toolbar.contains(document.activeElement) &&
        instance.lastRange && instance.canvas.contains(instance.lastRange.startContainer))
      node = instance.lastRange.startContainer;
    if (node && node.nodeType !== Node.ELEMENT_NODE) node = node.parentElement;
    const ancestors = [];
    for (let current = node; current && current !== instance.canvas; current = current.parentElement)
      ancestors.push(current);
    const tags = new Set(ancestors.map(element => element.tagName));
    const isBold = tags.has("STRONG") || tags.has("B") || ancestors.some(element =>
      /^(bold|bolder|[6-9]00)$/i.test(element.style?.fontWeight || ""));
    const isItalic = tags.has("EM") || tags.has("I") || ancestors.some(element => element.style?.fontStyle === "italic");
    const isUnderline = tags.has("U") || ancestors.some(element => /underline/i.test(element.style?.textDecoration || ""));
    const isStrike = tags.has("S") || tags.has("STRIKE") || tags.has("DEL") ||
      ancestors.some(element => /line-through/i.test(element.style?.textDecoration || ""));
    const active = {
      bold: isBold, italic: isItalic, underline: isUnderline, strike: isStrike,
      sup: tags.has("SUP"), sub: tags.has("SUB"), ul: tags.has("UL"), ol: tags.has("OL")
    };
    // A selection in one editor never affects the visible state of another editor.
    instance.toolbar.querySelectorAll("button[data-command],button[data-quick-command]").forEach(button => {
      const command = button.dataset.command || button.dataset.quickCommand;
      if (toggleCommands.has(command)) button.setAttribute("aria-pressed", String(Boolean(node && active[command])));
    });

    // Inspect only explicit inline formatting. A computed fallback would incorrectly
    // label unformatted text with the host page's default font and foreground color.
    const inlineValue = property => ancestors.map(el => el.style?.[property] || "").find(Boolean) || "";
    const family = inlineValue("fontFamily").replace(/["']/g, "").trim();
    const font = Array.from(allowedFonts).find(value => value.toLowerCase() === family.toLowerCase()) || "";
    const size = /^(\d{1,2})px$/i.exec(inlineValue("fontSize"))?.[1] || "";
    const textColor = cleanColor(inlineValue("color"));
    const highlightColor = cleanColor(inlineValue("backgroundColor"));
    const selectValues = { fontFamily: font, fontSize: size };
    for (const [field, value] of Object.entries(selectValues)) {
      const control = instance.toolbar.querySelector(`[data-select="${field}"]`);
      if (control) control.value = Array.from(control.options).some(option => option.value === value) ? value : "";
    }
    const pickerValues = { color: textColor, backgroundColor: highlightColor };
    for (const [field, value] of Object.entries(pickerValues)) {
      const control = instance.toolbar.querySelector(`[data-select="${field}"]`);
      if (!control) continue;
      if (!control.dataset.defaultColor) control.dataset.defaultColor = control.value;
      control.value = value || control.dataset.defaultColor;
      const label = field === "color" ? "Text color" : "Text highlight";
      control.setAttribute("aria-label", `${label}: ${value || "default"}`);
      control.title = `${label}: ${value || "default"}`;
    }
    const block = ancestors.find(el => /^(H[1-6]|P|LI|BLOCKQUOTE|DIV)$/.test(el.tagName));
    const paragraph = block?.tagName?.startsWith("H") ? `Heading ${block.tagName.slice(1)}` :
      block?.tagName === "BLOCKQUOTE" ? "Block quote" : "Paragraph";
    const styles = ["bold", "italic", "underline", "strike", "sup", "sub"]
      .filter(key => node && active[key]).map(key => key[0].toUpperCase() + key.slice(1));
    if (node && active.ul) styles.push("Bulleted list");
    else if (node && active.ol) styles.push("Numbered list");
    if (font) styles.push(font);
    if (size) styles.push(`${size}px`);
    if (textColor) styles.push(`Text ${textColor}`);
    if (highlightColor) styles.push(`Highlight ${highlightColor}`);
    if (instance.formatIndicator) instance.formatIndicator.textContent = [paragraph, ...styles].join(" · ");
  }

  // Fixed overlays keep both menus and contextual tools inside the visible
  // viewport even when the host has a narrow form or a scrolled document.
  function visibleViewport() {
    const view = window.visualViewport;
    const left = view?.offsetLeft || 0;
    const top = view?.offsetTop || 0;
    return { left, top, right: left + (view?.width || window.innerWidth),
      bottom: top + (view?.height || window.innerHeight) };
  }

  function positionMenu(instance) {
    if (!instance.menuPanel || instance.menuPanel.hidden) return;
    const viewport = visibleViewport();
    // All menus use viewport coordinates. Classic (and Compact) must both
    // use fixed positioning; otherwise a Classic dropdown is offset by the
    // editor's position in a scrolled page.
    instance.menuPanel.style.position = "fixed";
    if (instance.openMenu === "tablepicker" && instance.menuOpener) {
      const trigger = instance.menuOpener.getBoundingClientRect();
      // Unlike full-width command menus, the table palette hugs its toolbar
      // button while staying inside the visual viewport on mobile/touch.
      const width = Math.min(296, viewport.right - viewport.left - 16);
      const menuHeight = Math.min(instance.menuPanel.scrollHeight, 340);
      const below = viewport.bottom - trigger.bottom - 8;
      const above = trigger.top - viewport.top - 8;
      const placeAbove = below < menuHeight && above > below;
      instance.menuPanel.style.width = `${width}px`;
      instance.menuPanel.style.left = `${Math.max(viewport.left + 8, Math.min(trigger.left, viewport.right - width - 8))}px`;
      instance.menuPanel.style.maxHeight = `${Math.max(1, Math.min(placeAbove ? above : below, menuHeight))}px`;
      instance.menuPanel.style.top = placeAbove ? "auto" : `${Math.max(viewport.top + 8, trigger.bottom + 4)}px`;
      instance.menuPanel.style.bottom = placeAbove ? `${Math.max(0, window.innerHeight - trigger.top + 4)}px` : "auto";
      return;
    }
    const editor = instance.element.getBoundingClientRect();
    const contextOpener = instance.menuOpener?.closest("[data-context-tools]");
    const anchor = contextOpener && instance.menuAnchorRect
      ? instance.menuAnchorRect : instance.toolbar.getBoundingClientRect();
    // The More palette should span the editor, not stop at the historical
    // 860px cap. Clip to the visible viewport if the editor is partly outside
    // it (small screens, horizontal scrolling, fullscreen). Keep compact
    // size for the other command menus and for contextual dropdowns.
    const isWideMore = instance.openMenu === "more" && !contextOpener;
    const editorLeft = Math.max(viewport.left + 2, editor.left + 1);
    const editorRight = Math.min(viewport.right - 2, editor.right - 1);
    const width = isWideMore
      ? Math.max(1, editorRight - editorLeft)
      : Math.max(1, Math.min(860, editor.width - 16, viewport.right - viewport.left - 16));
    const left = isWideMore ? editorLeft : Math.max(viewport.left + 8,
      Math.min(contextOpener ? anchor.left : editor.left + 8, viewport.right - 8 - width));
    // Measure content after applying the new editor-relative width, since
    // responsive group wrapping directly affects panel scrollHeight.
    instance.menuPanel.style.width = `${width}px`;
    const below = viewport.bottom - anchor.bottom - 8;
    const above = anchor.top - viewport.top - 8;
    // Use the content height rather than a fixed 210px threshold. Near the
    // bottom of a viewport this selects the side with enough room, avoiding
    // a needlessly tiny scroll pane while keeping the overlay on-screen.
    const desiredHeight = Math.min(instance.menuPanel.scrollHeight, isWideMore ? 560 : 440);
    const placeAbove = below < desiredHeight && above > below;
    const maxHeight = Math.max(1, Math.min(desiredHeight, placeAbove ? above : below));
    instance.menuPanel.style.left = `${left}px`;
    instance.menuPanel.style.maxHeight = `${maxHeight}px`;
    instance.menuPanel.style.top = placeAbove ? "auto" : `${Math.max(viewport.top + 8, anchor.bottom)}px`;
    instance.menuPanel.style.bottom = placeAbove ? `${Math.max(0, window.innerHeight - anchor.top)}px` : "auto";
  }

  function positionContextTools(instance, target) {
    const tools = instance.contextTools;
    if (!tools || tools.hidden || !target?.isConnected) return;
    const viewport = visibleViewport();
    // The contextual tools use the same viewport coordinates in both modes.
    tools.style.position = "fixed";
    // A table toolbar anchored to a cell can cover adjacent cells AND the
    // column grip. Place it outside the entire table instead.
    const anchor = ["TD", "TH"].includes(target.tagName) && target.closest("table")
      ? target.closest("table") : target;
    const rect = anchor.getBoundingClientRect();
    const canvasBounds = instance.canvas.getBoundingClientRect();
    if (!rect.width || !rect.height || rect.bottom < Math.max(viewport.top, canvasBounds.top) || rect.top > Math.min(viewport.bottom, canvasBounds.bottom)) {
      tools.hidden = true;
      return;
    }
    const width = Math.min(tools.offsetWidth, viewport.right - viewport.left - 16);
    const height = tools.offsetHeight;
    const left = Math.max(viewport.left + 8, Math.min(rect.left, viewport.right - width - 8));
    const below = viewport.bottom - rect.bottom - 8;
    const top = below < height && rect.top - viewport.top >= height + 8
      ? rect.top - height - 8 : Math.min(rect.bottom + 8, viewport.bottom - height - 8);
    tools.style.left = `${left}px`;
    tools.style.top = `${Math.max(viewport.top + 8, top)}px`;
  }

  function repositionOverlays(instance) {
    if (!instance.menuPanel?.hidden) positionMenu(instance);
    if (instance.contextTools && !instance.contextTools.hidden) {
      const target = selectedImage(instance) || activeTableCell(instance);
      positionContextTools(instance, target);
    }
    positionTableControls(instance);
    updateResizeHandle(instance);
    // Scrolling can move the table away from a stationary mouse pointer.
    const point = instance.tableHoverPoint;
    if (point) updateTableColumnHover(instance);
  }

  function menuTriggers(instance) {
    return Array.from(instance.toolbar.querySelectorAll("[data-compact-menu] button[data-azunt-menu]"))
      .filter(button => button.getClientRects().length && !button.disabled);
  }

  // In editable forms the canvas (or HTML source) is the normal Tab stop.
  // Enable toolbar Tab navigation only while it owns focus. Read-only Edit
  // retains its original keyboard access to print/export and other commands.
  function syncToolbarTabStops(instance) {
    const active = document.activeElement;
    const enabled = instance.readOnly || instance.toolbar.contains(active);
    const triggers = Array.from(instance.toolbar.querySelectorAll("[data-compact-menu] button[data-azunt-menu]"));
    const current = triggers.includes(active) ? active :
      triggers.find(button => button.tabIndex === 0) || triggers[0];
    instance.toolbar.querySelectorAll("button, select, input, textarea").forEach(control => {
      // Open panels have their own Tab/arrow behavior (including the table grid).
      if (instance.menuPanel?.contains(control)) return;
      control.tabIndex = enabled && (!triggers.includes(control) || control === current) ? 0 : -1;
    });
  }

  function focusEditorContent(instance) {
    if (instance.sourceMode) {
      instance.source.focus({ preventScroll: true });
      return;
    }
    const range = instance.lastRange?.cloneRange();
    instance.canvas.focus({ preventScroll: true });
    if (range && instance.canvas.contains(range.commonAncestorContainer)) {
      const selection = window.getSelection();
      selection.removeAllRanges();
      selection.addRange(range);
    }
  }

  function focusMenuTrigger(instance, trigger) {
    if (!trigger) return;
    instance.toolbar.querySelectorAll("[data-compact-menu] button[data-azunt-menu]")
      .forEach(button => { button.tabIndex = button === trigger ? 0 : -1; });
    trigger.focus({ preventScroll: true });
  }

  function menuFocusables(instance) {
    if (instance.menuPanel?.hidden) return [];
    return Array.from(instance.menuPanel.querySelectorAll("button, select, input, textarea"))
      .filter(element => !element.disabled && element.getClientRects().length &&
        !element.closest("[hidden]"));
  }

  function focusMenuItem(instance, last = false) {
    const controls = menuFocusables(instance);
    if (controls.length) (last ? controls[controls.length - 1] : controls[0]).focus({ preventScroll: true });
    else instance.menuPanel?.focus({ preventScroll: true });
  }

  function closeMenu(instance, restoreFocus = false) {
    if (!instance.menuPanel) return;
    for (const group of instance.originalGroups || []) instance.classicGroups.appendChild(group);
    instance.menuPanel.replaceChildren();
    instance.menuPanel.hidden = true;
    instance.menuPanel.removeAttribute("data-menu-kind");
    instance.menuPanel.classList.remove("az-azunt-editor-table-picker");
    instance.menuPanel.style.position = "";
    instance.openMenu = null;
    instance.menuAnchorRect = null;
    instance.toolbar.querySelectorAll("[data-azunt-menu]").forEach(button => {
      button.setAttribute("aria-expanded", "false");
    });
    const opener = instance.menuOpener;
    instance.menuOpener = null;
    if (restoreFocus && opener?.isConnected && opener.getClientRects().length)
      opener.focus({ preventScroll: true });
    syncToolbarTabStops(instance);
  }

  function createCustomButton(def, compact = false, readOnly = false) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = compact ? "az-azunt-editor-button az-azunt-editor-quick-command" : "az-azunt-editor-button az-azunt-editor-custom-button";
    button.dataset.customCommand = def.id;
    button.title = def.label;
    button.setAttribute("aria-label", def.label);
    // Labels and icons are always text: no host-provided HTML is inserted.
    button.textContent = compact ? (def.icon || def.label) : def.label;
    if (readOnly && def.requiresEdit) button.disabled = true;
    return button;
  }

  function effectiveCommands(instance) {
    const commands = new Map(globalCommands);
    for (const [id, def] of commandsByEditor.get(instance.element.id) || []) commands.set(id, def);
    for (const [id, def] of instance.blazorCommands || []) commands.set(id, def);
    return commands;
  }

  function refreshQuickCommands(instance) {
    if (!instance.quickCustom) return;
    instance.quickCustom.replaceChildren();
    for (const def of effectiveCommands(instance).values()) {
      if (def.placement === "toolbar") instance.quickCustom.appendChild(createCustomButton(def, true, instance.readOnly));
    }
    if (instance.classicGroups) {
      instance.classicGroups.querySelector("[data-classic-custom]")?.remove();
      const defs = Array.from(effectiveCommands(instance).values());
      if (defs.length) {
        const group = document.createElement("div");
        group.className = "az-azunt-editor-group";
        group.dataset.groupLabel = "Custom commands";
        group.dataset.classicCustom = "true";
        group.setAttribute("role", "group");
        group.setAttribute("aria-label", "Custom commands");
        for (const def of defs) group.appendChild(createCustomButton(def, false, instance.readOnly));
        instance.classicGroups.appendChild(group);
      }
    }
    syncToolbarTabStops(instance);
    fitQuickToolbar(instance);
    if (!instance.menuPanel?.hidden) openMenu(instance, instance.openMenu);
  }

  // A 10x10 palette. Buttons (rather than canvas drawing) make each size
  // accessible on touch and by keyboard, without introducing HTML into saves.
  function previewTableSize(instance, rows, columns) {
    if (instance.openMenu !== "tablepicker") return;
    const picker = instance.menuPanel.querySelector("[data-table-grid]");
    if (!picker) return;
    picker.querySelectorAll("[data-table-rows]").forEach(button => {
      const selected = Number(button.dataset.tableRows) <= rows &&
        Number(button.dataset.tableColumns) <= columns;
      button.classList.toggle("az-azunt-editor-table-cell-selected", selected);
      button.setAttribute("aria-pressed", selected ? "true" : "false");
    });
    const status = instance.menuPanel.querySelector("[data-table-size]");
    if (status) status.textContent = `${columns} × ${rows}`;
  }

  function appendTablePicker(instance, target) {
    const grid = document.createElement("div");
    grid.className = "az-azunt-editor-table-grid";
    grid.dataset.tableGrid = "true";
    grid.setAttribute("role", "group");
    grid.setAttribute("aria-label", "Select columns and rows. Use arrow keys, then Enter.");
    for (let rows = 1; rows <= 10; rows++) {
      for (let columns = 1; columns <= 10; columns++) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "az-azunt-editor-table-cell";
        button.dataset.tableRows = String(rows);
        button.dataset.tableColumns = String(columns);
        button.tabIndex = rows === 1 && columns === 1 ? 0 : -1;
        button.title = `${columns} columns × ${rows} rows`;
        button.setAttribute("aria-label", `Insert ${columns} columns by ${rows} rows table`);
        grid.appendChild(button);
      }
    }
    const size = document.createElement("div");
    size.className = "az-azunt-editor-table-size";
    size.dataset.tableSize = "true";
    size.setAttribute("aria-live", "off");
    size.textContent = "1 × 1";
    target.append(grid, size);
    previewTableSize(instance, 1, 1);
  }

  function appendFormControlChoices(target) {
    const group = document.createElement("div");
    group.className = "az-azunt-editor-group az-azunt-editor-form-choices";
    group.dataset.groupLabel = "Form controls";
    group.setAttribute("role", "group");
    group.setAttribute("aria-label", "Insert an HTML form control");
    for (const [command, label, glyph] of [
      ["form-checkbox-checked", "Checkbox (checked)", "☑"],
      ["form-checkbox", "Checkbox (unchecked)", "☐"],
      ["form-radio", "Radio button (unchecked)", "○"],
      ["form-radio-checked", "Radio button (checked)", "◉"]
    ]) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "az-azunt-editor-button az-azunt-editor-form-choice";
      button.dataset.command = command;
      button.textContent = `${glyph}  ${label}`;
      button.setAttribute("aria-label", label);
      group.appendChild(button);
    }
    target.appendChild(group);
  }


  function appendCodeChoices(target) {
    const group = document.createElement("div");
    group.className = "az-azunt-editor-group az-code-choices";
    group.dataset.groupLabel = "Code blocks";
    group.setAttribute("role", "group");
    for (const [language, label] of [["csharp", "C#"], ["javascript", "JavaScript"], ["sql", "SQL"], ["html", "HTML"], ["css", "CSS"]]) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "az-azunt-editor-button";
      button.dataset.command = `code-block-${language}`;
      button.textContent = label;
      button.setAttribute("aria-label", `Insert ${label} code block`);
      group.appendChild(button);
    }
    target.appendChild(group);
  }

  function appendCustomCommands(instance, key, target) {
    const commands = Array.from(effectiveCommands(instance).values()).filter(def =>
      key === "more" || def.group === key || (key === "file" && def.group === "export"));
    if (!commands.length) return;
    const group = document.createElement("div");
    group.className = "az-azunt-editor-group az-azunt-editor-custom-group";
    group.setAttribute("role", "group");
    group.dataset.groupLabel = key === "more" ? "Custom commands" : "Commands";
    group.setAttribute("aria-label", group.dataset.groupLabel);
    for (const def of commands) group.appendChild(createCustomButton(def, false, instance.readOnly));
    target.appendChild(group);
  }

  function openMenu(instance, key, opener = null) {
    if (!instance.menuPanel || !menuNames.has(key)) return;
    // A table dropdown in the Compact "Table" command menu is moved back
    // into a hidden Classic group on close. Anchor the picker to the visible
    // top-level Table / More trigger instead of a now-hidden DOM element.
    if (key === "tablepicker" && instance.element.dataset.toolbarMode === "compact" &&
        opener && instance.originalGroups?.some(group => group.contains(opener))) {
      const tableMenu = instance.toolbar.querySelector('[data-compact-menu] [data-azunt-menu="table"]');
      opener = tableMenu?.getClientRects().length
        ? tableMenu : instance.toolbar.querySelector('[data-azunt-menu="more"]') || opener;
    }
    // A keyboard or programmatic menu open must also dismiss another editor's menu.
    for (const other of instances.values()) {
      if (other !== instance && other.menuPanel && !other.menuPanel.hidden) closeMenu(other);
    }
    const oldOpener = instance.menuOpener;
    closeMenu(instance);
    instance.menuOpener = opener || instance.toolbar.querySelector(`[data-azunt-menu="${key}"]`) || oldOpener;
    instance.menuAnchorRect = instance.menuOpener?.closest("[data-context-tools]")
      ? instance.menuOpener.getBoundingClientRect() : null;
    instance.openMenu = key;
    instance.menuPanel.dataset.menuKind = key;
    const accessibleMenuNames = {
      more: "More editor commands", tablepicker: "Insert table size",
      codepicker: "Insert code language", controls: "Insert form controls"
    };
    instance.menuPanel.setAttribute("aria-label", accessibleMenuNames[key]
      || `${key.charAt(0).toUpperCase()}${key.slice(1)} commands`);
    instance.menuPanel.hidden = false;
    for (const label of menuGroups[key]) {
      const group = instance.originalGroups.find(candidate => candidate.dataset.groupLabel === label);
      if (group) {
        instance.menuPanel.appendChild(group);
        // Restore keyboard access to Classic controls moved into an open menu.
        group.querySelectorAll("button, select, input, textarea").forEach(control => { control.tabIndex = 0; });
      }
    }
    if (key === "tablepicker") {
      instance.menuPanel.classList.add("az-azunt-editor-table-picker");
      appendTablePicker(instance, instance.menuPanel);
    }
    if (key === "controls" || key === "insert" || key === "more") appendFormControlChoices(instance.menuPanel);
    if (key === "codepicker" || key === "insert" || key === "more") appendCodeChoices(instance.menuPanel);
    appendCustomCommands(instance, key, instance.menuPanel);
    if (!instance.menuPanel.children.length) {
      const note = document.createElement("span");
      note.className = "az-azunt-editor-menu-empty";
      note.textContent = key === "export" ? "Export commands are supplied by the host application." : "No commands available.";
      instance.menuPanel.appendChild(note);
    }
    instance.toolbar.querySelectorAll("[data-azunt-menu]").forEach(button => {
      button.setAttribute("aria-expanded", String(button.dataset.azuntMenu === key));
    });
    if (instance.contextTools) instance.contextTools.hidden = true;
    positionMenu(instance);
  }

  function executeCustomCommand(instance, id) {
    const def = effectiveCommands(instance).get(id);
    if (!def) return;
    if (instance.readOnly && def.requiresEdit) return;
    const selectionToken = def.captureSelection ? saveSelection(instance.element.id) : null;
    try {
      if (typeof def.execute === "function") {
        const result = def.execute({
          editorId: instance.element.id, editor: instance.element,
          selectionToken,
          getHtml: () => getHtml(instance.element.id),
          getPageSettings: () => getPageSettings(instance.element.id),
          setHtml: html => setHtml(instance.element.id, html),
          showStatus: (text, kind = "info") => message(instance, text, kind)
        });
        if (result && typeof result.catch === "function") result.catch(() => {
          if (selectionToken) releaseSelection(instance.element.id, selectionToken);
          message(instance, "Command failed.", "error");
        });
      } else if (def.blazor && instance.dotNetReference) {
        const operation = def.captureSelection
          ? instance.dotNetReference.invokeMethodAsync("OnCustomCommandWithSelection", id, selectionToken)
          : instance.dotNetReference.invokeMethodAsync("OnCustomCommand", id);
        operation.catch(() => {
          if (selectionToken) releaseSelection(instance.element.id, selectionToken);
          message(instance, "Command failed.", "error");
        });
      }
    } catch (_) {
      if (selectionToken) releaseSelection(instance.element.id, selectionToken);
      message(instance, "Command failed.", "error");
    }
  }

  function normalizeCommand(raw, isBlazor = false) {
    if (!raw || typeof raw !== "object" || !/^[A-Za-z][A-Za-z0-9_-]{0,63}$/.test(raw.id || ""))
      throw new Error("Custom command requires an alphanumeric id (up to 64 characters).");
    if (typeof raw.label !== "string" || !raw.label.trim() || raw.label.length > 90)
      throw new Error("Custom command requires a label (up to 90 characters).");
    const group = String(raw.group || "tools").trim().toLowerCase();
    if (!menuNames.has(group) || group === "more") throw new Error("Unsupported command group.");
    const placement = String(raw.placement || "menu").trim().toLowerCase();
    if (placement !== "menu" && placement !== "toolbar") throw new Error("Custom command placement must be Menu or Toolbar.");
    return {
      id: raw.id, label: raw.label.trim(), icon: String(raw.icon || "").slice(0, 8), group,
      placement, requiresEdit: raw.requiresEdit === true || raw.captureSelection === true,
      captureSelection: raw.captureSelection === true, blazor: isBlazor,
      execute: typeof raw.execute === "function" && !isBlazor ? raw.execute : undefined
    };
  }

  // May be called before or after editor mount. editorId limits registration to one editor.
  function registerCommand(definition, editorId) {
    const def = normalizeCommand(definition);
    if (editorId) {
      let commands = commandsByEditor.get(editorId);
      if (!commands) { commands = new Map(); commandsByEditor.set(editorId, commands); }
      commands.set(def.id, def);
    } else globalCommands.set(def.id, def);
    for (const instance of instances.values()) {
      if (!editorId || instance.element.id === editorId) refreshQuickCommands(instance);
    }
  }

  // Validate the complete new list before replacing the live buttons.
  // One invalid command must not discard previously working commands.
  function setCommands(id, definitions) {
    const instance = getInstance(id);
    const next = new Map();
    for (const raw of definitions || []) {
      const def = normalizeCommand(raw, true);
      if (next.has(def.id)) throw new Error(`Duplicate custom command id: ${def.id}`);
      next.set(def.id, def);
    }
    instance.blazorCommands = next;
    refreshQuickCommands(instance);
  }

  // Remove only the selected registration; a global command with the same id
  // becomes visible again if an editor-scoped override is removed.
  function unregisterCommand(id, editorId) {
    if (typeof id !== "string" || !/^[A-Za-z][A-Za-z0-9_-]{0,63}$/.test(id))
      throw new Error("A valid custom command id is required.");
    const registry = editorId ? commandsByEditor.get(editorId) : globalCommands;
    const removed = registry?.delete(id) || false;
    if (editorId && registry?.size === 0) commandsByEditor.delete(editorId);
    if (removed) {
      for (const instance of instances.values()) {
        if (!editorId || instance.element.id === editorId) refreshQuickCommands(instance);
      }
    }
    return removed;
  }

  function updateContextTools(instance) {
    const tools = instance.contextTools;
    if (!tools || instance.element.dataset.toolbarMode !== "compact" || instance.readOnly || instance.sourceMode || !instance.menuPanel?.hidden) {
      if (tools) tools.hidden = true;
      return;
    }
    const image = selectedImage(instance);
    const cell = image ? null : activeTableCell(instance);
    if (!image && cell) selectTableControls(instance, cell.closest("table"), Array.from(cell.parentElement.cells).indexOf(cell));
    const target = image || cell;
    if (!target) { tools.hidden = true; return; }
    const actions = image ? [
      ["image-original", "Original size"], ["image-delete", "Delete image"], ["@image", "Image settings"]
    ] : [
      ["table-row-after", "+Row"], ["table-col-after", "+Column"],
      ["table-merge-right", "Merge"], ["table-split", "Split"], ["@table", "Table settings"]
    ];
    tools.replaceChildren();
    for (const [command, label] of actions) {
      const button = document.createElement("button");
      button.type = "button";
      button.textContent = label;
      button.className = "az-azunt-editor-button";
      button.setAttribute("aria-label", label);
      if (command.startsWith("@")) {
        button.dataset.operateMenu = command.slice(1);
        button.setAttribute("aria-haspopup", "dialog");
        if (instance.menuPanel) button.setAttribute("aria-controls", instance.menuPanel.id);
      } else button.dataset.quickCommand = command;
      tools.appendChild(button);
    }
    // Measure after showing the toolbar so wrapped touch targets are accounted for.
    tools.hidden = false;
    syncToolbarTabStops(instance);
    positionContextTools(instance, target);
  }

  function setToolbarMode(id, mode) {
    const instance = getInstance(id);
    closeMenu(instance);
    instance.element.dataset.toolbarMode = String(mode).toLowerCase() === "classic" ? "classic" : "compact";
    fitQuickToolbar(instance);
    updateContextTools(instance);
  }

  // Presentation is kept on the host, not in document HTML or history.
  const contentRenderings = Object.freeze({ Standard: "", BrowserDefault: "browser-default",
    CompactReport: "compact-report", BusinessDocument: "business-document", Minimal: "minimal" });

  // Custom styles are per instance, use validated scalar values, and never
  // inject host-provided CSS or modify the saved HTML.
  const documentFonts = Object.freeze({ System: 'system-ui, -apple-system, "Segoe UI", Arial, sans-serif',
    Arial: 'Arial, sans-serif', Georgia: 'Georgia, serif', TimesNewRoman: '"Times New Roman", serif',
    Consolas: 'Consolas, monospace' });
  const customStyleProperties = ['--azunt-document-font', '--azunt-document-size', '--azunt-document-line',
    '--azunt-document-padding', '--azunt-document-cell-size', '--azunt-document-cell-padding',
    '--azunt-document-border', '--azunt-document-header', '--azunt-document-color'];

  function validateDocumentStyles(styles) {
    if (!Array.isArray(styles) || styles.length > 20) throw new Error("Expected up to 20 custom document styles.");
    const result = new Map();
    for (const style of styles) {
      const { id, name, fontFamily = "System", fontSize = 13, tableFontSize = 12, lineHeight = 1.4,
        padding = 12, cellPaddingVertical = 3, cellPaddingHorizontal = 5,
        borderColor = "#cbd2da", headerColor = "#f1f4f7", textColor = "#202124" } = style || {};
      const integer = (v, min, max) => Number.isInteger(v) && v >= min && v <= max;
      const color = v => typeof v === "string" && /^#[0-9a-f]{6}$/i.test(v);
      if (typeof id !== "string" || !/^[a-z][a-z0-9-]{0,39}$/.test(id) || result.has(id) ||
          typeof name !== "string" || !name.trim() || name.length > 80 || !Object.hasOwn(documentFonts, fontFamily) ||
          !integer(fontSize, 8, 32) || !integer(tableFontSize, 8, 32) ||
          typeof lineHeight !== "number" || !Number.isFinite(lineHeight) || lineHeight < 1 || lineHeight > 2.5 ||
          !integer(padding, 0, 60) || !integer(cellPaddingVertical, 0, 24) || !integer(cellPaddingHorizontal, 0, 24) ||
          !color(borderColor) || !color(headerColor) || !color(textColor)) throw new Error("Invalid or duplicate custom document style.");
      result.set(id, { id, name: name.trim(), fontFamily, fontSize, tableFontSize, lineHeight, padding,
        cellPaddingVertical, cellPaddingHorizontal, borderColor, headerColor, textColor });
    }
    return result;
  }

  function applyCustomStyle(root, style) {
    const values = style ? [documentFonts[style.fontFamily], `${style.fontSize}px`, String(style.lineHeight),
      `${style.padding}px`, `${style.tableFontSize}px`, `${style.cellPaddingVertical}px ${style.cellPaddingHorizontal}px`,
      style.borderColor, style.headerColor, style.textColor] : [];
    customStyleProperties.forEach((key, index) => style ? root.style.setProperty(key, values[index]) : root.style.removeProperty(key));
  }

  function getContentRendering(id) {
    const instance = getInstance(id);
    const marker = instance.customDocumentStyle ? instance.baseContentRendering : instance.element.dataset.contentRendering || "";
    return Object.keys(contentRenderings).find(key => contentRenderings[key] === marker) || "Standard";
  }

  function getDocumentStyleId(id) {
    const instance = getInstance(id);
    return instance.customDocumentStyle ? `custom:${instance.customDocumentStyle}` : getContentRendering(id);
  }

  function syncContentRenderingSelector(instance) {
    const select = instance.toolbar?.querySelector('[data-select="documentStyle"]');
    if (!select) return;
    Array.from(select.options).filter(option => option.value.startsWith("custom:")).forEach(option => option.remove());
    for (const style of instance.documentStyles.values()) select.add(new Option(style.name, `custom:${style.id}`));
    select.value = getDocumentStyleId(instance.element.id);
  }

  function applyDocumentRendering(instance, marker, custom) {
    if ((instance.element.dataset.contentRendering || "") === marker && instance.customDocumentStyle === (custom?.id || null)) return;
    if (instance.imageResize) finishImageResize(instance, null, true);
    finishTableMove(instance, null, true);
    finishTableColumnResize(instance, null, true);
    if (marker) instance.element.dataset.contentRendering = marker;
    else delete instance.element.dataset.contentRendering;
    instance.customDocumentStyle = custom?.id || null;
    // Do not touch host-owned inline properties on ordinary built-in editors.
    if (custom || instance.hasCustomStyleProperties) applyCustomStyle(instance.element, custom);
    instance.hasCustomStyleProperties = !!custom;
    if (instance.tableMoveHandle) repositionOverlays(instance);
    updateResizeHandle(instance);
  }

  function viewScale(instance) { return (Number(instance.element.dataset.viewZoom) || 100) / 100; }

  function getViewOptions(id) {
    const instance = getInstance(id);
    const root = instance.element;
    return { heightMode: root.dataset.viewHeightMode === "fixed" ? "Fixed" : "AutoGrow",
      height: Number(root.dataset.viewHeight) || parseInt(instance.source.style.minHeight, 10) || 320,
      zoomPercent: Number(root.dataset.viewZoom) || 100 };
  }

  function syncViewSelectors(instance, options) {
    const height = instance.toolbar.querySelector('[data-select="viewHeightMode"]');
    const zoom = instance.toolbar.querySelector('[data-select="viewZoom"]');
    if (height) height.value = options.heightMode;
    if (zoom) {
      zoom.querySelectorAll('[data-view-extra]').forEach(option => option.remove());
      if (![...zoom.options].some(option => Number(option.value) === options.zoomPercent)) {
        const option = new Option(`${options.zoomPercent}%`, String(options.zoomPercent));
        option.dataset.viewExtra = "true"; zoom.add(option);
      }
      zoom.value = String(options.zoomPercent);
    }
  }

  function setViewOptions(id, heightMode, height, zoomPercent = 100) {
    const mode = String(heightMode).toLowerCase();
    if (!["autogrow", "fixed"].includes(mode) || !Number.isInteger(height) ||
        !Number.isInteger(zoomPercent) || zoomPercent < 50 || zoomPercent > 200)
      throw new Error("Invalid editor view options. Zoom must be 50–200 percent.");
    const instance = getInstance(id), root = instance.element;
    height = Math.max(40, Math.min(1400, height));
    // Cancel unfinished gestures before altering their coordinate system.
    finishImageResize(instance, null, true);
    finishTableMove(instance, null, true);
    finishTableColumnResize(instance, null, true);
    root.dataset.viewHeightMode = mode === "fixed" ? "fixed" : "auto-grow";
    root.dataset.viewHeight = String(height);
    root.dataset.viewZoom = String(zoomPercent);
    root.style.setProperty("--azunt-view-height", `${height}px`);
    root.style.setProperty("--azunt-view-zoom", `${zoomPercent}%`);
    root.style.setProperty("--azunt-view-scale", String(zoomPercent / 100));
    instance.canvas.style.minHeight = `${height}px`;
    instance.source.style.minHeight = `${height}px`;
    syncViewSelectors(instance, getViewOptions(id));
    instance.tableHoverPoint = null;
    updateTableColumnHover(instance, null);
    repositionOverlays(instance);
    updateResizeHandle(instance);
  }

  function setContentRendering(id, rendering) {
    const desired = Object.keys(contentRenderings).find(key => key.toLowerCase() === String(rendering).trim().toLowerCase());
    if (!desired) throw new Error("Unknown content rendering style.");
    const instance = getInstance(id);
    instance.baseContentRendering = contentRenderings[desired];
    applyDocumentRendering(instance, contentRenderings[desired], null);
    syncContentRenderingSelector(instance);
  }

  function setDocumentStyle(id, styleId) {
    const instance = getInstance(id);
    if (typeof styleId === "string" && styleId.startsWith("custom:")) {
      const custom = instance.documentStyles.get(styleId.slice(7));
      if (!custom) throw new Error("Unknown custom document style.");
      applyDocumentRendering(instance, "custom-document", custom);
      syncContentRenderingSelector(instance);
    } else setContentRendering(id, styleId);
  }

  function registerDocumentStyles(id, styles) {
    const instance = getInstance(id);
    const validated = validateDocumentStyles(styles); // atomic: invalid lists leave the editor unchanged
    instance.documentStyles = validated;
    if (instance.customDocumentStyle) {
      const custom = validated.get(instance.customDocumentStyle);
      if (custom) { applyCustomStyle(instance.element, custom); repositionOverlays(instance); updateResizeHandle(instance); }
      else setContentRendering(id, getContentRendering(id));
    }
    syncContentRenderingSelector(instance);
  }

  // Controls only the menu row, not command availability or the saved document.
  function setShowMenuBar(id, show) {
    const instance = getInstance(id);
    closeMenu(instance);
    instance.element.dataset.showMenuBar = show ? "true" : "false";
  }

  function setShowToolbarGroupLabels(id, show) {
    const instance = getInstance(id);
    closeMenu(instance);
    instance.element.dataset.showToolbarGroupLabels = show ? "true" : "false";
    fitQuickToolbar(instance);
    repositionOverlays(instance);
  }

  // Keep the largest useful prefix of commands that fits the actual host.
  // Split buttons are atomic; every omitted command remains in More.
  function fitQuickToolbar(instance) {
    const row = instance.toolbar?.querySelector("[data-compact-quick]");
    if (!row || !row.clientWidth || instance.element.dataset.toolbarMode !== "compact") return;
    row.querySelectorAll("[data-quick-overflow]").forEach(node => node.removeAttribute("data-quick-overflow"));
    const candidates = [];
    for (const child of row.children) {
      if (child.matches('[data-azunt-menu="more"], [data-quick-command="fullscreen"]')) continue;
      const controls = child.querySelector(":scope > .az-azunt-editor-quick-controls");
      if (controls || child.matches("[data-quick-custom]")) {
        const group = controls || child;
        for (const control of group.children) candidates.push({ control, group, container: child });
        if (!group.children.length) child.setAttribute("data-quick-overflow", "");
      } else candidates.push({ control: child });
    }
    const fits = () => {
      const bounds = row.getBoundingClientRect();
      const right = bounds.right - parseFloat(getComputedStyle(row).paddingRight || 0);
      return Array.from(row.children).every(child => child.hasAttribute("data-quick-overflow") || child.getBoundingClientRect().right <= right + 0.5);
    };
    // On very small screens preserve basic text formatting ahead of history.
    const priority = item => item.control.matches('[data-quick-command="bold"], [data-quick-command="italic"], [data-quick-command="underline"]') ? 2
      : item.control.matches('[data-quick-command="undo"], [data-quick-command="redo"]') ? 1 : 0;
    for (const item of candidates.reverse().sort((a, b) => priority(a) - priority(b))) {
      if (fits()) break;
      item.control.setAttribute("data-quick-overflow", "");
      if (item.group && Array.from(item.group.children).every(control => control.hasAttribute("data-quick-overflow")))
        item.container.setAttribute("data-quick-overflow", "");
    }
  }

  function initAdaptiveToolbar(instance) {
    instance.classicGroups = instance.toolbar.querySelector("[data-classic-groups]");
    instance.menuPanel = instance.toolbar.querySelector("[data-menu-panel]");
    instance.contextTools = instance.toolbar.querySelector("[data-context-tools]");
    instance.quickCustom = instance.toolbar.querySelector("[data-quick-custom]");
    instance.formatIndicator = instance.element.querySelector("[data-current-format]");
    const panelId = `${instance.element.id}-command-menu`;
    if (instance.menuPanel) {
      instance.menuPanel.id = panelId;
      instance.menuPanel.tabIndex = -1;
      instance.menuPanel.setAttribute("role", "dialog");
      instance.menuPanel.setAttribute("aria-modal", "false");
    }
    const triggers = instance.toolbar.querySelectorAll("[data-compact-menu] button[data-azunt-menu]");
    triggers.forEach((button, index) => {
      button.setAttribute("role", "menuitem");
      button.setAttribute("aria-haspopup", "dialog");
      button.setAttribute("aria-controls", panelId);
      button.setAttribute("aria-expanded", "false");
      button.tabIndex = index === 0 ? 0 : -1;
    });
    const more = instance.toolbar.querySelector('[data-azunt-menu="more"]');
    if (more) {
      more.setAttribute("aria-controls", panelId);
      more.setAttribute("aria-haspopup", "dialog");
      more.setAttribute("aria-label", "More editor commands");
    }
    instance.originalGroups = instance.classicGroups
      ? Array.from(instance.classicGroups.querySelectorAll(":scope > .az-azunt-editor-group")) : [];
    instance.openMenu = null;
    // Blazor commands can already be registered before a toolbar is initialized,
    // e.g. while the editor is in View mode. Do not reset them here.
    refreshQuickCommands(instance);
    syncToolbarTabStops(instance);
    // Apply the menu-row breakpoint and measure quick commands before first paint.
    const width = instance.element.getBoundingClientRect().width;
    instance.element.classList.toggle("az-azunt-editor-very-narrow", width < 515);
    fitQuickToolbar(instance);
    if (window.ResizeObserver) {
      instance.toolbarSizeObserver = new ResizeObserver(entries => {
        const width = entries[0]?.contentRect.width || instance.element.clientWidth;
        instance.element.classList.toggle("az-azunt-editor-very-narrow", width < 515);
        fitQuickToolbar(instance);
        repositionOverlays(instance);
      });
      instance.toolbarSizeObserver.observe(instance.element);
    }
  }

  // Blazor can preserve the outer editor node while replacing its children.
  // The attached canvas/toolbar must be the nodes that are actually visible.
  function bindingIsCurrent(instance, element) {
    return instance && instance.element === element && element.isConnected &&
      instance.canvas === element.querySelector(".az-azunt-editor-canvas") &&
      instance.toolbar === element.querySelector(".az-azunt-editor-toolbar") &&
      instance.value === element.querySelector("[data-azunt-value]") &&
      instance.images === element.querySelector("[data-azunt-images]") &&
      instance.source === element.querySelector("[data-azunt-source]") &&
      instance.linkPanel === element.querySelector("[data-link-panel]") &&
      instance.findPanel === element.querySelector("[data-find-panel]") &&
      instance.characterPanel === element.querySelector("[data-chars-panel]") &&
      instance.signaturePanel === element.querySelector("[data-signature-panel]") &&
      instance.templatePanel === element.querySelector("[data-template-panel]") &&
      instance.datePanel === element.querySelector("[data-date-panel]") &&
      instance.imageResizeHandle === element.querySelector("[data-image-resize-handle]") &&
      (!instance.tableMoveHandle || instance.tableMoveHandle === element.querySelector("[data-table-move-handle]")) &&
      (!instance.tableColumnHandle || instance.tableColumnHandle === element.querySelector("[data-table-column-handle]"));
  }

  function mount(id, dotNetReference, force = false) {
    const element = typeof id === "string" ? document.getElementById(id) : id;
    if (!element || !element.matches("[data-azunt-editor]")) return false;
    const previous = instances.get(element.id);
    if (!force && bindingIsCurrent(previous, element)) {
      if (dotNetReference) previous.dotNetReference = dotNetReference;
      return true;
    }
    // Preserve pending edits if a framework replaced only the canvas/toolbar.
    // Prefer visible canvas content where it exists; detached nodes can be stale.
    const currentCanvas = element.querySelector(".az-azunt-editor-canvas");
    const visibleHtml = currentCanvas?.innerHTML || "";
    const pendingHtml = previous?.canvas ? readHtml(previous) : "";
    const previousBlazorCommands = previous?.blazorCommands;
    const sameRoot = previous && previous.element === element;
    const registeredStyles = sameRoot ? previous.documentStyles : validateDocumentStyles(JSON.parse(element.dataset.documentStyles || "[]"));
    const initialStyleId = sameRoot ? (previous.customDocumentStyle ? `custom:${previous.customDocumentStyle}` : null) : element.dataset.documentStyleId;
    const baseContentRendering = sameRoot ? previous.baseContentRendering : (element.dataset.baseContentRendering ?? element.dataset.contentRendering ?? "");
    if (previous) {
      dotNetReference = dotNetReference || previous.dotNetReference;
      unmount(element.id);
    }
    const controller = new AbortController();
    const instance = {
      element, controller, canvas: currentCanvas,
      documentStyles: registeredStyles, baseContentRendering, customDocumentStyle: null,
      hasCustomStyleProperties: !!(sameRoot && previous.hasCustomStyleProperties),
      toolbar: element.querySelector(".az-azunt-editor-toolbar"),
      value: element.querySelector("[data-azunt-value]"),
      images: element.querySelector("[data-azunt-images]"),
      source: element.querySelector("[data-azunt-source]"),
      codePanel: element.querySelector("[data-code-panel]"),
      codeLanguage: element.querySelector("[data-code-language]"),
      codeText: element.querySelector("[data-code-text]"),
      editingCodeBlock: null,
      linkPanel: element.querySelector("[data-link-panel]"),
      linkText: element.querySelector("[data-link-text]"),
      linkUrl: element.querySelector("[data-link-url]"),
      imageWidth: element.querySelector("[data-image-width]"),
      imageAlt: element.querySelector("[data-image-alt]"),
      imageInsertSize: element.querySelector("[data-select=\"imageInsertSize\"]"),
      imageResizeHandle: element.querySelector("[data-image-resize-handle]"),
      imageResize: null, imageInsertGeneration: 0,
      status: element.querySelector(".az-azunt-editor-message"),
      stats: element.querySelector("[data-editor-stats]"),
      findPanel: element.querySelector("[data-find-panel]"),
      findQuery: element.querySelector("[data-find-query]"),
      findReplacement: element.querySelector("[data-find-replacement]"),
      findStatus: element.querySelector("[data-find-status]"),
      characterPanel: element.querySelector("[data-chars-panel]"),
      signaturePanel: element.querySelector("[data-signature-panel]"),
      signatureCanvas: element.querySelector("[data-signature-canvas]"),
      signatureContext: null, signatureHasInk: false, signatureDrawing: false, signatureBounds: null,
      signatureColor: element.querySelector("[data-signature-color]"),
      signatureWidth: element.querySelector("[data-signature-width]"),
      templatePanel: element.querySelector("[data-template-panel]"),
      templateList: element.querySelector("[data-template-list]"),
      datePanel: element.querySelector("[data-date-panel]"),
      mode: element.dataset.editorMode === "view" ? "view" : "edit",
      readOnly: element.dataset.editorMode === "view" || element.dataset.readOnly === "true",
      uploadUrl: element.dataset.uploadUrl || null,
      dotNetReference: dotNetReference || null, blazorCommands: new Map(previousBlazorCommands || []),
      emitTimer: null, messageTimer: null, messageVersion: 0,
      history: [], historySelections: [], historyIndex: 0, lastRange: null,
      revision: previous?.revision || 0, sourceMode: false, isComposing: false,
      selectedImage: null, activeCell: null, editingLink: null,
      selectedTable: null, tableColumnIndex: 0, tableMove: null, tableColumnResize: null,
      tableResizeTarget: null, tableHoverPoint: null,
      page: previous?.page || { paper: "A4", orientation: "portrait", margin: "normal" }
    };
    // On first mount the canvas is empty and the textarea owns the initial HTML.
    // After a Blazor subtree replacement, a visible edit wins over the textarea;
    // otherwise carry over the previous canvas content from this same root.
    const startingHtml = visibleHtml.trim() ? visibleHtml
      : (sameRoot ? pendingHtml : instance.value.value);
    instance.canvas.innerHTML = sanitizeHtml(startingHtml);
    decorateCodeBlocks(instance);
    setDocumentControlsReadOnly(instance);
    instance.value.value = readHtml(instance);
    // Switching modes retains undo history, but starts with the current sanitized HTML.
    const oldHistory = sameRoot && previous?.history;
    instance.history = oldHistory?.length ? oldHistory : [instance.value.value];
    instance.historySelections = oldHistory?.length
      ? (previous.historySelections || []).slice() : [null];
    instance.historyIndex = oldHistory?.length ? previous.historyIndex : 0;
    instances.set(element.id, instance);
    // Restore registered custom style after DOM hydration or a same-root remount.
    if (initialStyleId?.startsWith("custom:")) {
      const custom = registeredStyles.get(initialStyleId.slice(7));
      if (!custom) throw new Error("Unknown custom document style.");
      applyDocumentRendering(instance, "custom-document", custom);
    } else if (initialStyleId) {
      const desired = Object.keys(contentRenderings).find(key => key.toLowerCase() === initialStyleId.trim().toLowerCase());
      if (!desired) throw new Error("Unknown document style.");
      instance.baseContentRendering = contentRenderings[desired];
      applyDocumentRendering(instance, contentRenderings[desired], null);
    }
    syncContentRenderingSelector(instance);
    syncViewSelectors(instance, getViewOptions(element.id));
    instance.canvas.contentEditable = instance.readOnly ? "false" : "true";
    instance.canvas.addEventListener("click", event => handleCodeAction(instance, event), { signal: controller.signal });
    instance.canvas.addEventListener("dblclick", event => {
      const block = event.target.closest?.('pre[data-language]');
      if (block && !instance.readOnly) { event.preventDefault(); openCodePanel(instance, block.dataset.language, block); }
    }, { signal: controller.signal });
    instance.value.disabled = instance.mode === "view";
    if (instance.mode === "view") {
      instance.canvas.setAttribute("role", "document");
      instance.canvas.setAttribute("aria-label", "Document viewer");
      instance.canvas.removeAttribute("aria-multiline");
      instance.canvas.removeAttribute("aria-keyshortcuts");
      // A viewer has no toolbar/selection/image handlers or Blazor edit callbacks.
      return true;
    }
    instance.canvas.setAttribute("role", "textbox");
    instance.canvas.setAttribute("aria-label", "Document content");
    instance.canvas.setAttribute("aria-multiline", "true");
    if (!instance.readOnly) {
      instance.canvas.setAttribute("aria-keyshortcuts", "Alt+F10");
      instance.source.setAttribute("aria-keyshortcuts", "Alt+F10");
    }
    // initAdaptiveToolbar() also renders commands retained across Edit/View changes.
    initAdaptiveToolbar(instance);
    ensureTableControls(instance);
    updateStats(instance);

    const signal = controller.signal;
    instance.tableMoveHandle.addEventListener("pointerdown", event => startTableMove(instance, event), { signal });
    instance.tableMoveHandle.addEventListener("pointermove", event => dragTableMove(instance, event), { signal });
    instance.tableMoveHandle.addEventListener("pointerup", event => finishTableMove(instance, event), { signal });
    instance.tableMoveHandle.addEventListener("pointercancel", event => finishTableMove(instance, event, true), { signal });
    instance.tableMoveHandle.addEventListener("keydown", event => {
      if (event.altKey && ["ArrowUp", "ArrowDown"].includes(event.key)) {
        event.preventDefault(); moveTableByKeyboard(instance, event.key === "ArrowUp" ? -1 : 1);
      }
    }, { signal });
    instance.tableColumnHandle.addEventListener("pointerdown", event => startTableColumnResize(instance, event), { signal });
    instance.tableColumnHandle.addEventListener("pointermove", event => dragTableColumnResize(instance, event), { signal });
    instance.tableColumnHandle.addEventListener("pointerup", event => finishTableColumnResize(instance, event), { signal });
    instance.tableColumnHandle.addEventListener("pointercancel", event => finishTableColumnResize(instance, event, true), { signal });
    instance.tableColumnHandle.addEventListener("lostpointercapture", event => {
      // Normal pointerup already finished; unexpected capture loss rolls back.
      finishTableColumnResize(instance, event, true);
    }, { signal });
    function keyboardTableResize(event, target) {
      if (!target || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
      const keys = target.axis === "row" ? ["ArrowUp", "ArrowDown"] : ["ArrowLeft", "ArrowRight"];
      if (!keys.includes(event.key)) return;
      event.preventDefault();
      const pointerId = -1, delta = (event.key === keys[0] ? -8 : 8) * viewScale(instance);
      startTableColumnResize(instance, { button: 0, preventDefault() {}, pointerId, clientX: 0, clientY: 0 }, target);
      dragTableColumnResize(instance, { pointerId, preventDefault() {}, clientX: delta, clientY: delta });
      finishTableColumnResize(instance, { pointerId });
      instance.tableMoveHandle.focus({ preventScroll: true });
    }
    instance.tableColumnHandle.addEventListener("keydown", event => keyboardTableResize(event, instance.tableResizeTarget), { signal });
    // The move grip remains an accessible keyboard entry point when no edge is hovered.
    instance.tableMoveHandle.addEventListener("keydown", event => {
      const table = instance.selectedTable;
      if (!table) return;
      const axis = ["ArrowUp", "ArrowDown"].includes(event.key) ? "row" : "column";
      const index = axis === "row" ? (instance.activeCell?.closest("table") === table ? instance.activeCell.parentElement.rowIndex : 0) : instance.tableColumnIndex;
      keyboardTableResize(event, { table, axis, index });
    }, { signal });
    instance.toolbar.addEventListener("mousedown", event => {
      if (event.target.closest("button")) event.preventDefault();
    }, { signal });
    instance.toolbar.addEventListener("keydown", event => {
      const pickerTrigger = event.target.closest('button[data-azunt-menu="tablepicker"]');
      if (pickerTrigger && ["ArrowDown", "ArrowUp"].includes(event.key)) {
        event.preventDefault();
        if (instance.openMenu !== "tablepicker") openMenu(instance, "tablepicker", pickerTrigger);
        focusMenuItem(instance);
        return;
      }
      const formDropdown = event.target.closest('button[data-azunt-menu="controls"]');
      if (formDropdown && ["ArrowDown", "ArrowUp"].includes(event.key)) {
        event.preventDefault();
        if (instance.openMenu !== "controls") openMenu(instance, "controls", formDropdown);
        focusMenuItem(instance, event.key === "ArrowUp");
        return;
      }
      const trigger = event.target.closest("[data-compact-menu] button[data-azunt-menu]");
      if (trigger) {
        const triggers = menuTriggers(instance);
        const index = triggers.indexOf(trigger);
        if (event.key === "ArrowRight" || event.key === "ArrowLeft" ||
            event.key === "Home" || event.key === "End") {
          event.preventDefault();
          const next = event.key === "Home" ? 0 : event.key === "End" ? triggers.length - 1 :
            (index + (event.key === "ArrowRight" ? 1 : -1) + triggers.length) % triggers.length;
          const target = triggers[next];
          focusMenuTrigger(instance, target);
          if (!instance.menuPanel?.hidden) openMenu(instance, target.dataset.azuntMenu, target);
        } else if (event.key === "ArrowDown" || event.key === "ArrowUp") {
          event.preventDefault();
          if (instance.openMenu !== trigger.dataset.azuntMenu)
            openMenu(instance, trigger.dataset.azuntMenu, trigger);
          focusMenuItem(instance, event.key === "ArrowUp");
        }
        return;
      }
      if (instance.menuPanel?.hidden || !instance.menuPanel.contains(event.target)) return;
      const tableCell = event.target.closest("button[data-table-rows][data-table-columns]");
      if (tableCell && instance.openMenu === "tablepicker" &&
          ["ArrowRight", "ArrowLeft", "ArrowUp", "ArrowDown", "Home", "End"].includes(event.key)) {
        event.preventDefault();
        let rows = Number(tableCell.dataset.tableRows);
        let columns = Number(tableCell.dataset.tableColumns);
        if (event.key === "ArrowRight") columns = Math.min(10, columns + 1);
        if (event.key === "ArrowLeft") columns = Math.max(1, columns - 1);
        if (event.key === "ArrowDown") rows = Math.min(10, rows + 1);
        if (event.key === "ArrowUp") rows = Math.max(1, rows - 1);
        if (event.key === "Home") { rows = 1; columns = 1; }
        if (event.key === "End") { rows = 10; columns = 10; }
        const next = instance.menuPanel.querySelector(`[data-table-rows="${rows}"][data-table-columns="${columns}"]`);
        tableCell.tabIndex = -1;
        next.tabIndex = 0;
        next.focus({ preventScroll: true });
        previewTableSize(instance, rows, columns);
        return;
      }
      // Native inputs/selects own their arrows and text editing shortcuts.
      if (event.target.matches("input, select, textarea")) return;
      if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
      const controls = menuFocusables(instance);
      if (!controls.length) return;
      event.preventDefault();
      const index = controls.indexOf(event.target);
      const next = event.key === "Home" ? 0 : event.key === "End" ? controls.length - 1 :
        (index + (event.key === "ArrowDown" ? 1 : -1) + controls.length) % controls.length;
      controls[next].focus({ preventScroll: true });
    }, { signal });
    instance.toolbar.addEventListener("focusout", () => {
      queueMicrotask(() => {
        if (signal.aborted) return;
        if (!instance.toolbar.contains(document.activeElement) && !instance.menuPanel?.hidden)
          closeMenu(instance);
        syncToolbarTabStops(instance);
      });
    }, { signal });
    instance.toolbar.addEventListener("pointerover", event => {
      const cell = event.target.closest("button[data-table-rows][data-table-columns]");
      if (cell) previewTableSize(instance, Number(cell.dataset.tableRows), Number(cell.dataset.tableColumns));
    }, { signal });
    instance.toolbar.addEventListener("focusin", event => {
      syncToolbarTabStops(instance);
      const cell = event.target.closest("button[data-table-rows][data-table-columns]");
      if (cell) previewTableSize(instance, Number(cell.dataset.tableRows), Number(cell.dataset.tableColumns));
    }, { signal });
    instance.toolbar.addEventListener("click", event => {
      const cell = event.target.closest("button[data-table-rows][data-table-columns]");
      if (cell && instance.openMenu === "tablepicker") {
        const rows = Number(cell.dataset.tableRows);
        const columns = Number(cell.dataset.tableColumns);
        closeMenu(instance);
        if (!instance.readOnly && !instance.sourceMode) insertTable(instance, rows, columns);
        updateContextTools(instance);
        updateFormattingState(instance);
        return;
      }
      const menu = event.target.closest("button[data-azunt-menu]");
      if (menu) {
        const key = menu.dataset.azuntMenu;
        if (instance.openMenu === key) closeMenu(instance, true);
        else openMenu(instance, key, menu);
        return;
      }
      const relatedMenu = event.target.closest("button[data-operate-menu]");
      if (relatedMenu) { openMenu(instance, relatedMenu.dataset.operateMenu, relatedMenu); return; }
      const custom = event.target.closest("button[data-custom-command]");
      if (custom) {
        if (!instance.menuPanel?.hidden) closeMenu(instance, event.detail === 0);
        executeCustomCommand(instance, custom.dataset.customCommand);
        return;
      }
      const button = event.target.closest("button[data-command],button[data-quick-command]");
      if (button) {
        const command = button.dataset.command || button.dataset.quickCommand;
        if (!instance.menuPanel?.hidden) closeMenu(instance, event.detail === 0);
        runCommand(instance, command);
        updateContextTools(instance);
        updateFormattingState(instance);
      }
    }, { signal });
    // Preserve the canvas selection before native selects and color pickers take focus.
    instance.toolbar.addEventListener("pointerdown", () => {
      const selection = window.getSelection();
      if (selection?.rangeCount && instance.canvas.contains(selection.getRangeAt(0).commonAncestorContainer))
        instance.lastRange = selection.getRangeAt(0).cloneRange();
    }, { signal });
    instance.toolbar.addEventListener("change", event => {
      const target = event.target.closest("[data-select]");
      if (!target) return;
      runSelect(instance, target.dataset.select, target.value);
      if (!["paper", "orientation", "margin", "imageInsertSize", "documentStyle", "viewHeightMode", "viewZoom"].includes(target.dataset.select) && target.type !== "color") target.selectedIndex = 0;
      updateFormattingState(instance);
    }, { signal });
    instance.codePanel?.querySelector("[data-code-apply]")?.addEventListener("click", () => applyCodePanel(instance), { signal });
    instance.codePanel?.querySelector("[data-code-cancel]")?.addEventListener("click", () => closeCodePanel(instance), { signal });
    instance.codeText?.addEventListener("keydown", event => {
      if (event.isComposing) return;
      if (event.key === "Escape") { event.preventDefault(); closeCodePanel(instance); }
      else if (event.key === "Tab") {
        event.preventDefault();
        editCodeIndent(instance.codeText, event.shiftKey);
      } else if (event.key === "Enter") {
        event.preventDefault();
        insertCodeNewline(instance.codeText);
      }
    }, { signal });
    instance.linkPanel.querySelector("[data-link-apply]").addEventListener("click", () => applyLink(instance), { signal });
    instance.linkPanel.querySelector("[data-link-cancel]").addEventListener("click", () => {
      instance.linkPanel.hidden = true;
      instance.editingLink = null;
    }, { signal });
    instance.imageWidth.addEventListener("change", () => resizeImage(instance), { signal });
    instance.imageAlt?.addEventListener("change", () => setImageAlt(instance), { signal });
    if (instance.imageResizeHandle) {
      const handle = instance.imageResizeHandle;
      handle.addEventListener("pointerdown", event => beginImageResize(instance, event), { signal });
      handle.addEventListener("pointermove", event => dragImageResize(instance, event), { signal });
      handle.addEventListener("pointerup", event => finishImageResize(instance, event), { signal });
      handle.addEventListener("pointercancel", event => finishImageResize(instance, event, true), { signal });
      handle.addEventListener("lostpointercapture", event => finishImageResize(instance, event), { signal });
      handle.addEventListener("keydown", event => {
        if (!["ArrowLeft", "ArrowRight", "Escape"].includes(event.key)) return;
        event.preventDefault();
        if (event.key === "Escape") { finishImageResize(instance, null, true); handle.blur(); return; }
        const image = selectedImage(instance);
        if (!image) return;
        const width = Math.round(image.getBoundingClientRect().width) + (event.key === "ArrowRight" ? 10 : -10);
        setImagePixelWidth(instance, image, Math.max(24, Math.min(2500, width)));
        changed(instance);
      }, { signal });
      instance.canvas.addEventListener("scroll", () => updateResizeHandle(instance), { passive: true, signal });
      window.addEventListener("scroll", () => updateResizeHandle(instance), { capture: true, passive: true, signal });
      window.addEventListener("resize", () => updateResizeHandle(instance), { passive: true, signal });
    }
    if (instance.findPanel) {
      instance.findPanel.querySelector("[data-find-next]").addEventListener("click", () => findNext(instance), { signal });
      instance.findPanel.querySelector("[data-find-replace]").addEventListener("click", () => replaceMatch(instance), { signal });
      instance.findPanel.querySelector("[data-find-all]").addEventListener("click", () => replaceAllMatches(instance), { signal });
      instance.findPanel.querySelector("[data-find-close]").addEventListener("click", () => { instance.findPanel.hidden = true; }, { signal });
      instance.findQuery.addEventListener("keydown", event => {
        if (event.key === "Enter") { event.preventDefault(); findNext(instance); }
      }, { signal });
    }
    if (instance.characterPanel) {
      instance.characterPanel.addEventListener("mousedown", event => { if (event.target.closest("button[data-insert-character]")) event.preventDefault(); }, { signal });
      instance.characterPanel.addEventListener("click", event => {
        const character = event.target.closest("button[data-insert-character]");
        if (character) insertSpecial(instance, character.dataset.insertCharacter);
        if (event.target.closest("[data-chars-close]")) instance.characterPanel.hidden = true;
      }, { signal });
    }
    if (instance.datePanel) {
      instance.datePanel.addEventListener("click", event => {
        const button = event.target.closest("[data-date-format]");
        if (button) insertDateTime(instance, button.dataset.dateFormat);
        if (event.target.closest("[data-date-close]")) instance.datePanel.hidden = true;
      }, { signal });
    }
    if (instance.templatePanel) {
      refreshTemplateOptions(instance);
      instance.templatePanel.querySelector("[data-template-apply]").addEventListener("click", () => applyTemplate(instance), { signal });
      instance.templatePanel.querySelector("[data-template-close]").addEventListener("click", () => { instance.templatePanel.hidden = true; }, { signal });
    }
    if (instance.signaturePanel && instance.signatureCanvas) {
      const canvas = instance.signatureCanvas;
      canvas.addEventListener("pointerdown", event => {
        if (event.button !== 0 && event.pointerType === "mouse") return;
        event.preventDefault();
        if (!instance.signatureContext) showSignature(instance);
        const point = signaturePoint(instance, event);
        instance.signatureDrawing = true;
        instance.signatureHasInk = true;
        updateSignaturePen(instance);
        recordSignaturePoint(instance, point);
        instance.signatureContext.beginPath();
        instance.signatureContext.moveTo(point.x, point.y);
        instance.signatureContext.lineTo(point.x + .01, point.y + .01);
        instance.signatureContext.stroke();
        try { canvas.setPointerCapture(event.pointerId); } catch (_) { /* Browser can release capture. */ }
      }, { signal });
      canvas.addEventListener("pointermove", event => {
        if (!instance.signatureDrawing) return;
        event.preventDefault();
        const point = signaturePoint(instance, event);
        recordSignaturePoint(instance, point);
        instance.signatureContext.lineTo(point.x, point.y);
        instance.signatureContext.stroke();
      }, { signal });
      for (const type of ["pointerup", "pointercancel", "lostpointercapture"])
        canvas.addEventListener(type, () => { instance.signatureDrawing = false; }, { signal });
      instance.signatureColor?.addEventListener("change", () => updateSignaturePen(instance), { signal });
      instance.signatureWidth?.addEventListener("change", () => updateSignaturePen(instance), { signal });
      instance.signaturePanel.querySelector("[data-signature-clear]").addEventListener("click", () => clearSignature(instance), { signal });
      instance.signaturePanel.querySelector("[data-signature-insert]").addEventListener("click", () => insertSignature(instance), { signal });
      instance.signaturePanel.querySelector("[data-signature-close]").addEventListener("click", () => { instance.signaturePanel.hidden = true; clearSignature(instance); }, { signal });
    }
    // When the caret is inside a table, Tab/Shift+Tab navigate existing cells.
    // Do not create a new row or interfere with controls, modifiers, or the
    // browser's normal focus behavior at the first/last table cell.
    instance.canvas.addEventListener("keydown", event => {
      if (event.key !== "Tab" || event.altKey || event.ctrlKey || event.metaKey ||
          event.target.closest("input,textarea,select,button,a")) return;
      if (instance.menuPanel && !instance.menuPanel.hidden) closeMenu(instance);
      const selection = window.getSelection();
      if (!selection?.rangeCount || !selection.isCollapsed) return;
      const range = selection.getRangeAt(0);
      if (!instance.canvas.contains(range.startContainer)) return;
      const current = cellAtNode(instance, range.startContainer);
      if (!current || cellAtNode(instance, range.endContainer) !== current) return;
      const cells = Array.from(current.closest("table").rows).flatMap(row => Array.from(row.cells));
      const index = cells.indexOf(current);
      const next = cells[index + (event.shiftKey ? -1 : 1)];
      if (!next) return;
      event.preventDefault();
      selectTableCell(instance, next);
    }, { signal });
    // Intercept browser-native history (including Edit > Undo) so it cannot
    // diverge from Azunt.Editor's snapshots. Group IME composition (notably
    // Korean) into one undo step rather than one partial syllable per input.
    instance.canvas.addEventListener("beforeinput", event => {
      if (event.inputType === "historyUndo" || event.inputType === "historyRedo") {
        if (!event.cancelable) return; // Never compete with a non-cancelable browser edit.
        event.preventDefault();
        undoOrRedo(instance, event.inputType === "historyUndo" ? -1 : 1);
        return;
      }
      if (!instance.readOnly && !instance.sourceMode && !instance.isComposing &&
          !event.isComposing && event.cancelable &&
          ["deleteContentBackward", "deleteContentForward", "deleteByCut"].includes(event.inputType) &&
          selectionCoversDocument(instance)) {
        event.preventDefault();
        resetSelectedDocument(instance);
        return;
      }
      if (!instance.isComposing && !event.isComposing) rememberHistorySelection(instance);
    }, { signal });
    instance.canvas.addEventListener("compositionstart", () => {
      rememberHistorySelection(instance);
      instance.isComposing = true;
    }, { signal });
    instance.canvas.addEventListener("compositionend", () => {
      instance.isComposing = false;
      changed(instance);
    }, { signal });
    instance.canvas.addEventListener("input", () => {
      if (instance.isComposing) {
        updateStats(instance);
        queueEmit(instance);
      } else changed(instance);
    }, { signal });
    instance.canvas.addEventListener("change", event => {
      const control = event.target;
      if (!control?.matches?.('input[type="checkbox"],input[type="radio"]') || !instance.canvas.contains(control)) return;
      // Keep defaultChecked and the serialized attribute in sync with the live UI.
      if (control.checked) control.setAttribute("checked", "");
      else control.removeAttribute("checked");
      changed(instance);
    }, { signal });
    instance.canvas.addEventListener("blur", () => {
      window.clearTimeout(instance.emitTimer);
      instance.isComposing = false;
      remember(instance);
      emit(instance);
    }, { signal });
    instance.canvas.addEventListener("click", event => {
      const image = event.target.closest("img");
      setImageSelection(instance, image);
      const cell = event.target.closest("td,th");
      instance.activeCell = cell && instance.canvas.contains(cell) ? cell : null;
      selectTableControls(instance, instance.activeCell?.closest("table"),
        instance.activeCell ? Array.from(instance.activeCell.parentElement.cells).indexOf(instance.activeCell) : 0);
      updateContextTools(instance);
    }, { signal });
    instance.canvas.addEventListener("mouseup", () => selectedRange(instance), { signal });
    instance.canvas.addEventListener("keyup", () => {
      selectedRange(instance);
      const selection = window.getSelection();
      if (selection?.rangeCount && instance.canvas.contains(selection.getRangeAt(0).startContainer)) {
        instance.activeCell = cellAtNode(instance, selection.getRangeAt(0).startContainer);
        updateContextTools(instance);
      }
    }, { signal });
    instance.canvas.addEventListener("dragenter", event => {
      if (!isSupportedDrop(event.dataTransfer)) return;
      event.preventDefault();
      if (!instance.readOnly) instance.element.classList.add("az-azunt-editor-image-drop-active");
    }, { signal });
    instance.canvas.addEventListener("dragover", event => {
      if (!isSupportedDrop(event.dataTransfer)) return;
      event.preventDefault();
      if (event.dataTransfer) event.dataTransfer.dropEffect = instance.readOnly ? "none" : "copy";
      if (!instance.readOnly) instance.element.classList.add("az-azunt-editor-image-drop-active");
    }, { signal });
    instance.canvas.addEventListener("dragleave", event => {
      if (!instance.canvas.contains(event.relatedTarget))
        instance.element.classList.remove("az-azunt-editor-image-drop-active");
    }, { signal });
    instance.canvas.addEventListener("drop", event => {
      instance.element.classList.remove("az-azunt-editor-image-drop-active");
      if (!isSupportedDrop(event.dataTransfer)) return;
      event.preventDefault();
      if (instance.readOnly || instance.sourceMode) return;
      placeDropCaret(instance, event.clientX, event.clientY);
      const files = Array.from(event.dataTransfer?.files || []);
      if (files.length) void addImages(instance, files);
      else pasteCleanContent(instance,
        event.dataTransfer?.getData("text/html") || "",
        event.dataTransfer?.getData("text/plain") || "");
    }, { signal });
    instance.canvas.addEventListener("paste", event => {
      if (instance.readOnly) return;
      event.preventDefault();
      const files = Array.from(event.clipboardData?.files || []).filter(f => supportedTypes.has(f.type));
      if (files.length) void addImages(instance, files);
      else pasteCleanContent(instance,
        event.clipboardData?.getData("text/html") || "",
        event.clipboardData?.getData("text/plain") || "");
    }, { signal });
    document.addEventListener("selectionchange", () => {
      const selection = window.getSelection();
      if (selection?.rangeCount && instance.canvas.contains(selection.anchorNode)) {
        instance.lastRange = selection.getRangeAt(0).cloneRange();
        updateFormattingState(instance);
      }
    }, { signal });
    document.addEventListener("keydown", event => {
      if (!event.defaultPrevented && event.altKey && event.key === "F10" && !event.ctrlKey && !event.metaKey &&
          instance.element.contains(document.activeElement)) {
        const compact = instance.element.dataset.toolbarMode === "compact";
        const trigger = (compact && (menuTriggers(instance)[0]
          || instance.toolbar.querySelector('[data-azunt-menu="more"]:not(:disabled)')))
          || Array.from(instance.toolbar.querySelectorAll("button, select, input, textarea"))
            .find(control => !control.disabled && control.getClientRects().length && !instance.menuPanel?.contains(control));
        if (trigger && trigger.getClientRects().length) {
          event.preventDefault();
          if (trigger.matches('[data-compact-menu] button')) focusMenuTrigger(instance, trigger);
          else trigger.focus({ preventScroll: true });
        }
        return;
      }
      if (event.key === "Escape") {
        if (event.defaultPrevented) return;
        const owner = Array.from(instances.values()).find(other => other.menuPanel && !other.menuPanel.hidden)
          || Array.from(instances.values()).find(other => other.element.contains(document.activeElement))
          || Array.from(instances.values()).find(other => other.element.classList.contains("az-azunt-editor-fullscreen"));
        if (owner !== instance) return;
        if (instance.tableMove || instance.tableColumnResize) {
          event.preventDefault();
          finishTableMove(instance, null, true);
          finishTableColumnResize(instance, null, true);
          return;
        }
        if (instance.menuPanel && !instance.menuPanel.hidden) {
          event.preventDefault();
          closeMenu(instance, true);
          return;
        }
        if (!instance.readOnly && instance.toolbar.contains(document.activeElement)) {
          event.preventDefault();
          focusEditorContent(instance);
          return;
        }
        if (instance.element.classList.contains("az-azunt-editor-fullscreen")) {
          event.preventDefault();
          toggleFullscreen(instance, false);
        }
      }
    }, { signal });
    document.addEventListener("pointerdown", event => {
      if (!instance.toolbar.contains(event.target) && !instance.menuPanel?.hidden) closeMenu(instance);
    }, { signal });
    document.addEventListener("pointermove", event => {
      if (event.pointerType === "touch") return;
      if (event.buttons && !instance.tableColumnResize && !instance.tableMove) {
        instance.tableHoverPoint = null;
        updateTableColumnHover(instance, null);
        return;
      }
      instance.tableHoverPoint = { x: event.clientX, y: event.clientY };
      updateTableColumnHover(instance, event.target);
    }, { passive: true, signal });
    document.addEventListener("pointerout", event => {
      if (event.pointerType === "touch" || event.relatedTarget) return;
      instance.tableHoverPoint = null;
      updateTableColumnHover(instance, null);
    }, { passive: true, signal });
    window.addEventListener("blur", () => {
      finishTableColumnResize(instance, null, true);
      instance.tableHoverPoint = null;
      updateTableColumnHover(instance, null);
    }, { signal });
    window.addEventListener("scroll", () => repositionOverlays(instance), { capture: true, passive: true, signal });
    window.addEventListener("resize", () => repositionOverlays(instance), { passive: true, signal });
    window.visualViewport?.addEventListener("resize", () => repositionOverlays(instance), { passive: true, signal });
    window.visualViewport?.addEventListener("scroll", () => repositionOverlays(instance), { passive: true, signal });
    instance.canvas.addEventListener("keydown", event => {
      if (instance.readOnly || instance.isComposing || event.isComposing ||
          !(event.ctrlKey || event.metaKey)) return;
      const key = event.key.toLowerCase();
      if (key === "f") { event.preventDefault(); runCommand(instance, "find"); }
      if (key === "z") { event.preventDefault(); undoOrRedo(instance, event.shiftKey ? 1 : -1); }
      if (key === "y") { event.preventDefault(); undoOrRedo(instance, 1); }
      if (["b", "i", "u"].includes(key)) {
        event.preventDefault();
        runCommand(instance, { b: "bold", i: "italic", u: "underline" }[key]);
      }
    }, { signal });
    instance.images.addEventListener("change", () => {
      void addImages(instance, instance.images.files).finally(() => { instance.images.value = ""; });
    }, { signal });
    instance.source.addEventListener("keydown", event => {
      if (event.key === "Tab" && !event.altKey && !event.ctrlKey && !event.metaKey &&
          instance.menuPanel && !instance.menuPanel.hidden) closeMenu(instance);
    }, { signal });
    instance.source.addEventListener("input", () => {
      // Source edits remain local until switching back; MVC form/Blazor explicit Save
      // still reads sanitized source if submitted while this mode is open.
      instance.value.value = readHtml(instance);
      updateStats(instance);
      queueEmit(instance);
    }, { signal });
    const form = element.closest("form");
    if (form) form.addEventListener("submit", () => { instance.value.value = readHtml(instance); }, { signal, capture: true });

    // Detect replacement of editor children without observing every keystroke.
    // Blazor normally leaves the subtree alone; this protects unusual rerenders.
    instance.observer = new MutationObserver(() => {
      if (!bindingIsCurrent(instance, element)) {
        mount(element, instance.dotNetReference);
      }
    });
    instance.observer.observe(element, { childList: true });
    return true;
  }

  function mountAll() {
    // MVC and Razor Pages use automatic mounting. Interactive Blazor components
    // are prerendered first; touching their markup here can race with hydration.
    // Their OnAfterRenderAsync calls mount(id, dotNetReference) instead.
    document.querySelectorAll('[data-azunt-editor]:not([data-azunt-mount="manual"])')
      .forEach(element => mount(element));
  }

  // Mode changes operate on the live editor rather than replacing Blazor-owned markup.
  // In source mode use the sanitized source (the visible canvas may be stale).
  function setMode(id, mode) {
    const desired = String(mode).trim().toLowerCase();
    if (desired !== "edit" && desired !== "view")
      throw new Error("Editor mode must be Edit or View.");
    const instance = getInstance(id);
    if (instance.mode === desired) return;
    // View has no toolbar or Escape handler. Exit the editor's fullscreen
    // presentation before switching to a static read-only document.
    if (instance.element.classList.contains("az-azunt-editor-fullscreen")) toggleFullscreen(instance, false);
    closeCodePanel(instance);
    // Cancel a partially completed image drag BEFORE sending any pending HTML.
    // Otherwise Blazor can receive a transient width that View then rolls back.
    if (instance.imageResize) finishImageResize(instance, null, true);
    finishTableMove(instance, null, true);
    finishTableColumnResize(instance, null, true);
    // A mode switch can happen before the 200ms Blazor change notification.
    // Flush the pending edit before unmount() cancels its timer. This keeps
    // @bind-Value in sync without broadcasting an event for a mere mode change.
    if (instance.mode === "edit" && instance.emitTimer !== null) {
      window.clearTimeout(instance.emitTimer);
      instance.emitTimer = null;
      emit(instance);
    }
    const html = readHtml(instance);
    // The source textarea can have unsaved changes not yet in the undo stack.
    // Record them so switching back to Edit does not lose the final snapshot.
    if (instance.mode === "edit") remember(instance);
    instance.canvas.innerHTML = html;
    instance.value.value = html;
    instance.sourceMode = false;
    instance.source.hidden = true;
    instance.canvas.hidden = false;
    instance.element.dataset.editorMode = desired;
    mount(instance.element, instance.dotNetReference, true);
  }

  function getInstance(id) {
    const element = document.getElementById(id);
    if (!element || !element.matches("[data-azunt-editor]")) {
      throw new Error(`Azunt.Editor '${id}' is not present in the document.`);
    }
    // Heal a replaced DOM root even if the host asks for HTML before a later
    // OnAfterRenderAsync callback has a chance to run.
    const previous = instances.get(id);
    if (!bindingIsCurrent(previous, element)) {
      mount(element, previous?.dotNetReference || null);
    }
    return instances.get(id);
  }

  // Extension selection bookmarks are deliberately separate from history and lastRange.
  // They never fall back to the document end when a delayed host action becomes stale.
  function clearSelectionBookmark(instance) {
    instance.selectionBookmark?.observer.disconnect();
    instance.selectionBookmark = null;
  }

  function extensionInstance(id) {
    const instance = instances.get(id);
    return bindingIsCurrent(instance, document.getElementById(id)) ? instance : null;
  }

  function editableExtension(instance) {
    return instance && !instance.readOnly && !instance.sourceMode && !instance.isComposing &&
      !instance.imageResize && !instance.tableMove && !instance.tableColumnResize;
  }

  function editableEndpoint(instance, node) {
    const element = node.nodeType === Node.ELEMENT_NODE ? node : node.parentElement;
    return instance.canvas.contains(node) && !element?.closest('[contenteditable="false"]');
  }

  function saveSelection(id) {
    const instance = extensionInstance(id);
    if (!instance) return null;
    clearSelectionBookmark(instance);
    if (!editableExtension(instance)) return null;
    const selection = window.getSelection();
    let range = selection?.rangeCount ? selection.getRangeAt(0) : null;
    // A selection in another editor must not be treated as this editor's caret.
    if (!range || !instance.canvas.contains(range.commonAncestorContainer)) {
      const other = range && Array.from(instances.values()).some(i => i.canvas.contains(range.commonAncestorContainer));
      if (other) return null;
      range = instance.lastRange;
    }
    if (!range || !editableEndpoint(instance, range.startContainer) ||
        !editableEndpoint(instance, range.endContainer)) return null;
    const html = readHtml(instance);
    if (new Blob([html]).size > maxDocumentBytes) return null;
    const token = `selection-${++selectionSequence}`;
    const observer = new MutationObserver(() => {
      if (instance.selectionBookmark?.token === token) clearSelectionBookmark(instance);
    });
    // Ignore focus-only CSS/ARIA changes. A sanitized snapshot also checks
    // persisted attribute changes, including styles, image width and links.
    observer.observe(instance.canvas, { childList: true, characterData: true, subtree: true });
    instance.selectionBookmark = { token, range: range.cloneRange(), html, observer };
    return token;
  }

  function releaseSelection(id, token) {
    const instance = instances.get(id);
    if (!token || instance?.selectionBookmark?.token !== token) return false;
    clearSelectionBookmark(instance);
    return true;
  }

  function validBookmark(instance, token) {
    const saved = instance?.selectionBookmark;
    if (!token || saved?.token !== token) return null;
    if (!editableExtension(instance) || saved.observer.takeRecords().length ||
        readHtml(instance) !== saved.html || !editableEndpoint(instance, saved.range.startContainer) ||
        !editableEndpoint(instance, saved.range.endContainer)) {
      clearSelectionBookmark(instance);
      return null;
    }
    return saved;
  }

  function restoreSelection(id, token) {
    const instance = extensionInstance(id);
    if (!validBookmark(instance, token)) return false;
    instance.canvas.focus({ preventScroll: true });
    // Focus handlers in the host may change content or replace the component.
    if (extensionInstance(id) !== instance || document.activeElement !== instance.canvas) return false;
    const saved = validBookmark(instance, token);
    if (!saved) return false;
    const range = saved.range.cloneRange();
    clearSelectionBookmark(instance);
    const selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
    instance.lastRange = range.cloneRange();
    rememberHistorySelection(instance);
    updateFormattingState(instance);
    return true;
  }

  function insertExtensionFragment(id, token, clean) {
    const instance = extensionInstance(id);
    const saved = validBookmark(instance, token);
    if (!saved || !clean?.trim()) return false;
    // A conservative size check happens before touching the selection or document.
    if (new Blob([saved.html, clean]).size > maxDocumentBytes) return false;
    const template = document.createElement("template");
    template.innerHTML = clean;
    const images = Array.from(template.content.querySelectorAll('img[src^="data:"]'));
    let bytes = countInlineBytes(instance);
    for (const img of images) {
      const data = img.getAttribute("src").split(",")[1];
      const length = Math.floor(data.length * 3 / 4) - (data.endsWith("==") ? 2 : data.endsWith("=") ? 1 : 0);
      if (length > maxImageBytes) return false;
      bytes += length;
    }
    if (bytes > maxInlineImageBytes || !restoreSelection(id, token)) return false;
    // Commit the pre-insert state so host insertion is a single Undo step.
    remember(instance);
    setImageSelection(instance, null);
    insertClipboardFragment(instance, template.content);
    return true;
  }

  function insertHtml(id, token, html) {
    if (typeof html !== "string" || new Blob([html]).size > maxDocumentBytes) return false;
    return insertExtensionFragment(id, token, sanitizeHtml(html));
  }

  function insertText(id, token, text) {
    if (typeof text !== "string" || !text.length || new Blob([text]).size > maxDocumentBytes) return false;
    const element = document.createElement("div");
    text.split(/\r\n|\r|\n/).forEach((line, index) => {
      if (index) element.appendChild(document.createElement("br"));
      element.appendChild(document.createTextNode(line));
    });
    return insertExtensionFragment(id, token, element.innerHTML);
  }

  // Host supplies an already authorized image URL or small raster data URL.
  // The library does not fetch signatures, upload images or authenticate users.
  function insertImage(id, token, src, alt = "", width = null) {
    if (typeof src !== "string" || src.length > maxDocumentBytes || !safeResourceUrl(src, true)) return false;
    if (width !== null && (!Number.isInteger(width) || width < 1 || width > 2500)) return false;
    const inert = document.createElement("template");
    inert.innerHTML = "<img>";
    const img = inert.content.firstElementChild;
    img.setAttribute("src", src);
    img.setAttribute("alt", String(alt ?? "").slice(0, 250));
    if (width !== null) img.setAttribute("width", String(width));
    return insertHtml(id, token, img.outerHTML);
  }

  function getHtml(id) {
    const instance = getInstance(id);
    const html = readHtml(instance);
    instance.value.value = html;
    return html;
  }

  // .NET -> JS call with InvokeAsync<IJSStreamReference>:
  // Return a raw Blob, NOT DotNet.createJSStreamReference(Blob). The Blazor
  // JS interop runtime automatically wraps this return value as a stream.
  // Pre-wrapping it causes "Supplied value is not a typed array or blob."
  // MVC/Razor Pages continue using getHtml and form submission.
  function getHtmlStream(id) {
    return createHtmlBlob(getHtml(id));
  }

  function setHtml(id, html) {
    const instance = getInstance(id);
    closeCodePanel(instance);
    clearSelectionBookmark(instance);
    // Replacing a document must invalidate any pending asynchronous image read.
    instance.imageInsertGeneration++;
    if (instance.imageResize) finishImageResize(instance, null, true);
    finishTableMove(instance, null, true);
    finishTableColumnResize(instance, null, true);
    instance.sourceMode = false;
    instance.source.hidden = true;
    instance.canvas.hidden = false;
    instance.canvas.innerHTML = sanitizeHtml(html);
    decorateCodeBlocks(instance);
    setDocumentControlsReadOnly(instance);
    instance.lastRange = null;
    setImageSelection(instance, null);
    instance.activeCell = null;
    selectTableControls(instance, null);
    instance.history = [readHtml(instance)];
    instance.historySelections = [null];
    instance.historyIndex = 0;
    updateStats(instance);
    if (instance.mode === "edit") updateFormattingState(instance);
    // Programmatic SetHtml is allowed in both modes; View never makes the DOM editable.
    emit(instance);
  }

  function unmount(id) {
    const instance = instances.get(id);
    if (!instance) return;
    clearSelectionBookmark(instance);
    if (instance.imageResize) finishImageResize(instance, null, true);
    finishTableMove(instance, null, true);
    finishTableColumnResize(instance, null, true);
    clearTableHandles(instance);
    if (instance.tableControlsOwned) {
      instance.tableMoveHandle?.remove();
      instance.tableColumnHandle?.remove();
      instance.tableDropMarker?.remove();
    }
    instance.toolbarSizeObserver?.disconnect();
    closeMenu(instance);
    instance.controller.abort();
    instance.element.classList.remove("az-azunt-editor-fullscreen", "az-azunt-editor-image-drop-active");
    instance.observer?.disconnect();
    window.clearTimeout(instance.emitTimer);
    window.clearTimeout(instance.messageTimer);
    instances.delete(id);
  }

  function getPageSettings(id) {
    const instance = instances.get(id);
    if (!instance) throw new Error("Editor instance is unavailable.");
    return { paperSize: instance.page.paper, orientation: instance.page.orientation, margin: instance.page.margin };
  }

  window.AzuntEditor = Object.freeze({ mount, mountAll, getHtml, getHtmlStream, setHtml, setMode, getPageSettings, showStatus, unmount, sanitizeHtml, registerTemplate, registerCommand, unregisterCommand, setCommands, setToolbarMode, setContentRendering, getContentRendering, setDocumentStyle, getDocumentStyleId, registerDocumentStyles, setShowMenuBar, setShowToolbarGroupLabels, setViewOptions, getViewOptions, saveSelection, restoreSelection, releaseSelection, insertHtml, insertText, insertImage });
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", mountAll, { once: true });
  else mountAll();
})();
