# F-02. Успішний привілейований перехід не має audit-запису

**Статус:** Fixed (commit 9aa04b2)  
**Актив:** аудит привілейованої зміни Incident.Status (action incident.triage)  
**Компонент:** метод TriageAsync у IncidentMutationEndpoints  
**Пріоритет:** P2

## Передумови і межі огляду

Commit b7bc9ce (стан до виправлення); Development; користувачі Alice, Bob (Reporter), Morgan (Analyst); діагностичний журнал у файл поза репозиторієм; новий інцидент у статусі New.

## Відтворення

1. Alice створює інцидент (New).
2. Bob виконує triage: 403, у журналі authorization.denied із trace, що збігається з traceId відповіді.
3. Morgan виконує triage: 200, статус Triaged.
4. Пошук у журналі за incident.triage.

Очікувано: запис про успішний перехід із суб’єктом, дією, ресурсом, результатом і часом. Фактично: запис про відмову є, запису про успішний перехід немає.

## Вплив і ймовірність

**Статус перевірки:** спостережено локально. Привілейована зміна стану проходить без audit-сліду: неможливо відновити, хто, коли і над яким ресурсом її виконав. Умова: кожен успішний triage. CVSS не обчислюється.

## Першопричина

TriageAsync логує лише гілку відмови; після успішного збереження одразу повертає відповідь.

## Виправлення

Після SaveChangesAsync додано запис audit.transition (subject, action, resource, result=allowed, from, to, occurredAtUtc, trace): лише ідентифікатори, стани й час. Запис ставиться після збереження. Fixed commit: 9aa04b2.

## Retest

Той самий сценарій: у журналі рівно два записи (authorization.denied для Bob і audit.transition для Morgan), trace збігається з traceId відповідей.

## Positive regression

Повторний triage (409) нового audit-запису не створює; повний набір тестів пройшов (46 із 46).

## Залишковий ризик

Аудит охоплює лише incident.triage; PUT і DELETE audit-записів не пишуть.

## Доказ

Команда: `Select-String -Path <журнал> -Pattern 'authorization\.denied|audit\.transition'`. Рядки журналу зі зв’язком за trace; значень cookie, паролів і токенів немає.