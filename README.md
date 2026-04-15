# Archcraft

A declarative backend sandbox for load testing and chaos engineering. Define your service topology in YAML, run it in Docker, inject failures, and observe the results — all from a single interactive CLI.

---

## Why

Testing how a distributed system behaves under load or partial failure is hard to set up and hard to reproduce. Most teams either skip it entirely or maintain brittle custom scripts. Archcraft solves this by letting you describe your entire topology — services, databases, replicas, proxies — in one YAML file and then run repeatable load and chaos scenarios against it with a single command.

---

## What It Does

- **Spins up a full topology in Docker** from a `project.yaml` definition: services, replica groups, databases, ToxiProxy instances, and observability stack
- **Runs load and chaos scenarios**: constant-RPS load tests, timeline-based chaos sequences (latency injection, error injection, container kill/restore)
- **Seeds adapters with test data** so scenarios run against realistic database state
- **Generates Grafana dashboards** automatically — per service group, per database, and a project overview
- **Provides an interactive REPL** so you can run scenarios repeatedly, compare results across runs, and export session reports — without restarting containers
- **Hot-reloads scenario config** while the topology is running — edit `project.yaml`, scenarios update in 1 second

---

## Core Concepts

### project.yaml

Everything is defined in one file:

```yaml
name: my-project

services:
  - name: postgres
    image: bitnami/postgresql:latest
    port: 5432
    cluster:
      replicas: 1                    # one primary + one replica
      replication_password: secret
    env:
      POSTGRESQL_DATABASE: mydb
      POSTGRESQL_USERNAME: user
      POSTGRESQL_PASSWORD: secret

  - name: backend
    image: archcraft/synthetic:latest
    port: 8080
    replicas: 3                      # three backend instances
    proxy: backend-proxy             # attach ToxiProxy for chaos
    readiness:
      path: /health
      timeout: 30s
    synthetic:
      adapters:
        - pg-adapter
      endpoints:
        - alias: process
          pipeline:
            - operation: pg-call

adapters:
  - name: pg-adapter
    image: archcraft/pg-adapter:latest
    port: 8080
    technology: postgres
    connects_to: postgres
    seed_rows: 50000

connections:
  - from: backend
    to: postgres
    protocol: tcp
    port: 5432
    via: pg-adapter

scenarios:
  - name: baseline
    startup_timeout: 120s
    timeline:
      - at: 0s
        actions:
          - type: load
            target: backend
            endpoint: process
            rps: 10000
            duration: 60s

  - name: pg-degradation
    startup_timeout: 120s
    timeline:
      - at: 0s
        actions:
          - type: load
            target: backend
            endpoint: process
            rps: 10000
      - at: 10s
        actions:
          - type: inject_latency
            target: { from: backend, to: postgres }
            latency: 200ms
            duration: 30s

observability:
  prometheus:
    port: 9090
  grafana:
    port: 3000
```

### Services and Replicas

A service with `replicas: 3` expands to `backend-0`, `backend-1`, `backend-2`, each with its own ToxiProxy (`backend-proxy-0`, etc.). The compiler injects all necessary connection strings and adapter URLs automatically. A service with `cluster:` config gets a primary + N replicas with Bitnami-compatible replication environment variables pre-configured.

### Adapters

Adapters are small microservices that sit between synthetic workload generators and real databases. They expose a uniform `POST /execute` API so the synthetic service doesn't need to know about connection strings or protocols.

| Technology | Image | Operations |
|---|---|---|
| Postgres | `archcraft/pg-adapter` | query, insert, update |
| Redis | `archcraft/redis-adapter` | get, set |
| HTTP | `archcraft/http-adapter` | request (forward to another service) |

Each adapter also exposes `POST /seed` and `POST /clear` to populate or wipe test data. The number of rows is set via `seed_rows` in `project.yaml`.

### Synthetic Service

`archcraft/synthetic` is a configurable workload generator. It receives its endpoint pipeline definition as a JSON environment variable and executes it for each incoming HTTP request.

A pipeline step calls an adapter operation. Steps can have:
- `fallback`: executed if the operation returns NotFound
- `children`: executed if the operation succeeds
- `not_found_rate`: simulates cache misses at a configured probability

This lets you model realistic access patterns — cache-aside, read-through, write-through — without writing any application code.

### Scenarios

**Timeline scenarios** fire actions at precise timestamps:

| Action | Effect |
|---|---|
| `load` | Start sending requests at N RPS to a target endpoint |
| `inject_latency` | Add Nms latency on the proxy between two services |
| `inject_error` | Inject connection resets at a given error rate |
| `kill` | Stop a replica container (with optional auto-restore) |
| `restore` | Bring a killed replica back online |

Actions target services by name (`backend`), by replica index (`backend[1]`), or fan-out across a group (`backend` → all three replicas).

### Observability

The observability stack starts automatically when an `observability:` section is present. Archcraft generates:

- `prometheus.yml` scrape config for all synthetic services and infrastructure exporters
- Per-service-group Grafana dashboards with RPS, p50/p99 latency, error rate, per-replica breakdown, throughput per endpoint
- Per-database dashboards (Postgres: connections, query duration, lock contention; Redis: ops/sec, memory)
- A project overview dashboard

