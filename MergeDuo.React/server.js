import { createServer, request as httpRequest } from 'node:http';
import { request as httpsRequest } from 'node:https';
import { createReadStream, existsSync, statSync } from 'node:fs';
import { dirname, extname, join, normalize } from 'node:path';
import { pipeline } from 'node:stream';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const staticDir = resolveStaticRoot();
const indexPath = join(staticDir, 'index.html');
const port = Number(process.env.PORT || 8080);

const targets = {
  identity: baseUrl('IDENTITY_API_BASE_URL', 'VITE_IDENTITY_API_BASE_URL'),
  profile: baseUrl('PROFILE_API_BASE_URL', 'VITE_PROFILE_API_BASE_URL'),
  partnership: baseUrl('PARTNERSHIP_API_BASE_URL', 'VITE_PARTNERSHIP_API_BASE_URL'),
  cards: baseUrl('CARDS_API_BASE_URL', 'VITE_CARDS_API_BASE_URL'),
  fixedRules: baseUrl('FIXED_RULES_API_BASE_URL', 'VITE_FIXED_RULES_API_BASE_URL'),
  transactions: baseUrl('TRANSACTIONS_API_BASE_URL', 'VITE_TRANSACTIONS_API_BASE_URL'),
  aggregates: baseUrl('AGGREGATES_API_BASE_URL', 'VITE_AGGREGATES_API_BASE_URL'),
};

const contentTypes = {
  '.css': 'text/css; charset=utf-8',
  '.html': 'text/html; charset=utf-8',
  '.ico': 'image/x-icon',
  '.js': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
  '.webmanifest': 'application/manifest+json; charset=utf-8',
};

createServer((req, res) => {
  const requestUrl = new URL(req.url || '/', requestOrigin(req));
  res.setHeader('x-mergeduo-runtime', 'node-proxy');

  if (requestUrl.pathname === '/__health') {
    sendJson(res, 200, {
      code: 'ok',
      staticRoot: staticDir === __dirname ? 'wwwroot' : 'dist',
      index: existsSync(indexPath),
      proxyTargets: proxyTargetHealth(),
    });
    return;
  }

  const proxyTarget = routeProxy(requestUrl);

  if (proxyTarget) {
    proxy(req, res, requestUrl, proxyTarget);
    return;
  }

  if (isApiPath(requestUrl.pathname)) {
    sendJson(res, 404, {
      code: 'proxy_route_not_found',
      detail: `No proxy route is configured for ${requestUrl.pathname}.`,
    });
    return;
  }

  serveStatic(req, res, requestUrl.pathname);
}).listen(port, () => {
  console.log(`MergeDuo React server listening on ${port} using ${staticDir}`);
});

function baseUrl(primaryName, viteName) {
  const value = process.env[primaryName] || process.env[viteName] || '';
  return value.replace(/\/+$/, '');
}

function requestOrigin(req) {
  const host = req.headers.host || `localhost:${port}`;
  const forwardedProto = String(req.headers['x-forwarded-proto'] || '').split(',')[0].trim();
  const proto = forwardedProto || (host.startsWith('localhost') || host.startsWith('127.0.0.1') ? 'http' : 'https');
  return `${proto}://${host}`;
}

function proxyTargetHealth() {
  return Object.fromEntries(
    Object.entries(targets).map(([name, value]) => [name, Boolean(value)]),
  );
}

function routeProxy(requestUrl) {
  const path = requestUrl.pathname;

  if (path === '/auth' || path.startsWith('/auth/')) {
    return { base: targets.identity, path };
  }

  if (path === '/users' || path.startsWith('/users/')) {
    return { base: targets.identity, path };
  }

  if (path === '/.well-known' || path.startsWith('/.well-known/')) {
    return { base: targets.identity, path };
  }

  if (path === '/api/profile' || path.startsWith('/api/profile/')) {
    return { base: targets.profile, path: stripPrefix(path, '/api/profile') };
  }

  if (path === '/api/partnership' || path.startsWith('/api/partnership/')) {
    return { base: targets.partnership, path: stripPrefix(path, '/api/partnership') };
  }

  if (path === '/api/cards' || path.startsWith('/api/cards/')) {
    return { base: targets.cards, path: stripPrefix(path, '/api') };
  }

  if (path === '/api/fixed-rules' || path.startsWith('/api/fixed-rules/')) {
    return { base: targets.fixedRules, path: stripPrefix(path, '/api') };
  }

  if (path === '/api/transactions' || path.startsWith('/api/transactions/')) {
    return { base: targets.transactions, path: stripPrefix(path, '/api') };
  }

  if (path === '/api/aggregates' || path.startsWith('/api/aggregates/')) {
    return { base: targets.aggregates, path: stripPrefix(path, '/api') };
  }

  return null;
}

function stripPrefix(path, prefix) {
  const suffix = path.slice(prefix.length);
  return suffix.startsWith('/') ? suffix : suffix ? `/${suffix}` : '/';
}

