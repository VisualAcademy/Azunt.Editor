/* Azunt.Editor 0.2.0. Self-owned editing engine, no third-party JS. */
(function () {
  "use strict";

  const instances = new Map();
  const allowedTags = new Set([
    "p", "div", "h2", "h3", "strong", "b", "em", "i", "u", "s", "ul", "ol", "li",
    "blockquote", "pre", "code", "sup", "sub", "table", "thead", "tbody", "tr", "td", "th", "br", "hr", "img", "a", "span"
  ]);
  const dropWithChildren = new Set([
    "script", "style", "iframe", "frame", "frameset", "object", "embed", "svg", "math", "template",
    "form", "input", "textarea", "button", "select", "option", "link", "meta", "base", "video", "audio"
  ]);
  const imageDataUrlPattern = /^data:image\/(?:png|jpeg|gif|webp);base64,[A-Za-z0-9+/]+={0,2}$/i;
  const supportedTypes = new Set(["image/png", "image/jpeg", "image/gif", "image/webp"]);
  const maxImageBytes = 2 * 1024 * 1024;
  const maxInlineImageBytes = 8 * 1024 * 1024;
  // Keep regular JS -> .NET interop well below Blazor Server's default 32 KiB
  // SignalR message limit. Large documents use Microsoft's JS stream references.
  const inlineInteropMaxBytes = 16 * 1024;
  const maxDocumentBytes = 16 * 1024 * 1024;
  const allowedFonts = new Set(["Arial", "Georgia", "Times New Roman", "Verdana", "Segoe UI", "Noto Sans KR", "Malgun Gothic"]);
  const hexColor = /^#[0-9a-f]{6}$/i;
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
      const clean = document.createElement(tag);
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
      if (tag === "a") {
        const href = safeResourceUrl(node.getAttribute("href"), false);
        if (href) {
          clean.setAttribute("href", href);
          clean.setAttribute("rel", "noopener noreferrer");
        }
      }
      // Only allow narrowly-validated formatting properties. Never carry arbitrary CSS.
      if (tag === "span" || ["p", "div", "h2", "h3", "li", "td", "th"].includes(tag)) {
        const family = (node.style.fontFamily || "").replace(/^['"]|['"]$/g, "");
        if (allowedFonts.has(family)) clean.style.fontFamily = family.includes(" ") ? `"${family}"` : family;
        const size = /^(\d{1,2})px$/.exec(node.style.fontSize || "");
        if (size && Number(size[1]) >= 8 && Number(size[1]) <= 72) clean.style.fontSize = `${Number(size[1])}px`;
        const color = cleanColor(node.style.color);
        const background = cleanColor(node.style.backgroundColor);
        if (color) clean.style.color = color;
        if (background) clean.style.backgroundColor = background;
      }
      if (["p", "div", "h2", "h3", "li", "td", "th", "blockquote"].includes(tag)) {
        const align = (node.style.textAlign || "").toLowerCase();
        if (["left", "center", "right", "justify"].includes(align)) clean.style.textAlign = align;
        const margin = /^(\d{1,3})px$/.exec(node.style.marginLeft || "");
        if (margin && Number(margin[1]) <= 240 && Number(margin[1]) % 24 === 0)
          clean.style.marginLeft = `${Number(margin[1])}px`;
      }
      for (const child of Array.from(node.childNodes)) visit(child, clean);
      destination.appendChild(clean);
    }

    for (const node of Array.from(inert.content.childNodes)) visit(node, output);
    return output.innerHTML;
  }

  function message(instance, text) {
    instance.status.textContent = text;
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
        message(instance, "Blazor synchronization failed. Check the connection and browser console.");
      });
    } catch (error) {
      console.warn("Azunt.Editor Blazor synchronization failed:", error);
      message(instance, String(error.message || error));
    }
  }

  function queueEmit(instance) {
    window.clearTimeout(instance.emitTimer);
    instance.emitTimer = window.setTimeout(() => emit(instance), 200);
  }

  function remember(instance) {
    const html = readHtml(instance);
    if (instance.history[instance.historyIndex] === html) return;
    instance.history = instance.history.slice(0, instance.historyIndex + 1);
    instance.history.push(html);
    if (instance.history.length > 60) instance.history.shift();
    instance.historyIndex = instance.history.length - 1;
  }

  function changed(instance) {
    remember(instance);
    queueEmit(instance);
  }

  function selectedRange(instance) {
    const selection = window.getSelection();
    if (selection && selection.rangeCount) {
      const range = selection.getRangeAt(0);
      if (instance.canvas.contains(range.commonAncestorContainer)) {
        instance.lastRange = range.cloneRange();
        return range;
      }
    }
    if (instance.lastRange && instance.canvas.contains(instance.lastRange.commonAncestorContainer)) {
      selection.removeAllRanges();
      selection.addRange(instance.lastRange);
      return instance.lastRange;
    }
    const range = document.createRange();
    range.selectNodeContents(instance.canvas);
    range.collapse(false);
    selection.removeAllRanges();
    selection.addRange(range);
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
      message(instance, "Select text before applying a style.");
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
      message(instance, "Select text before applying this formatting.");
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
    if (!block) return message(instance, "Place the cursor inside a paragraph first.");
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
    if (!url) return message(instance, "Enter a valid HTTPS, HTTP, mailto, or relative URL.");
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
    if (!active) return message(instance, "Place the cursor inside a link to remove it.");
    const parent = active.parentNode;
    while (active.firstChild) parent.insertBefore(active.firstChild, active);
    active.remove();
    changed(instance);
  }

  function toggleSource(instance) {
    if (instance.readOnly) return;
    if (!instance.sourceMode) {
      instance.source.value = sanitizeHtml(instance.canvas.innerHTML);
      instance.source.hidden = false;
      instance.canvas.hidden = true;
      instance.sourceMode = true;
      instance.source.focus();
      message(instance, "HTML source mode. Select </> again to apply permitted markup.");
    } else {
      const clean = sanitizeHtml(instance.source.value);
      instance.canvas.innerHTML = clean;
      instance.sourceMode = false;
      instance.source.hidden = true;
      instance.canvas.hidden = false;
      instance.lastRange = null;
      changed(instance);
      message(instance, "HTML source applied; unsupported markup was removed.");
    }
  }

  function selectedImage(instance) {
    return instance.selectedImage && instance.canvas.contains(instance.selectedImage) ? instance.selectedImage : null;
  }

  function setImageSelection(instance, image) {
    if (instance.selectedImage) instance.selectedImage.classList.remove("azunt-editor-image-selected");
    instance.selectedImage = image && instance.canvas.contains(image) ? image : null;
    if (instance.selectedImage) {
      instance.selectedImage.classList.add("azunt-editor-image-selected");
      instance.imageWidth.value = String(instance.selectedImage.getAttribute("width") || instance.selectedImage.naturalWidth || "");
    } else {
      instance.imageWidth.value = "";
    }
  }

  function resizeImage(instance) {
    const image = selectedImage(instance);
    if (!image) return message(instance, "Click an image to select it first.");
    const width = Number(instance.imageWidth.value);
    if (!Number.isInteger(width) || width < 24 || width > 2500)
      return message(instance, "Image width must be between 24 and 2500 pixels.");
    image.setAttribute("width", String(width));
    image.removeAttribute("height");
    changed(instance);
  }

  function alignImage(instance, alignment) {
    const image = selectedImage(instance);
    if (!image) return message(instance, "Click an image to select it first.");
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
    if (!image) return message(instance, "Click an image to select it first.");
    const parent = image.parentElement;
    setImageSelection(instance, null);
    image.remove();
    if (parent && parent !== instance.canvas && parent.tagName === "P" && !parent.textContent?.trim() && !parent.querySelector("img")) parent.remove();
    changed(instance);
  }

  function activeTableCell(instance) {
    if (instance.activeCell && instance.canvas.contains(instance.activeCell)) return instance.activeCell;
    const cell = editableAncestor(instance, ["TD", "TH"]);
    return cell && instance.canvas.contains(cell) ? cell : null;
  }

  function modifyTable(instance, operation) {
    const cell = activeTableCell(instance);
    if (!cell) return message(instance, "Click a table cell first.");
    const row = cell.closest("tr");
    const table = cell.closest("table");
    if (!row || !table) return;
    const rows = Array.from(table.rows);
    const rowIndex = rows.indexOf(row);
    const colIndex = Array.from(row.cells).indexOf(cell);
    if (rows.some(r => r.cells.length !== row.cells.length || Array.from(r.cells).some(c => c.colSpan !== 1 || c.rowSpan !== 1))) {
      return message(instance, "Table editing currently supports rectangular tables without merged cells.");
    }
    if (operation === "table-delete") {
      table.remove();
    } else if (operation === "table-row-after") {
      const added = row.cloneNode(false);
      for (let index = 0; index < row.cells.length; index++) added.insertCell().appendChild(document.createElement("br"));
      row.after(added);
      instance.activeCell = added.cells[Math.min(colIndex, added.cells.length - 1)];
    } else if (operation === "table-row-delete") {
      if (rows.length === 1) table.remove(); else row.remove();
      instance.activeCell = null;
    } else if (operation === "table-col-after") {
      for (const r of rows) r.insertCell(colIndex + 1).appendChild(document.createElement("br"));
      instance.activeCell = row.cells[colIndex + 1];
    } else if (operation === "table-col-delete") {
      if (row.cells.length === 1) table.remove(); else for (const r of rows) r.deleteCell(colIndex);
      instance.activeCell = null;
    }
    instance.lastRange = null;
    changed(instance);
  }

  function nearestBlock(instance, range) {
    let current = range.startContainer.nodeType === Node.ELEMENT_NODE ? range.startContainer : range.startContainer.parentElement;
    while (current && current !== instance.canvas) {
      if (["P", "DIV", "H2", "H3", "LI", "BLOCKQUOTE"].includes(current.tagName)) return current;
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
      message(instance, "Place the cursor in a paragraph to change alignment.");
      return;
    }
    block.style.textAlign = alignment;
    changed(instance);
  }

  function insertTable(instance) {
    const table = document.createElement("table");
    const body = table.createTBody();
    for (let rowNumber = 0; rowNumber < 2; rowNumber++) {
      const row = body.insertRow();
      for (let columnNumber = 0; columnNumber < 2; columnNumber++) {
        const cell = row.insertCell();
        cell.appendChild(document.createElement("br"));
      }
    }
    insertAtSelection(instance, table);
    instance.activeCell = table.rows[0].cells[0];
  }

  function undoOrRedo(instance, delta) {
    if (delta < 0) remember(instance);
    const next = instance.historyIndex + delta;
    if (next < 0 || next >= instance.history.length) return;
    instance.historyIndex = next;
    instance.canvas.innerHTML = instance.history[next];
    instance.lastRange = null;
    setImageSelection(instance, null);
    instance.activeCell = null;
    queueEmit(instance);
  }

  function countInlineBytes(instance) {
    return Array.from(instance.canvas.querySelectorAll("img[src^='data:']"))
      .reduce((sum, image) => sum + Math.ceil((image.getAttribute("src") || "").length * 0.75), 0);
  }

  function asDataUrl(file) {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(String(reader.result || ""));
      reader.onerror = () => reject(new Error("Image could not be read."));
      reader.readAsDataURL(file);
    });
  }

  async function imageUrl(instance, file) {
    if (!instance.uploadUrl) {
      if (countInlineBytes(instance) + file.size > maxInlineImageBytes) {
        throw new Error("The inline image limit is 8 MB per document. Configure an upload URL for larger documents.");
      }
      return asDataUrl(file);
    }
    const form = new FormData();
    form.append("file", file, file.name);
    const headers = {};
    const requestToken = instance.element.closest("form")?.querySelector('input[name="__RequestVerificationToken"]');
    if (requestToken) headers.RequestVerificationToken = requestToken.value;
    const response = await fetch(instance.uploadUrl, { method: "POST", body: form, headers, credentials: "same-origin" });
    if (!response.ok) throw new Error(`Image upload failed (${response.status}).`);
    const result = await response.json();
    const url = safeResourceUrl(result.url, true);
    if (!url) throw new Error("Image upload response contains an invalid URL.");
    return url;
  }

  async function addImages(instance, files) {
    if (instance.readOnly) return;
    const images = Array.from(files).slice(0, 10);
    if (!images.length) return;
    selectedRange(instance); // Save the current caret position before the file dialog.
    let inserted = 0;
    for (const file of images) {
      if (!supportedTypes.has(file.type) || file.size > maxImageBytes) {
        message(instance, `Skipped ${file.name}: only PNG/JPEG/GIF/WebP up to 2 MB are supported.`);
        continue;
      }
      try {
        const url = await imageUrl(instance, file);
        const paragraph = document.createElement("p");
        const image = document.createElement("img");
        image.setAttribute("src", url);
        image.setAttribute("alt", file.name);
        image.setAttribute("width", "480");
        image.style.maxWidth = "100%";
        paragraph.appendChild(image);
        insertAtSelection(instance, paragraph);
        setImageSelection(instance, image);
        instance.lastRange = window.getSelection().getRangeAt(0).cloneRange();
        inserted++;
      } catch (error) {
        message(instance, String(error.message || error));
      }
    }
    if (inserted) message(instance, `${inserted} image(s) inserted.`);
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
    const range = selectedRange(instance);
    range.deleteContents();
    range.insertNode(fragment);
    selectAfter(marker);
    changed(instance);
  }

  function printDocument(instance, previewOnly = false) {
    const html = readHtml(instance);
    const paper = allowedPageSizes.has(instance.page.paper) ? instance.page.paper : "A4";
    const orientation = allowedOrientations.has(instance.page.orientation) ? instance.page.orientation : "portrait";
    const margin = allowedMargins[instance.page.margin] || allowedMargins.normal;
    // Open synchronously as part of the user's click to avoid popup blockers.
    const win = window.open("", "_blank", "width=1000,height=850");
    if (!win) return message(instance, "Please allow popups to preview or print.");
    win.opener = null;
    const style = `@page{size:${paper} ${orientation};margin:${margin}}` +
      `*{box-sizing:border-box}body{font:12pt/1.5 Arial,sans-serif;color:#1e293b;margin:0}` +
      `#azunt-print-controls{position:sticky;top:0;padding:12px;background:#e2e8f0;z-index:10}` +
      `#azunt-print-controls button{padding:8px 15px;cursor:pointer}` +
      `.azunt-print-page{margin:15px auto;padding:${margin};background:#fff;max-width:${orientation === "landscape" ? "297mm" : "210mm"};min-height:200mm;box-shadow:0 1px 12px #ccc}` +
      `.azunt-print-page img{max-width:100%;height:auto}` +
      `.azunt-print-page table{border-collapse:collapse;width:100%;break-inside:auto}` +
      `.azunt-print-page tr{break-inside:avoid}` +
      `.azunt-print-page th,.azunt-print-page td{border:1px solid #aaa;padding:6px}` +
      `@media print{#azunt-print-controls{display:none}.azunt-print-page{margin:0;padding:0;max-width:none;min-height:0;box-shadow:none}}`;
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
    button.addEventListener("click", () => win.print());
    controls.appendChild(button);
    controls.appendChild(win.document.createTextNode(`  ${paper} · ${orientation} · ${instance.page.margin} margins`));
    const page = win.document.createElement("main");
    page.className = "azunt-print-page";
    page.innerHTML = html; // Already restricted by sanitizeHtml; do not inject raw user HTML.
    win.document.body.append(controls, page);
    const ready = Array.from(page.querySelectorAll("img")).map(image => image.complete
      ? Promise.resolve()
      : new Promise(resolve => {
          image.addEventListener("load", resolve, { once: true });
          image.addEventListener("error", resolve, { once: true });
        }));
    if (!previewOnly) Promise.all(ready).then(() => { if (!win.closed) { win.focus(); win.print(); } });
  }

  function runSelect(instance, field, value) {
    if (["paper", "orientation", "margin"].includes(field)) {
      instance.page[field] = value;
      return;
    }
    if (instance.readOnly) return;
    if (instance.sourceMode) return message(instance, "Apply HTML source before using formatting tools.");
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
    if (command === "preview") return printDocument(instance, true);
    if (command === "print") return printDocument(instance, false);
    if (instance.readOnly) return;
    if (command === "source") return toggleSource(instance);
    if (instance.sourceMode) return message(instance, "Apply HTML source before using formatting tools.");
    if (command === "image-delete") return deleteImage(instance);
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

  // Blazor can preserve the outer editor node while replacing its children.
  // The attached canvas/toolbar must be the nodes that are actually visible.
  function bindingIsCurrent(instance, element) {
    return instance && instance.element === element && element.isConnected &&
      instance.canvas === element.querySelector(".azunt-editor-canvas") &&
      instance.toolbar === element.querySelector(".azunt-editor-toolbar") &&
      instance.value === element.querySelector("[data-azunt-value]") &&
      instance.images === element.querySelector("[data-azunt-images]") &&
      instance.source === element.querySelector("[data-azunt-source]") &&
      instance.linkPanel === element.querySelector("[data-link-panel]");
  }

  function mount(id, dotNetReference) {
    const element = typeof id === "string" ? document.getElementById(id) : id;
    if (!element || !element.matches("[data-azunt-editor]")) return false;
    const previous = instances.get(element.id);
    if (bindingIsCurrent(previous, element)) {
      if (dotNetReference) previous.dotNetReference = dotNetReference;
      return true;
    }
    // Preserve pending edits if a framework replaced only the canvas/toolbar.
    // Prefer visible canvas content where it exists; detached nodes can be stale.
    const currentCanvas = element.querySelector(".azunt-editor-canvas");
    const visibleHtml = currentCanvas?.innerHTML || "";
    const pendingHtml = previous?.canvas ? readHtml(previous) : "";
    const sameRoot = previous && previous.element === element;
    if (previous) {
      dotNetReference = dotNetReference || previous.dotNetReference;
      unmount(element.id);
    }
    const controller = new AbortController();
    const instance = {
      element, controller, canvas: currentCanvas,
      toolbar: element.querySelector(".azunt-editor-toolbar"),
      value: element.querySelector("[data-azunt-value]"),
      images: element.querySelector("[data-azunt-images]"),
      source: element.querySelector("[data-azunt-source]"),
      linkPanel: element.querySelector("[data-link-panel]"),
      linkText: element.querySelector("[data-link-text]"),
      linkUrl: element.querySelector("[data-link-url]"),
      imageWidth: element.querySelector("[data-image-width]"),
      status: element.querySelector(".azunt-editor-message"),
      readOnly: element.dataset.readOnly === "true", uploadUrl: element.dataset.uploadUrl || null,
      dotNetReference: dotNetReference || null, emitTimer: null, history: [], historyIndex: 0, lastRange: null,
      revision: previous?.revision || 0, sourceMode: false,
      selectedImage: null, activeCell: null, editingLink: null,
      page: previous?.page || { paper: "A4", orientation: "portrait", margin: "normal" }
    };
    // On first mount the canvas is empty and the textarea owns the initial HTML.
    // After a Blazor subtree replacement, a visible edit wins over the textarea;
    // otherwise carry over the previous canvas content from this same root.
    const startingHtml = visibleHtml.trim() ? visibleHtml
      : (sameRoot ? pendingHtml : instance.value.value);
    instance.canvas.innerHTML = sanitizeHtml(startingHtml);
    instance.value.value = readHtml(instance);
    instance.history = [instance.value.value];
    instances.set(element.id, instance);

    const signal = controller.signal;
    instance.toolbar.addEventListener("mousedown", event => {
      if (event.target.closest("button")) event.preventDefault();
    }, { signal });
    instance.toolbar.addEventListener("click", event => {
      const button = event.target.closest("button[data-command]");
      if (button) runCommand(instance, button.dataset.command);
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
      if (!["paper", "orientation", "margin"].includes(target.dataset.select) && target.type !== "color") target.selectedIndex = 0;
    }, { signal });
    instance.linkPanel.querySelector("[data-link-apply]").addEventListener("click", () => applyLink(instance), { signal });
    instance.linkPanel.querySelector("[data-link-cancel]").addEventListener("click", () => {
      instance.linkPanel.hidden = true;
      instance.editingLink = null;
    }, { signal });
    instance.imageWidth.addEventListener("change", () => resizeImage(instance), { signal });
    instance.canvas.addEventListener("input", () => changed(instance), { signal });
    instance.canvas.addEventListener("blur", () => {
      window.clearTimeout(instance.emitTimer);
      remember(instance);
      emit(instance);
    }, { signal });
    instance.canvas.addEventListener("click", event => {
      const image = event.target.closest("img");
      setImageSelection(instance, image);
      const cell = event.target.closest("td,th");
      instance.activeCell = cell && instance.canvas.contains(cell) ? cell : null;
    }, { signal });
    instance.canvas.addEventListener("mouseup", () => selectedRange(instance), { signal });
    instance.canvas.addEventListener("keyup", () => selectedRange(instance), { signal });
    instance.canvas.addEventListener("paste", event => {
      if (instance.readOnly) return;
      event.preventDefault();
      const files = Array.from(event.clipboardData?.files || []).filter(f => supportedTypes.has(f.type));
      if (files.length) void addImages(instance, files);
      else insertPlainText(instance, event.clipboardData?.getData("text/plain") || "");
    }, { signal });
    instance.canvas.addEventListener("keydown", event => {
      if (instance.readOnly || !(event.ctrlKey || event.metaKey)) return;
      const key = event.key.toLowerCase();
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
    instance.source.addEventListener("input", () => {
      // Source edits remain local until switching back; MVC form/Blazor explicit Save
      // still reads sanitized source if submitted while this mode is open.
      instance.value.value = readHtml(instance);
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
    document.querySelectorAll("[data-azunt-editor]").forEach(element => mount(element));
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
    instance.sourceMode = false;
    instance.source.hidden = true;
    instance.canvas.hidden = false;
    instance.canvas.innerHTML = sanitizeHtml(html);
    instance.lastRange = null;
    setImageSelection(instance, null);
    instance.activeCell = null;
    instance.history = [readHtml(instance)];
    instance.historyIndex = 0;
    emit(instance);
  }

  function unmount(id) {
    const instance = instances.get(id);
    if (!instance) return;
    instance.controller.abort();
    instance.observer?.disconnect();
    window.clearTimeout(instance.emitTimer);
    instances.delete(id);
  }

  window.AzuntEditor = Object.freeze({ mount, mountAll, getHtml, getHtmlStream, setHtml, unmount, sanitizeHtml });
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", mountAll, { once: true });
  else mountAll();
})();
