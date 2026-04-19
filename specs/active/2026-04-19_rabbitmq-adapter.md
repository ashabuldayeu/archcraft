# Spec: RabbitMQ Adapter — push-based интеграция с брокером сообщений

**Status:** approved
**Created:** 2026-04-19
**Author:** ashabuldayeu

---

## Summary

Добавить поддержку RabbitMQ в archcraft: новый тип адаптера `technology: rabbitmq`, который умеет продюсить сообщения (synthetic → RabbitMQ) и консюмить (RabbitMQ → synthetic). Каждая реплика synthetic-сервиса получает собственный экземпляр rabbitmq-адаптера. Kill/restore реплики автоматически kill/restore её rabbitmq-адаптера. Подход полностью аналогичен Kafka-адаптеру.

---

## Problem Statement

После добавления Kafka в archcraft появилась симметричная потребность в поддержке RabbitMQ — второго наиболее распространённого брокера в production-системах. Без него нельзя тестировать сценарии с consumer lag по очередям, поведение при падении consumer'а или backpressure от брокера для AMQP-топологий.

---

## Goals

- [ ] `technology: rabbitmq` принимается компилятором и environment runner'ом
- [ ] RabbitMQ-адаптер поддерживает роль **producer**: операция `rabbitmq-push` в pipeline → публикует сообщение в очередь
- [ ] RabbitMQ-адаптер поддерживает роль **consumer**: N concurrent consumers на один адаптер-инстанс; при получении сообщения вызывает `POST {synthetic-service}/{endpoint}` и делает ack
- [ ] Количество concurrent consumers на инстанс конфигурируется (`consumers: N`)
- [ ] Очередь конфигурируется: `durable` и `prefetch`
- [ ] Kill реплики → также останавливает её rabbitmq-адаптер; Restore → восстанавливает
- [ ] `archcraft new --db rabbitmq` и `--db postgres-rabbitmq` генерируют валидный scaffold
- [ ] `seed` / `clear` пропускают rabbitmq-адаптеры без ошибок
- [ ] RabbitMQ-метрики доступны в Prometheus и отображаются в Grafana

---

## Non-Goals (Out of Scope)

- Fanout, topic, headers exchange — только direct через имя очереди (default exchange)
- Dead letter queues, TTL, priority queues
- Схемы сообщений (Avro, Protobuf, JSON Schema)
- TLS / mutual TLS для AMQP-соединения
- Vhost-изоляция (использует vhost `/`)
- Сравнение производительности Kafka vs RabbitMQ в рамках одного проекта

---

## Acceptance Criteria

- [ ] **AC-1**: `technology: rabbitmq` в `project.yaml` проходит валидацию и компиляцию без ошибок
- [ ] **AC-2**: При старте проекта создаётся по одному контейнеру `rabbitmq-adapter-N` на каждую реплику сервиса, использующего rabbitmq-адаптер; каждый получает корректные `RABBITMQ_URL`, `RABBITMQ_QUEUE`, `RABBITMQ_CONSUMER_TARGET_URL`
- [ ] **AC-3**: `rabbitmq-push` в pipeline synthetic-сервиса публикует сообщение в очередь `{adapter-base-name}` (без суффикса реплики); сообщение видно в RabbitMQ Management UI
- [ ] **AC-4**: При `consumers: 3` на инстансе запускаются 3 параллельных consumer; все три конкурируют за сообщения из одной очереди — каждое сообщение обрабатывается ровно одним consumer'ом
- [ ] **AC-5**: После успешного `POST {synthetic}/{endpoint}` consumer делает ack; при ошибке форварда — nack с requeue
- [ ] **AC-6**: `durable: true` (default) создаёт durable-очередь; `prefetch: N` выставляет QoS channel prefetch count
- [ ] **AC-7**: `kill backend[1]` останавливает и `backend-1`, и `rabbitmq-adapter-1`; оставшиеся адаптеры продолжают получать сообщения
- [ ] **AC-8**: `restore backend[1]` запускает и `backend-1`, и `rabbitmq-adapter-1`; адаптер переподключается и возобновляет consumption
- [ ] **AC-9**: `seed all` и `clear all` завершаются без ошибок, rabbitmq-адаптеры пропускаются
- [ ] **AC-10**: `archcraft new myproject --db rabbitmq` создаёт `project.yaml` с RabbitMQ-сервисом, rabbitmq-адаптером, корректными connections и warmup-сценарием
- [ ] **AC-11**: `archcraft validate project.yaml` на сгенерированном RabbitMQ-проекте завершается без ошибок
- [ ] **AC-12**: Grafana содержит дашборд `rabbitmq.json` с панелями: очереди online, messages ready, messages unacked, publish rate, deliver rate, consumer count
- [ ] **AC-13**: После перезапуска брокера очередь восстанавливается (durable=true) и адаптер переподключается автоматически

