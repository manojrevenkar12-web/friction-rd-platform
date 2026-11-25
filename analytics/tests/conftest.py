from __future__ import annotations

import pytest

from dynolab.world.catalog import build_catalog, load_world_config
from dynolab.world.requests import make_requests


@pytest.fixture(scope="session")
def cfg() -> dict:
    return load_world_config()


@pytest.fixture(scope="session")
def cat(cfg):
    return build_catalog(5, cfg)


@pytest.fixture(scope="session")
def requests_(cat, cfg):
    return make_requests(5, "test", cat, cfg)
