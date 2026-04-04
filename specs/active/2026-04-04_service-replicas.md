# Spec: Service Replicas

**Status:** approved
**Created:** 2026-04-04

---

## Summary

Добавить поддержку горизонтального масштабирования synthetic-сервисов через поле `replicas: N` в `project.yaml`. При `replicas > 1` CLI создаёт N независимых контейнеров сервиса с общим DNS-алиасом (Docker Round-Robin), per-replica ToxiProxy и per-replica адаптерами. Timeline DSL расширяется синтаксисом `service[N]` для точечного таргетинга конкретной реплики, а также новыми action-типами `kill` и `restore`. Метрики собираются агрегированно и с разбивкой по репликам.

## Problem Statement

Сейчас archcraft не позволяет тестировать поведение системы при частичной деградации — когда тормозит или падает один из нескольких инстансов. Это один из самых реалистичных production-сценариев (rolling deploy, spot instance eviction, memory OOM на одной реплике), и проверить его нельзя без поддержки реплик.

## Goals

- [ ] Поле `replicas: N` на synthetic-сервисе создаёт N контейнеров с shared DNS-alias и уникальными алиасами per-replica
- [ ] `proxy: per_replica` (с указанием имени) автоматически создаёт per-replica ToxiProxy: `{name}-{index}`
- [ ] Для каждой реплики автоматически создаются per-replica адаптеры: `{adapter-name}-{index}`
- [ ] Timeline DSL поддерживает `service[N]` для точечного таргетинга конкретной реплики (0-based)
- [ ] `load` action поддерживает `target: service` (все реплики, RR) и `target: service[N]` (конкретная)
- [ ] `inject_latency` и `inject_error` поддерживают `to: service[N]` (конкретный proxy) и `to: service` (все proxies)
- [ ] Новые action-типы: `kill` (остановить реплику) и `restore` (перезапустить)
- [ ] `kill` с `duration:` автоматически вызывает restore по истечении
- [ ] `MetricSnapshot` содержит агрегат + разбивку по репликам
- [ ] `replicas: 1` (default) поведение идентично текущему

## Non-Goals (Out of Scope)

- Реплики инфраструктурных сервисов (redis, postgres) — отдельная история
- Autoscaling (изменение числа реплик во время сценария)
- Stateful replicas / sticky sessions
- Weighted load balancing (только RR)
- Health-check based LB (Docker DNS не учитывает health)
- Явный LB-контейнер (nginx/Traefik)

## Acceptance Criteria

- [ ] **AC-1:** Сервис с `replicas: 3` создаёт контейнеры `{name}-0`, `{name}-1`, `{name}-2`; каждый получает уникальный alias `{name}-{index}` и НЕ получает общий alias `{name}` напрямую
- [ ] **AC-2:** При наличии `proxy: {proxy-name}` создаются proxies `{proxy-name}-0`, `{proxy-name}-1`, `{proxy-name}-2`; каждый получает alias `{proxy-name}-{index}` и общий alias `{name}` (RR entry point для вызывающих)
- [ ] **AC-3:** При отсутствии `proxy` и наличии `replicas > 1` каждый контейнер получает alias `{name}-{index}` и shared alias `{name}` напрямую
- [ ] **AC-4:** Адаптеры, объявленные в `synthetic.adapters`, автоматически размножаются: `{adapter-name}-{index}` на каждую реплику; каждая реплика `{name}-{index}` получает env `ADAPTER_OP_*_URL` указывающий на свой адаптер `{adapter-name}-{index}`
- [ ] **AC-5:** `load target: backend` в timeline посылает запросы на все реплики через RR (циклически по host-портам реплик); `load target: backend[1]` — только на replica-1
- [ ] **AC-6:** `inject_latency to: backend[1]` инжектирует токсин только на `{proxy-name}-1`; `inject_latency to: backend` инжектирует на все per-replica proxies одновременно
- [ ] **AC-7:** `inject_error` работает аналогично `inject_latency` для таргетинга реплик
- [ ] **AC-8:** `kill target: backend[2]` останавливает контейнер `backend-2`; proxy `{proxy-name}-2` продолжает работать, возвращая connection refused вызывающим
- [ ] **AC-9:** `restore target: backend[2]` перезапускает контейнер `backend-2` и ждёт его readiness
- [ ] **AC-10:** `kill` с `duration: 15s` автоматически вызывает restore после истечения duration
- [ ] **AC-11:** Указание `target: backend[N]` где N >= replicas → ошибка компиляции с понятным сообщением
- [ ] **AC-12:** `MetricSnapshot` содержит агрегированные p50/p99/error_rate по всем репликам + `ReplicaSnapshots` с теми же метриками per-replica (опционально, только если реплик > 1)
- [ ] **AC-13:** Сервис с `replicas: 1` (или без поля) компилируется и запускается идентично текущему поведению без per-replica именования