---

## Domain & Architecture

### YAML-конфигурация

```yaml
services:
  - name: rabbitmq
    image: rabbitmq:3-management
    port: 5672
    readiness:
      tcp_port: 5672
      timeout: 60s
    env:
      RABBITMQ_DEFAULT_USER: user
      RABBITMQ_DEFAULT_PASS: secret

  - name: backend
    image: archcraft/synthetic:latest
    port: 8080
    replicas: 3
    proxy: backend-proxy
    synthetic:
      adapters:
        - rabbitmq-adapter
      endpoints:
        - alias: process
          pipeline:
            - operation: rabbitmq-push    # вызывает /execute на rabbitmq-adapter
        - alias: consume
          pipeline:
            - operation: pg-call

adapters:
  - name: rabbitmq-adapter
    image: archcraft/rabbitmq-adapter:latest
    port: 8080
    technology: rabbitmq
    connects_to: rabbitmq
    consumer:
      endpoint: consume
      consumers: 3      # concurrent consumers per adapter instance
      durable: true     # queue survives broker restart
      prefetch: 10      # QoS prefetch count per consumer

connections:
  - from: backend
    to: rabbitmq
    protocol: tcp
    port: 5672
    via: rabbitmq-adapter
```

### Изменяемые файлы

```
src/
├── Archcraft.Domain/Entities/
│   ├── AdapterDefinition.cs          # + RabbitMqConsumerConfig? RabbitMqConsumer
│   └── RabbitMqConsumerConfig.cs     # новый record: ConsumerCount, Durable, Prefetch, Endpoint
│
├── Archcraft.ProjectModel/
│   ├── AdapterModel.cs               # Consumer: ConsumerModel (обобщённый, shared с Kafka)
│   └── ConsumerModel.cs              # переименовать KafkaConsumerModel → ConsumerModel,
│                                     # добавить поля: Durable, Prefetch (nullable, игнорируются Kafka)
│
├── Archcraft.Serialization.Yaml/
│   └── YamlProjectLoader.cs          # маппинг consumer → RabbitMqConsumerConfig по technology
│
├── Archcraft.ProjectCompiler/
│   └── ArchcraftProjectCompiler.cs   # новая ветка "rabbitmq": инжект env vars,
│                                     # BuildRabbitMqExporter для observability
│
├── Archcraft.Execution.Docker/
│   └── DockerEnvironmentRunner.cs    # PairedReplicaName уже работает; расширение не нужно
│
├── Archcraft.App/UseCases/
│   ├── InteractiveSessionUseCase.cs  # IsStateless: добавить "rabbitmq" в skip-list
│   └── NewProjectUseCase.cs          # scaffold для --db rabbitmq и --db postgres-rabbitmq
│
├── Archcraft.Cli/Commands/
│   └── NewCommand.cs                 # добавить "rabbitmq", "postgres-rabbitmq" в valid --db values
│
└── Archcraft.Observability/
    ├── Dashboards/rabbitmq.dashboard.json   # новый Grafana дашборд
    └── DashboardGenerator.cs               # dispatch для rabbitmq-exporter

services/adapters/
└── RabbitMqAdapter/                  # новый Docker-сервис (.NET, RabbitMQ.Client)
    ├── RabbitMqAdapter.csproj
    ├── Program.cs
    ├── Configuration/
    │   └── RabbitMqAdapterOptions.cs
    ├── Operations/
    │   └── PublishOperation.cs       # IAdapterOperation, OperationName = "rabbitmq-push"
    ├── Consumer/
    │   └── RabbitMqConsumerWorker.cs # BackgroundService, N concurrent consumers
    └── Dockerfile

build-images.sh                       # + docker build для rabbitmq-adapter
samples/test_project/project.yaml     # добавить rabbitmq-сервис и rabbitmq-adapter
```

