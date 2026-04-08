# Spec: Service Clusters (Primary-Replica)

**Status:** approved
**Created:** 2026-04-08
**Author:** ashabuldayeu

---

## Summary

Добавить возможность объявить инфраструктурный сервис (PostgreSQL, Redis) как **кластер** с одним primary-узлом и N read-репликами. Если блок `cluster:` отсутствует — сервис запускается в единственном экземпляре (текущее поведение). При наличии `cluster:` компилятор разворачивает сервис в именованные под-сервисы (`{name}-primary`, `{name}-replica-0`, …), прокидывает нужные env-переменные для Bitnami-образов и позволяет явно адресовать любой узел в секции `connections:`.

## Problem Statement

Сейчас каждый инфраструктурный сервис запускается ровно одним контейнером. Нет возможности смоделировать типичные производственные топологии: PostgreSQL с read-репликами, Redis с репликацией. Без этого сценарии деградации (задержка на реплику чтения, падение primary) не могут быть реалистично описаны.

## Goals

- [ ] Поддержать блок `cluster:` для сервисов с `technology: postgres` и `technology: redis`
- [ ] Компилятор разворачивает кластер в именованные под-сервисы с корректными Bitnami env-переменными
- [ ] `connections:` может адресовать как групповое имя (→ primary), так и конкретный узел (`postgres-replica-0`)
- [ ] Адаптеры получают правильные connection strings в зависимости от целевого узла
- [ ] Replication credentials задаются явно в `cluster:` блоке

## Non-Goals (Out of Scope)

- Redis Cluster (шардирование) и Redis Sentinel — только primary-replica
- Patroni / Stolon / pgpool — только Bitnami streaming replication
- Автоматический failover / promotion реплики при падении primary
- Динамическое масштабирование кластера во время сценария
- Кластеры для synthetic-сервисов (у них есть `replicas:`)

## Acceptance Criteria

- [ ] AC-1: Сервис без `cluster:` продолжает запускаться как единственный контейнер — поведение не меняется.

- [ ] AC-2: Сервис с `cluster.replicas: N` компилируется в `N+1` под-сервис: `{name}-primary` и `{name}-replica-0` … `{name}-replica-{N-1}`.

- [ ] AC-3: Primary-узел postgres получает env-переменные Bitnami для master-режима:
  - `POSTGRESQL_REPLICATION_MODE=master`
  - `POSTGRESQL_REPLICATION_USER={replication_user}`
  - `POSTGRESQL_REPLICATION_PASSWORD={replication_password}`
  - Все env из оригинального блока `env:` сервиса переносятся на primary.

- [ ] AC-4: Каждая replica-нода postgres получает env-переменные Bitnami для slave-режима:
  - `POSTGRESQL_REPLICATION_MODE=slave`
  - `POSTGRESQL_MASTER_HOST={name}-primary`
  - `POSTGRESQL_MASTER_PORT_NUMBER={port}`
  - `POSTGRESQL_REPLICATION_USER={replication_user}`
  - `POSTGRESQL_REPLICATION_PASSWORD={replication_password}`

- [ ] AC-5: Primary-узел redis получает:
  - `REDIS_REPLICATION_MODE=master`
  - `REDIS_PASSWORD={password из env:}` (если задан)

- [ ] AC-6: Каждая replica-нода redis получает:
  - `REDIS_REPLICATION_MODE=slave`
  - `REDIS_MASTER_HOST={name}-primary`
  - `REDIS_MASTER_PORT_NUMBER={port}`
  - `REDIS_MASTER_PASSWORD={password из env:}` (если задан)

- [ ] AC-7: `connections: to: postgres` (без суффикса, когда у сервиса есть `cluster:`) резолвится к `postgres-primary`.

- [ ] AC-8: `connections: to: postgres-primary` и `connections: to: postgres-replica-0` работают как явная адресация конкретного узла.

- [ ] AC-9: Адаптер, подключённый к `postgres-replica-0`, получает connection string с хостом `postgres-replica-0`.

- [ ] AC-10: `startup_order` — primary-узел стартует раньше реплик (replicas зависят от primary через `POSTGRESQL_MASTER_HOST` / `REDIS_MASTER_HOST`).

- [ ] AC-11: Валидация отклоняет `cluster:` на сервисах, которые не являются инфраструктурными (т.е. имеют секцию `synthetic:`).

- [ ] AC-12: Если `replication_user` или `replication_password` не указаны в `cluster:` — используются дефолтные значения (`replicator` / `replicator_password`).

- [ ] AC-13: `project.yaml` в `samples/test_project` обновляется: postgres и redis получают `cluster: replicas: 1` (один primary + одна реплика) как демонстрационный пример.

## Domain & Architecture

```
src/
├── Archcraft.ProjectModel/
│   └── ClusterModel.cs           # новый: ClusterModel { Replicas, ReplicationUser, ReplicationPassword }
│   └── ServiceModel.cs           # добавить Cluster: ClusterModel?
├── Archcraft.Domain/
│   └── Entities/
│       └── ClusterDefinition.cs  # новый: ClusterDefinition { Replicas, ReplicationUser, ReplicationPassword }
│       └── ServiceDefinition.cs  # добавить Cluster: ClusterDefinition?
├── Archcraft.Serialization.Yaml/
│   └── YamlProjectLoader.cs      # маппинг ClusterModel → ClusterDefinition
├── Archcraft.ProjectCompiler/
│   └── ArchcraftProjectCompiler.cs # ExpandClusters() — аналог ExpandReplicas()
│                                   # InjectClusterEnvVars() для postgres/redis
│                                   # ConnectionResolver учитывает cluster-имена
```

## Key Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Именование узлов | `{name}-primary`, `{name}-replica-{i}` | Явно и предсказуемо; легко адресовать в `connections:` |
| Образы | Bitnami (`bitnami/postgresql`, `bitnami/redis`) | Стандарт для docker-compose кластеров; богатая env-конфигурация |
| Дефолт для `to: postgres` | → primary | Запись всегда идёт на primary; явная адресация реплик для чтения |
| Порядок старта | primary → replicas | Bitnami-реплика не стартует без доступного master |
| Технология определяется | из адаптеров (`connects_to` + `technology`) | Не дублировать `technology:` в `cluster:`; уже есть в адаптерах |
| synthetic + cluster | запрещено (validation error) | Synthetic-сервисы масштабируются через `replicas:` |

## Open Questions

_(нет)_

## Dependencies

- Bitnami PostgreSQL image (`bitnami/postgresql:16`)
- Bitnami Redis image (`bitnami/redis:7`)
- Существующая `ExpandReplicas()` логика в компиляторе — аналогичный паттерн, не конфликтует

---

*Approved by: — Date: —*
