# Spec: Observability Stack (Prometheus + Grafana)

**Status:** approved
**Created:** 2026-04-02

---

## Summary

Добавить опциональный блок `observability` в `project.yaml`, при наличии которого CLI автоматически поднимает Prometheus и Grafana, а также exporter-контейнеры для инфраструктурных сервисов (Redis, Postgres). Перед стартом CLI генерирует JSON-дашборды из embedded-шаблонов в папку `{project}/dashboards/`. Grafana стартует с предсозданными дашбордами и datasource через provisioning. Блок опциональный — отсутствие `observability` не влияет на запуск проекта.

## Problem Statement

После запуска нагрузочных и хаос-сценариев нет визуального наблюдения за состоянием сервисов в реальном времени. Пользователь видит только итоговый `MetricSnapshot`, но не динамику latency, error rate и ресурсов инфраструктуры во время деградации.

## Goals

- [ ] Опциональный блок `observability` в `project.yaml` с конфигурацией Prometheus и Grafana (порт, образ)
- [ ] При наличии блока: CLI поднимает контейнеры Prometheus и Grafana в той же Docker-сети проекта
- [ ] Автоматический старт exporter-контейнеров для каждого Redis и Postgres сервиса в проекте
- [ ] Перед стартом контейнеров CLI генерирует дашборды из embedded-шаблонов в `{project}/dashboards/`
- [ ] Grafana стартует с предсозданным datasource (Prometheus) и дашбордами через volume provisioning
- [ ] Базовые дашборды: synthetic-сервис (RPS, p50/p99, error rate, operations), Redis (memory, ops/sec, clients, hit rate), Postgres (connections, transactions/sec, query latency)
- [ ] Папка `{project}/dashboards/` добавляется в `.gitignore`

## Non-Goals (Out of Scope)

- Дашборды для ToxiProxy / прокси-контейнеров
- Алертинг (alert rules в Grafana/Prometheus)
- Долгосрочное хранение метрик (retention)
- Поддержка других баз данных (MongoDB и др.) в этой спеке
- Кастомизация дашбордов пользователем через `project.yaml` (только образ/порт)
- Отдельная команда `init-dashboards` (генерация происходит при `run`)

## Acceptance Criteria

- [ ] **AC-1:** Проект без блока `observability` запускается без изменений в поведении
- [ ] **AC-2:** При наличии `observability` компилятор создаёт `ObservabilityDefinition` с настройками Prometheus и Grafana; `ExecutionPlan` содержит это определение
- [ ] **AC-3:** Если указан образ Grafana/Prometheus ниже минимальной поддерживаемой версии — CLI выводит предупреждение (не ошибку) и продолжает запуск
- [ ] **AC-4:** Перед стартом Docker-контейнеров CLI генерирует файлы в `{project}/dashboards/`: по одному JSON-дашборду на каждый synthetic-сервис и на каждый Redis/Postgres сервис проекта
- [ ] **AC-5:** Для каждого Redis-сервиса автоматически стартует `redis_exporter` контейнер в той же сети; Prometheus scrape-конфиг включает его endpoint
- [ ] **AC-6:** Для каждого Postgres-сервиса автоматически стартует `postgres_exporter` контейнер; Prometheus scrape-конфиг включает его endpoint
- [ ] **AC-7:** Synthetic-сервисы уже экспортируют `/metrics` — Prometheus scrape-конфиг включает их endpoint
- [ ] **AC-8:** Prometheus стартует с `prometheus.yml`, сгенерированным CLI на основе запущенных сервисов (Docker-сетевые алиасы)
- [ ] **AC-9:** Grafana стартует с provisioning-конфигом datasource (Prometheus) и папкой дашбордов, примонтированной через volume
- [ ] **AC-10:** Grafana доступна с хоста по настроенному порту после старта
- [ ] **AC-11:** Observability-контейнеры (Prometheus, Grafana, exporters) поднимаются после основных сервисов и до выполнения сценариев
- [ ] **AC-12:** Папка `{project}/dashboards/` добавлена в корневой `.gitignore` паттерном `**/dashboards/`

## Domain & Architecture

```
src/
├── Archcraft.Domain/
│   └── Entities/
│       └── ObservabilityDefinition.cs   # новый: PrometheusConfig, GrafanaConfig
│
├── Archcraft.Domain/
│   └── Entities/
│       └── ExecutionPlan.cs             # + Observability?: ObservabilityDefinition
│
├── Archcraft.ProjectModel/
│   └── ObservabilityModel.cs            # новый YAML-model
│   └── ProjectFileModel.cs              # + Observability?: ObservabilityModel
│
├── Archcraft.Serialization.Yaml/
│   └── YamlProjectLoader.cs             # маппинг observability блока
│
├── Archcraft.ProjectCompiler/
│   └── ArchcraftProjectCompiler.cs      # + CompileObservability()
│
├── Archcraft.Observability/
│   ├── Dashboards/                      # embedded JSON-шаблоны
│   │   ├── synthetic.dashboard.json
│   │   ├── redis.dashboard.json
│   │   └── postgres.dashboard.json
│   ├── Templates/
│   │   ├── prometheus.yml.template      # embedded scrape-конфиг
│   │   └── grafana-datasource.yml       # embedded provisioning
│   └── DashboardGenerator.cs           # новый: генерация файлов из шаблонов
│
└── Archcraft.Execution.Docker/
    └── DockerEnvironmentRunner.cs       # + StartObservabilityAsync()

samples/test_project/
└── project.yaml                         # пример блока observability
```

### YAML-формат

```yaml
observability:
  prometheus:
    port: 9090
    image: prom/prometheus:v3.2.1        # опционально, есть дефолт
  grafana:
    port: 3000
    image: grafana/grafana:11.5.2        # опционально, есть дефолт
```

### Минимальные версии (предупреждение при несовпадении)

| Сервис | Минимальная версия |
|--------|-------------------|
| Grafana | 10.0.0 |
| Prometheus | 2.40.0 |

### Exporter-образы (фиксированные, не конфигурируются пользователем)

| Технология | Образ |
|------------|-------|
| Redis | `oliver006/redis_exporter:v1.67.0` |
| Postgres | `prometheuscommunity/postgres-exporter:v0.16.0` |

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Хранение шаблонов | Embedded resources в `Archcraft.Observability` | Версионируются с кодом, не требуют сети, всегда актуальны |
| Генерация дашбордов | При каждом `run` | Всегда актуально, не нужно помнить отдельную команду |
| Папка дашбордов | `{project}/dashboards/` в `.gitignore` | Генерируемый артефакт, как `results/` |
| Несовместимый образ | Предупреждение, не ошибка | Пользователь сам отвечает за выбор версии |
| Scrape через Docker-алиасы | Да | Prometheus в той же Docker-сети, алиасы гарантированно резолвятся |
| Exporter-конфиг | Автоматический, не конфигурируется | Упрощает UX; exporters — деталь реализации |
| Observability опциональна | Да | Не ломает существующие проекты без блока |

## Open Questions

- (нет)

## Dependencies

- Synthetic-сервисы уже экспортируют `/metrics` (OpenTelemetry Prometheus exporter — реализовано)
- Docker embedded volumes для Grafana provisioning (поддержано Testcontainers)

---

*Approved by: user Date: 2026-04-02*
