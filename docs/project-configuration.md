# Project Configuration Guide

`project.yaml` — главный файл конфигурации Archcraft. Он описывает сервисы, адаптеры, топологию и нагрузочные сценарии. CLI читает его и запускает окружение в Docker.

---

## Структура файла

```
name          — имя проекта
services      — список сервисов (инфра + synthetic)
adapters      — список адаптеров технологий
connections   — связи между сервисами
scenarios     — нагрузочные сценарии
```

---

## Пример: два сервиса с Redis, Postgres и HTTP-коммуникацией

**Топология:**
- `frontend` — synthetic-сервис, принимает запросы извне; обращается к `backend` через HTTP
- `backend` — synthetic-сервис; пытается прочитать из Redis (50% промахов) → при промахе падает в Postgres
- `redis`, `postgres` — инфраструктурные контейнеры
- `redis-adapter`, `pg-adapter` — адаптеры для `backend`
- `http-adapter` — адаптер для `frontend`, проксирует запросы к `backend`

```
[client] → POST /handle → [frontend]
                               │
                         http-adapter
                               │
                          POST /process → [backend]
                               │
                    ┌──────────┴──────────┐
               redis-adapter         pg-adapter
                    │                     │
                 [redis]             [postgres]
```

### Полный `project.yaml`

```yaml
name: shop-demo

# ── Инфраструктурные сервисы ────────────────────────────────────────────────

services:
  - name: redis
    image: redis:7
    port: 6379
    readiness:
      path: /
      timeout: 30s

  - name: postgres
    image: postgres:16
    port: 5432
    env:
      POSTGRES_DB: archcraft
      POSTGRES_USER: user
      POSTGRES_PASSWORD: secret
    readiness:
      path: /
      timeout: 60s

# ── Synthetic-сервисы ────────────────────────────────────────────────────────

  - name: backend
    image: archcraft/synthetic:latest
    port: 8080
    readiness:
      path: /health
      timeout: 30s
    synthetic:
      # Адаптеры, которые использует этот сервис
      adapters:
        - redis-adapter
        - pg-adapter

      endpoints:
        - alias: process
          pipeline:
            # 1. Попытка прочитать из Redis
            - operation: redis-call
              not-found-rate: 0.5        # 50% — ключ не найден, идём в fallback
              fallback:
                # 2. Промах → идём в Postgres
                - operation: pg-call

  - name: frontend
    image: archcraft/synthetic:latest
    port: 8080
    readiness:
      path: /health
      timeout: 30s
    synthetic:
      adapters:
        - http-adapter

      endpoints:
        - alias: handle
          pipeline:
            # Пробросить запрос к backend
            - operation: http-call

# ── Адаптеры ─────────────────────────────────────────────────────────────────

adapters:
  - name: redis-adapter
    image: archcraft/redis-adapter:latest
    port: 8081
    technology: redis
    connects-to: redis          # CLI построит REDIS_CONNECTION_STRING из этого сервиса

  - name: pg-adapter
    image: archcraft/pg-adapter:latest
    port: 8082
    technology: postgres
    connects-to: postgres       # CLI построит PG_CONNECTION_STRING из этого сервиса

  - name: http-adapter
    image: archcraft/http-adapter:latest
    port: 8083
    technology: http
    connects-to: backend        # CLI построит HTTP_TARGET_URL=http://backend:8080

# ── Связи между сервисами ────────────────────────────────────────────────────

connections:
  # frontend → backend через http-adapter
  - from: frontend
    to: backend
    protocol: http
    port: 8080
    via: http-adapter

  # backend → redis через redis-adapter
  - from: backend
    to: redis
    protocol: tcp
    port: 6379
    via: redis-adapter

  # backend → postgres через pg-adapter
  - from: backend
    to: postgres
    protocol: tcp
    port: 5432
    via: pg-adapter

# ── Сценарии нагрузки ────────────────────────────────────────────────────────

scenarios:
  - name: baseline
    type: http
    target: frontend            # CLI будет слать запросы на этот сервис
    rps: 50
    duration: 60s
    startup_timeout: 60s
```

