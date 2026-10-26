FROM python:3.14-slim AS build
COPY --from=ghcr.io/astral-sh/uv:0.13.5 /uv /bin/uv
WORKDIR /app
ENV UV_COMPILE_BYTECODE=1 UV_LINK_MODE=copy UV_PYTHON_DOWNLOADS=never
COPY pyproject.toml uv.lock ./
RUN uv sync --frozen --no-dev --no-install-project
COPY src/ src/
RUN uv sync --frozen --no-dev --no-editable

FROM python:3.14-slim
RUN useradd --uid 10001 --no-create-home --shell /usr/sbin/nologin app && mkdir /data && chown app /data
WORKDIR /app
COPY --from=build /app/.venv .venv
ENV PATH="/app/.venv/bin:$PATH" PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 GARMIN_TOKEN_DIR=/data
EXPOSE 8080
USER 10001
ENTRYPOINT ["python", "-m", "garmin_gateway"]
