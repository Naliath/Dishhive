import json
import unittest
from source_measurements import published_metric_ingredients


class PublishedMeasurementTests(unittest.TestCase):
    def html(self, ingredients, count=1):
        return '<script>window.wprm_recipes = ' + json.dumps({
            f'recipe-{index}': {'ingredients': ingredients} for index in range(count)
        }) + ';</script>'

    def ingredient(self, amount='1/4', name='flour', metrics=None):
        return {'type': 'ingredient', 'uid': 3, 'amount': amount, 'unit': 'cup',
                'name': name, 'notes': '', 'unit_systems': metrics or {
                    'unit-system-1': {'amount': '40', 'unit': 'g'},
                    'unit-system-2': {'amount': amount, 'unit': 'cup'}}}

    def test_uses_units_not_system_number_and_keeps_order(self):
        result = published_metric_ingredients(self.html([self.ingredient()]), ['1/4 cup flour'])
        self.assertEqual([{'quantity': 40, 'unit': 'g', 'name': 'flour'}], result)

    def test_partial_measurements_do_not_guess_missing_metrics(self):
        corn = self.ingredient('1', 'frozen corn', {'x': {'amount': '1', 'unit': 'cup'}})
        result = published_metric_ingredients(self.html([self.ingredient(), corn]), ['1/4 cup flour', '1 cup frozen corn'])
        self.assertIsNone(result[1])
        self.assertEqual(40, result[0]['quantity'])

    def test_misaligned_or_multiple_recipes_are_ignored(self):
        self.assertEqual([], published_metric_ingredients(self.html([self.ingredient()], 2), ['1/4 cup flour']))
        self.assertEqual([], published_metric_ingredients(self.html([self.ingredient()]), ['1/4 cup sugar']))
        self.assertEqual([], published_metric_ingredients('<script>window.wprm_recipes = bad();</script>', ['1 cup flour']))

    def test_invalid_metric_amount_keeps_source(self):
        ingredient = self.ingredient(metrics={'x': {'amount': 'NaN', 'unit': 'g'}})
        self.assertEqual([None], published_metric_ingredients(self.html([ingredient]), ['1/4 cup flour']))


if __name__ == '__main__':
    unittest.main()
