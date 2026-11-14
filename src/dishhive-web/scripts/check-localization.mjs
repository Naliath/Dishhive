import fs from 'node:fs';
import path from 'node:path';
import { parseTemplate } from '@angular/compiler';

const flatten = (object, prefix = '', result = {}) => {
  for (const [key, value] of Object.entries(object)) {
    if (typeof value === 'string') result[prefix + key] = value;
    else flatten(value, `${prefix}${key}.`, result);
  }
  return result;
};
const catalogs = Object.fromEntries(['en', 'nl'].map(language => [language,
  flatten(JSON.parse(fs.readFileSync(`public/i18n/${language}.json`, 'utf8')))]));
const failures = [];
for (const key of new Set([...Object.keys(catalogs.en), ...Object.keys(catalogs.nl)])) {
  const values = [catalogs.en[key], catalogs.nl[key]];
  if (values.some(value => value === undefined)) { failures.push(`Missing catalog key: ${key}`); continue; }
  const parameters = values.map(value => [...value.matchAll(/\{(\w+)\}/g)].map(match => match[1]).sort().join(','));
  if (parameters[0] !== parameters[1]) failures.push(`Different interpolation parameters: ${key}`);
}
const backendCatalogs = Object.fromEntries(['en', 'nl'].map(language => [language,
  flatten(JSON.parse(fs.readFileSync(`../Dishhive.Api/Resources/Messages/${language}.json`, 'utf8')))]));
for (const key of new Set([...Object.keys(backendCatalogs.en), ...Object.keys(backendCatalogs.nl)])) {
  const values = [backendCatalogs.en[key], backendCatalogs.nl[key]];
  if (values.some(value => value === undefined)) { failures.push(`Missing API message: ${key}`); continue; }
  const parameters = values.map(value => [...value.matchAll(/\{(\w+)\}/g)].map(match => match[1]).sort().join(','));
  if (parameters[0] !== parameters[1]) failures.push(`Different API interpolation parameters: ${key}`);
}
const files = directory => fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry =>
  entry.isDirectory() ? files(path.join(directory, entry.name)) : [path.join(directory, entry.name)]);
const exempt = value => /^(Dishhive|Freezy \(Freezer Inventory\)|recipe-scrapers|https?:\/\/.*|v|v\{p0\}|\{p0\} \(v\{p1\}\)|\{p0\} \{p1\} · p95 \{p2\}|· \{p0\} min)$/.test(value);
const normalize = value => value.replace(/\s+/g, ' ').trim();
let templates = 0;
for (const file of files('src/app').filter(file => /\.(html|ts)$/.test(file) && !file.endsWith('.spec.ts') && !file.includes('generated'))) {
  const source = fs.readFileSync(file, 'utf8');
  for (const match of source.matchAll(/['"]([a-zA-Z]\w*(?:\.\w+)+)['"]\s*\|\s*translate(?:Plural)?\b|\b(?:t|plural)\(\s*['"]([^'"]+)['"]/g)) {
    const key = match[1] ?? match[2];
    if (!(key in catalogs.en) && !(`${key}.other` in catalogs.en)) failures.push(`${file}: unknown message ${key}`);
  }
  if (!file.endsWith('.html')) continue;
  templates++;
  const parsed = parseTemplate(source, file);
  for (const error of parsed.errors ?? []) failures.push(`${file}: ${error.msg}`);
  const check = (value, node) => {
    value = normalize(value);
    if (/[A-Za-z]/.test(value) && !exempt(value)) failures.push(`${file}:${node.sourceSpan.start.line + 1}: untranslated text ${value}`);
  };
  const seen = new Set();
  function checkParameters(value) {
    if (!value || typeof value !== 'object' || seen.has(value)) return;
    seen.add(value);
    if (value.constructor.name === 'BindingPipe' && ['translate', 'translatePlural'].includes(value.name)
        && value.exp?.constructor.name === 'LiteralPrimitive') {
      const key = value.exp.value;
      const text = catalogs.en[key] ?? catalogs.en[`${key}.other`];
      const expected = [...(text ?? '').matchAll(/\{(\w+)\}/g)].map(match => match[1]);
      const plural = value.name === 'translatePlural';
      const supplied = new Set((value.args[plural ? 1 : 0]?.keys ?? []).map(item => item.key));
      if (plural) supplied.add('count');
      for (const name of expected) if (!supplied.has(name)) failures.push(`${file}: ${key} lacks parameter ${name}`);
    }
    for (const child of Object.values(value)) if (child && typeof child === 'object') {
      if (Array.isArray(child)) child.forEach(checkParameters); else checkParameters(child);
    }
  }
  parsed.nodes.forEach(checkParameters);
  function walk(node, parent) {
    if (node.constructor.name === 'Text' && parent !== 'mat-icon') check(node.value, node);
    if (node.constructor.name === 'BoundText' && parent !== 'mat-icon') {
      const ast = node.value.ast;
      if (ast.strings?.some(value => /[A-Za-z]/.test(value))) check(ast.strings.map((value, i) =>
        value + (i < ast.expressions.length ? `{p${i}}` : '')).join(''), node);
    }
    for (const attribute of node.attributes ?? []) {
      if (['aria-label', 'matTooltip', 'title', 'placeholder', 'alt'].includes(attribute.name) && attribute.value) check(attribute.value, attribute);
    }
    for (const child of [...(node.children ?? []), ...(node.branches ?? []), ...(node.cases ?? []), ...(node.groups ?? [])]) walk(child, node.name ?? parent);
    for (const key of ['empty', 'placeholder', 'loading', 'error']) if (node[key]) walk(node[key], node.name ?? parent);
  }
  parsed.nodes.forEach(node => walk(node));
}
if (failures.length) { console.error(failures.join('\n')); process.exitCode = 1; }
else console.log(`Localization passed: ${templates} templates; ${Object.keys(catalogs.en).length} matching English/Dutch messages and parameters.`);