## Domain & Architecture

```
src/
├── Archcraft.Domain/Entities/
│   ├── ServiceDefinition.cs        # + Replicas: int (default 1)
│   ├── TimelineAction.cs           # + KillAction, RestoreAction
│   └── MetricSnapshot.cs           # + ReplicaSnapshots: IReadOnlyDictionary<string, MetricSnapshot>?
│
├── Archcraft.ProjectModel/
│   └── ServiceModel.cs             # + Replicas: int, proxy: per_replica parsing
│
├── Archcraft.Serialization.Yaml/
│   └── YamlProjectLoader.cs        # маппинг replicas, parse proxy-name для per_replica
│
├── Archcraft.ProjectCompiler/
│   └── ArchcraftProjectCompiler.cs # ExpandReplicas(): разворачивает ServiceDefinition в N реплик,
│                                   # размножает адаптеры, строит per-replica ProxyDefinitions,
│                                   # валидирует индексы в timeline actions
│
├── Archcraft.Execution/
│   └── EnvironmentContext.cs       # поддержка регистрации N реплик одного сервиса
│
├── Archcraft.Contracts/
│   └── IEnvironmentRunner.cs       # + KillReplicaAsync, RestoreReplicaAsync
│
├── Archcraft.Execution.Docker/
│   └── DockerEnvironmentRunner.cs  # StartServiceAsync для реплик (alias стратегия),
│                                   # KillReplicaAsync / RestoreReplicaAsync (stop/start container)
│
└── Archcraft.Scenarios/
    └── TimelineScenarioRunner.cs   # обработка KillAction, RestoreAction,
                                    # RR-цикл по репликам для LoadAction,
                                    # fan-out inject на все proxies при `to: service`
```

### YAML-формат

```yaml
services:
  - name: backend
    image: archcraft/synthetic:latest
    port: 8080
    replicas: 3
    proxy: backend-proxy          # создаёт backend-proxy-0, backend-proxy-1, backend-proxy-2
    readiness:
      path: /health
      timeout: 30s
    synthetic:
      adapters:
        - redis-adapter            # auto: redis-adapter-0, redis-adapter-1, redis-adapter-2
      endpoints:
        - alias: process
          pipeline:
            - operation: redis-call
```

### Timeline DSL — новые примеры

```yaml
timeline:
  - at: 0s
    actions:
      - type: load
        target: backend            # RR по всем репликам
        endpoint: process
        rps: 30

  - at: 5s
    actions:
      - type: inject_latency
        target:
          from: frontend
          to: backend[1]           # только backend-proxy-1
        latency: 300ms
        duration: 10s

  - at: 8s
    actions:
      - type: kill
        target: backend[2]
        duration: 15s              # auto-restore через 15s

  - at: 23s
    actions:
      - type: restore              # явный restore (если duration не указан)
        target: backend[0]
```

### Именование при развёртке

| Компонент | replicas: 1 | replicas: 3 с proxy: bp |
|---|---|---|
| Контейнер | `backend` | `backend-0`, `backend-1`, `backend-2` |
| Уникальный alias | `backend` | `backend-0`, `backend-1`, `backend-2` |
| RR entry-point alias | — | на proxies: `backend` |
| ToxiProxy | `bp` (если есть) | `bp-0`, `bp-1`, `bp-2` |
| Proxy alias | `bp` | `bp-0`, `bp-1`, `bp-2` + shared `backend` |
| Адаптер | `redis-adapter` | `redis-adapter-0`, `redis-adapter-1`, `redis-adapter-2` |

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| DNS RR entry-point | Alias `{name}` на per-replica proxies | Весь трафик к сервису проходит через proxy; без proxy — alias на контейнерах |
| Именование реплик | `{name}-{index}`, 0-based | Просто, предсказуемо, без конфликтов |
| Proxy naming | `{proxy-name}-{index}` | Пользователь видит имя + может адресовать конкретный proxy |
| Адаптеры | Auto per-replica, без yaml-изменений | Zero-config; адаптер — деталь реализации реплики |
| `kill` без `restore` | Прокси продолжает принимать трафик, возвращает conn refused | Реалистично; replica failed, LB не знает |
| RR для `load` с host | Циклически по host-mapped портам реплик | Docker DNS RR недоступен с хоста |
| `to: service` в inject | Fan-out на все per-replica proxies | "Деградация всего сервиса" — частый сценарий |
| Метрики | Aggregate + per-replica в одном snapshot | Не ломает существующий reporting; разбивка опциональна |

## Open Questions

- (нет)

## Dependencies

- ToxiProxy интеграция (реализована)
- Timeline DSL (реализован)
- `IEnvironmentRunner` / `DockerEnvironmentRunner` (реализованы)

---

*Approved by: user Date: 2026-04-04*
