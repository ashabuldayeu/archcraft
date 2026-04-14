# Spec: CLI — `new` и `help` команды

**Status:** approved
**Created:** 2026-04-14
**Author:** ashabuldayeu

---

## Summary

Добавить в `archcraft` CLI две новые команды верхнего уровня:
- `new` — генерирует scaffold нового проекта (project.yaml + пустые рабочие папки) прямо в текущей директории
- `help` — выводит справку по командам и по структуре project.yaml

Цель: снизить порог входа — пользователь не должен вручную писать project.yaml с нуля.

---

## Problem Statement

Сейчас нет способа начать новый проект без копирования существующего sample. Нет и документации прямо в CLI — нужно лезть в README или смотреть примеры. Это тормозит онбординг.

---

## Goals

- [ ] `archcraft new <name>` создаёт рабочий project.yaml с прокомментированным скелетом
- [ ] Топология определяется параметрами `--services`, `--db`, `--replicas`
- [ ] При конфликте файлов запрашивается подтверждение
- [ ] `archcraft help` выводит общую справку по всем командам
- [ ] `archcraft help <command>` выводит детальную справку по конкретной команде
- [ ] `archcraft help project.yaml` выводит YAML-референс (все поля, типы, примеры)

---

## Non-Goals (Out of Scope)

- Интерактивный wizard (вопрос-ответ в консоли)
- Генерация адаптеров или кода сервисов
- Валидация сгенерированного yaml (покрыта командой `archcraft validate`)
- Поддержка `--db both` (postgres + redis одновременно) — отдельный spec при необходимости

---

## Acceptance Criteria

- [ ] **AC-1**: `archcraft new myproject` создаёт `project.yaml` в текущей директории с `name: myproject`, топологией по умолчанию (2 synthetic-сервиса, postgres, 3 реплики) и сценарием warmup
- [ ] **AC-2**: `archcraft new myproject --services 3` генерирует цепочку frontend → service1 → service2 → postgres с прокси и адаптерами для каждого звена
- [ ] **AC-3**: `archcraft new myproject --db redis` заменяет postgres на redis (другой адаптер и connection)
- [ ] **AC-4**: `archcraft new myproject --db none` генерирует цепочку без БД
- [ ] **AC-5**: `archcraft new myproject --replicas 1` генерирует каждый synthetic-сервис с `replicas: 1`
- [ ] **AC-6**: Если `project.yaml` уже существует, команда выводит `File project.yaml already exists. Overwrite? [y/N]` и прерывается при ответе не-`y`
- [ ] **AC-7**: Создаются пустые папки `results/` и `dashboards/` (с `.gitkeep`) рядом с project.yaml
- [ ] **AC-8**: Сгенерированный project.yaml содержит inline-комментарии на каждую секцию (services, adapters, connections, scenarios, observability)
- [ ] **AC-9**: `archcraft validate project.yaml` на сгенерированном файле завершается без ошибок
- [ ] **AC-10**: `archcraft help` выводит список всех команд (`new`, `run`, `validate`, `scenario`, `help`) с однострочными описаниями
- [ ] **AC-11**: `archcraft help new` выводит описание команды и все флаги с типами, допустимыми значениями и дефолтами
- [ ] **AC-12**: `archcraft help project.yaml` выводит структурированный YAML-референс: все секции верхнего уровня, вложенные поля, типы, допустимые значения, примеры

---

## Domain & Architecture

Затрагиваются только слои CLI и App. Доменные сущности не меняются.

```
src/
└── Archcraft.Cli/
    ├── Commands/
    │   ├── NewCommand.cs          # новый — парсит аргументы, вызывает use case
    │   └── HelpCommand.cs         # новый — выводит справку по топику
    └── Program.cs                 # добавить NewCommand.Build() и HelpCommand.Build() в RootCommand

src/
└── Archcraft.App/
    └── UseCases/
        └── NewProjectUseCase.cs   # новый — логика генерации scaffold
```

### Генерируемая топология

Правила построения по параметрам:

| Параметр | Значение по умолчанию | Допустимые значения |
|---|---|---|
| `--services` | `2` | целое ≥ 1 |
| `--db` | `postgres` | `postgres`, `redis`, `none` |
| `--replicas` | `3` | целое ≥ 1 |

Цепочка для `--services N --db <db>`:
- Сервисы: `frontend` → `service-1` → ... → `service-{N-1}` → (опционально) `<db>`
- При N=2: `frontend` → `backend` (имена прибиты гвоздями для читаемости)
- При N≥3: `frontend` → `svc-1` → ... → `svc-{N-1}` → `<db>`
- Каждый synthetic-сервис получает `replicas: <replicas>` и `proxy`
- Каждый synthetic-сервис получает адаптер к следующему звену цепочки
- БД-сервис получает `cluster.replicas: 1`

### YAML-референс (`help project.yaml`)

Выводится в stdout как форматированный текст. Покрывает:
- `name`, `services[]`, `adapters[]`, `connections[]`, `scenarios[]`, `observability`
- Для каждого поля: тип, описание, пример значения, обязательность

---

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Куда создаётся scaffold | Текущая директория | Пользователь сам управляет рабочей папкой (`cd myproject && archcraft new myproject`) |
| Конфликт файлов | Интерактивное подтверждение | Безопаснее silent overwrite, проще `--force` для скриптов (добавить флаг при необходимости) |
| help project.yaml | Отдельный топик в HelpCommand | `System.CommandLine` --help не покрывает YAML-специфику |
| Генерация yaml | StringBuilder в NewProjectUseCase | Шаблон с подстановкой, не YamlDotNet serialization — чтобы сохранить комментарии |

---

---

## Dependencies

- `System.CommandLine` (уже используется в проекте)
- `archcraft validate` (AC-9) должна корректно работать — уже реализована

---

*Approved by: — Date: —*
