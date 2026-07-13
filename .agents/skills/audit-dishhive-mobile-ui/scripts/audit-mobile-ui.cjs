const fs = require('node:fs');
const path = require('node:path');

const { chromium } = require(process.env.PLAYWRIGHT_CORE_PATH || 'playwright-core');

function argument(name, fallback) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : fallback;
}

function discoverRoutes() {
  const routesFile = path.resolve('src/dishhive-web/src/app/app.routes.ts');
  const source = fs.readFileSync(routesFile, 'utf8');
  const declaredRoutes = [...source.matchAll(/path:\s*['"]([^'"]*)['"]/g)]
    .map((match) => match[1])
    .filter((route) => route !== '**');
  return {
    staticRoutes: [...new Set(declaredRoutes.filter((route) => !route.includes(':')))],
    parameterizedRoutes: [...new Set(declaredRoutes.filter((route) => route.includes(':')))],
  };
}

function routeName(route) {
  return route ? route.replaceAll('/', '-') : 'week-planner';
}

async function geometry(page) {
  return page.evaluate(() => {
    const viewportWidth = window.innerWidth;
    const ignored = (element) => element.closest('.mat-drawer:not(.mat-drawer-opened)');
    const isInsideHorizontalScroller = (element) => {
      for (let parent = element.parentElement; parent; parent = parent.parentElement) {
        const overflow = getComputedStyle(parent).overflowX;
        if (overflow === 'auto' || overflow === 'scroll') return true;
      }
      return false;
    };
    const outsideViewport = [...document.querySelectorAll('body *')]
      .filter((element) => {
        if (ignored(element) || isInsideHorizontalScroller(element)) return false;
        const style = getComputedStyle(element);
        if (style.display === 'none' || style.visibility === 'hidden') return false;
        const rect = element.getBoundingClientRect();
        return rect.width > 0 && (rect.right > viewportWidth + 1 || rect.left < -1);
      })
      .slice(0, 20)
      .map((element) => {
        const rect = element.getBoundingClientRect();
        return {
          tag: element.tagName.toLowerCase(),
          classes: String(element.className || '').slice(0, 160),
          left: Math.round(rect.left),
          right: Math.round(rect.right),
          clientWidth: element.clientWidth,
          scrollWidth: element.scrollWidth,
        };
      });

    return {
      viewportWidth,
      documentWidth: document.documentElement.scrollWidth,
      bodyWidth: document.body.scrollWidth,
      outsideViewport,
    };
  });
}

async function dialogGeometry(page) {
  return page.evaluate(() =>
    [...document.querySelectorAll('.mat-mdc-dialog-surface, .mat-mdc-dialog-content')].map(
      (element) => ({
        classes: String(element.className),
        clientWidth: element.clientWidth,
        scrollWidth: element.scrollWidth,
        clientHeight: element.clientHeight,
        scrollHeight: element.scrollHeight,
        overflowX: getComputedStyle(element).overflowX,
        overflowY: getComputedStyle(element).overflowY,
      })
    )
  );
}

async function main() {
  const baseUrl = argument('base-url', 'http://127.0.0.1:4300').replace(/\/$/, '');
  const output = path.resolve(
    argument('output', path.join(process.env.TEMP, 'dishhive-mobile-audit'))
  );
  const width = Number(argument('width', '360'));
  const height = Number(argument('height', '800'));
  const discovered = discoverRoutes();
  const additionalRoutes = argument('additional-routes', '')
    .split(',')
    .map((route) => route.trim().replace(/^\//, ''))
    .filter(Boolean);
  const routes = [...new Set([...discovered.staticRoutes, ...additionalRoutes])];
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  const page = await browser.newPage({ viewport: { width, height } });
  const report = {
    baseUrl,
    viewport: { width, height },
    skippedRouteTemplates: discovered.parameterizedRoutes,
    routes: [],
    dialogs: [],
  };

  fs.mkdirSync(output, { recursive: true });

  for (const route of routes) {
    const entry = { route: `/${route}` };
    try {
      const response = await page.goto(`${baseUrl}/${route}`, { waitUntil: 'networkidle' });
      entry.status = response ? response.status() : null;
      entry.geometry = await geometry(page);
      entry.screenshot = `${routeName(route)}.png`;
      await page.screenshot({ path: path.join(output, entry.screenshot), fullPage: true });
    } catch (error) {
      entry.error = error.message;
    }
    report.routes.push(entry);
  }

  const probes = [
    { name: 'suggest-week-loading', route: '', role: 'button', accessibleName: /suggest week/i },
    { name: 'plan-meal', route: '', role: 'button', accessibleName: /plan/i },
  ];

  for (const probe of probes) {
    const entry = { name: probe.name, route: `/${probe.route}` };
    try {
      await page.goto(`${baseUrl}/${probe.route}`, { waitUntil: 'networkidle' });
      await page.getByRole(probe.role, { name: probe.accessibleName }).first().click();
      await page.locator('.mat-mdc-dialog-surface').waitFor({ timeout: 3000 });
      await page.waitForTimeout(250);
      entry.geometry = await dialogGeometry(page);
      entry.screenshot = `dialog-${probe.name}.png`;
      await page.screenshot({ path: path.join(output, entry.screenshot), fullPage: true });
      await page.keyboard.press('Escape');
    } catch (error) {
      entry.error = error.message;
    }
    report.dialogs.push(entry);
  }

  await browser.close();
  fs.writeFileSync(
    path.join(output, 'audit-report.json'),
    `${JSON.stringify(report, null, 2)}\n`
  );
  process.stdout.write(`${JSON.stringify(report, null, 2)}\n`);
}

main().catch((error) => {
  process.stderr.write(`${error.stack || error.message}\n`);
  process.exitCode = 1;
});
