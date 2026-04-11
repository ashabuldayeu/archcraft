# Spec: Interactive Run Mode

**Status:** approved
**Created:** 2026-04-11
**Author:** ashabuldayeu

---

## Summary

Переработать команду `run` в интерактивный режим: после старта всех контейнеров программа не завершается, а ждёт команд пользователя. Сценарии запускаются явно по команде. Контейнеры живут до явной команды `stop` или выхода из консоли (Ctrl+C / SIGTERM). Отчёт генерируется по команде `report`. Шум из логов .NET-инфраструктуры убирается — остаются только логи проекта.

## Problem Statement

Сейчас `run` запускает все сценарии автоматически и сразу останавливает контейнеры. Это не позволяет:
- Исследовать Grafana во время работы сценариев
- Запускать сценарии повторно или выборочно
- Держать окружение живым для ручного тестирования

## Goals

- [ ] После старта контейнеров программа входит в интерактивный REPL
- [ ] Команды: `run <name>`, `run all`, `report`, `help`, `stop`
- [ ] Все контейнеры (сервисы, адаптеры, observability) живут до явной остановки
- [ ] Корректный shutdown по `stop`, Ctrl+C, SIGTERM, закрытию консоли
- [ ] Логи фильтруются: остаются только `Archcraft.*` namespace

## Non-Goals (Out of Scope)

- Параллельный запуск нескольких сценариев одновременно
- Горячая перезагрузка конфига без перезапуска программы
- История команд (readline/up-arrow)
- Интерактивная команда `validate`

## Acceptance Criteria

- [ ] AC-1: После старта всех контейнеров в консоли появляется приветствие с доступными командами и Grafana URL (если есть observability).

- [ ] AC-2: Команда `help` выводит список доступных команд с кратким описанием.

- [ ] AC-3: Команда `run <scenario-name>` запускает конкретный сценарий по имени. Если имя не найдено — выводит ошибку, не падает.

- [ ] AC-4: Команда `run all` запускает все сценарии последовательно в том порядке, в котором они определены в `project.yaml`.

- [ ] AC-5: После выполнения `run` / `run all` в консоли выводится краткая таблица результатов только для только что запущенных сценариев.

- [ ] AC-6: Команда `report` выводит сводную таблицу всех сценариев, запущенных в текущей сессии, с разбивкой по запуску (если один сценарий запускался несколько раз — каждый запуск как отдельная строка).

- [ ] AC-7: Команда `report` сохраняет JSON-файл с накопленными результатами в `results/`.

- [ ] AC-8: Команда `stop` корректно останавливает все контейнеры и завершает программу.

- [ ] AC-9: Ctrl+C и SIGTERM (закрытие терминала) вызывают тот же graceful shutdown, что и `stop`.

- [ ] AC-10: В консоли отсутствуют логи из namespace `System.*`, `Microsoft.*`, `DotNet.Testcontainers.*`. Остаются только логи `Archcraft.*` и пользовательский вывод (таблицы, команды).

- [ ] AC-11: Testcontainers-логи вида `[testcontainers.org ...]` подавляются (они идут в stdout напрямую, не через ILogger — нужно переопределить `TestcontainersSettings`).

- [ ] AC-12: Неизвестная команда выводит `Unknown command. Type 'help' for available commands.` без завершения сессии.

## Domain & Architecture

```
src/
├── Archcraft.App/
│   ├── UseCases/
│   │   ├── RunProjectUseCase.cs     # разделить на SetupAsync / RunScenarioAsync / TeardownAsync
│   │   └── InteractiveSessionUseCase.cs  # новый: REPL-цикл, аккумуляция результатов
│   └── ServiceCollectionExtensions.cs   # регистрация нового use case
├── Archcraft.Cli/
│   └── Commands/
│       └── RunCommand.cs            # делегирует в InteractiveSessionUseCase
│       └── Program.cs (или аналог)  # настройка фильтрации логов
```

## Key Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Команды REPL | `run <name>`, `run all`, `report`, `help`, `stop` | Минимально достаточный набор; расширяемо |
| Хранение результатов сессии | `List<(string runLabel, MetricSnapshot snapshot)>` внутри `InteractiveSessionUseCase` | Позволяет строить разбивку по запускам в `report` |
| Graceful shutdown | `CancellationTokenSource` + `Console.CancelKeyPress` + `AppDomain.CurrentDomain.ProcessExit` | Стандартный паттерн .NET для терминальных приложений |
| Фильтрация логов ILogger | `AddFilter("System", LogLevel.None)` + `AddFilter("Microsoft", LogLevel.None)` в `ILoggingBuilder` | Самый простой способ без изменения кода сервисов |
| Подавление Testcontainers stdout | `TestcontainersSettings.Logger = NullLogger.Instance` | Единственный способ управлять их stdout-выводом |
| `run` без флагов | Всегда интерактивный режим | Упрощает UX; старое поведение убирается |

## Open Questions

_(нет)_

## Dependencies

- Существующий `RunProjectUseCase` — рефакторинг на setup/run/teardown
- `Console.CancelKeyPress` — работает на Windows/Linux/Mac
- `TestcontainersSettings` — статический класс, доступен из DI-конфигурации

---

*Approved by: — Date: —*
