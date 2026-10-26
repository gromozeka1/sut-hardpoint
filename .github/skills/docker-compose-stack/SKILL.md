---
name: docker-compose-stack
description: Use when changing, starting, debugging, or documenting the local Docker Compose stack for ProductAPI and WebApp.
---

# Local Compose stack

- Read `docker-compose.yml`, both service Dockerfiles, and their `.dockerignore` files before changing build contexts or runtime settings.
- The Compose services are `productapi` and `webapp`; WebApp must call `http://productapi:8080/` on the Compose network, not `localhost`.
- The published local endpoints are `http://localhost:5044` for ProductAPI and `http://localhost:5168` for WebApp.
- ProductAPI persists its SQLite file in `/app/Database`, backed by the `productapi-data` named volume. Do not remove or replace persistent storage unintentionally.
- Validate edits with `docker compose config`, then use `docker compose build` and `docker compose up` when Docker is available. Report startup or environmental failures rather than presenting configuration validation as an integration test.
