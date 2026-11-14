"""Read published metric alternatives without executing a site's JavaScript."""
import json
import math
import re
from bs4 import BeautifulSoup


def published_metric_ingredients(html: str, original_lines: list[str]) -> list[dict | None]:
    candidates = []
    for script in BeautifulSoup(html, "html.parser").find_all("script"):
        text = script.string or ""
        match = re.search(r"\bwindow\.wprm_recipes\s*=\s*", text)
        if not match:
            continue
        try:
            payload, _ = json.JSONDecoder().raw_decode(text[match.end():])
            if isinstance(payload, dict):
                candidates.extend(recipe for recipe in payload.values() if isinstance(recipe, dict))
        except (ValueError, TypeError):
            continue
    # A roundup may contain unrelated recipes. Ambiguous matches retain the normal extraction.
    if len(candidates) != 1:
        return []
    items = candidates[0].get("ingredients", [])
    if not isinstance(items, list):
        return []
    ingredients = [item for item in items
                   if isinstance(item, dict) and item.get("type") == "ingredient"]
    if len(ingredients) != len(original_lines):
        return []
    normalize = lambda value: " ".join(value.split()).casefold()
    result = []
    for item, original in zip(ingredients, original_lines):
        prefix = " ".join(str(item.get(key, "")) for key in ("amount", "unit", "name"))
        prefix = normalize(prefix)
        line = normalize(original)
        if not prefix or not (line == prefix or line.startswith(prefix + " ")):
            return []
        options = item.get("unit_systems", {})
        if not isinstance(options, dict):
            options = {}
        metrics = [measure for measure in options.values()
                   if isinstance(measure, dict) and measure.get("unit") in ("g", "kg", "ml", "l")]
        metric = None
        if len(metrics) == 1:
            measure = metrics[0]
            try:
                quantity = float(measure["amount"])
                name = " ".join(str(item.get(key, "")).strip() for key in ("name", "notes")).strip()
                if math.isfinite(quantity) and 0 < quantity <= 100000 and 0 < len(name) <= 200:
                    metric = {"quantity": quantity, "unit": measure["unit"], "name": name}
            except (KeyError, TypeError, ValueError):
                pass
        result.append(metric)
    return result
