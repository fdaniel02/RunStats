import os

import uvicorn

from .app import create_app
from .garmin import GarminService
from .logs import configure_logging
from .settings import Settings


def main() -> None:
    configure_logging()
    app = create_app(GarminService(Settings.from_env()))
    # All interfaces: the container is only reachable through its Kubernetes Service.
    uvicorn.run(app, host="0.0.0.0", port=int(os.environ.get("PORT", "8080")), log_config=None)


if __name__ == "__main__":
    main()
