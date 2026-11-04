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
| **BIM** | [ParamChecker](https://github.com/Alexpivo394/ToadTools/wiki/ParamChecker), [Подключение к ToadFlow](https://github.com/Alexpivo394/ToadTools/wiki/ToadFlow), [Коллизии](https://github.com/Alexpivo394/ToadTools/wiki/Collisions), [ToadExporter](https://github.com/Alexpivo394/ToadTools/wiki/ToadExporter), рабочие наборы (создание и проверка), копирование элементов, общие → необщие семейства |
| **Общие** | Умные выноски, запись количества, запись прочерков, код классификатора, длина элементов, создание пространств |
| **Лотки** | Артикулы и покраска кабельных лотков, размещение крышек |
| **Сети связи** | Структурная схема |
| **ОВ** | Синхронизация теплопотерь с Excel |
| **АР** | Открывание дверей, основание отделки |
| **КР** | Армирование по цвету |

Ссылки на страницу каждого инструмента — на [главной странице вики](https://github.com/Alexpivo394/ToadTools/wiki).

## Установка

Скачайте установщик `.msi` из [релизов](https://github.com/Alexpivo394/ToadTools/releases), закройте Revit и запустите установщик.
Изменения по версиям — в [Changelog.md](Changelog.md).

Панель «Коллизии» и отправка итогов ParamChecker в ToadFlow требуют сервера ToadFlow 2.0.0 и подключения: **ToadTools → Подключение к ToadFlow**.

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
| `install` | Установщик |
| `wiki` | Вики; публикуется в GitHub Wiki при изменении в `master` |

Тесты:

```powershell
dotnet run --project source/ToadTools.Tests -c Release -f net10.0-windows
dotnet run --project source/ToadTools.Tests -c Release -f net48
```

Установщик для всех версий Revit — в папку `output`:

```powershell
cd build; dotnet run -c Release -- pack
```

Код для отдельных версий Revit отделяют директивы `#if REVIT2024_OR_GREATER` и подобные.

### Выпуск

1. Раздел `# X.Y.Z` с изменениями в начале `Changelog.md`.
2. Тег `X.Y.Z` или **Actions → Publish Release → Run workflow** с номером версии.

Workflow собирает установщик и создаёт релиз на GitHub с текстом раздела из `Changelog.md`; версию без тега определяет GitVersion по истории git.
Каждый push проверяет `.github/workflows/Compile.yml`: тесты под `net10.0-windows` и `net48` и сборку под все версии Revit.

## Связанные проекты

- [ToadFlow](https://github.com/Alexpivo394/ToadFlow) — сервис автоматизации Revit по расписанию: выгрузка, проверка LOI, коллизии.
- [ToadExport](https://github.com/Alexpivo394/ToadExport) — ядро выгрузки RVT и NWC, общее для ToadExporter и ToadFlow.
- [ToadParamCheck](https://github.com/Alexpivo394/ToadParamCheck) — ядро проверки LOI, общее для ParamChecker и ToadFlow.
