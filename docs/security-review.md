# Security review SecureLab, ЛР 6

Рівень: відмінний («Відмінно»); рівні накопичувальні. Commit початку огляду (review-start): 7f95329c10839c28b11a23c17d9de4c0063f3b01. Хеші прийнятого merge commit і тегу v1.0.0 додає скрипт експорту в PDF. Дата, версія SDK, профіль запуску й клієнт: 18–20 листопада 2026; dotnet 10.0.404; Development для відтворень і Production-like для перевірок помилок і заголовків; Google Chrome 155.0.8059.40, Windows 11 Pro 25H2.

## 1. Executive summary
Огляд охопив відстежуваний стан власного проєкту від v0.5.0: межі й модель загроз, секрети й конфігурацію, Production-like профіль, заголовки й cookie, CSP і CORS, CSRF-периметр, rate limiting, залежності, журнали й regression pack. Виправлено три знахідки: F-01 (на 500 бракувало заголовків безпеки), F-02 (успішний перехід Incident.Status не мав audit-запису) і F-03 (гонка при переході в Triaged). Реалізовано погоджене з викладачем розширення: ETag і If-Match (428, 412). Antiforgery діє на всіх cookie-змінювальних маршрутах (REG-CSRF-01 автоматизовано), додано rate limit пошуку, залежності без вразливих пакетів. Результат останнього прогону додає скрипт експорту; на commit de7a42a повний набір із 52 тестів пройшов без збоїв. Залишкові ризики: HTTPS і HSTS не перевірялись, login і register без antiforgery, rate limit лише на вхід і пошук, аудит лише для triage.

## 2. Scope та exclusions
У межах: поточні відстежувані Git-файли на commit початку огляду: API на ASP.NET Core, клієнт (index, browser-lab, auth-lab, flow), PostgreSQL у Docker Compose, конфігурація, тести, журнали. Історія Git не переглядалась. Поза межами: промислове середовище, сторонні сервіси, інші браузери, навантажувальне тестування, повне сканування історії.

## 3. System overview і trust boundaries
Див. threat-model.md. Кожна межа пов’язана з конкретним test; зв’язок «актив → загроза → control → перевірка» подано там само.

## 4. Methodology та test environment
Старт: гілка lab/6-hardening-review від v0.5.0 (4bc1151) і коміт політики SDK 7f95329. Локальна БД: PostgreSQL у Docker Compose (порт 54329), приведена до відомого стану командою `dotnet run --project src/SecureLab.Api -- --reset-database`. Стартова перевірка `Startup.ps1`: restore, build без попереджень, 40 тестів без збоїв. Профілі: Development і Production-like (`Production.ps1`). Клієнти: curl.exe із cookie-jar і Chrome. Коди завершення: `dotnet restore` і `dotnet package list --vulnerable --include-transitive` повернули 0. Секрети: `git grep -n -I -i -E 'password|passwd|secret|api.?key' -- .` дав 103 збіги; реального секрету немає, три місця з відкритим навчальним значенням local-study-password (compose.yaml, SecureLabDesignTimeDbContextFactory.cs, appsettings.Development.json) прийнято як accepted, study-only.

## 5. Findings summary
| ID | Актив | Вплив | Ймовірність | Пріоритет | Статус | ID повторної перевірки |
|---|---|---|---|---|---|---|
| F-01 | захисні заголовки на відповіді 500 | низький–середній | висока | P3 | Fixed | retest 500 із трьома заголовками |
| F-02 | аудит переходу Incident.Status | середній | висока | P2 | Fixed | audit.transition у журналі |
| F-03 | цілісність переходу New → Triaged | середній | низька | P2 | Fixed | REG-RACE-01 (Lab06RaceTests) |

Пріоритизація: F-02 і F-03 мають середній вплив (відсутність сліду й спотворення привілейованої дії), F-01 низький (тіло безпечне). Порядок виправлення: F-01 (найдешевше), F-02, F-03 (потребує міграції).

## 6. Detailed findings
Окремі картки: docs/findings/F-01-security-headers-on-500.md, F-02-triage-audit.md, F-03-triage-race.md.

## 7. Regression results
Результати на commit de7a42a (повний набір 52 тести); повторний прогін на merge commit наведено в PDF-звіті.

