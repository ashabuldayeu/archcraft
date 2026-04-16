# Spec: Kafka Adapter — push-based интеграция с очередью сообщений

**Status:** approved
**Created:** 2026-04-15
**Author:** ashabuldayeu

---

## Summary

Добавить поддержку Apache Kafka в archcraft: новый тип адаптера `technology: kafka`, который умеет как продюсить сообщения (synthetic → kafka), так и консюмить (kafka → synthetic). Каждая реплика synthetic-сервиса получает собственный экземпляр kafka-адаптера; все экземпляры объединяются в одну consumer group. Kill/restore реплики автоматически kill/restore её kafka-адаптера. RabbitMQ — отдельный спек.

---

## Problem Statement

Сейчас archcraft поддерживает только синхронные HTTP/TCP связи. Реальные распределённые системы часто используют асинхронные очереди. Без поддержки Kafka нельзя тестировать сценарии с consumer lag, partition rebalancing при отказе реплики, или backpressure от брокера.

---

## Goals

- [ ] `technology: kafka` принимается компилятором и environment runner'ом
- [ ] Kafka-адаптер поддерживает роль **producer**: `POST /push` → публикует сообщение в топик
- [ ] Kafka-адаптер поддерживает роль **consumer**: подписывается на топик, при получении сообщения вызывает `POST {synthetic-service}/{endpoint}`
- [ ] Все экземпляры kafka-адаптера одного сервиса объединены в consumer group — каждое сообщение обрабатывается ровно одним экземпляром
- [ ] Kill реплики → также останавливает её kafka-адаптер; Restore → восстанавливает
- [ ] `archcraft new --db kafka` генерирует валидный scaffold с Kafka-топологией
- [ ] `seed` / `clear` пропускают kafka-адаптеры без ошибок

---

## Non-Goals (Out of Scope)

- RabbitMQ (отдельный спек)
- Dead letter queues, схемы сообщений (Avro/Protobuf), Kafka Streams
- Метрики kafka-адаптера в Grafana (отдельная задача)
- Kafka exporter для Prometheus

---

## Acceptance Criteria

- [ ] **AC-1**: `technology: kafka` в `project.yaml` проходит валидацию и компиляцию без ошибок
- [ ] **AC-2**: При старте проекта создаётся по одному контейнеру `kafka-adapter-N` на каждую реплику сервиса, использующего kafka-адаптер
- [ ] **AC-3**: Все `kafka-adapter-N` одного сервиса запускаются с одинаковым `KAFKA_CONSUMER_GROUP_ID` — при параллельной публикации 3 сообщений каждый из 3 адаптеров обрабатывает ровно 1
- [ ] **AC-4**: `POST /push` на kafka-адаптере публикует сообщение в сконфигурированный топик; synthetic-сервис использует операцию `kafka-push` в pipeline для вызова этого эндпоинта
- [ ] **AC-5**: Kafka-адаптер-консюмер при получении сообщения делает `POST http://{paired-service}:{port}/{endpoint}` и ждёт ответа
- [ ] **AC-6**: `kill backend[1]` останавливает и `backend-1`, и `kafka-adapter-1`; после kill Kafka перебалансирует партиции между оставшимися адаптерами
- [ ] **AC-7**: `restore backend[1]` запускает и `backend-1`, и `kafka-adapter-1`; адаптер переподключается к consumer group
- [ ] **AC-8**: `seed all` и `clear all` завершаются без ошибок, kafka-адаптеры пропускаются
- [ ] **AC-9**: `archcraft new myproject --db kafka` создаёт `project.yaml` с Kafka-сервисом, kafka-адаптером, корректными connections и warmup-сценарием
- [ ] **AC-10**: `archcraft validate project.yaml` на сгенерированном Kafka-проекте завершается без ошибок

---

## Domain & Architecture

### YAML-конфигурация

```yaml
services:
  - name: kafka
    image: bitnami/kafka:latest
    port: 9092

  - name: backend
    image: archcraft/synthetic:latest
    port: 8080
    replicas: 3
    proxy: backend-proxy
    synthetic:
      adapters:
        - kafka-adapter
      endpoints:
        - alias: handle
          pipeline:
            - operation: kafka-push   # вызывает POST /push на kafka-adapter

adapters:
  - name: kafka-adapter             # topic = "kafka-adapter" (по умолчанию = имя адаптера)
    image: archcraft/kafka-adapter:latest
    port: 8080
    technology: kafka
    connects_to: kafka
    consumer:                         # optional — роль консюмера
      group_id: backend-consumers
      endpoint: handle                # alias из synthetic.endpoints[]
    # producer роль всегда включена через POST /push

connections:
  - from: backend
    to: kafka
    protocol: tcp
    port: 9092
    via: kafka-adapter
```

