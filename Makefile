.PHONY: java python dotnet nodejs go ruby down reset-db logs health

java:
	cd java/deploy/docker && docker compose up --build -d

python:
	cd python/deploy/docker && docker compose up --build -d

dotnet:
	cd dotnet/deploy/docker && docker compose up --build -d

nodejs:
	cd nodejs/deploy/docker && docker compose up --build -d

go:
	cd go/deploy/docker && docker compose up --build -d

ruby:
	cd ruby/deploy/docker && docker compose up --build -d

down:
	@for lang in java python dotnet nodejs go ruby; do \
		compose_file="$$lang/deploy/docker/docker-compose.yml"; \
		if [ -f "$$compose_file" ]; then \
			cd $$lang/deploy/docker && docker compose down 2>/dev/null; cd ../../..; \
		fi; \
	done

reset-db:
	docker compose -f java/deploy/docker/docker-compose.yml exec postgres \
		psql -U payflow -d payflow -f /docker-entrypoint-initdb.d/init.sql

logs:
	@echo "Usage: cd <lang>/deploy/docker && docker compose logs -f"

health:
	@for port in 8080 8081 8082 8083 8084 8085; do \
		status=$$(curl -s -o /dev/null -w "%{http_code}" http://localhost:$$port/health 2>/dev/null || echo "unreachable"); \
		echo "Port $$port: $$status"; \
	done
