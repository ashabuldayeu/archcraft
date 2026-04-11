# Spec: Scenario Drain Phase & Post-Scenario Cleanup

**Status:** approved
**Created:** 2026-04-11
**Author:** ashabuldayeu

---

## Summary

Ввести двухфазное завершение HTTP-сценария: после окончания `duration` останавливается генерация новых запросов, но уже отправленные (in-flight) запросы получают настраиваемый таймаут (`drain_timeout`) для завершения. По истечении таймаута оставшиеся запросы отменяются через `CancellationToken` и засчитываются как ошибки. Дополнительно — опциональный перезапуск перечисленных контейнеров после сценария для сброса состояния.

## Problem Statement

Сейчас `HttpScenarioRunner` после окончания `duration` ждёт всего 500 мс и снимает метрики. Если сервис под нагрузкой деградировал, тысячи in-flight запросов не попадают в статистику (они просто "теряются" или завершаются уже вне окна сбора). Это даёт недостоверные `p50`/`p99`/`error_rate`. Кроме того, накопленное состояние (данные в БД, медленные коннекции) мешает следующему сценарию стартовать из чистого состояния.

## Goals

- [ ] После `duration` сценарий ждёт завершения in-flight запросов до `drain_timeout`
- [ ] После `drain_timeout` оставшиеся in-flight запросы отменяются через `CancellationToken` и попадают в `error_rate`
- [ ] После дрейна опционально перезапускаются указанные контейнеры по алиасу
- [ ] Оба параметра (`drain_timeout`, `restart_after`) задаются в `project.yaml` на уровне сценария

## Non-Goals (Out of Scope)

- Отдельная метрика `unhandled` в `MetricSnapshot` — отменённые запросы идут в `error_rate`
- Изменения в `TimelineScenarioRunner`
- "Reset API" для адаптеров/синтетик-сервисов
- Параллельный перезапуск контейнеров

## Acceptance Criteria

- [ ] AC-1: `ScenarioDefinition` содержит два новых опциональных поля: `DrainTimeout: Duration?` и `RestartAfter: IReadOnlyList<string>`.

- [ ] AC-2: `ScenarioModel` содержит `drain_timeout: string?` и `restart_after: List<string>?`, корректно десериализуются из YAML.

- [ ] AC-3: Если `drain_timeout` не задан в YAML, поведение `HttpScenarioRunner` не меняется (backward compatibility).

- [ ] AC-4: Если `drain_timeout` задан, `HttpScenarioRunner` после окончания load-фазы переходит в drain-фазу: ждёт завершения всех in-flight запросов до `drain_timeout`.

- [ ] AC-5: По истечении `drain_timeout` оставшиеся in-flight запросы принудительно отменяются через `CancellationToken`. Каждый отменённый запрос фиксируется в `IMetricsCollector` как ошибка.

- [ ] AC-6: `IEnvironmentRunner` содержит новый метод `RestartContainerAsync(string alias, CancellationToken ct)`.

- [ ] AC-7: `DockerEnvironmentRunner` реализует `RestartContainerAsync`: останавливает контейнер (`StopAsync`), затем запускает (`StartAsync`) по имени из `_containerByServiceName`.

- [ ] AC-8: Если `restart_after` задан, `HttpScenarioRunner` после drain-фазы последовательно перезапускает каждый контейнер из списка через `IEnvironmentRunner.RestartContainerAsync`.

- [ ] AC-9: Если контейнер из `restart_after` не найден в `IEnvironmentRunner`, выбрасывается `InvalidOperationException` с именем контейнера (аналогично `KillReplicaAsync`).

- [ ] AC-10: `HttpScenarioRunner` принимает `IEnvironmentRunner` как зависимость (через DI). Если `restart_after` пуст, `IEnvironmentRunner` не вызывается.

## Domain & Architecture

```
src/
├── Archcraft.Domain/
│   └── Entities/
│       └── ScenarioDefinition.cs     # +DrainTimeout: Duration?, +RestartAfter: IReadOnlyList<string>
├── Archcraft.ProjectModel/
│   └── ScenarioModel.cs              # +drain_timeout: string?, +restart_after: List<string>?
├── Archcraft.Serialization.Yaml/
│   └── YamlProjectLoader.cs          # маппинг новых полей ScenarioModel → ScenarioDefinition
├── Archcraft.Contracts/
│   └── IEnvironmentRunner.cs         # +RestartContainerAsync(string alias, CancellationToken ct)
├── Archcraft.Execution.Docker/
│   └── DockerEnvironmentRunner.cs    # реализация RestartContainerAsync
└── Archcraft.Scenarios/
    └── HttpScenarioRunner.cs         # drain-фаза + restart_after; +IEnvironmentRunner dependency
```

## Key Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Механизм отмены in-flight | `CancellationTokenSource` drain CTS + `Task.WhenAll(...).WaitAsync(drainTimeout)` | Не нужно менять сигнатуры `FireRequestAsync`; отменённые запросы автоматически записываются как ошибки |
| Отдельная метрика unhandled | Нет, идут в `error_rate` | Упрощает модель; всё, что не завершилось успешно до конца дрейна — ошибка |
| Инъекция `IEnvironmentRunner` в runner | Только в `HttpScenarioRunner` | Timeline runner вне скоупа; `IScenarioRunner` интерфейс не меняется |
| Перезапуск контейнеров | Последовательный stop → start | Простой путь; параллельный — вне скоупа |
| Backward compatibility | `DrainTimeout == null` → поведение не меняется | Существующие project.yaml не требуют изменений |

## Open Questions

_(нет)_

## Dependencies

- Существующий `IEnvironmentRunner` — добавляется один метод
- Существующий `IMetricsCollector` — не меняется (ошибки уже пишутся)
- Существующий `Duration` value object — используется для `DrainTimeout`

---

*Approved by: — Date: —*
