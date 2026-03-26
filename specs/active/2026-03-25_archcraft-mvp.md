# Spec: Archcraft MVP

**Status:** approved
**Created:** 2026-03-25
**Author:** —

---

## Summary

Archcraft — это CLI-инструмент уровня DevTool, который позволяет декларативно описать backend-систему в YAML, поднять её в реальных Docker-контейнерах, прогнать сценарии нагрузки и получить отчёт. Цель MVP — работающий end-to-end прототип: `archcraft run project.yaml` реально поднимает контейнеры, генерирует HTTP-нагрузку и выдаёт результаты.

---

## Problem Statement

Разработчикам и DevOps-инженерам нужен лёгкий инструмент для локального тестирования backend-систем в условиях, близких к продакшн: с реальными зависимостями (БД, API), с нагрузкой и с измерением базовых метрик — без сложной инфраструктуры и ручной оркестрации.

---

## Goals

- [ ] Декларативное описание системы в YAML (сервисы, topology, сценарии)
- [ ] Поднятие Docker-контейнеров через Testcontainers с разрешением зависимостей (topological sort)
- [ ] Полноценная service-to-service topology: API → API → DB → Worker и т.д.
- [ ] Проброс переменных окружения по `connections` (alias → `host:port` целевого сервиса)
- [ ] Все сервисы проекта — в одной Docker-сети; обращение по имени сервиса как hostname
- [ ] HTTP load-сценарий: заданный RPS на заданную длительность
- [ ] Сбор метрик: latency (p50, p99), error rate
- [ ] Вывод результатов в консоль (таблица) и в JSON-файл рядом с project.yaml
- [ ] Публикация как .NET global tool (`dotnet tool install -g archcraft`)

---

## Non-Goals (Out of Scope)

- Kubernetes / любая облачная оркестрация
- UI (web, desktop)
- Сложный Chaos-движок (только интерфейс + enum типов; `via: proxy` — stub без реализации)
- Unit-тесты (структура папки создаётся, но не заполняется)
- Хранение истории прогонов
- Аутентификация / безопасность
- Поддержка нескольких одновременных проектов
- Ручное указание URL сервисов пользователем — всегда авто-генерируется из `connections`
- Kubernetes-level сетевая модель (без NetworkPolicy, Ingress и т.д.)

---

## Acceptance Criteria

- [ ] **AC-1:** `dotnet tool install -g archcraft` выполняется успешно; `archcraft --version` возвращает строку версии
- [ ] **AC-2:** `archcraft validate project.yaml` завершается с кодом 0 на валидном файле и с кодом > 0 + читаемым сообщением об ошибке на невалидном
- [ ] **AC-3:** `archcraft run project.yaml` поднимает все объявленные сервисы как Docker-контейнеры; контейнеры видны в `docker ps` во время прогона
- [ ] **AC-4:** Все контейнеры проекта помещаются в одну Docker-сеть; сервисы обращаются друг к другу по имени (`name` из YAML) как hostname
- [ ] **AC-4a:** Для каждого `connection` с полем `alias` переменная окружения пробрасывается в контейнер `from` со значением `<service-name>:<port>` целевого сервиса
- [ ] **AC-4b:** Компилятор определяет порядок старта контейнеров через топологическую сортировку графа `connections`; при наличии циклической зависимости — `validate` завершается с ошибкой
- [ ] **AC-4c:** Ссылка на несуществующий сервис в `connections` (from/to) — ошибка валидации
- [ ] **AC-5:** Перед стартом сценария система ожидает HTTP 200 от каждого сервиса с `readiness.path`; если ответ не получен в течение `readiness.timeout` — прогон завершается с ошибкой
- [ ] **AC-6:** HTTP load-сценарий генерирует запросы с заданным RPS на `target` на протяжении `duration`; отклонение RPS не превышает ±20%
- [ ] **AC-7:** Если `target` недоступен в момент старта сценария — система повторяет попытки в течение configurable timeout (по умолчанию 30 сек), затем завершает сценарий с ошибкой
- [ ] **AC-8:** Собираются метрики: p50 latency, p99 latency, error rate (процент ответов не-2xx или timeout), raw список всех замеров
- [ ] **AC-9:** После завершения сценария в консоль выводится таблица: сценарий / p50 / p99 / error rate / total requests
- [ ] **AC-10:** JSON-файл с результатами сохраняется в папку `results/` рядом с `project.yaml`; папка создаётся автоматически; имя файла — `<slug>-<timestamp>.json`; включает p50, p99, error rate и массив всех замеров latency в мс
- [ ] **AC-11:** После завершения всех сценариев (или при ошибке) все поднятые контейнеры останавливаются и удаляются
- [ ] **AC-12:** `archcraft scenario run project.yaml --scenario <name>` запускает только один именованный сценарий

