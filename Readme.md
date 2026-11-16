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

## Как поставить

Плагин отдельно больше не ставится — за него это делает программа ToadTools. Скачайте `ToadTools-App-*.msi` со [страницы её релизов](https://github.com/Alexpivo394/ToadTools.Desktop/releases/latest) и запустите. Права администратора не нужны.

Дальше всё само: в трее появляется зелёная жаба, она находит Revit на компьютере, скачивает плагин и раскладывает его по нужным версиям. Вкладка **ToadTools** появится, когда вы в следующий раз откроете Revit.

Когда выходит новая версия, программа скачивает её заранее и ждёт, пока вы закроете Revit, — работу не прерывает. Закрыли — через пару секунд плагин обновлён, а в углу экрана об этом сообщение. Как часто проверять обновления, в какие версии Revit ставить, запускаться ли вместе с Windows — это в окне программы, оно открывается щелчком по значку. Подробнее — на странице [«Установка и обновление»](https://github.com/Alexpivo394/ToadTools/wiki/Install).

Что поменялось от версии к версии — в [Changelog.md](Changelog.md).

Пара вещей, о которых стоит знать заранее:

- панели «Коллизии» и отправке итогов ParamChecker нужен сервер ToadFlow 2.0.0 и вход в него: **ToadTools → Вход → Подключение к ToadFlow**;
- часть инструментов берёт семейства, шаблоны и таблицы Excel с сетевого диска компании. Если вы не в корпоративной сети, эти файлы придётся раздобыть отдельно.

## Для разработчиков

Понадобятся .NET 10 SDK и .NET Framework 4.8 Developer Pack, а писать удобно в Rider или Visual Studio.

Revit каждого года — своя конфигурация решения `ToadTools.slnx`: от `Debug.R20` / `Release.R20` до `Debug.R27` / `Release.R27`. Debug-сборка сразу кладёт плагин в папку надстроек нужного Revit — собрали, открыли Revit, проверили. Если код отличается между версиями Revit, его разводят директивами вроде `#if REVIT2024_OR_GREATER`.

Где что лежит:

- `source/ToadTools` — сама надстройка: лента и регистрация команд;
- `source/<Инструмент>` — по проекту на инструмент: команды, модели, окна;
- `source/ToadTools.Connection` — подключение к ToadFlow, общее для всех инструментов;
- `source/ToadTools.UI` — общие элементы интерфейса;
- `source/ToadTools.Tests` — тесты, которым не нужен Revit;
- `build` — сборка релиза;
- `wiki` — вики; при изменениях в `master` она сама публикуется на GitHub.

Тесты гоняются под оба рантайма, на которых живёт плагин:

```powershell
dotnet run --project source/ToadTools.Tests -c Release -f net10.0-windows
dotnet run --project source/ToadTools.Tests -c Release -f net48
```

### Как выходит новая версия

1. Допишите сверху `Changelog.md` раздел `# X.Y.Z` — что поменялось.
2. Поставьте тег `X.Y.Z` (или запустите **Actions → Publish release** с номером версии).

GitHub соберёт плагин под все версии Revit, упакует в архив `ToadTools-X.Y.Z.zip`, подпишет его и выложит релиз с текстом из Changelog. Установленные программы ToadTools увидят его при следующей проверке и обновят плагин у всех сами.

Собрать архив у себя можно так — он окажется в папке `output`:

```powershell
cd build; dotnet run -c Release -- pack
```

Про подпись. Программа ставит только архив, подписанный нашим ключом, — так никто не сможет подсунуть пользователям чужие DLL под видом обновления. Ключ лежит в секретах репозитория (`SIGNING_KEY`), тот же самый — у ToadTools.Desktop. Без него релиз просто не опубликуется. Для локальной сборки ключ можно положить в user-secrets: `dotnet user-secrets set Publish:SigningKey <ключ>` в папке `build`.

И про формат архива: внутри лежат папки по годам, в каждой `ToadTools.addin` и папка `ToadTools` — ровно как после сборки в `publish`. Программа рассчитывает именно на такую раскладку, поэтому если захочется её поменять — меняйте вместе с [ToadTools.Desktop](https://github.com/Alexpivo394/ToadTools.Desktop).

## Родственные проекты

- [ToadTools.Desktop](https://github.com/Alexpivo394/ToadTools.Desktop) — та самая программа в трее, которая ставит и обновляет плагин.
- [ToadFlow](https://github.com/Alexpivo394/ToadFlow) — сервис, который по расписанию выгружает модели, проверяет LOI и ищет коллизии.
- [ToadExport](https://github.com/Alexpivo394/ToadExport) — ядро выгрузки RVT и NWC, общее для ToadExporter и ToadFlow.
- [ToadParamCheck](https://github.com/Alexpivo394/ToadParamCheck) — ядро проверки LOI, общее для ParamChecker и ToadFlow.
- [ToadDesign](https://github.com/Alexpivo394/ToadDesign) — оформление всех окон.
