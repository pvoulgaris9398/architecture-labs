import { useEffect, useRef, useState } from 'react';

type ConnectionState = 'disconnected' | 'connecting' | 'connected';
type LogKind = 'system' | 'sent' | 'received' | 'http' | 'error';

interface LogEntry {
  id: number;
  time: string;
  kind: LogKind;
  label: string;
  payload?: string;
  route?: LogRoute;
}

interface LogRoute {
  from: string;
  to: string;
  endpoint: string;
  via?: string;
}

interface EventMessage {
  Type?: string;
  type?: string;
  Sequence?: number;
  sequence?: number;
  Message?: string;
  message?: string;
}

interface BurstPublishResult {
  Count?: number;
  count?: number;
  FirstSequence?: number;
  firstSequence?: number;
  LastSequence?: number;
  lastSequence?: number;
}

interface QueueMetrics {
  depth: number;
  capacity: number;
  waits: number;
  averageDelayMilliseconds: number;
}

const labels: Record<LogKind, string> = {
  system: 'System',
  sent: 'Sent',
  received: 'Received',
  http: 'HTTP',
  error: 'Error',
};

function formatPayload(value: unknown) {
  return typeof value === 'string' ? value : JSON.stringify(value, null, 2);
}

function sumMetric(metrics: string, name: string, connectionMode?: string) {
  return metrics
    .split('\n')
    .filter((line) => line.startsWith(name))
    .filter((line) => !connectionMode || line.includes(`connection_mode="${connectionMode}"`))
    .reduce((sum, line) => {
      const value = Number(line.trim().split(/\s+/).at(-1));
      return sum + (Number.isFinite(value) ? value : 0);
    }, 0);
}