---

## YAML DSL Schema (MVP)

```yaml
name: my-project

services:
  - name: api
    image: my-api:latest
    port: 8080
    env:
      LOG_LEVEL: debug
    readiness:
      path: /health
      timeout: 30s

  - name: billing
    image: my-billing:latest
    port: 8081
    readiness:
      path: /health
      timeout: 30s

  - name: db
    image: postgres:15
    port: 5432

  - name: worker
    image: my-worker:latest
    port: 9000

connections:
  # service-to-service (HTTP)
  # auto-generated: api получит BILLING_URL=http://billing:8081
  - from: api
    to: billing
    protocol: http              # http | grpc | tcp (default: tcp)
    port: 8081                  # если не указан — используется service.port

  # с кастомным именем env var
  - from: api
    to: billing
    protocol: http
    alias: PARTNER_SERVICE_URL  # переопределить авто-имя (опционально)

  # service-to-database (TCP)
  # auto-generated: billing получит DB_URL=db:5432
  - from: billing
    to: db
    protocol: tcp

  # с proxy-точкой для будущей chaos-инжекции
  - from: api
    to: billing
    protocol: http
    via: proxy                  # stub: в будущем — latency/errors/throttling между сервисами

scenarios:
  - name: basic-load
    type: http
    target: http://api:8080/checkout
    rps: 10
    duration: 30s
    startup_timeout: 30s
```

**Правила:**
- Все сервисы проекта помещаются в одну Docker-сеть; hostname = `name` сервиса (Docker DNS)
- Пользователь **никогда не прописывает URL сервисов вручную** — они генерируются автоматически из `connections`
- `connections` — отдельная секция, не вложена в `services`
- `port` в сервисе — exposed port контейнера; host port назначается динамически Testcontainers
- `port` в connection — порт на стороне `to`; если не указан → берётся `service.port` сервиса `to`
- `protocol` — `http`, `grpc`, `tcp`; определяет формат авто-генерируемого значения env var и точку chaos-инжекции
- Auto-generated env var имя: `<TO_NAME_UPPER>_URL` (можно переопределить через `alias`)
- Auto-generated env var значение: `http://<to-name>:<port>` (http/grpc) или `<to-name>:<port>` (tcp)
- `via: proxy` — stub для будущей вставки proxy между сервисами; в MVP парсится, но игнорируется
- Порядок запуска — топологическая сортировка графа connections (leafs first)
- Циклические зависимости и ссылки на несуществующие сервисы → ошибка валидации
- `readiness` — опционально; без него сервис считается готовым сразу после старта контейнера
- `duration`, `readiness.timeout`, `startup_timeout` — строки вида `30s`, `2m`

---

## Domain & Architecture

### Новые проекты (все)

```
src/
├── Archcraft.Cli                  # точка входа, System.CommandLine, global tool
├── Archcraft.App                  # use cases: RunProject, ValidateProject
├── Archcraft.Domain               # сущности, value objects, enums
├── Archcraft.Contracts            # интерфейсы портов
├── Archcraft.ProjectModel         # DTO 1:1 под YAML
├── Archcraft.ProjectCompiler      # DSL → ExecutionPlan
├── Archcraft.Execution            # абстракции runtime (IEnvironmentRunner)
├── Archcraft.Execution.Docker     # реализация через Testcontainers
├── Archcraft.Scenarios            # HTTP load generator
├── Archcraft.Observability        # сбор метрик, агрегация, IReportBuilder
└── Archcraft.Serialization.Yaml   # YamlDotNet, IProjectLoader

tests/
└── Archcraft.UnitTests            # структура создаётся, тесты не пишутся в MVP
```

### Domain (без внешних зависимостей)

**Ключевое правило:** `connections` — это отдельная доменная модель (граф топологии), а не поле внутри `ServiceDefinition`.

**Entities:**
- `ProjectDefinition` — name, `IReadOnlyList<ServiceDefinition>` services, `ServiceTopology` topology, `IReadOnlyList<ScenarioDefinition>` scenarios
- `ServiceDefinition` — name, image, port, env, readiness
- `ServiceTopology` — граф связей; содержит `IReadOnlyList<ConnectionDefinition>` и методы: `GetStartupOrder()` (topological sort), `GetConnectionsFrom(serviceName)`
- `ConnectionDefinition`:
  ```csharp
  public sealed class ConnectionDefinition
  {
      public string From { get; init; }
      public string To { get; init; }
      public string Protocol { get; init; } = "http"; // http | grpc | tcp
      public int Port { get; init; }                  // порт to-сервиса; 0 = использовать service.port
      public string? Alias { get; init; }             // имя env var; null = авто: <TO_UPPER>_URL
      public string? Via { get; init; }               // null | "proxy" — точка для chaos-инжекции (future)
  }
  ```
