# NebulaMind Document Worker

Document parsing and conversion worker for the NebulaMind platform.

## Overview

Consumes document processing tasks from the message queue:

- Document parsing
- OCR
- Embedding generation
- Document classification
- Document conversion

## Configuration

See `config/worker.yml` for worker configuration.

## Development

```bash
pip install -r requirements.txt
python app.py
```

## Internal Endpoints

- `GET /healthz` - Health check
- `POST /api/parse` - Parse remote document
- `GET /api/tasks/<taskId>` - Query task status
- `GET /api/queue/stats` - Queue statistics (requires worker token)

## License

Proprietary - NebulaMind AI Corp