### Env vars, инжектируемые компилятором в rabbitmq-adapter-N

| Переменная | Значение | Источник |
|---|---|---|
| `RABBITMQ_URL` | `amqp://user:secret@rabbitmq:5672/` | из env сервиса (DEFAULT_USER/PASS) + hostname + port |
| `RABBITMQ_QUEUE` | `rabbitmq-adapter` | имя адаптера-группы (без суффикса реплики) |
| `RABBITMQ_CONSUMER_TARGET_URL` | `http://backend-0:8080` | paired replica address |
| `RABBITMQ_CONSUMER_ENDPOINT` | `/consume` | adapter.consumer.endpoint |
| `RABBITMQ_CONSUMER_COUNT` | `3` | adapter.consumer.consumers |
| `RABBITMQ_DURABLE` | `true` | adapter.consumer.durable |
| `RABBITMQ_PREFETCH` | `10` | adapter.consumer.prefetch |

### ConsumerModel — обобщение KafkaConsumerModel

Вместо отдельных `KafkaConsumerModel` и `RabbitMqConsumerModel` используется одна `ConsumerModel` с union-полями:

```csharp
public sealed class ConsumerModel
{
    public string GroupId { get; set; } = string.Empty;   // Kafka only
    public string Endpoint { get; set; } = string.Empty;  // shared
    public int Consumers { get; set; } = 1;               // shared
    public int Partitions { get; set; } = 1;              // Kafka only
    public bool Durable { get; set; } = true;             // RabbitMQ only
    public int Prefetch { get; set; } = 1;                // RabbitMQ only
}
```

`YamlProjectLoader.MapAdapter` выбирает нужный domain-тип по `model.Technology`.

### Observability

Exporter: `kbudde/rabbitmq-exporter:v1.0.0-RC19` подключается к Management API `http://rabbitmq:15672`.

Grafana-панели (дашборд `rabbitmq.json`):
- Queues Online (stat)
- Messages Ready (stat)
- Messages Unacknowledged (stat)
- Consumers (stat)
- Publish Rate — msgs/s (timeseries)
- Deliver Rate — msgs/s (timeseries)
- Queue Depth over time (timeseries)
- Consumer Utilisation (timeseries)

Добавить в overview-дашборд: Messages Ready, Deliver Rate.

---

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Exchange | Default exchange (direct по имени очереди) | Достаточно для нагрузочного тестирования; минимальная конфигурация |
| Имя очереди | Базовое имя адаптера (без суффикса реплики) | Все N адаптеров пишут в / читают из одной очереди — competing consumers |
| Ack | Explicit ack после успешного forward, nack+requeue при ошибке | Гарантирует at-least-once; соответствует реальной архитектуре |
| ConsumerModel | Единая обобщённая модель вместо per-technology | Меньше дублирования YAML-маппинга; поля технологий не пересекаются |
| Reconnect | `RabbitMQ.Client` автоматический reconnect через `AutomaticRecoveryEnabled = true` | Покрывает сценарий перезапуска брокера без дополнительной логики |
| Образ | `rabbitmq:3-management` | Management UI нужен для exporter'а и отладки; official image |
| Exporter | `kbudde/rabbitmq-exporter` через Management API | Тот же паттерн что и kafka-exporter; не требует плагинов в образе |

---

## Open Questions

- [ ] Q1: Нужно ли добавить `rabbitmq` в `--db` как отдельный вариант без Postgres, или только `postgres-rabbitmq`? *(Ответ пользователя: оба — и `rabbitmq`, и `postgres-rabbitmq`)*

---

## Dependencies

- `rabbitmq:3-management` — official Docker image
- `RabbitMQ.Client` NuGet пакет — в новом RabbitMqAdapter сервисе
- `kbudde/rabbitmq-exporter:v1.0.0-RC19` — Prometheus exporter
- Kafka-адаптер спек (реализован) — структура PairedReplicaName, ConsumerModel, паттерн kill/restore

---

*Approved by: — Date: —*
