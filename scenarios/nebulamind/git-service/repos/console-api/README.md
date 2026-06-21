# NebulaMind Console API

Internal API service for the NebulaMind AI Console platform.

## Overview

This service provides the backend API for the NebulaMind console, including:

- Tenant management
- Knowledge base operations
- Audit logging
- GraphQL endpoint

## Tech Stack

- Python 3.11
- Flask
- PostgreSQL
- Redis (cache)

## Development

```bash
pip install -r requirements.txt
python app.py
```

## Deployment

Deployed via CI/CD pipeline. See `infra-playbooks` repository for deployment
configuration.

## Internal Endpoints

- `GET /healthz` - Health check
- `GET /api/v1/console/session/bootstrap` - Session bootstrap
- `POST /api/v1/auth/login` - Authentication
- `GET /api/v1/admin/audit/export` - Audit log export
- `POST /graphql` - GraphQL endpoint

## License

Proprietary - NebulaMind AI Corp
