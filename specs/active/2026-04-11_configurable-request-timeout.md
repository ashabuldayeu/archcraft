# Spec: Configurable Request Timeout

**Status:** approved
**Created:** 2026-04-11
**Author:** ashabuldayeu

---

## Summary

Сделать таймаут HTTP-запроса при нагрузочном тестировании настраиваемым через YAML. Сейчас в обоих runners hardcode `10s` для load-запросов и `5s` для health-check. Это не позволяет пользователю подобрать таймаут под характеристики тестируемого сервиса, что приводит к ложным ошибкам при высоком RPS.

## Problem Statement

`client.Timeout = TimeSpan.FromSeconds(10)` в `FireRequestAsync` — магическое число. При высоком RPS (1000+) с медленным сервисом этот таймаут определяет эффективную пропускную способность: `maxInFlight / timeout`. Пользователь не может его изменить без правки кода. Health-check таймаут `5s` также захардкожен.

## Goals

- [ ] `request_timeout` настраивается в YAML на уровне `load` action (для timeline-сценариев)
- [ ] `request_timeout` настраивается в YAML на уровне сценария (для HTTP-сценариев)
- [ ] Дефолт `request_timeout` = `5s` (было `10s`)
- [ ] Health-check таймаут в обоих runners = `2s` (было `5s`)

## Non-Goals (Out of Scope)

- Таймаут для других типов action (inject_latency, kill, restore)
- Настраиваемый health-check таймаут через YAML
- Retry-логика при таймауте

## Acceptance Criteria

- [ ] AC-1: `ScenarioDefinition` содержит поле `RequestTimeout: Duration` с дефолтом `5s`.

- [ ] AC-2: `LoadAction` содержит поле `RequestTimeout: Duration` с дефолтом `5s`.

- [ ] AC-3: `ScenarioModel` содержит YAML-поле `request_timeout: string?`. При отсутствии в YAML используется дефолт `5s`.

- [ ] AC-4: `TimelineActionModel` для type=`load` содержит YAML-поле `request_timeout: string?`. При отсутствии в YAML используется дефолт `5s`.

- [ ] AC-5: `YamlProjectLoader` маппит `request_timeout` из `ScenarioModel` в `ScenarioDefinition.RequestTimeout`.

- [ ] AC-6: `YamlProjectLoader` маппит `request_timeout` из `LoadAction`-модели в `LoadAction.RequestTimeout`.

- [ ] AC-7: `HttpScenarioRunner.FireRequestAsync` использует `scenario.RequestTimeout.Value` вместо hardcoded `10s`.

- [ ] AC-8: `HttpScenarioRunner.WaitForTargetAsync` использует `2s` вместо hardcoded `5s` для `client.Timeout`.

- [ ] AC-9: `TimelineScenarioRunner.FireRequestAsync` принимает `TimeSpan timeout` и использует его для `client.Timeout` вместо hardcoded `10s`.

- [ ] AC-10: `TimelineScenarioRunner.RunLoadLoopAsync` передаёт `load.RequestTimeout.Value` через `FireAndReleaseAsync` в `FireRequestAsync`.

- [ ] AC-11: `TimelineScenarioRunner.WaitForSyntheticServicesAsync` использует `2s` вместо hardcoded `5s` для `client.Timeout`.

## Domain & Architecture

```
src/
├── Archcraft.Domain/
│   └── Entities/
│       ├── ScenarioDefinition.cs   # +RequestTimeout: Duration (default 5s)
│       └── TimelineActions.cs      # LoadAction: +RequestTimeout: Duration (default 5s)
├── Archcraft.ProjectModel/
│   ├── ScenarioModel.cs            # +request_timeout: string?
│   └── TimelineActionModel.cs      # +request_timeout: string? (только для load)
├── Archcraft.Serialization.Yaml/
│   └── YamlProjectLoader.cs        # маппинг request_timeout для сценариев и load action
└── Archcraft.Scenarios/
    ├── HttpScenarioRunner.cs        # использует RequestTimeout; health-check 2s
    └── TimelineScenarioRunner.cs    # передаёт timeout через RunLoadLoopAsync → FireRequestAsync; health-check 2s
```

## Key Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Уровень параметра для timeline | `load` action (AC-4, не сценарий) | Один timeline может иметь несколько load action с разными сервисами и характеристиками |
| Дефолт | `5s` | Разумный компромисс; 10s слишком терпимо к зависшим соединениям |
| Health-check таймаут | Hardcode `2s`, не в YAML | Пользователю не нужно его менять; 2s достаточно для доступного сервиса |

## Open Questions

_(нет)_

## Dependencies

- `Duration` value object — уже используется в `ScenarioDefinition`, переиспользуется
- Нет внешних зависимостей

---

*Approved by: — Date: —*
