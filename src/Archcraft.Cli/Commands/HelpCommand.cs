using System.CommandLine;

namespace Archcraft.Cli.Commands;

public static class HelpCommand
{
    private static readonly Dictionary<string, string> Topics = new(StringComparer.OrdinalIgnoreCase)
    {
        ["new"]          = HelpNew,
        ["run"]          = HelpRun,
        ["validate"]     = HelpValidate,
        ["scenario"]     = HelpScenario,
        ["project.yaml"] = HelpProjectYaml,
        ["yaml"]         = HelpProjectYaml,
    };

    public static Command Build()
    {
        Argument<string?> topicArg = new("topic")
        {
            Description = "Command name or 'project.yaml' for YAML reference",
            Arity = ArgumentArity.ZeroOrOne
        };

        Command command = new("help", "Show help for a command or the project.yaml structure")
        {
            topicArg
        };

        command.SetAction((ParseResult result) =>
        {
            string? topic = result.GetValue(topicArg);

            if (string.IsNullOrWhiteSpace(topic))
            {
                Console.WriteLine(HelpOverview);
                return 0;
            }

            if (Topics.TryGetValue(topic, out string? text))
            {
                Console.WriteLine(text);
                return 0;
            }

            Console.Error.WriteLine($"Unknown help topic '{topic}'. Available: new, run, validate, scenario, project.yaml");
            return 1;
        });

        return command;
    }

    // ── Overview ──────────────────────────────────────────────────────────────

    private const string HelpOverview = """
Archcraft — declarative backend sandbox & scenario runner

USAGE
  archcraft <command> [options]

COMMANDS
  new        Scaffold a new project in the current directory
  run        Start an interactive REPL session for a project
  validate   Validate project.yaml without starting anything
  scenario   Run one or more named scenarios non-interactively
  help       Show help for a command or the project.yaml structure

EXAMPLES
  archcraft new myproject
  archcraft new myproject --services 3 --db redis --replicas 2
  archcraft run project.yaml
  archcraft validate project.yaml
  archcraft scenario run project.yaml --scenario warmup

HELP TOPICS
  archcraft help new           Options for the 'new' command
  archcraft help run           Options for the 'run' command
  archcraft help validate      Options for the 'validate' command
  archcraft help scenario      Options for the 'scenario' command
  archcraft help project.yaml  Full YAML reference
""";

    // ── Command help ──────────────────────────────────────────────────────────

    private const string HelpNew = """
archcraft new <name> [options]

  Scaffold a new archcraft project in the current directory.
  Creates project.yaml with an inline-commented template, plus
  empty results/ and dashboards/ directories.

ARGUMENTS
  <name>   Project name — sets the 'name:' field in project.yaml.
           Does not create a subfolder; files go into the current directory.

OPTIONS
  --services <n>            Number of synthetic services in the call chain.
                            Default: 2.  Minimum: 1.
                            2 → frontend → backend
                            3 → frontend → svc-1 → svc-2
                            N → frontend → svc-1 → ... → svc-{N-1}

  --db <postgres|redis|none>  Database at the end of the chain.
                            Default: postgres.
                            postgres  — adds a Bitnami PostgreSQL service + pg-adapter
                            redis     — adds a Bitnami Redis service + redis-adapter
                            none      — no database; last service has no downstream

  --replicas <n>            Number of replicas per synthetic service.
                            Default: 3.  Minimum: 1.
                            Each replica gets its own proxy for chaos injection.

EXAMPLES
  archcraft new myproject
  archcraft new myproject --services 3 --db redis --replicas 1
  archcraft new api-bench --db none --replicas 2

CONFLICT BEHAVIOUR
  If project.yaml already exists in the current directory, the command
  will prompt: "File project.yaml already exists. Overwrite? [y/N]"
  and abort unless the user types 'y'.
""";

    private const string HelpRun = """
archcraft run <project-file>

  Load project.yaml, start the Docker environment, and open an
  interactive REPL for running scenarios and inspecting results.

ARGUMENTS
  <project-file>   Path to project.yaml (e.g. project.yaml or ./myproject/project.yaml)

REPL COMMANDS
  run [scenario]   Run all scenarios, or a single named scenario
  seed [adapter]   Seed test data via adapters (all adapters if none specified)
  clear [adapter]  Clear seeded data
  report           Write JSON + HTML report to results/
  help             Print REPL command list
  exit / Ctrl+C    Tear down environment and quit

EXAMPLE
  archcraft run project.yaml
""";

    private const string HelpValidate = """
archcraft validate <project-file>

  Parse and validate project.yaml (topology, references, required fields).
  Does not start Docker or run any scenarios.

ARGUMENTS
  <project-file>   Path to project.yaml

EXIT CODES
  0  Validation passed
  1  Validation failed (errors printed to stderr)

EXAMPLE
  archcraft validate project.yaml
""";

    private const string HelpScenario = """
archcraft scenario run <project-file> [options]

  Start the Docker environment, run the specified scenarios
  non-interactively, write a report, and shut down.

ARGUMENTS
  <project-file>   Path to project.yaml

OPTIONS
  --scenario <name>   Name of the scenario to run (can be repeated).
                      If omitted, all scenarios are run.

EXAMPLES
  archcraft scenario run project.yaml
  archcraft scenario run project.yaml --scenario warmup --scenario baseline
""";

