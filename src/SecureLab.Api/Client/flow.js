const $ = (selector) => document.querySelector(selector);

// Cookie надсилається лише на той самий origin.
async function apiFetch(path, options = {}) {
  const url = new URL(path, location.origin);
  if (url.origin !== location.origin) {
    throw new Error("Потрібна адреса цього застосунку.");
  }
  return fetch(url, { ...options, credentials: "same-origin" });
}

const jsonHeaders = { "Content-Type": "application/json" };

function clearProfile() {
  $("#profile").replaceChildren();
  $("#profile-result").textContent = "";
}

async function refreshState() {
  try {
    const response = await apiFetch("/api/auth/session-check");
    if (response.status === 204) {
      $("#state").textContent = "Стан: сеанс активний";
    } else if (response.status === 401) {
      $("#state").textContent = "Стан: не ввійшли";
      clearProfile();
    } else {
      $("#state").textContent = "Стан: невизначений";
    }
  } catch {
    $("#state").textContent = "Стан: невизначений (помилка мережі)";
  }
}

async function loadProfile() {
  clearProfile();
  const response = await apiFetch("/api/me");
  if (response.status === 401) {
    $("#profile-result").textContent = "Профіль недоступний: потрібен вхід (401).";
    return;
  }
  const isJson = (response.headers.get("Content-Type") ?? "").includes("application/json");
  if (!response.ok || !isJson) {
    $("#profile-result").textContent = `Не вдалося отримати профіль (статус ${response.status}).`;
    return;
  }
  const profile = await response.json();
  const list = $("#profile");
  for (const [label, value] of [["id", profile.id], ["userName", profile.userName], ["displayName", profile.displayName]]) {
    const item = document.createElement("li");
    item.textContent = `${label}: ${value}`;
    list.append(item);
  }
}

$("#register-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = event.currentTarget;
  const data = new FormData(form);
  const body = {
    userName: data.get("userName"),
    email: data.get("email"),
    displayName: data.get("displayName"),
    password: data.get("password"),
  };
  form.elements.password.value = "";
  const response = await apiFetch("/api/auth/register", {
    method: "POST", headers: jsonHeaders, body: JSON.stringify(body),
  });
  if (response.status === 201) {
    form.reset();
    $("#register-result").textContent = "Реєстрацію виконано (201). Тепер увійдіть.";
  } else if (response.status === 409) {
    $("#register-result").textContent = "Обліковий запис із такими даними вже існує (409).";
  } else {
    $("#register-result").textContent = `Реєстрація не виконана (статус ${response.status}).`;
  }
});

$("#login-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = event.currentTarget;
  const data = new FormData(form);
  const body = { userName: data.get("userName"), password: data.get("password") };
  form.elements.password.value = "";
  const response = await apiFetch("/api/auth/login", {
    method: "POST", headers: jsonHeaders, body: JSON.stringify(body),
  });
  if (response.status === 204) {
    $("#login-result").textContent = "Вхід виконано (204).";
    await refreshState();
    await loadProfile();
  } else if (response.status === 429) {
    $("#login-result").textContent = "Забагато спроб входу (429). Спробуйте пізніше.";
  } else {
    $("#login-result").textContent = `Вхід не виконано (статус ${response.status}).`;
  }
});

$("#profile-button").addEventListener("click", loadProfile);

$("#incident-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  // У запиті лише поля контракту; власник, роль, статус і id не передаються.
  const body = {
    title: data.get("title"),
    description: data.get("description"),
    severity: data.get("severity"),
    occurredAtUtc: new Date(data.get("occurredAt")).toISOString(),
  };
  const response = await apiFetch("/api/incidents", {
    method: "POST", headers: jsonHeaders, body: JSON.stringify(body),
  });
  if (response.status === 201) {
    const created = await response.json();
    $("#incident-result").textContent = `Створено (201): ${created.title}, статус ${created.status}.`;
  } else if (response.status === 401) {
    $("#incident-result").textContent = "Потрібно увійти (401).";
    await refreshState();
  } else if (response.status === 409) {
    $("#incident-result").textContent = "Інцидент із таким заголовком уже існує (409).";
  } else {
    $("#incident-result").textContent = `Інцидент не створено (статус ${response.status}).`;
  }
});

$("#logout-button").addEventListener("click", async () => {
  const response = await apiFetch("/api/auth/logout", { method: "POST" });
  clearProfile();
  $("#incident-result").textContent = "";
  $("#login-result").textContent = "";
  if (response.status === 204) {
    const check = await apiFetch("/api/me");
    $("#logout-result").textContent = `Вихід виконано (204). Повторний /api/me: ${check.status}.`;
  } else {
    $("#logout-result").textContent = `Вихід не виконано (статус ${response.status}).`;
  }
  await refreshState();
});

refreshState();