---

## Ключевые концепции

### `synthetic` — описание поведения сервиса

Раздел `synthetic` определяет, что делает сервис при входящем запросе. Каждый `endpoint` задаёт `alias` (путь `POST /{alias}`) и `pipeline` — дерево операций.

```yaml
synthetic:
  adapters:
    - redis-adapter   # имена адаптеров, которые использует сервис
  endpoints:
    - alias: my-endpoint
      pipeline:
        - operation: redis-call
```

### `pipeline` — дерево операций

Каждый шаг pipeline — это вызов операции через адаптер. Шаг может иметь:

| Поле | Описание |
|------|----------|
| `operation` | Тип операции: `redis-call`, `pg-call`, `http-call` |
| `not-found-rate` | Вероятность исхода `not_found` (0.0–1.0). При срабатывании выполняется `fallback` |
| `fallback` | Список шагов, выполняемых при `not_found` |
| `children` | Список шагов, выполняемых последовательно после успешной операции |

**Пример: Redis с fallback в Postgres**

```yaml
pipeline:
  - operation: redis-call
    not-found-rate: 0.5       # 50% запросов идут в fallback
    fallback:
      - operation: pg-call    # при промахе — читаем из Postgres
```

**Пример: последовательные шаги (children)**

```yaml
pipeline:
  - operation: redis-call
    children:
      - operation: pg-call    # после Redis — всегда ходим в Postgres
```

### `adapters` — технологические адаптеры

Адаптер — отдельный контейнер, реализующий протокол `POST /execute`. CLI запускает его в той же Docker-сети и инжектирует connection string из `connects-to`.

| Поле | Описание |
|------|----------|
| `name` | Имя контейнера = Docker network alias |
| `image` | Docker-образ адаптера |
| `port` | Порт, на котором слушает адаптер |
| `technology` | `redis`, `postgres`, `http` |
| `connects-to` | Имя сервиса из `services`, к которому подключается адаптер |

**Что строит CLI в зависимости от `technology`:**

| technology | Инжектируемый env var | Формат значения |
|------------|----------------------|-----------------|
| `postgres` | `PG_CONNECTION_STRING` | `Host={name};Port=5432;Database={POSTGRES_DB};Username={POSTGRES_USER};Password={POSTGRES_PASSWORD}` |
| `redis` | `REDIS_CONNECTION_STRING` | `{name}:6379` или `{name}:6379,password={REDIS_PASSWORD}` |
| `http` | `HTTP_TARGET_URL` | `http://{name}:{port}` |

### `connections` — топология сети

Определяет, какие сервисы между собой общаются. CLI использует это для:
- Определения порядка запуска (зависимости поднимаются первыми)
- Инжекции env vars для service discovery

| Поле | Описание |
|------|----------|
| `from` | Сервис-источник |
| `to` | Целевой сервис |
| `protocol` | `http`, `grpc`, `tcp` |
| `port` | Порт целевого сервиса |
| `via` | Имя адаптера, через который идёт коммуникация (опционально) |

### `scenarios` — нагрузочные сценарии

```yaml
scenarios:
  - name: baseline
    type: http          # тип сценария (сейчас только http)
    target: frontend    # сервис, на который идёт нагрузка
    rps: 50             # запросов в секунду
    duration: 60s       # продолжительность
    startup_timeout: 60s
```

---

## Маппинг операций и технологий

| Операция в pipeline | Требуемая технология | Адаптер |
|---------------------|---------------------|---------|
| `redis-call` | `redis` | `redis-adapter` |
| `pg-call` | `postgres` | `pg-adapter` |
| `http-call` | `http` | `http-adapter` |

Сервис обязан объявить в `synthetic.adapters` все имена адаптеров, операции которых используются в его pipeline. CLI проверяет это при компиляции проекта.