    // ── YAML Reference ────────────────────────────────────────────────────────

    private const string HelpProjectYaml = """
project.yaml — full field reference

TOP-LEVEL FIELDS
  name: <string>          (required) Project name shown in reports and Grafana.

  services: []            (required) List of service definitions (infra + synthetic).
  adapters: []            (required) List of adapter definitions.
  connections: []         (required) List of connections between services.
  scenarios: []           (required) List of scenario definitions.
  observability: {}       (optional) Prometheus + Grafana configuration.

──────────────────────────────────────────────────────────────────────────────
SERVICE FIELDS  (services[])
──────────────────────────────────────────────────────────────────────────────
  name: <string>          (required) Unique service identifier.
  image: <string>         (required) Docker image reference.
  port: <int>             (required) Port the service listens on inside Docker.
  proxy: <string>         (optional) Toxiproxy proxy name prefix for chaos injection.
                          With replicas: 3 and proxy: backend-proxy, creates
                          backend-proxy-0, backend-proxy-1, backend-proxy-2.
  replicas: <int>         (optional, default 1) Number of synthetic service instances.
  readiness:              (optional) Health check before scenarios start.
    path: <string>        HTTP path, e.g. /health.
    timeout: <duration>   Max wait time, e.g. 30s.
  env:                    (optional) Environment variables (key: value map).
  cluster:                (optional) Bitnami replication config (postgres / redis).
    replicas: <int>       Number of read replicas (default 1).
    replication_user: <string>      (postgres only)
    replication_password: <string>  (required for cluster)
  synthetic:              (optional) Marks service as a synthetic load generator.
    adapters: []          List of adapter names this service uses.
    endpoints: []         List of HTTP endpoints exposed by the service.
      - alias: <string>   Endpoint alias used in scenario load targets.
        pipeline: []      Ordered list of operations for this endpoint.
          - operation: <string>        Operation name (maps to adapter env var).
            not_found_rate: <0.0–1.0>  Probability of NOT_FOUND result.
            fallback: []               Operations to run on NOT_FOUND.

──────────────────────────────────────────────────────────────────────────────
ADAPTER FIELDS  (adapters[])
──────────────────────────────────────────────────────────────────────────────
  name: <string>          (required) Unique adapter identifier.
  image: <string>         (required) Docker image reference.
  port: <int>             (required) Port the adapter listens on.
  technology: <string>    (required) postgres | redis | http
  connects_to: <string>   (required) Name of the service this adapter wraps.
  seed_rows: <int>        (optional) Rows to insert on 'seed' REPL command.
  env:                    (optional) Extra environment variables.

──────────────────────────────────────────────────────────────────────────────
CONNECTION FIELDS  (connections[])
──────────────────────────────────────────────────────────────────────────────
  from: <string>          (required) Source service name.
  to: <string>            (required) Target service name.
  protocol: http | tcp    (required)
  port: <int>             (required) Target port.
  via: <string>           (required) Adapter name that handles this connection.

──────────────────────────────────────────────────────────────────────────────
SCENARIO FIELDS  (scenarios[])
──────────────────────────────────────────────────────────────────────────────
  name: <string>          (required) Unique scenario name.
  startup_timeout: <dur>  (optional, default 60s) Wait for services to be ready.

  Simple HTTP scenario:
    type: http             (required for simple scenarios)
    target: <string>       Service name to target.
    rps: <int>             Requests per second.
    duration: <dur>        How long to run, e.g. 30s, 2m.

  Timeline scenario (recommended):
    timeline: []           List of timed action points.
      - at: <duration>     Time offset from scenario start (e.g. 0s, 30s).
        actions: []        Actions to execute at this time.

    Action types:
      type: load
        target: <string>   Service name or service[N] for specific replica.
        endpoint: <string> Endpoint alias.
        rps: <int>         Requests per second.
        duration: <dur>    (optional) If omitted, load runs until scenario ends.

      type: inject_latency
        target:
          from: <string>   Source service (or service[N]).
          to: <string>     Target service (or service[N]).
        latency: <dur>     Added latency, e.g. 300ms, 1s.
        duration: <dur>    (optional) Auto-removed after this time.

      type: inject_error
        target:
          from: <string>
          to: <string>
        error_rate: <0.0–1.0>  Fraction of requests to fail.
        duration: <dur>    (optional)

      type: kill
        target: <string>   Service name or service[N] for specific replica.
        duration: <dur>    (optional) Auto-restored after this time.

      type: restore
        target: <string>   Service name or service[N].

──────────────────────────────────────────────────────────────────────────────
OBSERVABILITY FIELDS  (observability)
──────────────────────────────────────────────────────────────────────────────
  prometheus:
    port: <int>            Host port for Prometheus UI (default 9090).
  grafana:
    port: <int>            Host port for Grafana UI (default 3000).

──────────────────────────────────────────────────────────────────────────────
DURATION FORMAT
  All duration values use suffix notation: 30s, 5m, 2h, 500ms.

EXAMPLE — minimal timeline scenario
  scenarios:
    - name: warmup
      startup_timeout: 120s
      timeline:
        - at: 0s
          actions:
            - type: load
              target: frontend
              endpoint: handle
              rps: 500
              duration: 20s
""";
}