Grafana runs at the port you configure (e.g., `localhost:3000`) with anonymous admin access — no login required.

---

## Installation

**Requirements:** [.NET 10 SDK](https://dotnet.microsoft.com/download) and [Docker](https://www.docker.com/products/docker-desktop)

```bash
dotnet tool install -g archcraft
```

To update to the latest version:

```bash
dotnet tool update -g archcraft
```

---

## Getting Started

```bash
# Run a project interactively
archcraft run samples/test_project/project.yaml

# Run a single scenario and exit
archcraft scenario run samples/test_project/project.yaml --scenario baseline

# Validate project.yaml without starting anything
archcraft validate samples/test_project/project.yaml
```

### Interactive REPL

```
> seed all               # populate all adapters with test data
> run baseline           # run the baseline scenario
> run pg-degradation     # run the chaos scenario
> report                 # print session summary and save JSON report
> clear all              # wipe adapter data
> stop                   # stop all containers and exit
```

After each `run`, results are printed inline:

```
  Scenario                    p50 (ms)   p99 (ms)    Error %     Actual     Target   Sat %
  ──────────────────────────────────────────────────────────────────────────────────────────
  baseline                       12.4       48.1       0.02     600000     600000  100.0%
  pg-degradation                214.0      830.5       1.40     481200     600000   80.2%
```

`Sat %` (saturation) shows what fraction of the target request volume actually reached the system — a value below 100% means the system was the bottleneck, not the load generator.

`report` saves a timestamped JSON file alongside `project.yaml` and prints a full session table.

### Hot Reload

While a project is running, you can edit `project.yaml` and save — scenarios reload automatically within 1 second. Changes to services, adapters, or connections only take effect after a full restart.

---

## Problems It Solves

**"We don't know our system's real throughput ceiling"** — run a baseline scenario, observe saturation %, adjust RPS until you find it.

**"We don't know how a database outage affects the frontend"** — inject latency on the proxy between services and watch error rates and latencies cascade in Grafana in real time.

**"Our tests pass but production falls over when we deploy a new replica count"** — configure replicas in `project.yaml` and run the same scenarios to see how the cluster behaves.

**"Chaos tests are impossible to reproduce"** — the entire topology and scenario sequence is in a single YAML file, committed to version control, and runs identically on every machine.

**"We have no load data in CI"** — use `archcraft scenario run` in a pipeline to run a named scenario non-interactively and parse the JSON report.

---

## Project Structure

```
/
├── src/
│   ├── Archcraft.Domain/          # Entities, value objects — no external deps
│   ├── Archcraft.Contracts/       # Interfaces (IEnvironmentRunner, IScenarioRunner, …)
│   ├── Archcraft.ProjectModel/    # YAML deserialization models
│   ├── Archcraft.Serialization.Yaml/  # YAML → domain mapping
│   ├── Archcraft.ProjectCompiler/ # Replica expansion, env injection, proxy wiring
│   ├── Archcraft.Scenarios/       # Load runner, timeline runner, metrics collector
│   ├── Archcraft.Execution/       # EnvironmentContext, RunningService/Adapter/Proxy
│   ├── Archcraft.Execution.Docker/# Testcontainers-backed Docker runner
│   ├── Archcraft.Observability/   # Dashboard generation, report builder
│   ├── Archcraft.App/             # Use cases: RunProject, InteractiveSession, Validate
│   └── Archcraft.Cli/             # System.CommandLine entry point
├── services/
│   ├── synthetic/                 # Configurable workload generator (ASP.NET Core)
│   └── adapters/
│       ├── Adapters.Contracts/    # IAdapterOperation, IDataSeeder, endpoint mapping
│       ├── PgAdapter/             # Postgres adapter
│       ├── RedisAdapter/          # Redis adapter
│       └── HttpAdapter/           # HTTP passthrough adapter
├── tests/                         # Unit and integration tests (mirrors src/)
├── specs/
│   ├── active/                    # Specs in progress
│   ├── done/                      # Completed specs
│   └── _template.md
└── samples/
    └── test_project/              # Example project.yaml with all features demonstrated
```

---

## Development Workflow

Archcraft follows a spec-first discipline:

1. **`/spec`** — describe the feature, agree on acceptance criteria, save to `specs/active/`
2. **Implement** — work against the spec; reference it in commits
3. **`/spec-verify`** — check each acceptance criterion; move to `specs/done/` on PASS

No code is written before a spec is approved.

---

## Tech Stack

- **.NET 10 / C#** — all src and services
- **Testcontainers.NET** — Docker container lifecycle management
- **ToxiProxy** — network fault injection (latency, errors, bandwidth)
- **OpenTelemetry** — metrics and traces from synthetic services
- **Prometheus** — metrics scraping and storage
- **Grafana** — dashboard rendering
- **YamlDotNet** — YAML deserialization
- **System.CommandLine** — CLI parsing
- **xUnit + FluentAssertions** — tests
