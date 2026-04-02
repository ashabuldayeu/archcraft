# Spec: Timeline Scenarios DSL

**Status:** approved
**Created:** 2026-03-31

---

## Summary

Расширить движок сценариев новым форматом `timeline` — структурированным таймлайном событий. Таймлайн описывает последовательность временных меток (`at`), в каждой из которых запускается один или несколько экшнов: `load` (HTTP-нагрузка), `inject_latency` (ToxiProxy latency), `inject_error` (ToxiProxy error rate). Каждый экшн с `duration` автоматически откатывается после истечения времени. Формат `timeline` мержится с текущим форматом: поле `type: http` заменяется таймлайном, при этом `startup_timeout` остаётся.

## Problem Statement

Текущий формат сценариев (`type: http`, фиксированные `rps`/`duration`) не позволяет описывать динамические нагрузочные сценарии с изменением условий во времени — например, «старт нагрузки, затем деградация сети, затем восстановление». Для реалистичного хаос-тестирования нужен таймлайн с управлением ToxiProxy в рантайме.

## Goals

- [ ] Новый YAML-формат `timeline` в секции `scenarios` с поддержкой временных меток `at`
- [ ] Три типа экшнов: `load`, `inject_latency`, `inject_error`
- [ ] Экшн `load`: HTTP POST-нагрузка на endpoint synthetic-сервиса с заданным `rps`; при перекрытии — новый заменяет предыдущий для того же `target`
- [ ] Экшн `inject_latency`: добавляет latency-токсин в ToxiProxy между `from` и `to`; после `duration` — откат (удаление токсина)
- [ ] Экшн `inject_error`: добавляет error-токсин в ToxiProxy между `from` и `to`; после `duration` — откат
- [ ] Валидация при компиляции: `target` для `load` должен быть synthetic-сервисом; прокси между `from` и `to` должен существовать для inject-экшнов
- [ ] `TimelineScenarioRunner` выполняет таймлайн, управляя нагрузкой и ToxiProxy через `EnvironmentContext`

## Non-Goals (Out of Scope)

- Параллельный запуск нескольких `load` на разные `target` с суммированием (заменяет, не суммирует)
- Поддержка других протоколов кроме HTTP для `load`
- Новые типы токсинов ToxiProxy кроме latency и error_rate
- Визуализация таймлайна
- Горячая перезагрузка конфигурации во время выполнения

## Acceptance Criteria

- [ ] **AC-1:** YAML `timeline` корректно десериализуется в `TimelineScenarioDefinition` с полным деревом `TimelinePoint → TimelineAction`
- [ ] **AC-2:** Компилятор проверяет: `target` в `load`-экшне — это synthetic-сервис (имеет `synthetic:` секцию); при ошибке — `InvalidOperationException` с понятным сообщением
- [ ] **AC-3:** Компилятор проверяет: для `inject_latency` и `inject_error` существует прокси между `from` и `to` (т.е. сервис `to` имеет `proxy:`, и через этот прокси идёт соединение `from → to`); при ошибке — `InvalidOperationException`
- [ ] **AC-4:** `TimelineScenarioRunner` запускает экшны точек `at` в момент `offset` от старта (±100ms точность)
- [ ] **AC-5:** Экшн `load` запускает HTTP POST-нагрузку на `http://{resolvedTarget}/{endpoint}` с заданным `rps`; при новом `load` на тот же `target` — предыдущая нагрузка останавливается
- [ ] **AC-6:** Экшн `inject_latency` вызывает ToxiProxy REST API (`POST /proxies/{name}/toxics`) с типом `latency` и заданным `latency` (мс); по истечении `duration` — удаляет токсин (`DELETE /proxies/{name}/toxics/{toxic_name}`)
- [ ] **AC-7:** Экшн `inject_error` вызывает ToxiProxy REST API с типом `limit_data` или `reset_peer` для имитации ошибок с заданным `error_rate`; по истечении `duration` — удаляет токсин
- [ ] **AC-8:** После истечения `duration` у любого экшна состояние возвращается к нулевому: `load` — 0 rps (остановлен), токсины — удалены
- [ ] **AC-9:** Сценарий без `timeline` (старый формат с `type: http`) продолжает работать без изменений
- [ ] **AC-10:** `startup_timeout` в новом формате работает так же: runner ждёт доступности `target`-сервиса перед стартом таймлайна
- [ ] **AC-11:** Общая длительность сценария = максимальный `at + duration` среди всех точек таймлайна; сценарий завершается когда все экшны отработали и откаты применены

## Domain & Architecture

```
src/
├── Archcraft.Domain/
│   └── Entities/
│       ├── ScenarioDefinition.cs        # без изменений (legacy)
│       └── TimelineScenarioDefinition.cs # новый: Name, StartupTimeout, Timeline
│       └── TimelinePoint.cs             # новый: At (Duration), Actions
│       └── TimelineAction.cs            # новый: sealed hierarchy — LoadAction, InjectLatencyAction, InjectErrorAction
│
├── Archcraft.ProjectModel/
│   └── ScenarioModel.cs                 # + Timeline: List<TimelinePointModel>?
│   └── TimelinePointModel.cs            # новый YAML-model
│   └── TimelineActionModel.cs           # новый YAML-model
│
├── Archcraft.Serialization.Yaml/
│   └── YamlProjectLoader.cs             # маппинг timeline → TimelineScenarioDefinition
│
├── Archcraft.ProjectCompiler/
│   └── ArchcraftProjectCompiler.cs      # валидация AC-2, AC-3; компиляция timeline-сценариев
│
├── Archcraft.Scenarios/
│   └── TimelineScenarioRunner.cs        # новый runner
│   └── IScenarioRunner.cs               # без изменений
│
└── Archcraft.Contracts/
    └── IEnvironmentRunner.cs            # + GetRunningProxy(name): RunningProxy
```

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Тип токсина для `inject_error` | `reset_peer` (обрывает соединение) | Наиболее близко к реальной ошибке соединения; `error_rate` применяется через процент обрываемых байт |
| Разрешение `target` для `load` | Через `EnvironmentContext.GetMappedAddress(name)` + endpoint из конфига | Прокси и адреса уже разрешены в рантайме |
| Обнаружение прокси между from→to | Сервис `to` имеет `Proxy != null` + в `connections` есть `from→to` via этот прокси | Данные уже есть в скомпилированном плане |
| Точность таймера | `Task.Delay` с коррекцией дрейфа (вычитаем elapsed) | Достаточно для интервалов ≥1s |
| Откат токсинов | `DELETE /proxies/{name}/toxics/{toxic_name}` через ToxiProxy API | Стандартный ToxiProxy API |
| Перекрытие load-экшнов | Новый `load` на тот же `target` отменяет `CancellationToken` предыдущего | Простая и предсказуемая семантика |

## Open Questions

- [ ] Q1: `inject_error` — уточнить точный тип токсина ToxiProxy для `error_rate`. Предлагается `reset_peer` с `rate` (от 0 до 1). Требует подтверждения при реализации.

## Dependencies

- Спека `2026-03-31_toxiproxy-integration.md` — `RunningProxy` в `EnvironmentContext`, ToxiProxy REST API
- ToxiProxy REST API: `POST /proxies/{name}/toxics`, `DELETE /proxies/{name}/toxics/{name}`

---

*Approved by: user Date: 2026-03-31*
