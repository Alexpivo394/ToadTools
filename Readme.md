<p align="center">
    <h1 align="center">ToadTools</h1>
    <p align="center">Плагины для Autodesk Revit 2020–2027</p>
</p>

ToadTools — набор инструментов для Revit, который снимает рутину с проектировщиков и BIM-специалистов: проверка и заполнение параметров, рабочие наборы, пакетная выгрузка моделей, кабельные лотки, теплопотери, армирование, коллизии из [ToadFlow](https://github.com/Alexpivo394/ToadFlow).
Все инструменты — на вкладке **ToadTools** ленты Revit.

Инструкции по каждому инструменту — в [вики](https://github.com/Alexpivo394/ToadTools/wiki).

## Инструменты

| Панель | Инструменты |
|---|---|
| **Вход** | [Подключение к ToadFlow](https://github.com/Alexpivo394/ToadTools/wiki/ToadFlow) |
| **BIM** | [ParamChecker](https://github.com/Alexpivo394/ToadTools/wiki/ParamChecker), [ToadExporter](https://github.com/Alexpivo394/ToadTools/wiki/ToadExporter), рабочие наборы (создание и проверка), копирование элементов, общие → необщие семейства |
| **Общие** | [Коллизии](https://github.com/Alexpivo394/ToadTools/wiki/Collisions), умные выноски, запись количества, запись прочерков, код классификатора, длина элементов, создание пространств |
| **Лотки** | Артикулы и покраска кабельных лотков, размещение крышек |
| **Сети связи** | Структурная схема |
| **ОВ** | Синхронизация теплопотерь с Excel |
| **АР** | Открывание дверей, основание отделки |
| **КР** | Армирование по цвету |

Ссылки на страницу каждого инструмента — на [главной странице вики](https://github.com/Alexpivo394/ToadTools/wiki).

## Установка

Скачайте `ToadTools-App-*.msi` из [последнего релиза программы ToadTools](https://github.com/Alexpivo394/ToadTools.Desktop/releases/latest) и запустите. Права администратора не нужны.

Программа сидит в трее, сама скачивает плагин для найденных версий Revit и ставит новые версии, когда Revit закрыт. Частота проверки, версии Revit, автозапуск и уведомления настраиваются в её окне: щёлкните значок в трее. Подробно — [Установка и обновление](https://github.com/Alexpivo394/ToadTools/wiki/Install).
Изменения по версиям — в [Changelog.md](Changelog.md).

Панель «Коллизии» и отправка итогов ParamChecker в ToadFlow требуют сервера ToadFlow 2.0.0 и подключения: **ToadTools → Вход → Подключение к ToadFlow**.

> [!NOTE]
> Часть инструментов берёт семейства, шаблоны и таблицы Excel с сетевого диска компании. Вне корпоративной сети эти файлы нужно получить отдельно.

## Разработка

Нужны .NET 10 SDK и .NET Framework 4.8 Developer Pack; IDE — JetBrains Rider или Visual Studio.

Решение `ToadTools.slnx` собирается под каждую версию Revit своей конфигурацией: `Debug.R20`…`Debug.R27` и `Release.R20`…`Release.R27`.
Debug-сборка раскладывает аддин в папку аддинов Revit этой версии.

| Папка | Что там |
|---|---|
| `source/ToadTools` | Приложение Revit: лента и регистрация команд |
| `source/<Инструмент>` | Проект каждого инструмента: команды, модели представления, окна |
| `source/ToadTools.Connection` | Общее подключение к ToadFlow: вход, токен, клиент API, очередь итогов ParamChecker |
| `source/ToadTools.UI` | Общие элементы интерфейса |
| `source/ToadTools.Tests` | Тесты без Revit API |
| `build` | Пайплайн сборки ModularPipelines |
| `wiki` | Вики; публикуется в GitHub Wiki при изменении в `master` |

Тесты:

```powershell
dotnet run --project source/ToadTools.Tests -c Release -f net10.0-windows
dotnet run --project source/ToadTools.Tests -c Release -f net48
```

Архив плагина для всех версий Revit `ToadTools-X.Y.Z.zip` и его подпись `.zip.sig` — в папку `output`:

```powershell
cd build; dotnet run -c Release -- pack
```

Архив ставит программа [ToadTools.Desktop](https://github.com/Alexpivo394/ToadTools.Desktop) — у неё свой репозиторий, свои релизы и своя версия. Внутри архива `{год}/ToadTools.addin` и `{год}/ToadTools/**`, как папка `publish` сборки: это договорённость с программой, менять её — только вместе с ней.

Архив подписывается ключом ECDSA P-256: приватный ключ — секрет `SIGNING_KEY` (тот же, что в ToadTools.Desktop; локально `dotnet user-secrets set Publish:SigningKey <ключ>` в `build`), публичный зашит в программу. Без подписи релиз не публикуется, а программа не ставит архив. Подписать файл вручную: `SIGNING_KEY=<ключ> dotnet run --project build -- sign <файл>`.

Код для отдельных версий Revit отделяют директивы `#if REVIT2024_OR_GREATER` и подобные.

### Выпуск

1. Раздел `# X.Y.Z` с изменениями в начале `Changelog.md`.
2. Тег `X.Y.Z` или **Actions → Publish Release → Run workflow** с номером версии.

Workflow собирает и подписывает архив плагина и создаёт релиз на GitHub с текстом раздела из `Changelog.md`; версию без тега определяет GitVersion по истории git.
Каждый push проверяет `.github/workflows/Compile.yml`: тесты под `net10.0-windows` и `net48` и сборку под все версии Revit.

## Связанные проекты

- [ToadFlow](https://github.com/Alexpivo394/ToadFlow) — сервис автоматизации Revit по расписанию: выгрузка, проверка LOI, коллизии.
- [ToadExport](https://github.com/Alexpivo394/ToadExport) — ядро выгрузки RVT и NWC, общее для ToadExporter и ToadFlow.
- [ToadTools.Desktop](https://github.com/Alexpivo394/ToadTools.Desktop) — программа в трее, которая ставит и обновляет этот плагин.
- [ToadParamCheck](https://github.com/Alexpivo394/ToadParamCheck) — ядро проверки LOI, общее для ParamChecker и ToadFlow.
