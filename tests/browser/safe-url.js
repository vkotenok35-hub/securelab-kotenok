function safeHttpUrl(value, base) {
  try {
    const url = new URL(value, base);
    return url.protocol === "https:" || url.protocol === "http:" ? url.href : null;
  } catch {
    return null;
  }
}

const base = "http://localhost:5080";
console.log(safeHttpUrl("https://example.com/a", base));
console.log(safeHttpUrl("/incidents/1", base));
console.log(safeHttpUrl("javascript:alert(1)", base));
console.log(safeHttpUrl(" JaVaScRiPt:alert(1)", base));
console.log(safeHttpUrl("http://[bad", base));

const anchor = document.createElement("a");
const accepted = safeHttpUrl("/incidents/1", base);
if (accepted !== null) {
  anchor.href = accepted;
  anchor.textContent = "Локальне посилання";
}
console.log(anchor.getAttribute("href"));
console.log(anchor.getAttribute("onclick"));