| ID | Property | Передумова | Команда / тест | Результат |
|---|---|---|---|---|
| S-02 | запит не змінює структуру SQL | reset, seed | Lab02SecurityTests.Search_ControlInput_ReturnsEmptySet; Search_Usb_ReturnsExactlyOneSeedIncident | pass |
| A-01…A-07 | автентифікація, сеанс | акаунти після reset | Lab03SecurityTests A01…A07; LoginRateLimitTests | pass |
| A-02, A-03, A-05 | авторизація й IDOR | Alice, Bob | Lab04ReadAccessTests; Lab04MutationTests | pass |
| REG-XSS-01 | безпечний вивід значень з БД | сторінка browser-lab | tests/browser/dom-test.js | PASS |
| REG-CSRF-01 | відмова без токена, стан незмінний, успіх із токеном | login, cookie | Lab06CsrfTests (5 тестів) | pass |
| T-02, T-10 | серверна валідація | login, токен | Lab02SecurityTests.Post_WithNumericSeverity…; Post_WithTitleWithoutLetters… | pass |
| REG-CORS-01 | CORS лише для дозволеного origin | probes 4173 і 4174 | Console fetch; curl -i з Origin | 4173 читає, 4174 ні, ACAO лише для 4173 |
| RATE-SEARCH, RATE-LOGIN | два partitions, quota, 429, відновлення | reset | Lab06SearchRateLimitTests; LoginRateLimitTests | пошук 20×200 і 429; вхід 20×401 і 429 |
| REG-RACE-01 | один перехід під конкуренцією | бар’єр у тесті | Lab06RaceTests | 200, 412; рядків історії 1 |
| IFMATCH-01 | ETag, 428, 412 | два клієнти | Lab06IfMatchTests (4 тести) | pass |

## 8. Hardening controls
**Заголовки** (nosniff, Referrer-Policy: no-referrer, CSP `default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self'; frame-ancestors 'none'`) перевірено на п’яти класах: статична сторінка, успішна API, validation 400, 404 (Development, curl із сесією Alice) і контрольована 500 (Production). До F-01 на 500 заголовків не було, після виправлення вони є по одному разу. Cookie сеансу: HttpOnly, SameSite=Lax, Secure у Development немає. CSP не ламає клієнта; виняток для /scalar лише в Development.

**CORS:** origin 4173 читає відповідь, 4174 не читає, direct HTTP без обмежень; політика лише в Development; клієнт same-origin, тому ETag і If-Match до CORS-заголовків не додавалися.

**Периметр змінювальних маршрутів і CSRF:**

| ID | Method + route | Джерело | Зміна стану | Cookie auth | Browser-accessible | Antiforgery | Regression ID |
|---|---|---|---|---|---|---|---|
| SC-01 | POST /api/auth/login | ЛР 3 | створює сеанс | ні | так | absent (токен до входу неможливий; ризик у розділі 10) | — |
| SC-02 | POST /api/auth/register | ЛР 3 | створює акаунт | ні | так | absent (розділ 10) | — |
| SC-03 | POST /api/auth/logout | ЛР 3 | завершує сеанс | так | так | explicit | REG-CSRF-01 |
| SC-04 | POST /api/incidents | ЛР 2–3 | створює інцидент | так | так | explicit | REG-CSRF-01 |
| SC-05 | PUT /api/incidents/{id} | ЛР 4 | змінює вміст | так | так | explicit | REG-CSRF-01; IFMATCH-01 |
| SC-06 | DELETE /api/incidents/{id} | ЛР 4 | видаляє інцидент | так | так | explicit | REG-CSRF-01; IFMATCH-01 |
| SC-07 | POST /api/incidents/{id}/triage | ЛР 4 | змінює Incident.Status | так | так | explicit | REG-CSRF-01; IFMATCH-01 |

Контракт: header X-CSRF-TOKEN, cookie SecureLab.Antiforgery, видача GET /api/csrf-token після входу; валідація в єдиному AntiforgeryFilter.

**Транспорт:** у Program.cs немає UseHttpsRedirection і UseHsts, застосунок слухає http://localhost:5080; HTTPS і HSTS не перевірялись.

**Rate limiting:** login limiter: 20 невдалих входів → 401, далі 429, після вікна 401; відповіді для існуючого й неіснуючого акаунта однакові. Search limiter: partition = id автентифікованого користувача, fixed window 10 с, quota 20, без черги; Alice 20×200 і 429, Bob одразу 200, після вікна 200. Redis не використовується.

**Development і Production:** у Development /openapi/v1.json дає 200 і діє CORS для 4173; у Production 404 і CORS-заголовків немає; reset лише в Development.

**Advanced variant (If-Match):** тему погоджено з викладачем до реалізації. GET повертає сильний ETag; If-Match обов’язковий для PUT, DELETE і triage: без заголовка 428, зі застарілою версією 412 (тип concurrency-conflict), недопустимий перехід 409 (тип invalid-transition). Порядок: автентифікація, авторизація, If-Match, валідація, правило переходу, збереження. Клієнт browser-lab має дію triage; на 412 перечитує запис. Сценарій 412 у браузері окремо не знімався. RFC 9110 і RFC 6585.

