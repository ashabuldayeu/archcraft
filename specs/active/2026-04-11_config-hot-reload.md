# Spec: Config Hot Reload (Scenarios)

**Status:** approved
**Created:** 2026-04-11
**Author:** ashabuldayeu

---

## Summary

Добавить `FileSystemWatcher` на `project.yaml` в интерактивной сессии. При изменении файла — перечитывать и применять только список сценариев (`Scenarios`, `TimelineScenarios`). Контейнеры не трогаем. Пользователь получает уведомление в консоль. При невалидном YAML — ошибка в консоль, старый конфиг сохраняется, REPL продолжает работу.

## Problem Statement

Сейчас `ExecutionPlan` создаётся один раз при старте сессии и не обновляется. Чтобы поменять сценарий (rps, duration, target), нужно полностью перезапускать программу с остановкой и повторным запуском всех контейнеров. Это замедляет итерацию при настройке нагрузочных тестов.

## Goals

- [ ] `FileSystemWatcher` следит за `project.yaml` в течение всей интерактивной сессии
- [ ] При изменении файла — 1-секундный debounce, затем reload сценариев
- [ ] Reload обновляет `Scenarios` и `TimelineScenarios` в живом `ExecutionPlan`; контейнеры не перезапускаются
- [ ] Консольное уведомление после успешного reload с количеством сценариев
- [ ] При невалидном YAML — ошибка в консоль, старый план сохраняется, REPL продолжает работу
- [ ] `FileSystemWatcher` корректно останавливается при выходе из сессии

## Non-Goals (Out of Scope)

- Reload `services`, `adapters`, `connections`, `observability`
- Обнаружение того, что именно изменилось (сервисы vs сценарии)
- Перезапуск контейнеров при изменении конфигурации сервисов
- Hot reload вне интерактивного режима (`ExecuteAsync` / `ScenarioCommand`)

## Acceptance Criteria

- [ ] AC-1: После `SetupAsync` в `InteractiveSessionUseCase` запускается `FileSystemWatcher` на директорию `project.yaml` с фильтром по имени файла.

- [ ] AC-2: При каждом событии `Changed` / `Renamed` / `Created` — debounce-таймер сбрасывается и запускается заново на 1 секунду. Несколько быстрых сохранений вызывают ровно один reload.

- [ ] AC-3: После истечения debounce вызываются `IProjectLoader.LoadAsync` и `IProjectCompiler.Compile` на актуальном файле.

- [ ] AC-4: Если reload успешен — `_currentPlan` обновляется через `plan with { Scenarios = ..., TimelineScenarios = ... }`. Запущенный в этот момент сценарий использует старую ссылку на план и завершается штатно; следующий `run` использует новый план.

- [ ] AC-5: После успешного reload в консоль выводится:
  ```
  [config] Scenarios reloaded — N scenario(s) available.
  Note: changes to services/adapters/connections take effect only after restart.
  ```

- [ ] AC-6: Если `IProjectLoader.LoadAsync` или `IProjectCompiler.Compile` бросает исключение — в консоль выводится `[config] Reload failed: <message>`. Старый `_currentPlan` сохраняется. REPL продолжает работу.

- [ ] AC-7: `_currentPlan` хранится в поле `InteractiveSessionUseCase` (не в локальной переменной `RunAsync`), чтобы file watcher мог обновить его из фонового потока.

- [ ] AC-8: Обновление `_currentPlan` потокобезопасно — через `Interlocked.Exchange` или `lock`.

- [ ] AC-9: `FileSystemWatcher` и debounce-ресурсы утилизируются (`Dispose`) при выходе из `RunAsync` (команда `stop`, Ctrl+C, завершение сессии).

- [ ] AC-10: Если `project.yaml` не существует в момент срабатывания (например, временно удалён редактором) — reload пропускается молча до следующего события.

## Domain & Architecture

```
src/
└── Archcraft.App/
    └── UseCases/
        └── InteractiveSessionUseCase.cs   # +FileSystemWatcher, debounce, _currentPlan field,
                                           #  ReloadScenariosAsync(), потокобезопасное обновление
```

Зависимости, которые уже есть и переиспользуются:
- `IProjectLoader` — для повторного чтения файла
- `IProjectCompiler` — для компиляции нового плана
- `ExecutionPlan` (record) — `with`-выражение для частичного обновления

## Key Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Debounce механизм | `CancellationTokenSource` + `Task.Delay(1s)` | Отменяем предыдущий delayed task при каждом новом событии — чисто, без таймеров |
| Обновление плана во время работы сценария | Применяем сразу; бегущий сценарий использует старую ссылку | Сценарии запускаются с захваченной ссылкой на план; обновление поля не влияет на текущий запуск |
| Потокобезопасность | `lock (_reloadLock)` при чтении/записи `_currentPlan` | FSW срабатывает на thread pool; REPL читает `_currentPlan` на основном потоке |
| Предупреждение о контейнерах | Всегда, при каждом reload | Не усложняем diff-логику; пользователь сам знает что менял |

## Open Questions

_(нет)_

## Dependencies

- `InteractiveSessionUseCase` — уже реализован, расширяется
- `IProjectLoader`, `IProjectCompiler` — уже в DI, доступны через конструктор

---

*Approved by: — Date: —*
