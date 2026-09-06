# rshared

Общие библиотеки-«рельсы» для проектов revealyan: каждая живёт сама по себе, минимум зависимостей, подключение одной строкой в хост.

## Структура

- `src/RShared.<Name>/` — один каталог = один NuGet-пакет, у каждого свой `README.md`.
- `RShared.slnx` — решение в XML-формате, все проекты перечислены только в нём.
- `Directory.Build.props` — общие свойства сборки: версия пакетов, автор, ссылка на репозиторий.
- `.github/workflows/publish.yml` — публикация: тег `v*` → pack решения → GitHub Packages.

## Конвенции кода

- net10.0, `ImplicitUsings`, `Nullable` в каждом csproj; web-пакеты берут ASP.NET через `FrameworkReference Microsoft.AspNetCore.App`, не через PackageReference.
- Отступы в коде — табы; в csproj — два пробела.
- XML-doc на публичном API — английский; README пакетов — русский; UTF-8.
- Публичная поверхность пакета: `Option`-класс конфигурации + статический `Extensions` (`Add*` для DI, `Map*` для пайплайна); регистрации через `TryAdd*`, чтобы потребитель мог перебить своей.
- Пакеты изолированы: `ProjectReference` на соседей — исключение, а не правило (сейчас только `Orm.EntityFrameworkCore` → `Orm`). Внешняя зависимость добавляется осознанно, версия пинуется в csproj пакета.
- Ветки/коммиты: main, conventional commits.

## Потребление пакетов

GitHub Packages требует авторизацию даже на чтение: в проекте-потребителе `nuget.config` с source `https://nuget.pkg.github.com/revealyan/index.json` и PAT со `read:packages` (переменные окружения), см. корневой `README.md`.
