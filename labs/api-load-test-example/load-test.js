import http from 'k6/http';
import { sleep, check, fail } from 'k6';
import { correlatedRequest, reportFailedRequest } from './k6-correlation.js';

const baseUrl = (__ENV.K6_BASE_URL || 'http://127.0.0.1:18080').replace(/\/$/, '');
const healthUrl = `${baseUrl}/health`;
// Prefer a slashless ENDPOINT value on Git Bash for Windows. MSYS otherwise treats a value such
// as /v1/admin-report as a filesystem path and rewrites it before k6 receives the argument.
const endpoint = `/${(__ENV.ENDPOINT || 'v1/data-endpoint').replace(/^\/+/, '')}`;

export const options = {
  setupTimeout: __ENV.SETUP_TIMEOUT || '3m',
  stages: [
    { duration: '30s', target: 200 },  // Ramp to 200 virtual users
    { duration: '2m', target: 1000 },  // Scale up to full load of 1,000 users
    { duration: '30s', target: 0 },    // Cool down
  ],
  thresholds: {
    http_req_failed: ['rate<0.01'],    // Under 1% failures
    http_req_duration: ['p(95)<150'],  // 95% of queries must stick near target 100ms
  },
};

export function setup() {
  const attempts = Number(__ENV.READINESS_ATTEMPTS || 60);
  const intervalSeconds = Number(__ENV.READINESS_INTERVAL_SECONDS || 2);

  console.log(`Checking API readiness at ${healthUrl}`);

  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    const request = correlatedRequest('api-readiness', {}, 'setup');
    const response = http.get(healthUrl, { ...request.params, timeout: '2s' });

    if (response.status === 200) {
      console.log(`API is ready at ${healthUrl}`);
      return;
    }

    if (attempt === 1 || attempt % 5 === 0) {
      const server = response.headers.Server || response.headers.server || 'unknown';
      const bodyPreview = String(response.body || '').replace(/[\r\n]+/g, ' ').slice(0, 160);
      console.warn(
        `API readiness attempt ${attempt}/${attempts} returned status ${response.status}` +
          ` from server ${server}${response.error ? `: ${response.error}` : ''}` +
          `${bodyPreview ? `. Response preview: ${bodyPreview}` : ''}`,
      );
      if (response.status > 0) {
        console.warn(
          'A different process may own this host/port if the responding server is not Kestrel.',
        );
      }
    }

    if (attempt < attempts) {
      sleep(intervalSeconds);
    }
  }

  fail(
    `API did not become ready at ${healthUrl}. Start the lab with ` +
      '`docker compose up --build -d` and inspect `docker compose logs api-service`.',
  );
}

export default function () {
  const scenario = 'connection-pool';
  const request = correlatedRequest(scenario, { endpoint });
  const res = http.get(`${baseUrl}${endpoint}`, request.params);

  const passed = check(res, {
    'status is 200': (r) => r.status === 200,
  });

  if (!passed) {
    reportFailedRequest(res, request, scenario);
  }

  sleep(0.1);
}
