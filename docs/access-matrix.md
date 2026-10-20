# Матриця доступу — робочий шаблон

| Дія | Рівень | Анонім | Reporter | Analyst | Administrator | Власник має значення? | Стан реалізації |
|---|---|---|---|---|---|---|---|
| Перегляд списку GET /api/incidents | добрий | deny, 401 | own: лише власні (owner = principal) | allow: усі інциденти для аналізу | лише власні (як Reporter), додаткових прав немає | Так, для Reporter і Administrator | query scoping у IncidentQueries; тест A-05 |
| Пошук GET /api/incidents/search | відмінний (A-11) | deny, 401 | лише власні | усі інциденти для аналізу | лише власні | Так, для Reporter і Administrator | умова за owner у запиті до сортування й Take(50); тест A-11 |
| Зведення GET /api/incidents/severity-summary | відмінний (A-11) | deny, 401 | лише власні | усі інциденти для аналізу | лише власні | Так, для Reporter і Administrator | умова за owner у запиті до групування; тест A-11 |
| Перегляд деталей GET /api/incidents/{id} | достатній, головна action | deny, 401 | owner — allow; non-owner — deny, 404 (як для неіснуючого) | allow для аналізу | owner — allow; чужий — deny, 404 | Так | query scoping + resource handler; тести A-02, A-03, A-03R, A-10 |
| Створення інциденту POST /api/incidents | успадкований baseline | deny, 401 | allow for self | allow for self | allow for self | Сервер призначає власника з principal | перевірено baseline ЛР 3 |
| Update content PUT /api/incidents/{id} | добрий | deny, 401 | owner — allow (лише title, description, severity, occurredAtUtc); non-owner — deny, 404 | deny, 403 (немає права змінювати вміст) | owner — allow; чужий — deny, 404 | Так | resource handler, операція Update; тести A-04, A-08a |
| Delete DELETE /api/incidents/{id} | добрий | deny, 401 | owner — allow лише поки status = New; інакше deny, 403; non-owner — deny, 404 | deny, 403 | як Reporter | Так | resource handler, операція Delete; тест A-04 |
| Зміна status POST /api/incidents/{id}/triage | добрий | deny, 401 | deny, 403 (навіть для owner: це службова дія) | allow лише New → Triaged; інший поточний status — deny, 409 | deny, 403 (прав не опубліковано) | Ні (роль і поточний status) | перевірка ролі всередині endpoint + status; audit authorization.denied; тести A-06, A-07, A-09 |
| Перегляд коментаря GET /api/incident-comments/{id} | добрий | deny, 401 | internal comment — deny, 404; звичайний — allow лише якщо Reporter бачить батьківський інцидент, інакше 404 | allow після авторизації батьківського інцидента, включно з internal | як Reporter | Ні для internal-правила; доступ до parent перевіряється окремо | child через parent у запиті; тест A-12 |
| Export/audit | відмінний, якщо flow існує | не застосовується: export-endpoint у проєкті немає; audit лише записує подію, а endpoint читання не створюється |  |  |  |  | не застосовується |
## Політика HTTP-відмов

- 401 — анонімний запит до будь-якого захищеного маршруту (немає authentication context); не HTML-перенаправлення.
- 404 — authenticated non-owner звертається до чужого головного ресурсу (details, update, delete) або child через чужий parent. Відповідь побудовано тим самим helper, що й для неіснуючого id, тож відрізняється лише traceId. Так сервер не розкриває, що такий ресурс існує.
- 403 — користувач уже знає про ресурс або дія закрита роллю: власне, але не дозволене за станом (delete не в статусі New), Analyst без права змінювати вміст, Reporter на triage, Administrator без опублікованого права.
- 409 — Analyst виконує triage для інциденту, який уже не в статусі New (допустимий перехід лише New → Triaged).
- Політику застосовано однаково до details, update, delete, child, search і severity-summary.