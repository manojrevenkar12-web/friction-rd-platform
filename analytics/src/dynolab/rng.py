"""Keyed random streams: a draw depends on what it is for, never on how many draws came before.
That is what gives every design the same luck."""

from __future__ import annotations

import hashlib
import random
from collections.abc import Sequence
from typing import TypeVar

import numpy as np

T = TypeVar("T")


def _seed(*keys: object) -> int:
    return int.from_bytes(hashlib.sha256("|".join(map(str, keys)).encode()).digest()[:8], "big")


class Rng:
    def __init__(self, *keys: object) -> None:
        self.keys = keys
        self._r = random.Random(_seed(*keys))

    def child(self, *keys: object) -> Rng:
        return Rng(*self.keys, *keys)

    def random(self) -> float:
        return self._r.random()

    def uniform(self, a: float, b: float) -> float:
        return self._r.uniform(a, b)

    def randint(self, a: int, b: int) -> int:
        return self._r.randint(a, b)

    def choice(self, seq: Sequence[T]) -> T:
        return self._r.choice(seq)

    def sample(self, seq: Sequence[T], k: int) -> list[T]:
        return self._r.sample(list(seq), k)

    def weighted(self, options: dict[str, float]) -> str:
        keys = list(options)
        return self._r.choices(keys, weights=[options[k] for k in keys])[0]

    def gauss(self, mu: float, sigma: float) -> float:
        return self._r.gauss(mu, sigma)

    def poisson(self, lam: float) -> int:
        return int(np.random.default_rng(_seed(*self.keys, "poisson")).poisson(lam))

    def numpy(self) -> np.random.Generator:
        return np.random.default_rng(_seed(*self.keys, "numpy"))
