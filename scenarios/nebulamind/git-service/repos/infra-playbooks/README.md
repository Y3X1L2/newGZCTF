# NebulaMind Infrastructure Playbooks

Ansible playbooks and infrastructure configuration for NebulaMind platform.

## Overview

This repository contains:

- Deployment playbooks for all NebulaMind services
- Infrastructure setup roles
- Monitoring and alerting configuration
- Disaster recovery procedures

## Structure

```
playbooks/     - Main deployment playbooks
roles/         - Reusable Ansible roles
inventory/     - Environment inventories
group_vars/    - Group variables
```

## Usage

```bash
ansible-playbook -i inventory/prod playbooks/deploy-console-api.yml
```

## Services

- `console-api` - NebulaMind Console API
- `doc-worker` - Document parsing worker
- `cache-broker` - Redis-compatible message broker
- `git-service` - Internal Git service
- `portal-web` - Public portal
- `edge-gateway` - Edge gateway / reverse proxy
- `support-upload` - Support ticket upload service

## License

Proprietary - NebulaMind AI Corp