## 9. Dependency and secret audit
**Залежності:**

| Пакет | Версія | Advisory | Рішення | Обґрунтування і межа |
|---|---|---|---|---|
| SecureLab.Api, SecureLab.Api.Tests (прямі й транзитивні) | за lock-файлами | не виявлено (api.nuget.org) | без змін | лише відомі на дату перевірки advisories; повторити перед релізом |

**Секрети:** класифікація 103 збігів: навчальне значення (compose.yaml, DesignTimeDbContextFactory, appsettings.Development.json) accepted, study-only; документація й коментарі; поля форм клієнта; код DbSeeder; назва PasswordHash у міграціях; політика пароля в Lab03Identity; тести (читання SeedUsers__Password); плейсхолдер у tests/http/lab-04-access.http. Реальних секретів немає. Систематичний пошук: 82 відстежувані файли, незакомічені файли не входять, історія не переглядалась.

**Журнали й аудит:** відмова Bob (authorization.denied) і перехід Morgan (audit.transition) мають trace, що збігається з traceId відповіді. Журнал шести сценаріїв (вхід, невдалий вхід, відмова, валідація, 500, перехід) не містить пароля seed, пароля невдалого входу, cookie, X-CSRF-TOKEN і значення токена (по 0 збігів).

## 10. Residual risks
| Ризик | Статус | Компенсаційний контроль | Відповідальний | Наступна дія |
|---|---|---|---|---|
| HTTPS і HSTS не перевірялись | прийнятий | localhost | автор | додати й перевірити перед розгортанням |
| login і register без antiforgery | прийнятий | rate limit входу, валідація, SameSite=Lax | автор | окремий limiter реєстрації |
| rate limit лише на вхід і пошук | прийнятий | автентифікація й авторизація | автор | розширити на змінювальні маршрути |
| аудит лише для incident.triage | прийнятий | ресурсна авторизація | автор | audit для PUT і DELETE |
| невідомий API-шлях повертає 200 text/html | спостереження | заголовки безпеки є | автор | 404 Problem Details для /api/* |
| браузерний flow у Production-like і 412 у браузері не знімались | не перевірено | серверні докази 412 | автор | за потреби повторити |
| історія Git не переглядалась | не перевірено | пошук по поточному commit | автор | за потреби просканувати |

## 11. Conclusion
Перевірено межі, секрети (реального секрету немає), Production-like профіль, безпечну 500, заголовки, cookie, CSP, CORS, CSRF-периметр із 7 маршрутів, rate limiting, залежності, журнали й аудит. Виправлено F-01, F-02, F-03 із відтворенням до й після; реалізовано ETag і If-Match. На commit de7a42a пройшло 52 тести; результат останнього прогону додає скрипт експорту. Не перевірялись: HTTPS і HSTS, Production-like браузерний flow, сценарій 412 у браузері, історія Git.

## 12. Evidence appendix
Докази очищені: без паролів, cookie, токенів і connection strings; відповіді пов’язані із записами журналу за traceId. Передумови для повтору: reset командою --reset-database; користувачі Alice, Bob, Morgan; пароль лише зі змінної SeedUsers__Password; команда тесту для кожного REG-сценарію наведена в розділі 7. Знімки екрана подано в PDF-звіті.

## Декларація й журнал ШІ
Використовувала, у навчальній ролі помічника, у межах правил курсу. Сервіс: Claude (Anthropic). Що отримала: розбір методички, план етапів, чернетки коду й тестів, каркас тексту звіту. Що зробила сама: підготовка й запуск стенда, усі запуски й перевірки, інтеграція коду, відтворення знахідок і retest, погодження теми з викладачем, коміти, злиття, тег, знімки екрана.

| Дата | Сервіс | Завдання/фрагмент | Авторська адаптація | Перевірка й результат |
|---|---|---|---|---|
| 18–20.11.2026 | Claude | розбір методички, план етапів | опрацювала вимоги, виконала всі команди сама | кожен крок підтверджено запуском і знімком |
| 18.11.2026 | Claude | повідомлення для погодження теми | узгоджено з викладачем, тему розширено за зауваженнями | відповіді викладача збережено |
| 18–20.11.2026 | Claude | чернетки коду й тестів | інтегрувала у проєкт, виконала збірку, міграцію й усі запуски | збірка без помилок; 52 тести без збоїв |