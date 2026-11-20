# Модель загроз SecureLab, ЛР 6

| Елемент | Що зафіксувати | Запис у проєкті |
|---|---|---|
| Версія та середовище | commit початку огляду, профіль запуску, seed/reset, SDK | review-start 7f95329; Development і Production-like; reset командою --reset-database; SDK 10.0.404 |
| Компоненти в межах огляду | браузерний клієнт, API, PostgreSQL, конфігурація, журнали | сторінки index, browser-lab, auth-lab, flow; API ASP.NET Core; PostgreSQL у Docker Compose; журнали застосунку |
| Активи | облікові дані, власні записи, привілейовані переходи, дані аудиту, секрети | облікові дані користувачів (хеші в БД); інциденти й коментарі; перехід Incident.Status; журнал authorization.denied і audit.transition; навчальні значення в конфігурації |
| Суб’єкти | анонім, автентифікований користувач, привілейована роль, розробник | анонімний відвідувач; Reporter (Alice, Bob); Analyst (Morgan); Administrator (admin); локальний розробник |
| Entry points | маршрути, login, форми, конфігурація, restore, журнали | /api/auth/*, /api/incidents/*, /api/csrf-token, /health; форми auth-lab, browser-lab, flow; appsettings, compose.yaml; restore пакетів |
| Межі довіри | browser→API, API→PostgreSQL, repository→build, environment→configuration | чотири межі, див. схему нижче |
| Наявні controls | validation, authentication, authorization, DOM, CORS, CSP, antiforgery | валідація DTO; cookie-автентифікація й rate limit входу; ресурсна авторизація й аудит відмов; безпечний DOM; CORS для 4173; заголовки безпеки й CSP; antiforgery; rate limit пошуку; токен xmin, ETag і If-Match |
| Виключення | що не перевірялося й чому | HTTPS і HSTS, сторонні сервіси, інші браузери, історія Git, навантажувальне тестування |

## Схема меж довіри

```text
[Браузер] --(1) HTTP, cookie сеансу, X-CSRF-TOKEN--> [API ASP.NET Core] --(2) Npgsql--> [PostgreSQL]
[Репозиторій] --(3) restore, build--> [Збірка]
[Environment, appsettings, змінні] --(4) конфігурація--> [API]
```

## Очікувані властивості на межах

| Межа | Очікувана властивість | Конкретний test або перевірка |
|---|---|---|
| browser→API | змінювальні запити з cookie потребують токена; чужий origin не читає відповідь; значення з БД виводяться як текст | REG-CSRF-01 (Lab06CsrfTests); REG-CORS-01 (probes 4173, 4174); REG-XSS-01 (tests/browser/dom-test.js); Lab06IfMatchTests |
| API→PostgreSQL | запити параметризовані; доступ до даних за суб’єктом; перехід статусу не дублюється | S-02 (Lab02SecurityTests); A-02, A-03, A-05 (Lab04ReadAccessTests); REG-RACE-01 (Lab06RaceTests) |
| repository→build | залежності відомі, advisory мають рішення; секретів у відстежуваних файлах немає | dotnet package list --vulnerable --include-transitive; git grep по відстежуваних файлах |
| environment→configuration | секрети лише у змінних середовища (крім відкритих навчальних значень); Production не показує діагностики | Production.ps1 normal і broken; відмова reset у Production |

## Актив → загроза → control → перевірка

- Сеанс користувача (cookie) → CSRF → antiforgery (AntiforgeryFilter, X-CSRF-TOKEN) → REG-CSRF-01.
- Цілісність переходу Incident.Status → гонка й втрачені оновлення → токен xmin, частковий унікальний індекс, If-Match (428/412) → REG-RACE-01, Lab06IfMatchTests.
- Audit-слід привілейованих дій → дія без сліду → authorization.denied і audit.transition → перегляд журналу (відмова Bob, перехід Morgan).
- Секрети конфігурації й журналів → витік у репозиторій або журнал → SeedUsers__Password поза Git; журнал без чутливих значень → git grep і пошук у журналі (0 збігів).