# payflow

A realistic payment processing pipeline used as the target application for Datadog APM SDK instrumentation labs on Instruqt. The same application is implemented in six languages — pick your language and instrument it with Datadog APM.

## What This Is

`payflow` simulates the payment processing backend of a fintech platform. It is made up of six microservices that communicate synchronously via HTTP and asynchronously via Kafka. The application is fully functional and pre-built. Your job in the lab is to add Datadog APM instrumentation.

## Services

| Service | Port | What It Does |
|---|---|---|
| api-gateway | 8080 | Validates requests, routes to orchestrator |
| payment-orchestrator | 8081 | Coordinates the full payment flow |
| fraud-detection | 8082 | Scores transactions, returns APPROVE / FLAG / DENY |
| payment-processor | 8083 | Submits to external payment gateway (stub) |
| settlement-service | 8084 | Debits sender, credits receiver in one DB transaction |
| notification-service | 8085 | Kafka consumer — records payment outcome notifications |

## Request Flow

```
Client
  → api-gateway
    → payment-orchestrator
      → fraud-detection        (sync HTTP)
      → payment-processor      (sync HTTP → gateway stub)
      → settlement-service     (sync HTTP → PostgreSQL)
      → [Kafka: payment.completed | payment.failed]
        → notification-service (async consumer → PostgreSQL)
```

## Languages

| Language | Runtime | Framework |
|---|---|---|
| Java | OpenJDK 25 | Spring Boot 3 |
| Python | 3.14.4 | FastAPI |
| .NET | 10.0.5 | ASP.NET Core Web API |
| Node.js | 24.14.1 LTS | Express |
| Go | 1.26.2 | Gin |
| Ruby | 3.2.11 | Sinatra |

## Quick Start

### Prerequisites

- Docker + Docker Compose
- `make`

### Run a language stack

```bash
make java      # start the Java stack
make python    # start the Python stack
make dotnet    # start the .NET stack
make nodejs    # start the Node.js stack
make go        # start the Go stack
make ruby      # start the Ruby stack
```

Each command starts the full stack for that language: Postgres, Kafka, all 6 services, and the traffic generator.

### Other commands

```bash
make down       # stop all running containers
make reset-db   # restore Postgres to its seeded state
make logs       # tail logs for the running stack
make health     # check /health on all 6 service ports
```

## Deployment Environments

Each language directory contains a `deploy/` folder with configuration for three environments:

- `deploy/docker/` — Docker Compose
- `deploy/kubernetes/` — Kubernetes manifests
- `deploy/vm/` — Linux VM startup scripts (Ubuntu 22.04 LTS)

## Infrastructure

Shared infrastructure lives in `infra/` and is reused across all language versions:

- `infra/postgres/` — schema + 10 seeded accounts
- `infra/kafka/` — topic creation
- `infra/gateway-stub/` — simulates an external payment gateway
- `infra/traffic-generator/` — Go service that sends continuous payment traffic

## VM Setup

To set up a build environment on Ubuntu 22.04 LTS (or as an Instruqt lifecycle script):

```bash
bash scripts/setup-vm.sh
```

Installs all language runtimes, Docker, kubectl, and common build tools.

## APM Instrumentation

This repo ships the app **without** Datadog instrumentation. In the lab, you will:

1. Install the Datadog Agent
1. Set `DD_SERVICE`, `DD_ENV`, `DD_VERSION` on each service
1. Add the Datadog APM SDK for your language
1. Enable auto-instrumentation for the HTTP framework, database client, and Kafka client
1. Observe distributed traces across all six services in Datadog