### Изменяемые файлы

```
src/
├── Archcraft.Domain/Entities/
│   └── AdapterDefinition.cs          # + KafkaProducer?, KafkaConsumer? records
│
├── Archcraft.ProjectModel/
│   └── AdapterModel.cs               # + ProducerModel?, ConsumerModel? YAML fields
│
├── Archcraft.Serialization.Yaml/
│   └── YamlProjectLoader.cs          # маппинг новых полей → domain
│
├── Archcraft.ProjectCompiler/
│   └── ArchcraftProjectCompiler.cs   # инжект env vars для Kafka при расширении реплик
│
├── Archcraft.Execution.Docker/
│   └── DockerEnvironmentRunner.cs    # + _consumerAdapterByReplica dict
│                                     # KillReplicaAsync → также стопит адаптер
│                                     # RestoreReplicaAsync → также стартует адаптер
│
├── Archcraft.App/UseCases/
│   └── InteractiveSessionUseCase.cs  # seed/clear: пропускать technology=kafka
│   └── NewProjectUseCase.cs          # --db kafka scaffold
│
└── Archcraft.Cli/Commands/
    └── HelpCommand.cs                # обновить YAML-референс: kafka поля

services/adapters/
└── KafkaAdapter/                     # новый Docker-сервис (NET, Confluent.Kafka)
    ├── KafkaAdapter.csproj
    ├── Program.cs
    ├── Endpoints/
    │   └── ProducerEndpoints.cs      # POST /push, POST /seed (no-op), POST /clear (no-op)
    ├── Consumer/
    │   └── KafkaConsumerWorker.cs    # IHostedService — consumer loop
    └── Dockerfile
```

### Env vars, инжектируемые компилятором в kafka-adapter-N

| Переменная | Значение | Источник |
|---|---|---|
| `KAFKA_BROKERS` | `kafka:9092` | connection.to + port |
| `KAFKA_TOPIC` | `kafka-adapter` | имя адаптера (группы), используется и для produce, и для consume |
| `KAFKA_CONSUMER_GROUP_ID` | `backend-consumers` | adapter.consumer.group_id (одинаков для всех N) |
| `KAFKA_CONSUMER_TARGET_URL` | `http://backend-0:8080` | paired replica address (N совпадает с индексом) |
| `KAFKA_CONSUMER_ENDPOINT` | `/handle` | adapter.consumer.endpoint |

### Kill/Restore расширение

`DockerEnvironmentRunner` заводит новый словарь:
```csharp
private readonly Dictionary<string, IContainer> _consumerAdapterByReplica = new();
```

При старте: если у adapter есть `Consumer`, регистрируем `_consumerAdapterByReplica["backend-0"] = kafka-adapter-0-container`.

```csharp
// KillReplicaAsync
if (_consumerAdapterByReplica.TryGetValue(replicaName, out IContainer? adapterContainer))
    await adapterContainer.StopAsync(cancellationToken);

// RestoreReplicaAsync
if (_consumerAdapterByReplica.TryGetValue(replicaName, out IContainer? adapterContainer))
    await adapterContainer.StartAsync(cancellationToken);
```

---

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Один адаптер — обе роли | producer + consumer в одном контейнере | Адаптер уже привязан к реплике; нет смысла дублировать контейнер |
| Consumer group | Все N экземпляров одного адаптера — одна группа | Соответствует реальной архитектуре Kafka consumer groups |
| Push direction | Адаптер → Synthetic | Synthetic не усложняется polling-логикой; соответствует event-driven паттерну |
| Payload | Фиксированный (synthetic не заботится о содержимом) | Достаточно для нагрузочного тестирования |
| Seed/Clear | No-op для kafka | Очередь не хранит данные как БД; pre-seed создал бы неконтролируемый burst |
| Kill coupling | Kill реплики → Kill адаптера | Имитирует реальный сценарий: при падении пода падает и consumer worker |

---

---

## Dependencies

- `bitnami/kafka:latest` — Kafka Docker image (уже используется Bitnami для postgres/redis)
- `Confluent.Kafka` NuGet пакет — в новом KafkaAdapter сервисе
- Спек RabbitMQ — отдельная задача

---

*Approved by: — Date: —*
