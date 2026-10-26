import os

import uvicorn

from .app import create_app
from .garmin import GarminService
from .logs import configure_logging
from .settings import Settings


def main() -> None:
    configure_logging()
    app = create_app(GarminService(Settings.from_env()))
    # The Docker image sets HOST to all interfaces; the container is only reachable through its Kubernetes Service.
    host = os.environ.get("HOST", "127.0.0.1")
    uvicorn.run(app, host=host, port=int(os.environ.get("PORT", "8080")), log_config=None)


if __name__ == "__main__":
    main()