- `ResolvedConnection` — вычисленная связь после компиляции: from, to, envVarName, envVarValue (`<service-name>:<port>`)
- `ScenarioDefinition` — name, type, target, rps, duration, startupTimeout
- `ExecutionPlan` — orderedServices (topological order), resolvedConnections, networkName, scenarios
- `MetricSnapshot` — scenarioName, p50, p99, errorRate, totalRequests, rawLatenciesMs
- `RunReport` — runId, timestamp, snapshots

**Value Objects:**
- `ServicePort` (int, 1–65535)
- `Duration` (parsed от `30s`, `2m`)
- `RpsTarget` (positive int)

**Enums:**
- `ScenarioType` — `Http`
- `ConnectionProtocol` — `Http`, `Grpc`, `Tcp`
- `ChaosActionType` — `NetworkLatency`, `ContainerKill`

### Правила генерации env vars (ProjectCompiler)

Пользователь **не прописывает URL вручную**. Compiler автоматически:

1. Для каждого `ConnectionDefinition`:
   - Вычисляет `envVarValue = "<to-name>:<resolved-port>"` (Docker DNS)
   - Если `protocol = http` → `envVarValue = "http://<to-name>:<port>"`
   - Имя переменной: `alias ?? "<TO_NAME_UPPER>_URL"`
2. Добавляет вычисленные переменные в `ServiceDefinition.env` для контейнера `from` при построении `ExecutionPlan`

Пример:
```
connection: api → billing, protocol: http, port: 8081
→ env var в api: BILLING_URL=http://billing:8081
```

### Contracts

```csharp
IProjectLoader       // LoadAsync(string path) → ProjectDefinition
IProjectCompiler     // Compile(ProjectDefinition) → ExecutionPlan
ITopologyValidator   // Validate(ServiceTopology, IReadOnlyList<ServiceDefinition>) → ValidationResult
IEnvironmentRunner   // StartAsync(ExecutionPlan), StopAsync()
IScenarioRunner      // RunAsync(ScenarioDefinition) → MetricSnapshot
IMetricsCollector    // Record(result), GetSnapshot() → MetricSnapshot
IReportBuilder       // Build(IEnumerable<MetricSnapshot>) → RunReport
IChaosEngine         // Apply(ChaosActionType, ConnectionDefinition) → stub
```

### Граф зависимостей

```
Domain          ← (нет зависимостей)
Contracts       ← Domain
ProjectModel    ← (нет зависимостей, чистые DTO)
ProjectCompiler ← Domain, Contracts, ProjectModel
Serialization   ← Contracts, ProjectModel
Execution       ← Domain, Contracts
Execution.Docker← Execution
Scenarios       ← Domain, Contracts
Observability   ← Domain, Contracts
App             ← Domain, Contracts
Cli             ← App, все адаптеры (для регистрации DI)
```

---

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Target framework | .NET 10 | Последняя версия, C# 13 |
| Оркестрация контейнеров | Testcontainers (NuGet) | Нативная интеграция, не требует внешних зависимостей |
| Service networking | Единая Docker-сеть + DNS aliases | Сервисы достижимы по имени без дополнительной конфигурации |
| Topology model | `ServiceTopology` как отдельный граф | Разделение concerns: services ≠ connections |
| Env var generation | Авто-генерация из connections | Пользователь не вводит URL вручную; меньше ошибок |
| Chaos extension point | `via: proxy` в DSL, `IChaosEngine(ConnectionDefinition)` | Подготовка к per-link chaos без смены контракта |
| CLI фреймворк | System.CommandLine | Официальный Microsoft, без лишних зависимостей |
| YAML парсинг | YamlDotNet | Де-факто стандарт для .NET |
| HTTP клиент в нагрузочном сценарии | HttpClient с `IHttpClientFactory` | Пул соединений, управление lifecycle |
| Метрики | In-memory (ConcurrentBag) | MVP, без внешних зависимостей |
| Отчёт | Console table + JSON файл | Удобство + машиночитаемость |
| Публикация | dotnet global tool | Простая установка одной командой |
| DI | Microsoft.Extensions.DependencyInjection | Стандарт экосистемы |

---

## Open Questions

Все вопросы закрыты:
- **Q1 → resolved:** readiness check через HTTP GET + timeout, поле `readiness` в YAML (опционально)
- **Q2 → resolved:** retry с configurable `startup_timeout` (default 30s), затем fail
- **Q3 → resolved:** JSON включает p50, p99, error rate + raw массив всех замеров latency в мс

---

## Dependencies

- .NET 10
- Testcontainers (NuGet)
- YamlDotNet (NuGet)
- System.CommandLine (NuGet)
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging
- Docker Desktop / Docker Engine на машине пользователя

---

*Approved by: user — Date: 2026-03-25*
