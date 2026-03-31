# Spec: ToxiProxy Integration

**Status:** approved
**Created:** 2026-03-31

---

## Summary

Добавить поддержку ToxiProxy как прокси-прослойки между адаптером и реальным сервисом технологии (Redis, Postgres, HTTP и др.). Пользователь объявляет `proxy: <name>` на сервисе в `project.yaml` — CLI автоматически поднимает контейнер ToxiProxy, настраивает его через REST API и перенаправляет connection string адаптера через прокси. Запущенные прокси сохраняются в контексте исполнения для последующего runtime-управления.

## Problem Statement

Для имитации сетевых сбоев (latency, jitter, connection drops) в load-тестах нужна прокси-прослойка с управляемыми «токсинами». Без этого невозможно тестировать поведение сервисов при деградации зависимостей.

## Goals

- [ ] В `project.yaml` у сервиса появляется необязательное поле `proxy: <name>`, где `<name>` — имя ToxiProxy-контейнера
- [ ] При компиляции проекта: адаптеры, чей `connects_to` указывает на сервис с `proxy`, получают connection string через прокси (hostname заменяется на имя прокси, порт остаётся тем же)
- [ ] При запуске: CLI поднимает один ToxiProxy-контейнер на каждый уникальный прокси, объявленный в проекте
- [ ] После старта контейнера: CLI конфигурирует прокси через ToxiProxy REST API — создаёт proxy-запись с upstream по Docker-алиасу реального сервиса
- [ ] Запущенные прокси сохраняются в `RunContext` для последующего runtime-управления

## Non-Goals (Out of Scope)

- Runtime-изменение конфигурации прокси (токсины, задержки) — отдельная спека
- CLI-команды для управления прокси в рантайме — отдельная спека
- Поддержка прокси для соединений между synthetic-сервисами (только адаптер → сервис)
- Автоматическое добавление токсинов по умолчанию

## Acceptance Criteria

- [ ] **AC-1:** Сервис в `project.yaml` с `proxy: redis-proxy` при компиляции порождает `ProxyDefinition` с именем `redis-proxy`, upstream `redis:{port}`, listen-портом равным порту сервиса
- [ ] **AC-2:** Адаптер, чей `connects_to` указывает на сервис с `proxy: redis-proxy`, получает `REDIS_CONNECTION_STRING=redis-proxy:6379` вместо `redis:6379`
- [ ] **AC-3:** Аналогично для Postgres: `Host=pg-proxy;Port=5432;...` вместо `Host=postgres;...`
- [ ] **AC-4:** Аналогично для HTTP: `HTTP_TARGET_URL=http://http-proxy:8080` вместо `http://backend:8080`
- [ ] **AC-5:** При запуске проекта для каждого `ProxyDefinition` стартует контейнер ToxiProxy (`ghcr.io/shopify/toxiproxy:2.12.0`) с сетевым алиасом равным имени прокси, в той же Docker-сети проекта
- [ ] **AC-6:** После старта контейнера CLI вызывает ToxiProxy REST API (`POST /proxies`) и создаёт proxy-запись: `listen=0.0.0.0:{port}`, `upstream={serviceAlias}:{port}` (алиас реального сервиса в Docker-сети)
- [ ] **AC-7:** Прокси-контейнеры поднимаются после инфраструктурных сервисов и до адаптеров
- [ ] **AC-8:** Каждый запущенный прокси представлен в `RunContext` как `RunningProxy` с полями: `Name`, `ProxiedService`, `ApiUrl` (адрес ToxiProxy REST API, доступный с хоста), `ListenPort`
- [ ] **AC-9:** Если у сервиса нет поля `proxy` — поведение не меняется, прокси не создаётся
- [ ] **AC-10:** Валидация при компиляции: если два сервиса объявляют одинаковое имя прокси — ошибка

## Domain & Architecture

```
src/
├── Archcraft.Domain/
│   └── Entities/
│       ├── ServiceDefinition.cs    # + Proxy?: string
│       └── ProxyDefinition.cs      # новый record
│
├── Archcraft.Domain/
│   └── Entities/
│       └── ExecutionPlan.cs        # + Proxies: IReadOnlyList<ProxyDefinition>
│
├── Archcraft.ProjectModel/
│   └── ServiceModel.cs             # + Proxy?: string
│
├── Archcraft.Serialization.Yaml/
│   └── YamlProjectLoader.cs        # маппинг proxy поля
│
├── Archcraft.ProjectCompiler/
│   └── ArchcraftProjectCompiler.cs # BuildProxies(), InjectAdapterEnvVars учитывает прокси
│
├── Archcraft.Execution/
│   └── RunningProxy.cs             # новый record
│
└── Archcraft.Execution.Docker/
    └── DockerEnvironmentRunner.cs  # StartProxyAsync(), ConfigureProxyAsync()

services/
└── (без изменений — прокси управляется CLI, не адаптерами)

samples/test_project/
└── project.yaml                    # пример с proxy: redis-proxy
```

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Один контейнер на прокси | 1 ToxiProxy контейнер = 1 проксируемый сервис | Чёткий маппинг 1:1 для runtime-управления |
| Порт прокси | Тот же, что у реального сервиса | Connection string меняется только в hostname |
| Upstream в ToxiProxy | Docker-алиас реального сервиса (`redis:6379`) | Оба контейнера в одной сети, алиас гарантированно резолвится |
| Конфигурация upstream | CLI вызывает ToxiProxy REST API после старта контейнера | Стандартный подход ToxiProxy; не требует кастомного образа |
| Порядок старта | Инфраструктурные сервисы → Прокси → Адаптеры | Прокси нужен реальный сервис как upstream, адаптеры нужен прокси |
| ToxiProxy image | `ghcr.io/shopify/toxiproxy:2.12.0` | Официальный образ, стабильная версия |
| ToxiProxy API порт | 8474 (дефолтный) | Нет причин менять |
| Имя прокси | Задаётся пользователем в `proxy: <name>` | Имя используется в будущих сценариях/командах |

## Open Questions

- (нет)

## Dependencies

- `ghcr.io/shopify/toxiproxy:2.12.0` — Docker-образ
- ToxiProxy REST API (`POST /proxies`, `GET /proxies`) — для конфигурации при старте

---

*Approved by: user Date: 2026-03-31*
