let current;
const id = () => encodeURIComponent(document.querySelector("#incident-id").value.trim());
const status = document.querySelector("#status");
const notice = document.querySelector("#notice");
let etag = null; // версія запису з GET; лише в пам'яті сторінки

// Єдиний безпечний helper: значення з API потрапляє лише в текстовий вузол,
// а структуру елементів задає код. Повторний render замінює дітей, а не додає нових.
function renderText(container, text) {
  container.replaceChildren(document.createTextNode(text ?? ""));
  return container;
}

async function load() {
  const response = await fetch(`/api/incidents/${id()}`);
  status.textContent = `GET: ${response.status}`;
  if (!response.ok) { etag = null; return; }
  etag = response.headers.get("ETag");
  current = await response.json();
  document.querySelector("#title").textContent = current.title;
  renderText(document.querySelector("#description"), current.description);
  const comments = document.querySelector("#comments");
  comments.replaceChildren();
  for (const comment of current.comments ?? []) {
    comments.append(renderText(document.createElement("p"), comment.text));
  }
  document.querySelector("#edit").elements.description.value = current.description;
}
document.querySelector("#load").addEventListener("click", load);
document.querySelector("#edit").addEventListener("submit", async event => {
  event.preventDefault();
    const response = await Csrf.mutate("/api/incidents", { method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ title: event.currentTarget.elements.title.value, description: event.currentTarget.elements.description.value,
      severity: "Low", occurredAtUtc: new Date().toISOString() }) });
  status.textContent = `POST: ${response.status}`;
  if (response.ok) {
    const created = await response.json();
    document.querySelector("#incident-id").value = created.id;
    await load();
  }
});
document.querySelector("#triage").addEventListener("click", async () => {
  if (!etag) { notice.textContent = "Спершу прочитайте інцидент."; return; }
  const response = await Csrf.mutate(`/api/incidents/${id()}/triage`, {
    method: "POST", headers: { "If-Match": etag }
  });
  if (response.status === 412) {
    // Запис змінився: не перезаписуємо мовчки, а перечитуємо актуальний стан
    await load();
    status.textContent = "TRIAGE: 412";
    notice.textContent = "Запис змінився, його перечитано. Перевірте актуальний стан і повторіть дію.";
  } else if (response.status === 428) {
    status.textContent = "TRIAGE: 428";
    notice.textContent = "Потрібна версія запису: спершу прочитайте інцидент.";
  } else if (response.status === 409) {
    status.textContent = "TRIAGE: 409";
    notice.textContent = "Перехід неможливий: тріаж можливий лише для нового інциденту.";
  } else if (response.status === 403) {
    status.textContent = "TRIAGE: 403";
    notice.textContent = "Дію не дозволено для вашої ролі.";
  } else if (response.ok) {
    notice.textContent = "Тріаж виконано.";
    await load();
    status.textContent = `TRIAGE: ${response.status}`;
  } else {
    status.textContent = `TRIAGE: ${response.status}`;
    notice.textContent = `Дію не виконано (статус ${response.status}).`;
  }
});