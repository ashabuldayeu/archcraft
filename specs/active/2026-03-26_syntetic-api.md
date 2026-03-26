# Spec: SynteticApi — Synthetic Service Container

**Status:** approved
**Created:** 2026-03-26
**Author:** —

---

## Summary

`SynteticApi` — это ASP.NET Minimal API (.NET 10), собираемый в Docker-образ, который имитирует реальный backend-сервис в системе archcraft. Сервис позволяет описать в конфигурации цепочку операций (pipeline) для каждого HTTP-эндпоинта, собирает метрики через OpenTelemetry, поддерживает pull-модель (`/metrics`, Prometheus) и push-модель (OTLP), а также позволяет изменять параметры симуляции в runtime через HTTP API.

## Problem Statement

Для тестирования observability-стека и нагрузочных сценариев в archcraft нужен контейнер, который ведёт себя как настоящий сервис: принимает запросы, выполняет цепочку операций (Redis, PostgreSQL, HTTP к другим сервисам), порождает реалистичные метрики, трейсы и логи. Без такого сервиса невозможно симулировать топологии из нескольких взаимодействующих компонентов.

## Goals

- [ ] Сервис запускается как Docker-контейнер из образа, собранного по Dockerfile в репозитории
- [ ] Конфигурация задаётся через расширение `ServiceModel` в `project.yaml`
- [ ] Каждый именованный endpoint (`alias`) доступен по `POST /{alias}`
- [ ] Каждый endpoint выполняет описанный pipeline операций (дерево с поддержкой `fallback` и `children`)
- [ ] Операции: `redis-call`, `pg-call`, `http-call` — реализованы как заглушки (stubs)
- [ ] Каждый шаг pipeline имеет настраиваемый `not-found-rate` (вероятность not-found ответа)
- [ ] Метрики собираются через OpenTelemetry: `http.server.request.duration`, `operation.duration`, `operation.calls.total`
- [ ] Pull-модель: `/metrics` в Prometheus exposition format
- [ ] Push-модель: OTLP HTTP/gRPC экспорт, адрес через `OTEL_EXPORTER_OTLP_ENDPOINT`
- [ ] Distributed tracing: каждый вызов pipeline порождает OTel span
- [ ] `correlation-id` генерируется на входе (или берётся из `X-Correlation-Id`), пробрасывается через pipeline и исходящие вызовы
- [ ] Runtime-изменение параметров через `PATCH /config`
- [ ] Structured logging через `Microsoft.Extensions.Logging`

## Non-Goals (Out of Scope)

- Реальная реализация операций (`redis-call`, `pg-call`, `http-call`) — только заглушки
- Аутентификация и авторизация эндпоинтов
- Персистентность конфигурации (конфиг in-memory, сбрасывается при рестарте)
- Горизонтальное масштабирование / state sharing между инстансами
- Поддержка других HTTP-методов кроме `POST /{alias}` для pipeline-эндпоинтов

## Acceptance Criteria

- [ ] AC-1: Сервис стартует в Docker-контейнере и отвечает `200 OK` на `GET /health`
- [ ] AC-2: При `POST /{alias}` выполняется pipeline, описанный для данного alias в конфиге; если alias не найден — `404`
- [ ] AC-3: `correlation-id` берётся из заголовка `X-Correlation-Id` входящего запроса; если заголовок отсутствует — генерируется новый UUID; значение присутствует в заголовке ответа и во всех span-атрибутах pipeline
- [ ] AC-4: При выполнении шага с `not-found-rate: 0.3` операция возвращает not-found примерно в 30% вызовов (случайно); при not-found выполняется `fallback`-ветка если она задана
- [ ] AC-5: `children`-операции выполняются последовательно после успешного завершения родительской операции
- [ ] AC-6: `GET /metrics` возвращает метрики в Prometheus exposition format; присутствуют `http_server_request_duration`, `operation_duration`, `operation_calls_total`
- [ ] AC-7: При заданном `OTEL_EXPORTER_OTLP_ENDPOINT` метрики и трейсы экспортируются через OTLP
- [ ] AC-8: `PATCH /config` с валидным телом обновляет `not-found-rate` для указанного шага без рестарта; последующие вызовы используют новое значение
- [ ] AC-9: `PATCH /config` с невалидным телом возвращает `400 Bad Request` с описанием ошибки
- [ ] AC-12: `GET /config` возвращает текущее состояние конфигурации (включая изменения применённые через `PATCH`)
- [ ] AC-10: Каждый вызов `POST /{alias}` порождает OTel span с атрибутами `alias`, `correlation_id`; каждый шаг pipeline порождает дочерний span с атрибутами `operation_type`, `result`
- [ ] AC-11: Dockerfile собирает образ без ошибок; образ запускается командой `docker run` с передачей конфига через env vars

