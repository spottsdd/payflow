import json
import sys
from datetime import datetime, timezone

SERVICE_NAME = "api-gateway"


def log(level: str, message: str, **kwargs):
    entry = {
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "level": level,
        "service": SERVICE_NAME,
        "message": message,
        "trace_id": "",
        "span_id": "",
        **kwargs,
    }
    sys.stdout.write(json.dumps(entry) + "\n")
    sys.stdout.flush()
