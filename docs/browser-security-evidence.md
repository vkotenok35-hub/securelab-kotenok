# Докази безпеки браузера (DEL-01)

Рівень: відмінний («Відмінно»).

## Стенд і Git-стан (Для всіх)

ОС, browser/version, API/probe origins: Windows 11 Pro 25H2 (збірка 26200.9711); Google Chrome 155.0.8059.40 (64 біт); API http://localhost:5080, probes http://localhost:4173 (allowed) і http://localhost:4174 (контроль). Release/base_commit/entry_parent з receipt: lab-05-start-v1 / e56926850bb1913db1f6cad4085efd47093f954d / e56926850bb1913db1f6cad4085efd47093f954d (збігаються, бо scaffold встановлено безпосередньо на тег v0.4.0). Vulnerable/fixed hashes, v0.5.0, main і спосіб передавання: vulnerable 33aae07; fixed [ЗАПОВНИТИ: hash], тег v0.5.0 [ЗАПОВНИТИ: hash], main [ЗАПОВНИТИ: hash], передавання [ЗАПОВНИТИ: remote / lab05.bundle].

Механізм сеансу (Application → Storage → Cookies → http://localhost:5080): назва .AspNetCore.Identity.Application, Domain localhost, Path /, HttpOnly так, SameSite Lax, Secure ні (локальний HTTP), тип Session, розмір 891 байт, Priority Medium. Value не записую. Докази: Рис. 5.

## Гіпотеза й REG-XSS-01 (Для всіх)

Карта Description: docs/source-to-sink.md. Потік: textarea «Опис нового інциденту» (browser-lab.html) → POST /api/incidents (browser-lab.js) → CreateIncidentRequest і валідація (Lab02Endpoints.cs) → таблиця incidents → GET /api/incidents/{id} (IncidentQueries.GetDetailsAsync) → current.description у browser-lab.js → #description.innerHTML.
Гіпотеза source → storage → response → sink/context: значення Description, збережене через POST і повернуте API без змін, потрапляє в innerHTML, тому браузер розбере його як HTML, а не як текст; marker має виконатися під час читання запису іншим користувачем.
Передумови: локальний стенд, користувачі Alice і Morgan, Guid запису [ЗАПОВНИТИ: Guid із Рис. 9], marker із resources/lab05-marker.txt (одним рядком, без змін), reset: dotnet run --project src/SecureLab.Api -- --reset-database, запуск: dotnet run --project src/SecureLab.Api --launch-profile http. Створення: Alice у /browser-lab.html (назва «Контрольний marker»); читання: Morgan у приватному вікні; контроль: Bob.

| Спостереження | До fix | Після fix | Посилання на доказ |
|---|---|---|---|
| Значення в БД/API | у відповіді GET поле description містить marker як звичайний рядок JSON (лапки записано як \") | те саме значення, збережений запис не змінювався | Рис. 11, Рис. 13 |
| DOM основного Description | усередині #description елемент <img src="x" onerror=…> | усередині #description один текстовий вузол, елемента <img> немає | Рис. 9, Рис. 13 |
| Дія marker | контрольний span #lab05-marker-target отримує data-lab05-marker="executed" | атрибута немає, команда перевірки повертає null | Рис. 9, Рис. 13 |
| Апостроф/лапки, 5 < 7, Unicode, HTML-like рядок | до fix не перевірялось (тестовий запис створено після виправлення) | відображається буквально, <b>жирний</b> лишається текстом | Рис. 14 |
| Довжина за власними правилами ЛР 2 | до fix не перевірялось | текст завдовжки 4000 символів відображено повністю, нічого не обрізано | Рис. 14 |
| Вікно Morgan: GET і перевірка marker; контроль Bob | Morgan: GET 200, до читання null, після читання 'executed'; Bob: GET 404, null | retest виконано на тому самому Guid у сеансі Alice: null, marker видно як текст | Рис. 10, Рис. 13 |

Finding: актив — browser-сеанс користувача, який читає чужий інцидент (дії від його імені); передумови — локальний тестовий акаунт Alice може зберегти довільний текст в Description, а Morgan має право читати цей ресурс за матрицею ЛР 4; першопричина — збережений текст з API присвоюється в innerHTML (browser-lab.js, load()), тому браузер розбирає його як HTML і виконує inline-обробник події; клас — stored XSS за збереженням і DOM-based за sink; вплив на стенді — marker виконав лише безпечну локальну дію (один атрибут на власному контрольному елементі); safe DOM diff — innerHTML = current.description замінено на textContent (Рис. 12), далі на єдиний helper renderText (Рис. 22); retest — той самий marker відображається як текст, атрибут null (Рис. 13); залишковий ризик — інші sinks і контексти (URL, атрибути), third-party scripts, нові місця з innerHTML; CSP і backend-валідація sink не виправляють.
Повторювана REG-XSS-01: reset; запуск API; вхід Alice; у /browser-lab.html створити інцидент із назвою «Контрольний marker» і marker в описі; Console: команда document.querySelector("#lab05-marker-target").getAttribute("data-lab05-marker"); вхід Morgan у приватному вікні, читання того самого Guid. Очікування до fix: 'executed' у обох сеансах; після fix: null і marker видно як текст. Негативний контроль: Bob читає той самий Guid — GET 404, null.

## CORS і REG-CORS-01 (Для всіх)

Прогноз (Етап 3, крок 1), заповнено до зміни policy:

| Сценарій | Чи доходить request до API? | Чи може JavaScript прочитати response? | Чому |
|---|---|---|---|
| same-origin 5080 → API | так | так | same-origin: CORS не потрібен |
| simple GET 4173 → API | так | ні без valid CORS | browser SOP/CORS |
| simple GET 4174 → API | так | ні, якщо origin не в allowlist | browser SOP/CORS |
| curl/.http → API | так | не застосовується | це не browser JavaScript |
| preflighted 4173 → API («Добре / Відмінно») | спочатку доходить OPTIONS; actual request залежить від відповіді | ні без успішного preflight | browser preflight і CORS |

Команди запуску probes: py -3 -m http.server 4173 --bind localhost --directory demonstrations/cors-probe; py -3 -m http.server 4174 --bind localhost --directory demonstrations/cors-probe (два окремі термінали з кореня проєкту). Policy — лише Development, один exact origin 4173 (WithOrigins, WithMethods("GET"), WithHeaders("X-Lab-Cors-Probe")); 4174 — контроль. Для «Задовільно» без AllowCredentials().

| Клієнт / простий запит | До policy | Fixed state | Status, ACAO, Console / доказ |
|---|---|---|---|
| Same-origin 5080 | /browser-lab.html → /api/incidents: GET 200, CORS не потрібен | без змін: GET 200 | status 200; ACAO не потрібен; Рис. 4 |
| Allowed 4173 | TypeError: Failed to fetch; Console: No 'Access-Control-Allow-Origin' header is present | {status: 200, body: '{"status":"ready"}'} | status 200, ACAO: http://localhost:4173; Рис. 15, Рис. 16 |
| Disallowed 4174 | TypeError: Failed to fetch; No 'Access-Control-Allow-Origin' | TypeError: Failed to fetch; запит дійшов до API, браузер не віддав response | status 200 на боці сервера, ACAO відсутній; Рис. 16 |
| Direct HTTP / curl | 200, response прочитано | 200, response прочитано для обох Origin | ACAO лише для Origin 4173; Рис. 17 |

REG-CORS-01: browser evidence — Рис. 15 (до policy), Рис. 16 (4173 дозволено, 4174 ні); дві actual HTTP-відповіді curl.exe -i з Origin: http://localhost:4173 і http://localhost:4174 до /health — обидві 200 з тілом {"status":"ready"}, заголовок Access-Control-Allow-Origin лише для 4173 (Рис. 17). Перевірка порядку через читання 401 без cross-origin cookie: з probe 4173 fetch до /api/incidents без credentials повертає число 401 без слів «blocked by CORS policy»; UseCors стоїть до UseAuthentication/UseAuthorization. Пояснення різниці browser/direct HTTP: CORS — політика, яку оголошує API, а застосовує тільки браузер; curl її не застосовує й читає response незалежно від заголовків; CORS не є authorization і не замінює CSRF-захист.

## Додатково для Добре та Відмінно

Другий flow IncidentComment.Text: Public seed comment Guid 30000000-0000-0000-0000-000000000005, автор Morgan, текст — marker із resources/lab05-marker.txt (константа Lab05Marker у DbSeeder.cs), інцидент Alice …0001; публічних коментарів цього інциденту стало 2. DOM до fix: у #comments елемент <img>, команда перевірки 'executed' (Рис. 21); після fix: текстовий вузол, null (Рис. 23). Єдиний safe helper renderText(container, text) у browser-lab.js викликається для #description і для кожного comment.text (Рис. 22).

Preflight з 4173 і 4174: запит із заголовком X-Lab-Cors-Probe; для 4173 — OPTIONS 204 з Access-Control-Allow-Origin: http://localhost:4173, Allow-Methods: GET, Allow-Headers: x-lab-cors-probe, далі actual GET 200; для 4174 — OPTIONS 204 без Access-Control-*, TypeError, actual GET не надіслано (Рис. 24). Cookie-дослід: з AllowCredentials() і входом Alice probe 4173 (credentials: "include") — 200, ACAO http://localhost:4173, Access-Control-Allow-Credentials: true; probe 4174 — response не надано, хоча запит із cookie до API надіслано й виконано; без AllowCredentials() — TypeError: Access-Control-Allow-Credentials '' замість true; без входу (приватне вікно) — 401, прочитаний JavaScript (Рис. 25). Кінцевий AllowCredentials() — лише Development policy для 4173. Два порти localhost — різні origins, але той самий site, тож не доводять поведінки SameSite. Review XSS/CORS/CSRF/authorization — у звіті DEL-03, розділ «Короткий review XSS, CORS, CSRF і authorization».

| Директива CSP | Фактичні ресурси застосунку | Обране джерело / причина |
|---|---|---|
| default-src | усі ресурси завантажуються з http://localhost:5080, зовнішніх хостів у Network немає | 'self': fallback лише на власний origin |
| script-src | власні скрипти app.js, browser-lab.js, auth-lab.js, inline-скриптів немає | 'self'; unsafe-inline не потрібен |
| style-src | власний CSS застосунку | 'self' |
| connect-src | fetch до API того самого origin 5080 | 'self'; probe-origins 4173/4174 тимчасові й до постійної CSP не входять |
| img-src | власні зображення, зовнішніх немає | 'self' |
| frame-ancestors | застосунок не має вбудовуватися в чужий frame | 'none'; для цієї директиви немає fallback до default-src, тому вказано явно |

Місце зміни middleware: Program.cs, спільна middleware response headers після UseStatusCodePages і до UseDefaultFiles/UseStaticFiles; виняток /scalar лише в Development (довідник зі сторонніми ресурсами), межу й причину винятку записано як залишковий ризик. Enforcement: document 200 і фактичний CSP header [ЗАПОВНИТИ: значення з Рис. 28]; три сторінки flow [ЗАПОВНИТИ: працюють без порушень — Рис. 26]; кероване порушення new Image() до 4173 — результат: [ЗАПОВНИТИ: повідомлення з «The action has been blocked» — Рис. 27]. Для «Добре» докази report-only не обов’язкові.

| Header / control | Фактичне значення | Ризик і межа захисту / policy rationale |
|---|---|---|
| X-Content-Type-Options | nosniff [ЗАПОВНИТИ: перевірити на Рис. 28] | зменшує ризик інтерпретації файла як іншого MIME-типу; не замінює правильний Content-Type і валідацію |
| Referrer-Policy | no-referrer [ЗАПОВНИТИ: перевірити на Рис. 28] | зменшує витік адреси сторінки в Referer на інші сайти; відповідає навігації клієнта (зовнішніх переходів немає); не замінює заборону класти секрети в URL |
| frame-ancestors | [ЗАПОВНИТИ: 'none' у CSP, Рис. 28] | запобігає вбудовуванню в чужий frame (clickjacking); не замінює авторизацію чи виправлення XSS |

## Додатково для Відмінно

DOM-тест: файл/команда [ЗАПОВНИТИ], browser [ЗАПОВНИТИ], vulnerable commit і FAIL [ЗАПОВНИТИ], fixed commit і PASS [ЗАПОВНИТИ]. Comments, повторний render без дублювання та позитивні рядки: повторне «Прочитати» дає 2 публічні коментарі без дублювання вузлів (document.querySelectorAll("#comments p").length = 2, Рис. 33); коментар із marker і HTML-like рядок відображаються як текст. CSP report-only → enforcement: фактичні headers, flow і кероване порушення в обох режимах [ЗАПОВНИТИ: Рис. 31 (report-only), Рис. 26–27 (enforcement)]. safe-url.js / атрибут: [ЗАПОВНИТИ: код і результат перевірки URL-протоколів, Рис. 32]. Пояснення text/attribute/URL/JavaScript та ризик third-party scripts — у звіті DEL-03, розділи «Контексти виведення» і «Ризик third-party scripts».

Додаткових DEL, навчальних демо або нового журналу не подавайте. Перед поданням повторіть чекліст для свого накопичувального рівня.