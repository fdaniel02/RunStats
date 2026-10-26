from unittest.mock import patch

from garmin_gateway import __main__


def test_main_serves_the_app_on_localhost_port_8080(monkeypatch):
    monkeypatch.delenv("PORT", raising=False)
    monkeypatch.delenv("HOST", raising=False)
    with patch.object(__main__, "configure_logging") as logging_setup, patch.object(__main__.uvicorn, "run") as run:
        __main__.main()

    logging_setup.assert_called_once()
    _, kwargs = run.call_args
    assert (kwargs["host"], kwargs["port"], kwargs["log_config"]) == ("127.0.0.1", 8080, None)


def test_host_and_port_come_from_the_environment(monkeypatch):
    monkeypatch.setenv("HOST", "192.0.2.10")
    monkeypatch.setenv("PORT", "9000")
    with patch.object(__main__, "configure_logging"), patch.object(__main__.uvicorn, "run") as run:
        __main__.main()

    assert (run.call_args.kwargs["host"], run.call_args.kwargs["port"]) == ("192.0.2.10", 9000)
