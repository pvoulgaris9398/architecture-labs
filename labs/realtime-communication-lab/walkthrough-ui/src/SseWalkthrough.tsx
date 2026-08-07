import { useEffect, useRef, useState } from 'react';

interface SseEvent { sequence: number; timestamp: string; message: string }
interface LogEntry { id: number; label: string; detail: string }

export default function SseWalkthrough() {
  const sourceRef = useRef<EventSource | null>(null);
  const nextId = useRef(1);
  const [connected, setConnected] = useState(false);
  const [slowMode, setSlowMode] = useState(false);
  const [sendDelay, setSendDelay] = useState(25);
  const [replayFrom, setReplayFrom] = useState(0);
  const [message, setMessage] = useState('Hello over SSE');
  const [burstCount, setBurstCount] = useState(750);
  const [pending, setPending] = useState(false);
  const [latestSequence, setLatestSequence] = useState(0);
  const [logs, setLogs] = useState<LogEntry[]>([]);

  const log = (label: string, detail: unknown) => setLogs((current) => [
    ...current.slice(-99),
    { id: nextId.current++, label, detail: typeof detail === 'string' ? detail : JSON.stringify(detail, null, 2) },
  ]);

  const connect = () => {
    const query = new URLSearchParams();
    if (slowMode) query.set('sendDelayMs', String(sendDelay));
    if (replayFrom > 0) query.set('lastEventId', String(replayFrom));
    const endpoint = `/sse/events/stream${query.size ? `?${query}` : ''}`;
    const source = new EventSource(endpoint);
    sourceRef.current = source;
    log('Opening SSE stream', endpoint);
    source.onopen = () => { setConnected(true); log('SSE stream opened', endpoint); };
    source.addEventListener('message', (event) => {
      const record = JSON.parse((event as MessageEvent<string>).data) as SseEvent;
      setLatestSequence((current) => Math.max(current, record.sequence));
      log(`Event ${record.sequence}`, record);
    });
    source.onerror = () => {
      if (source.readyState === EventSource.CLOSED) {
        setConnected(false);
        log('SSE stream closed', 'The EventSource is no longer reconnecting.');
      } else {
        log('SSE reconnecting', 'The browser will retry and send Last-Event-ID automatically.');
      }
    };
  };

  const disconnect = () => {
    sourceRef.current?.close();
    sourceRef.current = null;
    setConnected(false);
    log('SSE stream closed', 'Closed by walkthrough.');
  };

  const post = async (path: string, body: object, label: string) => {
    setPending(true);
    try {
      const response = await fetch(`/sse${path}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}: ${await response.text()}`);
      const result = await response.json();
      log(label, result);
    } catch (error) {
      log(`${label} failed`, error instanceof Error ? error.message : String(error));
    } finally { setPending(false); }
  };

  useEffect(() => () => sourceRef.current?.close(), []);

  return (
    <section className="walkthrough" aria-labelledby="sse-title">
      <div className="walkthrough-intro">
        <div>
          <div className="eyebrow"><span>Transport 02</span><span className="eyebrow-line" /><span>Server-Sent Events</span></div>
          <h1 id="sse-title">Stream ordered events over ordinary HTTP.</h1>
          <p>SSE is a one-way UTF-8 event stream. Publishing and client commands use separate HTTP requests.</p>
        </div>
        <div className={`connection-badge ${connected ? 'connected' : 'disconnected'}`}><span />{connected ? 'connected' : 'disconnected'}</div>
      </div>
      <div className="walkthrough-layout">
        <div className="steps-column">
          <article className="walkthrough-step"><div className="step-number">01</div><div className="step-content">
            <p className="step-kicker">Event stream</p><h2>Open the server-to-client stream</h2>
            <p>The browser uses EventSource. Configure replay or a controlled send delay before connecting.</p>
            <div className="button-row"><button className="primary-action compact" disabled={connected} onClick={connect}>Connect SSE</button><button className="secondary-action" disabled={!connected} onClick={disconnect}>Disconnect</button></div>
            <label className="mode-toggle"><input type="checkbox" checked={slowMode} disabled={connected} onChange={(e) => setSlowMode(e.target.checked)} /><span><strong>Controlled slow client</strong>Delay each server write for this stream.</span></label>
            {slowMode && <div className="inline-field"><label className="field-label" htmlFor="sse-delay">Send delay (ms)</label><input id="sse-delay" type="number" min="1" max="2000" value={sendDelay} disabled={connected} onChange={(e) => setSendDelay(Math.min(2000, Math.max(1, Number(e.target.value) || 1)))} /></div>}
          </div></article>
          <article className="walkthrough-step"><div className="step-number">02</div><div className="step-content">
            <p className="step-kicker">Separate HTTP command</p><h2>Publish an event</h2><p>POST stores and broadcasts an event; the open SSE response carries it back to every subscriber.</p>
            <div className="input-action"><input value={message} onChange={(e) => setMessage(e.target.value)} /><button className="primary-action compact" disabled={pending || !message.trim()} onClick={() => void post('/api/events', { message }, 'Event published')}>Publish</button></div>
            <div className="sequence-summary">Latest sequence <strong>{latestSequence || 'â€”'}</strong></div>
          </div></article>
          <article className="walkthrough-step"><div className="step-number">03</div><div className="step-content">
            <p className="step-kicker">Recovery</p><h2>Resume after an event ID</h2><p>Disconnect, publish while offline, enter the last received ID, and reconnect. Native retries use the Last-Event-ID header automatically; this field makes manual replay visible.</p>
            <div className="input-action input-action-small"><input type="number" min="0" value={replayFrom} disabled={connected} onChange={(e) => setReplayFrom(Math.max(0, Number(e.target.value) || 0))} /><button className="secondary-action" disabled={connected} onClick={() => setReplayFrom(latestSequence)}>Use latest ID</button></div>
          </div></article>
          <article className="walkthrough-step experiment-step"><div className="step-number">04</div><div className="step-content">
            <p className="step-kicker">Backpressure</p><h2>Fill this stream's bounded channel</h2><p>Connect in slow mode and publish more events than the 500-message channel can hold.</p>
            <div className="input-action input-action-small"><input type="number" min="1" max="1000" value={burstCount} onChange={(e) => setBurstCount(Math.min(1000, Math.max(1, Number(e.target.value) || 1)))} /><button className="primary-action compact" disabled={!connected || !slowMode || pending} onClick={() => void post('/api/events/burst', { count: burstCount, messagePrefix: 'sse-slow-client-test' }, 'Burst enqueued')}>{pending ? 'Publishingâ€¦' : 'Publish burst'}</button></div>
          </div></article>
        </div>
        <aside className="event-console"><div className="console-header"><div><p>Live session</p><h2>SSE console</h2></div><button onClick={() => setLogs([])}>Clear</button></div><div className="console-body">{logs.length === 0 ? <div className="empty-console"><span>_</span><p>Connect to begin.</p></div> : logs.map((entry) => <div className="log-entry fresh-entry received" key={entry.id}><div className="log-meta"><span>{entry.id}</span><span>SSE</span></div><strong>{entry.label}</strong><pre>{entry.detail}</pre></div>)}</div><div className="console-footer"><span>{logs.length} entries</span><span>Newest events appear last</span></div></aside>
      </div>
    </section>
  );
}
