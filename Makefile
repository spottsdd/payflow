COMPOSE = docker compose
ACTIVE_PROFILE ?=

.PHONY: java python dotnet nodejs go ruby down reset-db logs health

java:
	$(COMPOSE) --profile java up --build -d

python:
	$(COMPOSE) --profile python up --build -d

dotnet:
	$(COMPOSE) --profile dotnet up --build -d

nodejs:
	$(COMPOSE) --profile nodejs up --build -d

go:
	$(COMPOSE) --profile go up --build -d

ruby:
	$(COMPOSE) --profile ruby up --build -d

down:
	$(COMPOSE) --profile java --profile python --profile dotnet --profile nodejs --profile go --profile ruby down

reset-db:
	$(COMPOSE) exec postgres psql -U payflow -d payflow -f /docker-entrypoint-initdb.d/init.sql

logs:
	$(COMPOSE) logs -f

health:
	@for port in 8080 8081 8082 8083 8084 8085; do \
		status=$$(curl -s -o /dev/null -w "%{http_code}" http://localhost:$$port/health); \
		echo "Port $$port: $$status"; \
	done
