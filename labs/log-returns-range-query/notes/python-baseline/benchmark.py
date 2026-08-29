#!/usr/bin/env python3
"""Supporting note: compare product-based and precomputed log-return aggregation."""

from __future__ import annotations

import argparse
import math
import random
import time
from collections.abc import Callable, Sequence


Range = tuple[int, int]


def linear_return(multipliers: Sequence[float], start: int, end: int) -> float:
    return math.prod(multipliers[start:end]) - 1.0


def log_return(log_returns: Sequence[float], start: int, end: int) -> float:
    return math.expm1(math.fsum(log_returns[start:end]))


def make_data(days: int, queries: int, seed: int) -> tuple[list[float], list[float], list[Range]]:
    randomizer = random.Random(seed)
    daily_returns = [randomizer.uniform(-0.03, 0.03) for _ in range(days)]
    multipliers = [1.0 + value for value in daily_returns]
    log_returns = [math.log1p(value) for value in daily_returns]
    ranges = []

    for _ in range(queries):
        start = randomizer.randrange(days)
        end = randomizer.randrange(start + 1, days + 1)
        ranges.append((start, end))

    return multipliers, log_returns, ranges


def run_queries(values: Sequence[float], ranges: Sequence[Range], aggregate: Callable[[Sequence[float], int, int], float]) -> float:
    checksum = 0.0
    for start, end in ranges:
        checksum += aggregate(values, start, end)
    return checksum


def measure(action: Callable[[], float], repetitions: int) -> tuple[float, float]:
    durations = []
    checksum = 0.0
    for _ in range(repetitions):
        started = time.perf_counter()
        checksum = action()
        durations.append(time.perf_counter() - started)
    return min(durations), checksum


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--days", type=int, default=10_000)
    parser.add_argument("--queries", type=int, default=1_000)
    parser.add_argument("--repetitions", type=int, default=5)
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args()
    if min(args.days, args.queries, args.repetitions) < 1:
        parser.error("days, queries, and repetitions must be positive")
    return args


def main() -> None:
    args = parse_args()
    multipliers, log_returns, ranges = make_data(args.days, args.queries, args.seed)

    for start, end in ranges:
        if not math.isclose(
            linear_return(multipliers, start, end),
            log_return(log_returns, start, end),
            rel_tol=1e-11,
            abs_tol=1e-11,
        ):
            raise RuntimeError(f"methods disagree for range [{start}, {end})")

    linear_seconds, linear_checksum = measure(
        lambda: run_queries(multipliers, ranges, linear_return), args.repetitions
    )
    log_seconds, log_checksum = measure(
        lambda: run_queries(log_returns, ranges, log_return), args.repetitions
    )

    print(f"days={args.days} queries={args.queries} repetitions={args.repetitions} seed={args.seed}")
    print(f"linear best: {linear_seconds:.6f}s")
    print(f"log best:    {log_seconds:.6f}s")
    print(f"ratio (linear/log): {linear_seconds / log_seconds:.3f}x")
    print(f"checksum delta: {abs(linear_checksum - log_checksum):.3e}")


if __name__ == "__main__":
    main()
