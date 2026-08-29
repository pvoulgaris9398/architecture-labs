import math
import unittest

from benchmark import linear_return, log_return, make_data


class AggregationTests(unittest.TestCase):
    def test_methods_are_equivalent_for_generated_ranges(self) -> None:
        multipliers, log_returns, ranges = make_data(days=100, queries=25, seed=7)

        for start, end in ranges:
            self.assertTrue(
                math.isclose(
                    linear_return(multipliers, start, end),
                    log_return(log_returns, start, end),
                    rel_tol=1e-12,
                    abs_tol=1e-12,
                )
            )

    def test_single_day_return(self) -> None:
        self.assertAlmostEqual(linear_return([1.02], 0, 1), 0.02)
        self.assertAlmostEqual(log_return([math.log1p(0.02)], 0, 1), 0.02)


if __name__ == "__main__":
    unittest.main()
