/**
 * Table scan vs index load test
 *
 * Run 1 — baseline (no index, full table scan):
 *   bash run-k6.sh load-test-scan.js
 *
 * Between runs, add the index:
 *   curl -X POST http://127.0.0.1:18080/v1/add-index
 *
 * Run 2 — with index:
 *   bash run-k6.sh load-test-scan.js
 *
 * To reset back to no index:
 *   curl -X POST http://127.0.0.1:18080/v1/drop-index
 */

import http from 'k6/http';
import { sleep, check, fail } from 'k6';

const baseUrl = (__ENV.K6_BASE_URL || 'http://127.0.0.1:18080').replace(/\/$/, '');
const endpoint = '/v1/orders/by-customer';
const url = `${baseUrl}${endpoint}`;
const healthUrl = `${baseUrl}/health`;

export const options = {
  setupTimeout: __ENV.SETUP_TIMEOUT || '3m',
  stages: [
    { duration: '30s', target: 50 },   // Ramp up gently
    { duration: '2m',  target: 200 },  // Sustained load
    { duration: '30s', target: 0 },    // Cool down
  ],
  thresholds: {
    http_req_failed:   ['rate<0.01'],   // Under 1% errors
    http_req_duration: ['p(95)<500'],   // 500ms threshold — scan will likely breach this
  },
};

export function setup() {
  const attempts = Number(__ENV.READINESS_ATTEMPTS || 60);
  const intervalSeconds = Number(__ENV.READINESS_INTERVAL_SECONDS || 2);

  console.log(`Checking API readiness at ${healthUrl}`);

  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    const response = http.get(healthUrl, {
      timeout: '2s',
      tags: { scenario: 'api-readiness' },
    });

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
  const res = http.get(url, {
    tags: { scenario: 'table-scan-comparison', endpoint },
  });

  check(res, {
    'status is 200': (r) => r.status === 200,
  });

  sleep(0.1);
}
