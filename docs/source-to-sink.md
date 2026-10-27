# Власна карта source-to-sink

Поле: Incident.Description. Стан/commit: vulnerable 33aae07. Вхід/Guid запису: Alice, 20000000-0000-0000-0000-000000000001.

| Крок | Файл та функція/DTO | Конкретне поле/значення | Доказ і довіра |
|---|---|---|---|
| Source | Client/browser-lab.html, форма #edit, textarea name="description" | текст поля «Опис нового інциденту» | контрольоване користувачем |
| Request | Client/browser-lab.js, обробник submit форми #edit: fetch POST /api/incidents | JSON-поле description | Network → Payload; контрольоване користувачем |
| Validation/mapping | Scaffolding/Lab02Endpoints.cs, CreateIncidentRequest і обробник POST | Description після Trim(), не порожнє, до 4000 символів | перевіряє довжину й обов'язковість, але не HTML; значення не стає довіреним для DOM |
| Storage | Incident.Description, таблиця incidents у PostgreSQL | те саме значення | збереження не змінює рядок і не санітизує його |
| Response | Application/Incidents/IncidentQueries.cs, GetDetailsAsync → IncidentDetailsResponse.Description | поле description у JSON GET /api/incidents/{id} | Network → Response; авторизація ЛР 4 перевіряє право на ресурс, а не вміст |
| Client transform | Client/browser-lab.js, load(): current = await response.json(); current.description | той самий рядок | перетворень немає |
| Sink/context | Client/browser-lab.js, load(): document.querySelector("#description").innerHTML = current.description | елемент #description | контекст HTML: браузер розбирає рядок як розмітку |

Інвентаризація (файл/функція → категорія даних → контекст → рішення):

- browser-lab.js, load(): current.title → назва інциденту → текстовий вузол (#title.textContent) → безпечно, лишити.
- browser-lab.js, load(): current.description → опис, довільний текст користувача → HTML (#description.innerHTML) → небезпечно, замінити на textContent.
- browser-lab.js, load(): comment.text → текст коментаря (Добре / Відмінно) → HTML (<p>.innerHTML) → небезпечно, замінити на безпечний helper.
- browser-lab.js, load(): elements.description.value = current.description → значення поля форми → value textarea (не розбирається як HTML) → безпечно, лишити.
- browser-lab.js: status.textContent → службовий рядок статусу → текстовий вузол → безпечно, лишити.

Другий потік (Добре / Відмінно): IncidentComment.Text.

| Крок | Файл та функція/DTO | Конкретне поле/значення | Доказ і довіра |
|---|---|---|---|
| Source | Data/DbSeeder.cs, SeedAsync, коментар Id …0005 (Lab05Marker) | marker-рядок | у продукті джерелом був би будь-який автор коментаря; тут seed |
| Response | IncidentDetailsResponse.Comments → IncidentCommentResponse.Text | comments[].text у JSON GET /api/incidents/{id} | Network → Response; значення без змін |
| Sink/context | Client/browser-lab.js, load(): paragraph.innerHTML = comment.text | елемент <p> у #comments | контекст HTML: браузер розбирає рядок як розмітку |