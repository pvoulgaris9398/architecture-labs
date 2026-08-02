import crypto from 'k6/crypto';
import exec from 'k6/execution';

export const testId = __ENV.K6_TEST_ID || 'direct-k6';

const maxDiagnosticVus = boundedInteger(__ENV.K6_DIAGNOSTIC_VUS, 10, 0, 100);
const diagnosticsPerVu = boundedInteger(__ENV.K6_DIAGNOSTICS_PER_VU, 1, 0, 10);
let diagnosticsEmitted = 0;

function boundedInteger(value, fallback, minimum, maximum) {
  const parsed = Number(value);
  if (!Number.isInteger(parsed) || parsed < minimum || parsed > maximum) {
    return fallback;
  }

  return parsed;
}

function randomHex(byteCount) {
  let value;

  do {
    value = Array.from(new Uint8Array(crypto.randomBytes(byteCount)))
      .map((byte) => byte.toString(16).padStart(2, '0'))
      .join('');
  } while (/^0+$/.test(value));

  return value;
}

function responseHeader(response, expectedName) {
  const expected = expectedName.toLowerCase();

  for (const [name, value] of Object.entries(response.headers || {})) {
    if (name.toLowerCase() === expected) {
      return value;
    }
  }

  return undefined;
}

export function correlatedRequest(scenario, tags = {}, executionContext = 'vu') {
  const requestPrefix = executionContext === 'setup'
    ? 'k6-setup'
    : `k6-${exec.vu.idInTest}-${exec.vu.iterationInInstance}`;
  const requestId = `${requestPrefix}-${randomHex(6)}`;
  const traceId = randomHex(16);

  return {
    requestId,
    traceId,
    params: {
      headers: {
        traceparent: `00-${traceId}-${randomHex(8)}-01`,
        'X-Request-ID': requestId,
        'X-Test-ID': testId,
        'X-Test-Scenario': scenario,
      },
      tags: { ...tags, scenario, test_id: testId },
    },
  };
}

export function reportFailedRequest(response, request, scenario) {
  if (
    diagnosticsEmitted >= diagnosticsPerVu ||
    exec.vu.idInTest > maxDiagnosticVus
  ) {
    return;
  }

  diagnosticsEmitted += 1;
  const responseRequestId = responseHeader(response, 'X-Request-ID');
  const responseTraceId = responseHeader(response, 'X-Trace-ID');

  console.error(
    'k6 request failed' +
      ` status=${response.status}` +
      ` test_id=${testId}` +
      ` scenario=${scenario}` +
      ` request_id=${responseRequestId || request.requestId}` +
      ` trace_id=${responseTraceId || request.traceId}` +
      `${response.error ? ` error=${JSON.stringify(response.error)}` : ''}`,
  );
}
