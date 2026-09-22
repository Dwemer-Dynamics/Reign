// Render only the native formatter's exact action-span convention. Never insert
// model text with innerHTML: links, images and arbitrary tags remain literal text.
export function appendActionRichText(element, value) {
  const text = String(value ?? "");
  const pattern = /<span style="Action">([\s\S]*?)<\/span>/g;
  let end = 0;
  element.replaceChildren();
  for (const match of text.matchAll(pattern)) {
    element.appendChild(document.createTextNode(text.slice(end, match.index)));
    const action = document.createElement("em");
    action.textContent = match[1];
    action.dataset.reignAction = "true";
    element.appendChild(action);
    end = match.index + match[0].length;
  }
  element.appendChild(document.createTextNode(text.slice(end)));
}

// Provider-free fixture equivalent of ReignChatLineVM.RichText. The native
// implementation remains authoritative; the parity fixtures test both routes.
export function previewActionRichText(value, enabled = true) {
  let text = String(value ?? "");
  const truncated = text.length > 4000;
  text = text.slice(0, 4000).replaceAll("<", "(").replaceAll(">", ")");
  let output = "";
  for (let i = 0; i < text.length; i++) {
    if (enabled && text[i] === "\\" && text[i + 1] === "*") { output += "*"; i++; continue; }
    if (!enabled || text[i] !== "*" || text[i - 1] === "*" || text[i + 1] === "*" || /\s/.test(text[i + 1] || " ")) {
      output += text[i]; continue;
    }
    let close = i + 1;
    for (; close < text.length; close++) {
      if (text[close] === "\\" && text[close + 1] === "*") { close++; continue; }
      if (text[close] === "*") break;
    }
    if (close === text.length || close === i + 1 || /\s/.test(text[close - 1]) || text[close + 1] === "*") { output += text[i]; continue; }
    output += `<span style="Action">${text.slice(i + 1, close).replaceAll("\\*", "*")}</span>`;
    i = close;
  }
  return output + (truncated ? "..." : "");
}
