// Production assets and a disposable, in-memory API. No development DB/provider calls.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const root = path.resolve(__dirname, '../dist/dishhive-web/browser');
const id = '11111111-1111-1111-1111-111111111111';
let state = { outage: false, version: 1, imageVersion: 1, item: 'carrots', writes: 0, images: [] };
const index = () => fs.readFileSync(path.join(root, 'index.html'), 'utf8').replace('<html', `<html data-pwa-version="${state.version}"`);
const json = (res, body, status = 200) => { res.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' }); res.end(JSON.stringify(body)); };
const recipe = () => ({ id, title: 'PWA fixture soup', servings: 4, tags: [], cookbookIds: [], hasLocalImage: true,
  imageUrl: `/api/recipes/${id}/image?v=${state.imageVersion}`, ingredients: [], steps: [],
  dietaryFacts: { contains: [], status: 2, recipeRevision: 1, assessedRevision: 1, isCurrent: true, history: [] },
  createdAt: '2026-11-13T12:00:00Z', updatedAt: '2026-11-13T12:00:00Z' });
http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://127.0.0.1:4301');
  const route = url.pathname.toLowerCase();
  if (route === '/__fixture') {
    if (req.method === 'POST') {
      let text = ''; for await (const chunk of req) text += chunk;
      Object.assign(state, JSON.parse(text));
    }
    return json(res, state);
  }
  if (route.startsWith('/api/')) {
    if (state.outage) return json(res, { title: 'Fixture API unavailable' }, 503);
    if (req.method !== 'GET') {
      state.writes++;
      if (route === '/api/plannedmeals') {
        let text = ''; for await (const chunk of req) text += chunk;
        state.item = 'beans';
        return json(res, { ...JSON.parse(text), id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', attendeeIds: [], ratings: [] }, 201);
      }
      return json(res, {}, 404);
    }
    if (route.endsWith('/image')) {
      state.images.push(url.searchParams.get('v'));
      res.writeHead(200, { 'Content-Type': 'image/svg+xml', 'Cache-Control': 'no-cache' });
      return res.end(`<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20"><rect width="20" height="20" fill="${state.imageVersion === 1 ? 'red' : 'blue'}"/></svg>`);
    }
    if (route === '/api/settings/preferences') return json(res, { measurementSystem: 0, firstDayOfWeek: 0,
      preferredLanguage: 'en', translateImportedRecipes: false, householdTimeZone: 'Europe/Brussels',
      supportedLanguages: [{ code: 'en', displayName: 'English' }, { code: 'nl', displayName: 'Nederlands' }] });
    if (route === '/api/settings/languages') return json(res, [{ code: 'en', displayName: 'English' }, { code: 'nl', displayName: 'Nederlands' }]);
    if (route === '/api/onboarding') return json(res, { shouldShow: false });
    if (route === '/api/shoppinglist') return json(res, { from: '2026-11-09', to: '2026-11-15',
      items: [{ name: state.item, quantity: 2, unit: 'g', sourceRecipes: ['PWA fixture soup'] }], reminders: [] });
    if (route === `/api/recipes/${id}`) return json(res, recipe());
    if (route === '/api/recipes/page') return json(res, { items: [recipe()], totalCount: 1, page: 1, pageSize: 24 });
    if (route === '/api/statistics/dishes') return json(res, { dishes: [], unspecifiedCount: 0 });
    return json(res, []);
  }
  if (route === '/ngsw.json') {
    const manifest = JSON.parse(fs.readFileSync(path.join(root, 'ngsw.json'), 'utf8'));
    manifest.hashTable['/index.html'] = crypto.createHash('sha1').update(index()).digest('hex');
    return json(res, manifest);
  }
  const file = path.resolve(root, '.' + url.pathname);
  if (!file.startsWith(root + path.sep) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) {
    res.writeHead(200, { 'Content-Type': 'text/html', 'Cache-Control': 'no-store' }); return res.end(index());
  }
  if (route === '/index.html') { res.writeHead(200, { 'Content-Type': 'text/html', 'Cache-Control': 'no-store' }); return res.end(index()); }
  const types = { '.js': 'text/javascript', '.json': 'application/json', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.webmanifest': 'application/manifest+json' };
  res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
  fs.createReadStream(file).pipe(res);
}).listen(4301, '127.0.0.1');
