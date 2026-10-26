import io
import logging
import threading
import zipfile
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Annotated, Any

from fastapi import FastAPI, Path, Query, Request
from fastapi.responses import JSONResponse, PlainTextResponse, Response
from garminconnect import Garmin, GarminConnectConnectionError, GarminConnectNotFoundError

from .garmin import GarminService, GarminUnavailable

logger = logging.getLogger(__name__)

ActivityId = Annotated[int, Path(gt=0)]


class NoFitFile(Exception):
    pass


def extract_fit(archive: bytes) -> bytes:
    """Garmin sends the original upload as a zip; activities imported from GPX or TCX have no FIT file in it."""
    with zipfile.ZipFile(io.BytesIO(archive)) as zip_file:
        names = [n for n in zip_file.namelist() if n.lower().endswith(".fit")]
        if len(names) != 1:
            raise NoFitFile(f"Expected one FIT file in the download, found {len(names)}")
        return zip_file.read(names[0])


def create_app(service: GarminService) -> FastAPI:
    @asynccontextmanager
    async def lifespan(_: FastAPI) -> AsyncIterator[None]:
        # Logging in before the first sync shows a wrong password in the logs right away, without blocking startup.
        app.state.login = threading.Thread(target=_login_in_background, args=(service,), daemon=True)
        app.state.login.start()
        yield

    app = FastAPI(title="garmin-gateway", lifespan=lifespan)

    @app.exception_handler(GarminUnavailable)
    async def unavailable(_: Request, e: GarminUnavailable) -> JSONResponse:
        return JSONResponse({"error": e.reason}, status_code=503, headers={"Retry-After": str(e.retry_after_seconds)})

    @app.exception_handler(GarminConnectNotFoundError)
    async def not_found(_: Request, e: GarminConnectNotFoundError) -> JSONResponse:
        return JSONResponse({"error": "Not found in Garmin Connect"}, status_code=404)

    @app.exception_handler(NoFitFile)
    async def no_fit_file(_: Request, e: NoFitFile) -> JSONResponse:
        return JSONResponse({"error": str(e)}, status_code=404)

    @app.exception_handler(GarminConnectConnectionError)
    async def garmin_failed(request: Request, e: GarminConnectConnectionError) -> JSONResponse:
        logger.warning("Garmin request for %s failed: %s", request.url.path, e)
        return JSONResponse({"error": "Garmin Connect request failed"}, status_code=502)

    @app.get("/activities")
    def activities(start: Annotated[int, Query(ge=0)] = 0, limit: Annotated[int, Query(ge=1, le=1000)] = 100) -> Any:
        return service.call(lambda g: g.get_activities(start, limit))

    @app.get("/activities/{activity_id}")
    def activity(activity_id: ActivityId) -> Any:
        return service.call(lambda g: g.get_activity(str(activity_id)))

    @app.get("/activities/{activity_id}/splits")
    def splits(activity_id: ActivityId) -> Any:
        return service.call(lambda g: g.get_activity_splits(str(activity_id)))

    @app.get("/activities/{activity_id}/hr-zones")
    def hr_zones(activity_id: ActivityId) -> Any:
        return service.call(lambda g: g.get_activity_hr_in_timezones(str(activity_id)))

    @app.get("/activities/{activity_id}/weather")
    def weather(activity_id: ActivityId) -> Any:
        return service.call(lambda g: g.get_activity_weather(str(activity_id)))

    @app.get("/activities/{activity_id}/fit")
    def fit(activity_id: ActivityId) -> Response:
        archive = service.call(
            lambda g: g.download_activity(str(activity_id), dl_fmt=Garmin.ActivityDownloadFormat.ORIGINAL)
        )
        return Response(extract_fit(archive), media_type="application/vnd.ant.fit")

    @app.get("/personal-records")
    def personal_records() -> Any:
        return service.call(lambda g: g.get_personal_record())

    @app.get("/healthz/live", response_class=PlainTextResponse)
    def live() -> str:
        return "Healthy"

    @app.get("/healthz/ready", response_class=PlainTextResponse)
    def ready() -> PlainTextResponse:
        return PlainTextResponse("Healthy") if service.logged_in else PlainTextResponse("Unhealthy", status_code=503)

    return app


def _login_in_background(service: GarminService) -> None:
    try:
        service.login()
    except GarminUnavailable:
        pass  # Already logged with the cause; requests retry once the cooldown is over.
    except Exception:
        logger.exception("Garmin login failed")
