# STORY-001 — Шаблон строгости (template-kit)

- Статус: backlog
- Волна: пилот коллаб-протокола (первая)
- Источник: личный docs-репо — `idea-collab-protocol.md` (раздел «Пилот: rshared») + `idea-code-strictness.md`

## Цель

rshared становится пилотом коллаб-протокола и источником шаблона строгости: `rshared/template/` — канонические файлы для обряда инициализации новых репо; `docs/stories/` — работа по протоколу; конституция репо получает блок канона (волна 1).

## Техзацепки

- Канон кита: `idea-code-strictness.md`, раздел «Стандартный кит .NET» (флаги, editorconfig, анализаторы, BannedSymbols, ArchTests, ci.yml).
- Текст вставки волны 1: `idea-collab-protocol.md`, раздел «Распространение».
- Существующий `Directory.Build.props` rshared (версия/автор) — не трогать: шаблон живёт отдельным файлом в `template/`.

## План (draft — уточняется и согласуется на план-гейте)

- Что/зачем: пять файлов шаблона + TEMPLATE/ROADMAP (уже лежат) + блок канона в конституции; пилот проверяет канон в бою.
- Зона поражения: `rshared/template/**` (новое), конституция репо (+блок в конец); `src/` и корневые конфиги — не трогаем.
- Оценка объёма: ~300–400 строк суммарно.
- Горизонт: шаблон размножится на все новые репо и волны 2–3 существующих — ошибки формулировок наследуются всеми; аккуратность выше обычного. Шов: шаблон — копия, не пакет (RShared.Build — этап 2).
- Готовое: всё берётся из канона; свои решения — только компоновка файлов.

## Критерии готовности

- [ ] `template/Directory.Build.props` — Nullable, ImplicitUsings, TreatWarningsAsErrors, AnalysisLevel latest-recommended, EnforceCodeStyleInBuild
- [ ] `template/.editorconfig` — стиль + серьёзности порогов (метод ≤ 40 строк, параметров ≤ 4, сложность ≤ 15) и анализаторов (Roslynator, Meziantou, BannedApiAnalyzers с списком в `template/Directory.Packages.props`)
- [ ] `template/manifest.md` — манифест дизайна (12 строк канона, включая ORM/БД и велосипеды)
- [ ] `template/BannedSymbols.txt` — скелет с закомментированным примером (rengine, доктрина 2)
- [ ] `template/ci.yml` — build → dotnet format --verify-no-changes → test
- [ ] `template/README.md` — как применять шаблон при инициализации нового репо
- [ ] конституция rshared: блок канона волны 1, не раздута
- [ ] `dotnet build` решения зелёный (шаблон ничего не ломает)
- [ ] разбор заполнен, статус → review; сторя закрыта после прочтения человеком

## Вне рамок

Применение кита к `src/` самого rshared; ArchTests для rshared; волны в других репо; правка глобальной конституции; пакет RShared.Build.

## Разбор

(агент заполнит до перевода в review)

## Журнал

- 07.09: сторя создана из docs-сессии (brainstorm → сторя; прецедент rcode). TEMPLATE.md и ROADMAP.md положены вместе со сторей.