## Domain & Architecture

Новый автономный сервис, не изменяет существующие проекты archcraft.

```
services/
└── synthetic/
    ├── SynteticApi.csproj
    ├── Dockerfile
    ├── Program.cs
    ├── Configuration/
    │   ├── SynteticApiOptions.cs       # корневая конфигурация сервиса
    │   ├── EndpointOptions.cs          # alias + pipeline
    │   └── PipelineStepOptions.cs      # operation, not-found-rate, fallback, children
    ├── Pipeline/
    │   ├── IPipelineExecutor.cs
    │   ├── PipelineExecutor.cs         # рекурсивный обход дерева шагов
    │   └── PipelineResult.cs
    ├── Operations/
    │   ├── IOperation.cs
    │   ├── OperationContext.cs         # correlation-id, span context
    │   ├── RedisCallOperation.cs       # stub
    │   ├── PgCallOperation.cs          # stub
    │   └── HttpCallOperation.cs        # stub
    ├── Observability/
    │   ├── Metrics.cs                  # OTel Meter + instrument definitions
    │   └── Tracing.cs                  # ActivitySource definitions
    └── Endpoints/
        ├── PipelineEndpoints.cs        # POST /{alias}
        ├── ConfigEndpoints.cs          # GET /config, PATCH /config
        ├── HealthEndpoints.cs          # GET /health
        └── MetricsEndpoints.cs         # GET /metrics (Prometheus)
```

Расширение `ServiceModel` в `project.yaml` (новые поля, обратно совместимы):
```yaml
services:
  - name: order-api
    image: archcraft/syntetic-api:latest
    port: 8080
    synthetic:
      endpoints:
        - alias: checkout
          pipeline:
            - operation: redis-call
              not-found-rate: 0.3
              fallback:
                - operation: pg-call
            - operation: http-call
              target: payment-service
        - alias: orders
          pipeline:
            - operation: pg-call
```

`PATCH /config` body:
```json
{
  "endpoints": {
    "checkout": {
      "steps": {
        "redis-call": { "not-found-rate": 0.5 }
      }
    }
  }
}
```

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| HTTP-метод для pipeline эндпоинтов | `POST /{alias}` | Фиксированный паттерн, alias в пути |
| Runtime конфиг | `PATCH /config` HTTP endpoint | Мгновенно, без рестарта, автоматизируемо из сценариев |
| Конфиг при старте | In-memory из YAML/env | Простота, достаточно для симуляции |
| Операции | Заглушки (stubs) | Реальные реализации — следующий этап |
| Fallback-семантика | Вложенное дерево (`fallback` / `children`) | Естественно отражает stack вызовов |
| Correlation ID | Из `X-Correlation-Id` или новый UUID | Стандарт для распределённых систем |
| Метрики pull | Prometheus `/metrics` | Стандарт de facto |
| Метрики push | OTLP через `OTEL_EXPORTER_OTLP_ENDPOINT` | OTel стандарт |

## Open Questions

- [x] Q1: Archcraft CLI сериализует `synthetic:` секцию и пробрасывает как env var `SYNTETIC_CONFIG` при запуске контейнера. Пользователь работает только с `project.yaml`.
- [x] Q2: `GET /config` добавляется — возвращает текущее in-memory состояние конфигурации (полезно для отладки и верификации `PATCH`).
- [x] Q3: `http-call` — заглушка, имитирует задержку. Резолвинг `target` по топологии — следующий этап.

## Dependencies

- `OpenTelemetry.Exporter.Prometheus.AspNetCore` — pull `/metrics`
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` — push OTLP
- `OpenTelemetry.Extensions.Hosting` — интеграция с ASP.NET host
- `Microsoft.Extensions.Options` — конфигурация
- .NET 10 / ASP.NET Minimal API

---

*Approved by: user Date: 2026-03-26*
