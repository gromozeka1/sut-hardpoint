---
applyTo: "**/Dockerfile,**/docker-compose.yml,**/.dockerignore"
---

# Container guidance

- Keep the Dockerfiles multi-stage and aligned with the repository's .NET 10 SDK/runtime images.
- Preserve the Compose service names `productapi` and `webapp`, their container port `8080`, and the internal WebApp-to-API URL `http://productapi:8080/`.
- Keep host port mappings at `5044` for ProductAPI and `5168` for WebApp unless the task explicitly changes the documented local endpoints.
- ProductAPI stores SQLite data under `/app/Database`; preserve the `productapi-data` named volume and ensure the runtime user can write to that path.
- Check Docker build contexts and `COPY` paths together when changing either Dockerfile. Keep build inputs minimal and do not copy local secrets or generated output into images.
- Validate Compose edits with `docker compose config`; when available, build and start the affected services to check cross-service configuration.
