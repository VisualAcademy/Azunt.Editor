/* Azunt.Editor shared editing engine, no third-party JavaScript dependencies. */
(function () {
  "use strict";

  const instances = new Map();
  // Built-in templates are safe, deliberately small HTML examples. Host code can
  // register additional content via AzuntEditor.registerTemplate (no CDN/network).
  const templates = new Map([
    ["blank", { title: "Blank document", html: "<p><br></p>" }],
    ["meeting", { title: "Meeting notes", html: "<h2>Meeting notes</h2><p>Date: </p><p>Attendees: </p><h3>Agenda</h3><ul><li>Agenda item</li></ul><h3>Decisions</h3><p><br></p><h3>Action items</h3><table><tbody><tr><th>Action</th><th>Owner</th><th>Due</th></tr><tr><td><br></td><td><br></td><td><br></td></tr></tbody></table>" }],
    ["report", { title: "Simple report", html: "<h2>Report title</h2><p>Date: </p><h3>Summary</h3><p><br></p><h3>Details</h3><table><tbody><tr><th>Item</th><th>Notes</th></tr><tr><td><br></td><td><br></td></tr></tbody></table><h3>Conclusion</h3><p><br></p>" }],
    ["letter", { title: "Letter", html: "<p>Date: </p><p>To: </p><p>Subject: </p><p>Dear recipient,</p><p><br></p><p>Sincerely,</p><p>Your name</p>" }]
  ]);
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
  // Base64 images can make each undo snapshot several MB. Bound the total
  // browser history instead of retaining 60 full copies of large documents.
  const maxHistoryBytes = 32 * 1024 * 1024;
  const maxHistoryEntries = 60;
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
      if (["table", "td", "th"].includes(tag)) {
        const borderColor = cleanColor(node.style.borderColor);
        const borderWidth = /^([1-4])px$/.exec(node.style.borderWidth || "");
        if (borderColor) clean.style.borderColor = borderColor;
        if (borderWidth) clean.style.borderWidth = `${borderWidth[1]}px`;
        if (borderColor || borderWidth) clean.style.borderStyle = "solid";
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
    // Always retain at least the current snapshot, even if it alone exceeds
    // the history budget. Large image-rich documents intentionally have fewer
    // undo steps to avoid exhausting browser memory.
    let totalBytes = instance.history.reduce((total, value) => total + new Blob([value]).size, 0);
    while (instance.history.length > 1 &&
      (instance.history.length > maxHistoryEntries || totalBytes > maxHistoryBytes)) {
      totalBytes -= new Blob([instance.history.shift()]).size;
    }
    instance.historyIndex = instance.history.length - 1;
  }

  function updateStats(instance) {
    if (!instance.stats) return;
    // textContent excludes image Base64 and HTML markup. Do not treat code or
    // attributes as words, and do not persist view-only counters into HTML.
    const text = instance.sourceMode
      ? (() => { const node = document.createElement("div"); node.innerHTML = sanitizeHtml(instance.source.value); return node.textContent || ""; })()
      : instance.canvas.innerText || instance.canvas.textContent || "";
    const count = (text.match(/[\p{L}\p{N}]+(?:['’‐-―-][\p{L}\p{N}]+)*/gu) || []).length;
    instance.stats.textContent = `${count} word${count === 1 ? "" : "s"} · ${Array.from(text).length} characters`;
  }

  function changed(instance) {
    remember(instance);
    updateStats(instance);
    updateResizeHandle(instance);
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
      updateResizeHandle(instance);
      instance.source.focus();
      updateStats(instance);
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

  function updateResizeHandle(instance) {
    const image = selectedImage(instance);
    const handle = instance.imageResizeHandle;
    if (!handle) return;
    if (!image || instance.sourceMode || instance.readOnly || !instance.element.isConnected) {
      handle.hidden = true;
      return;
    }
    const imageRect = image.getBoundingClientRect();
    const editorRect = instance.element.getBoundingClientRect();
    // The handle is outside contenteditable: it is never included in saved HTML.
    if (!imageRect.width || !imageRect.height || imageRect.bottom < 0 || imageRect.top > window.innerHeight) {
      handle.hidden = true;
      return;
    }
    handle.style.left = `${imageRect.right - editorRect.left - 9}px`;
    handle.style.top = `${imageRect.bottom - editorRect.top - 9}px`;
    handle.hidden = false;
  }

  function setImageSelection(instance, image) {
    if (instance.selectedImage) instance.selectedImage.classList.remove("azunt-editor-image-selected");
    instance.selectedImage = image && instance.canvas.contains(image) ? image : null;
    if (instance.selectedImage) {
      instance.selectedImage.classList.add("azunt-editor-image-selected");
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
    if (!image) return message(instance, "Click an image to select it first.");
    image.removeAttribute("width");
    image.removeAttribute("height");
    image.style.removeProperty("width");
    image.style.removeProperty("height");
    instance.imageWidth.value = "";
    updateResizeHandle(instance);
    changed(instance);
    message(instance, "Original image dimensions restored. Large images fit the editor width on screen.");
  }

  function resizeImage(instance) {
    const image = selectedImage(instance);
    if (!image) return message(instance, "Click an image to select it first.");
    if (instance.imageWidth.value.trim() === "") return restoreOriginalImageSize(instance);
    const width = Number(instance.imageWidth.value);
    if (!Number.isInteger(width) || width < 24 || width > 2500)
      return message(instance, "Image width must be between 24 and 2500 pixels.");
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
      displayWidth: image.getBoundingClientRect().width,
      moved: false
    };
    handle.classList.add("is-resizing");
    try { handle.setPointerCapture(event.pointerId); } catch (_) { /* Pointer capture is optional. */ }
  }

  function dragImageResize(instance, event) {
    const state = instance.imageResize;
    if (!state || event.pointerId !== state.pointerId || !instance.canvas.contains(state.image)) return;
    event.preventDefault();
    // Limit the size to the editable area; export preserves the chosen width.
    const maxVisible = Math.min(2500, Math.max(24, instance.canvas.clientWidth - 48));
    const width = Math.round(Math.max(24, Math.min(maxVisible, state.displayWidth + event.clientX - state.startX)));
    if (Math.abs(width - state.displayWidth) >= 1) state.moved = true;
    setImagePixelWidth(instance, state.image, width);
  }

  function finishImageResize(instance, event, cancelled = false) {
    const state = instance.imageResize;
    if (!state || (event?.pointerId !== undefined && event.pointerId !== state.pointerId)) return;
    instance.imageResize = null;
    instance.imageResizeHandle?.classList.remove("is-resizing");
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
      message(instance, `Image resized to ${state.image.getAttribute("width")} px wide (aspect ratio preserved).`);
    }
    updateResizeHandle(instance);
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

  function mergeTableRight(instance) {
    const cell = activeTableCell(instance);
    if (!cell) return message(instance, "Click a table cell first.");
    const right = cell.nextElementSibling;
    if (!right || !["TD", "TH"].includes(right.tagName) || cell.rowSpan !== 1 || right.rowSpan !== 1)
      return message(instance, "No adjacent cell available for merging.");
    const width = cell.colSpan + right.colSpan;
    if (width > 12) return message(instance, "Merged cell cannot exceed 12 columns.");
    if (right.textContent?.trim() || right.querySelector("img")) {
      if (cell.textContent?.trim() || cell.querySelector("img")) cell.appendChild(document.createElement("br"));
      while (right.firstChild) cell.appendChild(right.firstChild);
    }
    cell.colSpan = width;
    right.remove();
    instance.activeCell = cell;
    instance.lastRange = null;
    changed(instance);
    message(instance, `Merged ${width} columns. Split before adding/deleting rows or columns.`);
  }

  function splitTableCell(instance) {
    const cell = activeTableCell(instance);
    if (!cell) return message(instance, "Click a merged table cell first.");
    const count = cell.colSpan;
    if (count < 2) return message(instance, "This cell is not merged.");
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
    instance.activeCell = cell;
    instance.lastRange = null;
    changed(instance);
    message(instance, `Split into ${count} cells. Content remains in the first cell.`);
  }

  function updateTableBorders(instance, field, value) {
    const cell = activeTableCell(instance);
    if (!cell) return message(instance, "Click a table cell before setting its table border.");
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
      if (!context) return message(instance, "Signature canvas is unavailable.");
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
    if (!instance.signatureHasInk) return message(instance, "Draw a signature before inserting it.");
    const result = signatureImage(instance);
    if (!result || !imageDataUrlPattern.test(result.dataUrl) || result.dataUrl.length > 2 * 1024 * 1024)
      return message(instance, "Signature image is too large.");
    const image = document.createElement("img");
    image.src = result.dataUrl;
    image.alt = "Signature";
    // Insert the tightly cropped signature at its natural pixel size.
    insertAtSelection(instance, image);
    instance.signaturePanel.hidden = true;
    clearSignature(instance);
    message(instance, "Cropped signature inserted (image only, not a verified digital signature).");
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
    if (!selected) return message(instance, "Choose a template first.");
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
    message(instance, `Loaded template: ${selected.title}`);
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
    if (!cell) return message(instance, "Click a table cell first.");
    const row = cell.closest("tr");
    const table = cell.closest("table");
    if (!row || !table) return;
    const rows = Array.from(table.rows);
    const colIndex = Array.from(row.cells).indexOf(cell);
    if (rows.some(r => r.cells.length !== row.cells.length || Array.from(r.cells).some(c => c.colSpan !== 1 || c.rowSpan !== 1))) {
      return message(instance, "Split merged cells before changing rows, columns, or header status.");
    }
    const emptyCell = tag => {
      const result = document.createElement(tag);
      result.appendChild(document.createElement("br"));
      return result;
    };
    if (operation === "table-delete") {
      table.remove();
      instance.activeCell = null;
    } else if (operation === "table-row-before" || operation === "table-row-after") {
      const added = document.createElement("tr");
      for (const existing of Array.from(row.cells)) added.appendChild(emptyCell(existing.tagName.toLowerCase()));
      if (operation === "table-row-before") row.before(added); else row.after(added);
      instance.activeCell = added.cells[Math.max(0, colIndex)];
    } else if (operation === "table-row-delete") {
      if (rows.length === 1) table.remove(); else row.remove();
      instance.activeCell = null;
    } else if (operation === "table-col-before" || operation === "table-col-after") {
      const insertAt = colIndex + (operation === "table-col-after" ? 1 : 0);
      for (const r of rows) {
        const reference = r.cells[insertAt] || null;
        const styleSource = r.cells[Math.min(colIndex, r.cells.length - 1)];
        const inserted = emptyCell(styleSource.tagName.toLowerCase());
        r.insertBefore(inserted, reference);
        if (r === row) instance.activeCell = inserted;
      }
    } else if (operation === "table-col-delete") {
      if (row.cells.length === 1) table.remove(); else for (const r of rows) r.deleteCell(colIndex);
      instance.activeCell = null;
    } else if (operation === "table-header-toggle") {
      const nextTag = Array.from(row.cells).every(c => c.tagName === "TH") ? "td" : "th";
      for (const oldCell of Array.from(row.cells)) {
        const replacement = document.createElement(nextTag);
        for (const attribute of Array.from(oldCell.attributes)) replacement.setAttribute(attribute.name, attribute.value);
        while (oldCell.firstChild) replacement.appendChild(oldCell.firstChild);
        if (oldCell === cell) instance.activeCell = replacement;
        oldCell.replaceWith(replacement);
      }
      message(instance, nextTag === "th" ? "Header row enabled." : "Header row removed.");
    } else {
      return;
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
    setImageSelection(instance, null);
    instance.lastRange = null;
    setImageSelection(instance, null);
    instance.activeCell = null;
    updateStats(instance);
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
          credentials: "same-origin", cache: "no-store"
        });
        if (!tokenResponse.ok) throw new Error("The image upload token could not be obtained.");
        const token = await tokenResponse.json();
        if (!token.requestToken) throw new Error("Image upload antiforgery token is missing.");
        instance.uploadToken = token.requestToken;
      }
      headers.RequestVerificationToken = instance.uploadToken;
    }
    const response = await fetch(uploadTarget.href, { method: "POST", body: form, headers, credentials: "same-origin" });
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
        ["P", "H2", "H3"].includes(block.tagName)) {
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
        const image = document.createElement("img");
        image.setAttribute("src", url);
        image.setAttribute("alt", file.name);
        // Original is the default. No width attribute means native image pixels;
        // CSS max-width:100% keeps oversized images within the editor viewport.
        if (instance.imageInsertSize?.value === "480") image.setAttribute("width", "480");
        insertImageParagraph(instance, image);
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

  // Paste only editor-approved elements; never append untrusted clipboard HTML
  // directly. Formatting (headings, tables, links) survives without Office CSS.
  function pasteCleanContent(instance, html, plainText) {
    const clean = html && html.length <= maxDocumentBytes ? sanitizeHtml(html) : "";
    if (!clean || !clean.trim()) return insertPlainText(instance, plainText || "");
    const template = document.createElement("template");
    template.innerHTML = clean;
    const fragment = template.content;
    // A caret marker remains in the DOM after the fragment moves, allowing
    // insertion in the middle of a paragraph and subsequent typing.
    const marker = document.createTextNode("");
    fragment.appendChild(marker);
    instance.canvas.focus();
    const range = selectedRange(instance);
    range.deleteContents();
    range.insertNode(fragment);
    selectAfter(marker);
    changed(instance);
  }

  function clearFormatting(instance) {
    const range = selectedRange(instance);
    if (range.collapsed) return message(instance, "Select the text whose formatting you want to clear.");
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
    if (block && block.parentElement === instance.canvas && ["P", "DIV", "H2", "H3"].includes(block.tagName)) {
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
    message(instance, pageBreak ? "Page break inserted." : "Horizontal line inserted.");
  }

  function toggleFullscreen(instance, enable = !instance.element.classList.contains("azunt-editor-fullscreen")) {
    instance.element.classList.toggle("azunt-editor-fullscreen", enable);
    const toggle = instance.toolbar.querySelector('[data-command="fullscreen"]');
    if (toggle) toggle.setAttribute("aria-pressed", String(enable));
    if (enable) instance.canvas.focus();
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
    if (instance.sourceMode) return message(instance, "Apply HTML source before searching text.");
    const query = instance.findQuery?.value || "";
    if (!query) return message(instance, "Enter a search term.");
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
      `.azunt-print-page hr[data-azunt-page-break="true"]{break-before:page;page-break-before:always;border:0;margin:0;height:0}` +
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
    if (instance.sourceMode) return message(instance, "Apply HTML source before using formatting tools.");
    if (command === "clear-format") return clearFormatting(instance);
    if (command === "page-break") return insertRule(instance, true);
    if (command === "horizontal-rule") return insertRule(instance, false);
    if (command === "datetime") {
      if (instance.datePanel) instance.datePanel.hidden = !instance.datePanel.hidden;
      return;
    }
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

  // Blazor can preserve the outer editor node while replacing its children.
  // The attached canvas/toolbar must be the nodes that are actually visible.
  function bindingIsCurrent(instance, element) {
    return instance && instance.element === element && element.isConnected &&
      instance.canvas === element.querySelector(".azunt-editor-canvas") &&
      instance.toolbar === element.querySelector(".azunt-editor-toolbar") &&
      instance.value === element.querySelector("[data-azunt-value]") &&
      instance.images === element.querySelector("[data-azunt-images]") &&
      instance.source === element.querySelector("[data-azunt-source]") &&
      instance.linkPanel === element.querySelector("[data-link-panel]") &&
      instance.findPanel === element.querySelector("[data-find-panel]") &&
      instance.characterPanel === element.querySelector("[data-chars-panel]") &&
      instance.signaturePanel === element.querySelector("[data-signature-panel]") &&
      instance.templatePanel === element.querySelector("[data-template-panel]") &&
      instance.datePanel === element.querySelector("[data-date-panel]") &&
      instance.imageResizeHandle === element.querySelector("[data-image-resize-handle]");
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
      imageAlt: element.querySelector("[data-image-alt]"),
      imageInsertSize: element.querySelector("[data-select=\"imageInsertSize\"]"),
      imageResizeHandle: element.querySelector("[data-image-resize-handle]"),
      imageResize: null,
      status: element.querySelector(".azunt-editor-message"),
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
    updateStats(instance);

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
      if (!["paper", "orientation", "margin", "imageInsertSize"].includes(target.dataset.select) && target.type !== "color") target.selectedIndex = 0;
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
    instance.canvas.addEventListener("dragenter", event => {
      if (!isSupportedDrop(event.dataTransfer)) return;
      event.preventDefault();
      if (!instance.readOnly) instance.element.classList.add("azunt-editor-image-drop-active");
    }, { signal });
    instance.canvas.addEventListener("dragover", event => {
      if (!isSupportedDrop(event.dataTransfer)) return;
      event.preventDefault();
      if (event.dataTransfer) event.dataTransfer.dropEffect = instance.readOnly ? "none" : "copy";
      if (!instance.readOnly) instance.element.classList.add("azunt-editor-image-drop-active");
    }, { signal });
    instance.canvas.addEventListener("dragleave", event => {
      if (!instance.canvas.contains(event.relatedTarget))
        instance.element.classList.remove("azunt-editor-image-drop-active");
    }, { signal });
    instance.canvas.addEventListener("drop", event => {
      instance.element.classList.remove("azunt-editor-image-drop-active");
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
    document.addEventListener("keydown", event => {
      if (event.key === "Escape" && instance.element.classList.contains("azunt-editor-fullscreen"))
        toggleFullscreen(instance, false);
    }, { signal });
    instance.canvas.addEventListener("keydown", event => {
      if (event.key === "Escape" && instance.element.classList.contains("azunt-editor-fullscreen")) {
        event.preventDefault(); toggleFullscreen(instance, false); return;
      }
      if (instance.readOnly || !(event.ctrlKey || event.metaKey)) return;
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
    updateStats(instance);
    emit(instance);
  }

  function unmount(id) {
    const instance = instances.get(id);
    if (!instance) return;
    if (instance.imageResize) finishImageResize(instance, null, true);
    instance.controller.abort();
    instance.element.classList.remove("azunt-editor-fullscreen", "azunt-editor-image-drop-active");
    instance.observer?.disconnect();
    window.clearTimeout(instance.emitTimer);
    instances.delete(id);
  }

  function getPageSettings(id) {
    const instance = instances.get(id);
    if (!instance) throw new Error("Editor instance is unavailable.");
    return { paperSize: instance.page.paper, orientation: instance.page.orientation, margin: instance.page.margin };
  }

  window.AzuntEditor = Object.freeze({ mount, mountAll, getHtml, getHtmlStream, setHtml, getPageSettings, unmount, sanitizeHtml, registerTemplate });
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", mountAll, { once: true });
  else mountAll();
})();