export default function WebSocketWalkthrough() {
  const socketRef = useRef<WebSocket | null>(null);
  const consoleBodyRef = useRef<HTMLDivElement | null>(null);
  const nextLogId = useRef(1);
  const [connectionState, setConnectionState] = useState<ConnectionState>('disconnected');
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const [message, setMessage] = useState('Hello from the lab');
  const [latestSequence, setLatestSequence] = useState(0);
  const [acknowledgedSequence, setAcknowledgedSequence] = useState<number | null>(null);
  const [replayFrom, setReplayFrom] = useState(0);
  const [requestPending, setRequestPending] = useState(false);
  const [slowMode, setSlowMode] = useState(false);
  const [sendDelayMilliseconds, setSendDelayMilliseconds] = useState(25);
  const [burstCount, setBurstCount] = useState(750);
  const [burstPending, setBurstPending] = useState(false);
  const [queueMetrics, setQueueMetrics] = useState<QueueMetrics | null>(null);
  const browserNode = `Browser UI (${window.location.host})`;
  const apiEndpoint = `${window.location.origin}/api/events`;
  const socketProtocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
  const socketEndpoint = `${socketProtocol}//${window.location.host}/ws`;
  const proxyNode = `Same-origin proxy (${window.location.host})`;

  const addLog = (kind: LogKind, label: string, payload?: unknown, route?: LogRoute) => {
    const entry: LogEntry = {
      id: nextLogId.current++,
      time: new Date().toLocaleTimeString(),
      kind,
      label,
      payload: payload === undefined ? undefined : formatPayload(payload),
      route,
    };

    setLogs((current) => [...current.slice(-99), entry]);
  };

  const captureEventSequence = (value: EventMessage) => {
    const type = value.Type ?? value.type;
    const sequence = value.Sequence ?? value.sequence;

    if (type?.toLowerCase() === 'event' && typeof sequence === 'number') {
      setLatestSequence((current) => Math.max(current, sequence));
    }
  };

  const connect = () => {
    if (socketRef.current?.readyState === WebSocket.OPEN || connectionState === 'connecting') {
      return;
    }

    const connectionEndpoint = slowMode
      ? `${socketEndpoint}?sendDelayMs=${sendDelayMilliseconds}`
      : socketEndpoint;
    const socket = new WebSocket(connectionEndpoint);

    socketRef.current = socket;
    setConnectionState('connecting');
    addLog('system', 'Opening WebSocket connection', undefined, {
      from: browserNode,
      to: 'WebSocket server',
      endpoint: connectionEndpoint,
      via: proxyNode,
    });

    socket.addEventListener('open', () => {
      setConnectionState('connected');
      addLog('system', 'Connection established', undefined, {
        from: 'WebSocket server',
        to: browserNode,
        endpoint: connectionEndpoint,
        via: proxyNode,
      });
    });

    socket.addEventListener('message', (event) => {
      try {
        const parsed = JSON.parse(String(event.data)) as EventMessage;
        captureEventSequence(parsed);
        addLog('received', parsed.Type ?? parsed.type ?? 'Message received', parsed, {
          from: 'WebSocket server',
          to: browserNode,
          endpoint: socketEndpoint,
          via: proxyNode,
        });
      } catch {
        addLog('received', 'Text message received', String(event.data), {
          from: 'WebSocket server',
          to: browserNode,
          endpoint: socketEndpoint,
          via: proxyNode,
        });
      }
    });

    socket.addEventListener('error', () => {
      addLog('error', 'WebSocket connection error', undefined, {
        from: 'WebSocket server',
        to: browserNode,
        endpoint: socketEndpoint,
        via: proxyNode,
      });
    });

    socket.addEventListener('close', (event) => {
      setConnectionState('disconnected');
      socketRef.current = null;
      addLog('system', 'Connection closed', {
        code: event.code,
        reason: event.reason || 'No reason supplied',
      }, {
        from: 'WebSocket server',
        to: browserNode,
        endpoint: socketEndpoint,
        via: proxyNode,
      });
    });
  };

  const disconnect = () => {
    socketRef.current?.close(1000, 'Walkthrough disconnect');
  };

  const send = (payload: object, label: string) => {
    const socket = socketRef.current;

    if (!socket || socket.readyState !== WebSocket.OPEN) {
      addLog('error', `${label} was not sent`, 'Connect the WebSocket first.', {
        from: browserNode,
        to: 'WebSocket server',
        endpoint: socketEndpoint,
        via: proxyNode,
      });
      return;
    }

    socket.send(JSON.stringify(payload));
    addLog('sent', label, payload, {
      from: browserNode,
      to: 'WebSocket server',
      endpoint: socketEndpoint,
      via: proxyNode,
    });
  };

  const publishEvent = async () => {
    if (!message.trim()) {
      return;
    }

    setRequestPending(true);
    addLog('http', 'Publish request', { message: message.trim() }, {
      from: browserNode,
      to: 'HTTP event API',
      endpoint: `POST ${apiEndpoint}`,
      via: proxyNode,
    });

    try {
      const response = await fetch('/api/events', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message: message.trim() }),
      });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const event = (await response.json()) as EventMessage;
      const sequence = event.Sequence ?? event.sequence;
      if (typeof sequence === 'number') {
        setLatestSequence((current) => Math.max(current, sequence));
      }
      addLog('http', 'Event published', event, {
        from: 'HTTP event API',
        to: browserNode,
        endpoint: `${response.status} POST ${apiEndpoint}`,
        via: proxyNode,
      });
    } catch (error) {
      addLog('error', 'Publish failed', error instanceof Error ? error.message : String(error), {
        from: 'HTTP event API',
        to: browserNode,
        endpoint: `POST ${apiEndpoint}`,
        via: proxyNode,
      });
    } finally {
      setRequestPending(false);
    }
  };

  const publishBurst = async () => {
    setBurstPending(true);
    addLog('http', 'Controlled burst started', {
      count: burstCount,
      connectionMode: slowMode ? 'slow' : 'normal',
      sendDelayMilliseconds: slowMode ? sendDelayMilliseconds : 0,
    }, {
      from: browserNode,
      to: 'HTTP event API',
      endpoint: `POST ${apiEndpoint}/burst`,
      via: proxyNode,
    });

    try {
      const response = await fetch('/api/events/burst', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ count: burstCount, messagePrefix: 'slow-client-test' }),
      });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${await response.text()}`);
      }

      const result = (await response.json()) as BurstPublishResult;
      const lastSequence = result.LastSequence ?? result.lastSequence;
      if (typeof lastSequence === 'number') {
        setLatestSequence((current) => Math.max(current, lastSequence));
      }
      addLog('http', 'Controlled burst enqueued', result, {
        from: 'HTTP event API',
        to: browserNode,
        endpoint: `${response.status} POST ${apiEndpoint}/burst`,
        via: proxyNode,
      });
    } catch (error) {
      addLog('error', 'Controlled burst failed', error instanceof Error ? error.message : String(error), {
        from: 'HTTP event API',
        to: browserNode,
        endpoint: `POST ${apiEndpoint}/burst`,
        via: proxyNode,
      });
    } finally {
      setBurstPending(false);
    }
  };

  const acknowledgeLatest = () => {
    if (latestSequence < 1) {
      addLog('error', 'Nothing to acknowledge', 'Publish or replay an event first.', {
        from: browserNode,
        to: 'WebSocket server',
        endpoint: socketEndpoint,
        via: proxyNode,
      });
      return;
    }

    send({ type: 'ack', sequence: latestSequence }, `Acknowledge sequence ${latestSequence}`);

    if (socketRef.current?.readyState === WebSocket.OPEN) {
      setAcknowledgedSequence(latestSequence);
    }
  };

  useEffect(() => {
    return () => {
      socketRef.current?.close(1000, 'Walkthrough closed');
    };
  }, []);

  useEffect(() => {
    const consoleBody = consoleBodyRef.current;
    if (!consoleBody || logs.length === 0) {
      return;
    }

    const frame = window.requestAnimationFrame(() => {
      const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
      consoleBody.scrollTo({
        top: consoleBody.scrollHeight,
        behavior: reduceMotion ? 'auto' : 'smooth',
      });
    });

    return () => window.cancelAnimationFrame(frame);
  }, [logs.length]);

  useEffect(() => {
    if (connectionState !== 'connected') {
      return;
    }

    let cancelled = false;

    const refreshMetrics = async () => {
      try {
        const response = await fetch('/metrics', { cache: 'no-store' });
        if (!response.ok) {
          return;
        }

        const metrics = await response.text();
        const mode = slowMode ? 'slow' : 'normal';
        const delaySum = sumMetric(
          metrics,
          'websocket_outbound_queue_delay_milliseconds_sum',
          mode,
        );
        const delayCount = sumMetric(
          metrics,
          'websocket_outbound_queue_delay_milliseconds_count',
          mode,
        );

        if (!cancelled) {
          setQueueMetrics({
            depth: sumMetric(metrics, 'websocket_outbound_queue_depth', mode),
            capacity: sumMetric(metrics, 'websocket_outbound_queue_capacity', mode),
            waits: sumMetric(metrics, 'websocket_outbound_backpressure_waits_total', mode),
            averageDelayMilliseconds: delayCount > 0 ? delaySum / delayCount : 0,
          });
        }
      } catch {
        // The metrics panel is supplemental; connection controls remain usable if it is unavailable.
      }
    };

    void refreshMetrics();
    const interval = window.setInterval(() => void refreshMetrics(), 1000);

    return () => {
      cancelled = true;
      window.clearInterval(interval);
    };
  }, [connectionState, slowMode]);

  const isConnected = connectionState === 'connected';

  return (
    <section className="walkthrough" aria-labelledby="walkthrough-title">
      <div className="walkthrough-intro">
        <div>
          <div className="eyebrow">
            <span>Transport 01</span>
            <span className="eyebrow-line" aria-hidden="true" />
            <span>Raw WebSocket</span>
          </div>
          <h1 id="walkthrough-title">Follow one message through the connection.</h1>
          <p>
            Work through the controls in order. The console records the application messages that
            cross the socket and the events published through HTTP.
          </p>
        </div>

        <div className={`connection-badge ${connectionState}`} role="status" aria-live="polite">
          <span aria-hidden="true" />
          {connectionState}
        </div>
      </div>

      <div className="walkthrough-layout">
        <div className="steps-column">
          <article className="walkthrough-step">
            <div className="step-number">01</div>
            <div className="step-content">
              <p className="step-kicker">Connection</p>
              <h2>Open a persistent channel</h2>
              <p>
                Establish the HTTP upgrade to <code>/ws</code>. Once connected, the same channel
                carries messages in both directions.
              </p>
              <div className="button-row">
                <button
                  className="primary-action compact"
                  type="button"
                  disabled={connectionState !== 'disconnected'}
                  onClick={connect}
                >
                  {connectionState === 'connecting' ? 'Connecting…' : 'Connect'}
                </button>
                <button
                  className="secondary-action"
                  type="button"
                  disabled={!isConnected}
                  onClick={disconnect}
                >
                  Disconnect
                </button>
              </div>
              <label className="mode-toggle">
                <input
                  type="checkbox"
                  checked={slowMode}
                  disabled={connectionState !== 'disconnected'}
                  onChange={(event) => setSlowMode(event.target.checked)}
                />
                <span>
                  <strong>Controlled slow client</strong>
                  Delay each server send for this connection.
                </span>
              </label>
              {slowMode && (
                <div className="inline-field">
                  <label className="field-label" htmlFor="send-delay">
                    Send delay (milliseconds)
                  </label>
                  <input
                    id="send-delay"
                    type="number"
                    min="1"
                    max="2000"
                    value={sendDelayMilliseconds}
                    disabled={connectionState !== 'disconnected'}
                    onChange={(event) =>
                      setSendDelayMilliseconds(
                        Math.min(2000, Math.max(1, Number(event.target.value) || 1)),
                      )
                    }
                  />
                </div>
              )}
            </div>
          </article>

          <article className="walkthrough-step">
            <div className="step-number">02</div>
            <div className="step-content">
              <p className="step-kicker">Round trip</p>
              <h2>Send ping, receive pong</h2>
              <p>
                Send a typed application message and observe the server route it to the ping
                handler before returning a pong.
              </p>
              <button
                className="secondary-action"
                type="button"
                disabled={!isConnected}
                onClick={() => send({ type: 'ping' }, 'Ping')}
              >
                Send ping
              </button>
            </div>
          </article>

          <article className="walkthrough-step">
            <div className="step-number">03</div>
            <div className="step-content">
              <p className="step-kicker">Broadcast</p>
              <h2>Publish an event</h2>
              <p>
                The HTTP endpoint stores the event, assigns its sequence, and broadcasts it to
                every connected WebSocket client.
              </p>
              <label className="field-label" htmlFor="event-message">
                Event message
              </label>
              <div className="input-action">
                <input
                  id="event-message"
                  value={message}
                  onChange={(event) => setMessage(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') void publishEvent();
                  }}
                />
                <button
                  className="primary-action compact"
                  type="button"
                  disabled={requestPending || !message.trim()}
                  onClick={() => void publishEvent()}
                >
                  {requestPending ? 'Publishing…' : 'Publish'}
                </button>
              </div>
              <div className="sequence-summary">
                Latest sequence <strong>{latestSequence || '—'}</strong>
              </div>
            </div>
          </article>

          <article className="walkthrough-step">
            <div className="step-number">04</div>
            <div className="step-content">
              <p className="step-kicker">Client state</p>
              <h2>Acknowledge receipt</h2>
              <p>
                Tell the server which sequence this connection has observed. The current baseline
                records the value in memory and intentionally sends no response.
              </p>
              <button
                className="secondary-action"
                type="button"
                disabled={!isConnected || latestSequence < 1}
                onClick={acknowledgeLatest}
              >
                Acknowledge latest
              </button>
              <div className="sequence-summary">
                Acknowledged <strong>{acknowledgedSequence ?? '—'}</strong>
              </div>
            </div>
          </article>

          <article className="walkthrough-step">
            <div className="step-number">05</div>
            <div className="step-content">
              <p className="step-kicker">Recovery</p>
              <h2>Replay missed events</h2>
              <p>
                Disconnect, publish an event while offline, reconnect, then request every event
                after the last sequence you received.
              </p>
              <label className="field-label" htmlFor="replay-sequence">
                Replay after sequence
              </label>
              <div className="input-action input-action-small">
                <input
                  id="replay-sequence"
                  type="number"
                  min="0"
                  value={replayFrom}
                  onChange={(event) => setReplayFrom(Math.max(0, Number(event.target.value) || 0))}
                />
                <button
                  className="secondary-action"
                  type="button"
                  disabled={!isConnected}
                  onClick={() =>
                    send({ type: 'replay', lastSequence: replayFrom }, `Replay after ${replayFrom}`)
                  }
                >
                  Request replay
                </button>
              </div>
            </div>
          </article>

          <article className="walkthrough-step experiment-step">
            <div className="step-number">06</div>
            <div className="step-content">
              <p className="step-kicker">Backpressure experiment</p>
              <h2>Fill one client's outbound channel</h2>
              <p>
                Disconnect, enable the controlled slow client above, reconnect, and publish a
                burst. At the default settings, 750 events arrive faster than the 500-message
                channel can drain, forcing publishers to wait for capacity.
              </p>
              <label className="field-label" htmlFor="burst-count">
                Burst event count
              </label>
              <div className="input-action input-action-small">
                <input
                  id="burst-count"
                  type="number"
                  min="1"
                  max="1000"
                  value={burstCount}
                  onChange={(event) =>
                    setBurstCount(Math.min(1000, Math.max(1, Number(event.target.value) || 1)))
                  }
                />
                <button
                  className="primary-action compact"
                  type="button"
                  disabled={!isConnected || !slowMode || burstPending}
                  onClick={() => void publishBurst()}
                >
                  {burstPending ? 'Publishing burstâ€¦' : 'Publish controlled burst'}
                </button>
              </div>
              <div className="metrics-strip" aria-live="polite">
                <div><span>Queue</span><strong>{queueMetrics?.depth ?? 'â€”'} / {queueMetrics?.capacity ?? 'â€”'}</strong></div>
                <div><span>Waits</span><strong>{queueMetrics?.waits ?? 'â€”'}</strong></div>
                <div><span>Avg delay</span><strong>{queueMetrics ? `${queueMetrics.averageDelayMilliseconds.toFixed(1)} ms` : 'â€”'}</strong></div>
              </div>
              <p className="experiment-note">
                Metrics refresh once per second. Grafana retains the full time series after the
                queue drains.
              </p>
            </div>
          </article>
        </div>

        <aside className="event-console" aria-labelledby="console-title">
          <div className="console-header">
            <div>
              <p>Live session</p>
              <h2 id="console-title">Event console</h2>
            </div>
            <button type="button" onClick={() => setLogs([])} disabled={logs.length === 0}>
              Clear
            </button>
          </div>

          <div className="console-body" ref={consoleBodyRef} aria-live="polite">
            {logs.length === 0 ? (
              <div className="empty-console">
                <span aria-hidden="true">_</span>
                <p>Connect to begin the walkthrough.</p>
              </div>
            ) : (
              logs.map((entry) => (
                <div className={`log-entry fresh-entry ${entry.kind}`} key={entry.id}>
                  <div className="log-meta">
                    <span>{entry.time}</span>
                    <span>{labels[entry.kind]}</span>
                  </div>
                  <strong>{entry.label}</strong>
                  {entry.route && (
                    <dl className="log-route">
                      <div>
                        <dt>From</dt>
                        <dd className="tooltip-value" title={entry.route.from} tabIndex={0}>
                          {entry.route.from}
                        </dd>
                      </div>
                      <div>
                        <dt>To</dt>
                        <dd className="tooltip-value" title={entry.route.to} tabIndex={0}>
                          {entry.route.to}
                        </dd>
                      </div>
                      {entry.route.via && (
                        <div>
                          <dt>Via</dt>
                          <dd className="tooltip-value" title={entry.route.via} tabIndex={0}>
                            {entry.route.via}
                          </dd>
                        </div>
                      )}
                      <div className="route-endpoint">
                        <dt>Endpoint</dt>
                        <dd
                          className="tooltip-value"
                          title={entry.route.endpoint}
                          tabIndex={0}
                        >
                          {entry.route.endpoint}
                        </dd>
                      </div>
                    </dl>
                  )}
                  {entry.payload && <pre>{entry.payload}</pre>}
                </div>
              ))
            )}
          </div>

          <div className="console-footer">
            <span>{logs.length} entries</span>
            <span>Newest events appear last</span>
          </div>
        </aside>
      </div>
    </section>
  );
}