function proxy(req, res, requestUrl, target) {
  if (!target.base) {
    sendJson(res, 503, {
      code: 'proxy_target_not_configured',
      detail: `No upstream target is configured for ${requestUrl.pathname}.`,
    });
    return;
  }

  const upstream = new URL(`${target.path}${requestUrl.search}`, target.base);
  const headers = proxyHeaders(req, upstream);
  const transport = upstream.protocol === 'http:' ? httpRequest : httpsRequest;
  const proxyReq = transport(upstream, {
    method: req.method,
    headers,
  }, (proxyRes) => {
    res.writeHead(proxyRes.statusCode || 502, proxyRes.headers);
    pipeline(proxyRes, res, (err) => {
      if (err) console.error('Proxy response stream failed', err);
    });
  });

  proxyReq.on('error', (err) => {
    console.error(`Proxy request failed for ${requestUrl.pathname}`, err);
    if (!res.headersSent) {
      sendJson(res, 502, {
        code: 'upstream_unavailable',
        detail: 'The upstream service did not respond.',
      });
    } else {
      res.destroy(err);
    }
  });

  pipeline(req, proxyReq, (err) => {
    if (err && !res.destroyed) console.error('Proxy request stream failed', err);
  });
}

function proxyHeaders(req, upstream) {
  const headers = { ...req.headers };
  for (const name of [
    'connection',
    'keep-alive',
    'proxy-authenticate',
    'proxy-authorization',
    'te',
    'trailer',
    'transfer-encoding',
    'upgrade',
  ]) {
    delete headers[name];
  }

  const forwardedFor = headers['x-forwarded-for'];
  const remoteAddress = req.socket.remoteAddress || '';
  headers.host = upstream.host;
  headers['x-forwarded-host'] = req.headers.host || '';
  headers['x-forwarded-proto'] = 'https';
  headers['x-forwarded-for'] = forwardedFor
    ? `${forwardedFor}, ${remoteAddress}`
    : remoteAddress;
  return headers;
}

function serveStatic(req, res, pathname) {
  if (req.method !== 'GET' && req.method !== 'HEAD') {
    sendJson(res, 405, { code: 'method_not_allowed', detail: 'Method not allowed.' });
    return;
  }

  const filePath = resolveStaticPath(pathname);
  if (!filePath) {
    sendJson(res, 400, { code: 'invalid_path', detail: 'Invalid static file path.' });
    return;
  }

  if (isForbiddenStaticRequest(filePath.relative)) {
    sendJson(res, 404, { code: 'not_found', detail: 'Not found.' });
    return;
  }

  const resolvedPath = existsSync(filePath.absolute) && statSync(filePath.absolute).isFile()
    ? filePath.absolute
    : indexPath;

  if (!existsSync(resolvedPath)) {
    sendJson(res, 500, { code: 'static_index_missing', detail: 'Static index.html was not found.' });
    return;
  }

  res.writeHead(200, {
    'content-type': contentTypes[extname(resolvedPath)] || 'application/octet-stream',
  });

  if (req.method === 'HEAD') {
    res.end();
    return;
  }

  createReadStream(resolvedPath).pipe(res);
}

function resolveStaticPath(pathname) {
  try {
    const decoded = decodeURIComponent(pathname);
    const normalized = normalize(decoded).replace(/^(\.\.[/\\])+/, '');
    const relative = normalized.replace(/^[/\\]+/, '') || 'index.html';
    const absolute = join(staticDir, relative);
    return absolute.startsWith(staticDir) ? { absolute, relative } : null;
  } catch {
    return null;
  }
}

function resolveStaticRoot() {
  const distDir = join(__dirname, 'dist');
  return existsSync(join(distDir, 'index.html')) ? distDir : __dirname;
}

function isForbiddenStaticRequest(relativePath) {
  const firstSegment = relativePath.split(/[\\/]/)[0];
  const fileName = relativePath.split(/[\\/]/).pop() || '';

  return (
    firstSegment === 'node_modules' ||
    firstSegment === '_del_node_modules' ||
    firstSegment === 'src' ||
    firstSegment === 'public' ||
    fileName === 'server.js' ||
    fileName === 'package.json' ||
    fileName === 'package-lock.json' ||
    fileName === 'node_modules.tar.gz' ||
    fileName === 'oryx-manifest.toml' ||
    fileName === 'README.md' ||
    fileName.endsWith('.ts') ||
    fileName.endsWith('.tsx') ||
    fileName.endsWith('.config.js')
  );
}

function isApiPath(pathname) {
  return (
    pathname === '/api' ||
    pathname.startsWith('/api/') ||
    pathname === '/auth' ||
    pathname.startsWith('/auth/') ||
    pathname === '/users' ||
    pathname.startsWith('/users/') ||
    pathname === '/.well-known' ||
    pathname.startsWith('/.well-known/')
  );
}

function sendJson(res, status, body) {
  res.writeHead(status, { 'content-type': 'application/json; charset=utf-8' });
  res.end(JSON.stringify({ status, ...body }));
}
