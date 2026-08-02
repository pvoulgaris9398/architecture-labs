/**
 * Table scan vs index load test
 *
 * Run 1 — baseline (no index, full table scan):
 *   k6 run load-test-scan.js
 *
 * Between runs, add the index:
 *   curl -X POST http://localhost:8080/v1/add-index
 *
 * Run 2 — with index:
 *   k6 run load-test-scan.js
 *
 * To reset back to no index:
 *   curl -X POST http://localhost:8080/v1/drop-index
 */

import http from 'k6/http';
import { sleep, check } from 'k6';

const baseUrl = (__ENV.BASE_URL || 'http://localhost:8080').replace(/\/$/, '');
const endpoint = '/v1/orders/by-customer';
const url = `${baseUrl}${endpoint}`;

export const options = {
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

export default function () {
  const res = http.get(url, {
    tags: { scenario: 'table-scan-comparison', endpoint },
  });

  check(res, {
    'status is 200': (r) => r.status === 200,
  });

  sleep(0.1);
}

