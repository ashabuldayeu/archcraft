# Spec: Публикация archcraft как глобального dotnet tool на NuGet.org

**Status:** approved
**Created:** 2026-04-15
**Author:** ashabuldayeu

---

## Summary

Опубликовать `archcraft` CLI на NuGet.org как глобальный dotnet tool. После публикации любой разработчик сможет установить инструмент одной командой (`dotnet tool install -g archcraft`) и использовать `archcraft` в любом терминале без привязки к директории проекта.

## Problem Statement

Сейчас запустить CLI можно только из директории репозитория через `dotnet run --project src/Archcraft.Cli`. Это неудобно для реального использования: нужно клонировать репо, знать путь к проекту, иметь исходники. Инструмент недоступен для людей, которые хотят просто использовать archcraft, не разрабатывать его.

## Goals

- [ ] `archcraft` устанавливается командой `dotnet tool install -g archcraft` с NuGet.org
- [ ] После установки команда `archcraft` работает в любом терминале на Windows / Linux / macOS
- [ ] NuGet-пакет содержит все необходимые метаданные (описание, лицензия, репозиторий, теги)
- [ ] Описан ручной процесс сборки и публикации новой версии

## Non-Goals (Out of Scope)

- CI/CD автопубликация при пуше тега
- Self-contained бинарник без зависимости от .NET runtime
- Приватный NuGet feed
- Versioning policy / semver автоматизация

## Acceptance Criteria

- [ ] **AC-1**: `dotnet pack src/Archcraft.Cli` завершается без ошибок и создаёт `.nupkg` файл
- [ ] **AC-2**: `dotnet tool install -g archcraft --add-source ./nupkg` устанавливает инструмент локально и команда `archcraft --help` работает в новом терминале
- [ ] **AC-3**: `Archcraft.Cli.csproj` содержит поля: `PackageId`, `Version`, `Description`, `Authors`, `RepositoryUrl`, `PackageLicenseExpression`, `PackageTags`, `PackageReadmeFile`
- [ ] **AC-4**: `dotnet nuget push` с API-ключом NuGet.org успешно публикует пакет (ручной процесс задокументирован в README)
- [ ] **AC-5**: Установленный инструмент корректно находит и запускает `archcraft new`, `archcraft run`, `archcraft help` из произвольной директории
- [ ] **AC-6**: `README.md` содержит секцию Installation с командой `dotnet tool install -g archcraft`

## Domain & Architecture

Затрагивается только `.csproj` и `README.md`. Никакой бизнес-логики не меняется.

```
src/
└── Archcraft.Cli/
    └── Archcraft.Cli.csproj   # добавить NuGet-метаданные, PackageReadmeFile

README.md                      # добавить секцию Installation
```

### Текущее состояние `.csproj`

Уже присутствуют:
```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>archcraft</ToolCommandName>
<PackageId>Archcraft.Cli</PackageId>
<Version>0.1.0</Version>
```

Нужно добавить:
```xml
<Description>...</Description>
<Authors>...</Authors>
<RepositoryUrl>...</RepositoryUrl>
<PackageLicenseExpression>MIT</PackageLicenseExpression>
<PackageTags>...</PackageTags>
<PackageReadmeFile>README.md</PackageReadmeFile>
<PackageIcon>icon.png</PackageIcon>   <!-- опционально -->
```

И подключить README как файл пакета:
```xml
<ItemGroup>
  <None Include="..\..\README.md" Pack="true" PackagePath="\" />
</ItemGroup>
```

### Ручной процесс публикации

```bash
# 1. Обновить Version в Archcraft.Cli.csproj
# 2. Собрать пакет
dotnet pack src/Archcraft.Cli -c Release -o ./nupkg

# 3. Опубликовать на NuGet.org
dotnet nuget push ./nupkg/Archcraft.Cli.<version>.nupkg \
  --api-key <NUGET_API_KEY> \
  --source https://api.nuget.org/v3/index.json
```

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Дистрибуция | NuGet.org (публичный) | Одна команда установки, без скачивания файлов вручную |
| Runtime | Framework-dependent (net10.0) | Аудитория — .NET-разработчики; маленький пакет (~3 MB vs ~80 MB) |
| Публикация | Ручной процесс | CI/CD автопубликация — отдельная задача |
| PackageId | `Archcraft.Cli` | Уже задан в проекте; менять не нужно |

## Dependencies

- Аккаунт на NuGet.org и API-ключ с правом publish
- `README.md` должен существовать в корне репозитория (уже есть)

---

*Approved by: — Date: —*
