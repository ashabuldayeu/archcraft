# Spec: Grafana Link Output

**Status:** approved
**Created:** 2026-04-11
**Author:** ashabuldayeu

---

## Summary

После того как Grafana поднялась, выводить в консоль ссылку на неё и учётные данные по умолчанию. То же самое дублировать в итоговом отчёте. Если секция `observability:` в `project.yaml` отсутствует — ничего не выводить.

## Problem Statement

После запуска проекта пользователь не знает порт, на котором доступна Grafana (Testcontainers выдаёт случайный хост-порт). Приходится искать порт вручную в логах Testcontainers.

## Goals

- [ ] Сразу после успешного старта Grafana вывести в консоль URL и учётные данные
- [ ] Включить URL Grafana в итоговый отчёт (консольный и JSON)

## Non-Goals (Out of Scope)

- Ссылка на Prometheus в консоли или отчёте
- Кастомные учётные данные Grafana (всегда выводим admin/admin)
- Автоматическое открытие браузера
- Изменение порта Grafana в конфиге

## Acceptance Criteria

- [ ] AC-1: После старта Grafana в stdout выводится сообщение вида:
  ```
  Grafana:  http://localhost:PORT  (admin / admin)
  ```
  где `PORT` — реальный хост-порт, выданный Testcontainers.

- [ ] AC-2: Сообщение выводится **до** начала выполнения сценариев (сразу как Grafana готова).

- [ ] AC-3: В консольном блоке итогового отчёта (после таблицы сценариев) добавляется строка:
  ```
  Grafana:  http://localhost:PORT  (admin / admin)
  ```

- [ ] AC-4: В JSON-отчёте появляется поле `"grafana_url": "http://localhost:PORT"`.

- [ ] AC-5: Если `observability:` не задана в `project.yaml`, ни AC-1, ни AC-3, ни AC-4 не срабатывают — поле `grafana_url` в JSON отсутствует (или `null`).

- [ ] AC-6: Если `observability:` задана, но Grafana не запустилась (исключение), сообщение не выводится и поле остаётся `null`.

## Domain & Architecture

```
src/
├── Archcraft.Domain/
│   └── Entities/
│       └── RunReport.cs          # добавить GrafanaUrl: string?
├── Archcraft.Execution.Docker/
│   └── DockerEnvironmentRunner.cs # вывод в консоль после StartGrafanaAsync
├── Archcraft.Observability/
│   ├── ConsoleReportRenderer.cs   # добавить Grafana-строку в финальный блок
│   └── JsonReportWriter.cs        # сериализовать GrafanaUrl
├── Archcraft.App/
│   └── UseCases/RunProjectUseCase.cs # передать GrafanaUrl в RunReport
```

## Key Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Где брать порт | `container.GetMappedPublicPort(grafanaPort)` после старта | Единственный надёжный источник реального хост-порта |
| Учётные данные | Всегда `admin / admin` | Grafana default; кастомизация out of scope |
| Формат строки | `Grafana:  http://localhost:PORT  (admin / admin)` | Достаточно информативно, читается с первого взгляда |
| JSON-поле | `grafana_url` (snake_case, nullable) | Консистентно с остальными полями отчёта |

## Open Questions

_(нет)_

## Dependencies

- Существующий `StartGrafanaAsync` в `DockerEnvironmentRunner` — контейнер уже запускается и порт уже маппируется
- Существующий `RunReport` и `ConsoleReportRenderer` — нужно только расширить

---

*Approved by: — Date: —*
