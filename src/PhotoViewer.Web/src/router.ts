type RouteHandler = (params: Record<string, string>) => void;

interface Route {
  pattern: RegExp;
  handler: RouteHandler;
  paramNames: string[];
}

const routes: Route[] = [];
let currentCleanup: (() => void) | null = null;

export function addRoute(path: string, handler: RouteHandler): void {
  const paramNames: string[] = [];
  const pattern = path.replace(/:(\w+)/g, (_, name) => {
    paramNames.push(name);
    return '([^/]+)';
  });
  routes.push({
    pattern: new RegExp(`^${pattern}$`),
    handler,
    paramNames,
  });
}

export function navigate(path: string): void {
  window.location.hash = `#${path}`;
}

export function setCleanup(fn: () => void): void {
  currentCleanup = fn;
}

function handleRoute(): void {
  const hash = window.location.hash.slice(1) || '/login';

  if (currentCleanup) {
    currentCleanup();
    currentCleanup = null;
  }

  for (const route of routes) {
    const match = hash.match(route.pattern);
    if (match) {
      const params: Record<string, string> = {};
      route.paramNames.forEach((name, i) => {
        params[name] = match[i + 1];
      });
      route.handler(params);
      return;
    }
  }

  // Default: login
  navigate('/login');
}

export function initRouter(): void {
  window.addEventListener('hashchange', handleRoute);
  handleRoute();
}
