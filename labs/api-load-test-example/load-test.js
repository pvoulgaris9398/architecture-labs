import http from 'k6/http';
import { sleep, check } from 'k6';

const baseUrl = (__ENV.BASE_URL || 'http://localhost:8080').replace(/\/$/, '');
// Prefer a slashless ENDPOINT value on Git Bash for Windows. MSYS otherwise treats a value such
// as /v1/admin-report as a filesystem path and rewrites it before k6 receives the argument.
const endpoint = `/${(__ENV.ENDPOINT || 'v1/data-endpoint').replace(/^\/+/, '')}`;

export const options = {
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

export default function () {
  const res = http.get(`${baseUrl}${endpoint}`, {
    tags: { scenario: 'connection-pool', endpoint },
  });

  check(res, {
    'status is 200': (r) => r.status === 200,
  });

  sleep(0.1);
}
