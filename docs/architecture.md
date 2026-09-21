# Карта архітектури

Карта доповнена власним трасуванням запиту, конкретними файлами та
спостереженнями з DevTools і журналу PostgreSQL, зібраними під час
виконання лабораторної роботи №1 (варіант 2-A).

## Компоненти

| Компонент | Розташування | Відповідальність |
|---|---|---|
| Browser client | `src/SecureLab.Api/Client/` | Надсилає HTTP-запити, безпечно показує відповідь через DOM API |
| Presentation | `Presentation/` | Описує endpoints, читає зовнішні параметри, формує HTTP-відповідь |
| Application | `Application/` | Виконує сценарій отримання списку, деталей інциденту та підсумку за severity |
| Data | `Data/` | Відображає C#-сутності на PostgreSQL через EF Core/Npgsql |
| PostgreSQL | `infra/compose.yaml` | Зберігає навчальні дані у локальному контейнері (порт 54329) |

## Досліджений маршрут (деталі інциденту)

```text
натискання картки інциденту у Client/app.js (loadIncidentDetails)
  → GET /api/incidents/{id}
  → Presentation/Endpoints/IncidentEndpoints.cs (GetDetailsAsync)
  → Application/Incidents/IncidentQueries.cs (GetDetailsAsync)
  → Data/SecureLabDbContext.cs (DbSet<Incident> Incidents)
  → PostgreSQL: таблиця incidents
  → IncidentDetailsResponse (Presentation/Contracts/IncidentResponses.cs)
  → JSON response
  → renderIncidentDetails у Client/app.js
  → DOM-вузли через textContent і document.createTextNode
```

Перевірено у DevTools Network для успішного ідентифікатора
`20000000-0000-0000-0000-000000000003` (200 OK) і для синтаксично
коректного, але відсутнього в seed ідентифікатора
`aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa` (404 Problem Details).

## Реалізований маршрут (наскрізне розширення, ЛР 1, етап 3)

```text
кнопка «Показати підсумок» у Client/index.html
  → обробник click у Client/app.js (loadSeveritySummary)
  → GET /api/incidents/severity-summary
  → Presentation/Endpoints/IncidentEndpoints.cs (GetSeveritySummaryAsync)
  → Application/Incidents/IncidentQueries.cs (GetSeveritySummaryAsync)
  → Data/SecureLabDbContext.cs (DbSet<Incident> Incidents, GroupBy за Severity)
  → PostgreSQL: таблиця incidents
  → IncidentSeveritySummaryResponse (Presentation/Contracts/IncidentResponses.cs)
  → JSON response
  → DOM-вузли (<li>) через textContent у Client/app.js
```

До реалізації endpoint повертав `501 Not Implemented`. Після реалізації
повертає `200 OK` з JSON-масивом елементів `{severity, count}`.
Використано політику повного переліку рівнів: відсутні в таблиці рівні
severity доповнюються елементом з `count: 0` (на baseline seed —
рівень Critical). Порядок елементів відповідає порядку оголошення
`enum IncidentSeverity` (Low, Medium, High, Critical).

## Межі довіри

| Межа | Чому даним ще не можна довіряти | Де перевіряємо або обмежуємо |
|---|---|---|
| Browser client → API | Клієнт повністю контролює method, URL, path parameter `{id}` і заголовки; будь-який інший HTTP-клієнт може надіслати той самий запит без форми | маршрутне обмеження `:guid` у `MapGet("/{id:guid}", ...)`; для відсутнього ресурсу окрема гілка `404 Problem Details` замість тихого збою |
| API → PostgreSQL | Ідентифікатор та умови фільтра надходять ззовні; збережений текст не є автоматично безпечним | параметризація запиту засобами EF Core (`Where`, `GroupBy`), `AsNoTracking()` для read-only сценарію, явна проєкція лише дозволених полів |
| API → Browser (response DTO) | Право прочитати entity не означає права одержати всі її поля (наприклад `OwnerUserId`, email, внутрішні коментарі) | окремі DTO `IncidentDetailsResponse` та `IncidentSeveritySummaryResponse`, які не розкривають службових полів entity |
| Дані response → DOM | Текст із PostgreSQL міг походити від попереднього користувацького вводу (наприклад seed-інцидент з `<script>` у description) і не стає безпечним лише тому, що вже збережений у БД | запис виключно через `textContent` і `document.createTextNode`/`createElement`; `innerHTML` не використовується ніде в `Client/app.js` |

## Конфігураційні входи

- `global.json` — версія .NET SDK (смуга 10.0.3xx, не нижче 10.0.302);
- `src/SecureLab.Api/appsettings*.json` — режим міграцій і локальний connection string для середовища Development;
- `infra/compose.yaml` — версія образу PostgreSQL (`postgres:18.4-alpine3.24`), локальний порт `54329`, навчальні облікові дані `securelab / local-study-password` (дійсні лише для стенда на `127.0.0.1`);
- змінна середовища `ConnectionStrings__SecureLab` — спосіб перевизначити connection string поза репозиторієм, без запису реального значення до Git.

## Відновлення відомого стану

Стенд повертається до відомого seed-стану командою:

```bash
dotnet run --no-build --project src/SecureLab.Api -- --reset-database
```

Команда застосовує наявні EF Core migrations, очищує лише відомі
навчальні таблиці (`incidents`, `incident_comments`,
`incident_status_history`, `study_users`) і повторно заповнює їх
фіксованими seed-значеннями. Працює лише в явно налаштованому
середовищі Development.