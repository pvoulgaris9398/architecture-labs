import { useEffect, useRef, useState } from 'react';
interface Record { sequence: number; message: string; timestamp: string }
interface Entry { id: number; text: string }

export default function LongPollingWalkthrough() {
  const abortRef = useRef<AbortController | null>(null);
  const runningRef = useRef(false);
  const nextId = useRef(1);
  const [running, setRunning] = useState(false);
  const [sequence, setSequence] = useState(0);
  const [message, setMessage] = useState('Hello from long polling');
  const [entries, setEntries] = useState<Entry[]>([]);
  const log = (text: string) => setEntries((items) => [...items.slice(-99), { id: nextId.current++, text }]);

  const pollLoop = async (start: number) => {
    let since = start;
    while (runningRef.current) {
      const controller = new AbortController(); abortRef.current = controller;
      log(`GET /poll?since=${since} â€” waiting`);
      try {
        const response = await fetch(`/long-polling/api/events/poll?since=${since}&timeoutSeconds=10`, { signal: controller.signal, cache: 'no-store' });
        if (response.status === 204) { log('204 No Content â€” immediately polling again'); continue; }
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const records = await response.json() as Record[];
        for (const record of records) { since = Math.max(since, record.sequence); log(`Event ${record.sequence}: ${record.message}`); }
        setSequence(since);
      } catch (error) {
        if (!controller.signal.aborted) log(error instanceof Error ? error.message : String(error));
      }
    }
  };

  const start = () => { runningRef.current = true; setRunning(true); void pollLoop(sequence); };
  const stop = () => { runningRef.current = false; setRunning(false); abortRef.current?.abort(); log('Outstanding poll cancelled'); };
  const publish = async () => {
    const response = await fetch('/long-polling/api/events', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ message }) });
    log(response.ok ? 'POST published; pending poll should complete' : `Publish failed: HTTP ${response.status}`);
  };
  useEffect(() => () => { runningRef.current = false; abortRef.current?.abort(); }, []);

  return <section className="walkthrough" aria-labelledby="long-poll-title">
    <div className="walkthrough-intro"><div><div className="eyebrow"><span>Transport 03</span><span className="eyebrow-line" /><span>Long polling</span></div><h1 id="long-poll-title">Turn waiting into repeated HTTP requests.</h1><p>Each poll waits for an event or timeout, returns, and is immediately replaced by the next request.</p></div><div className={`connection-badge ${running ? 'connected' : 'disconnected'}`}><span />{running ? 'polling' : 'stopped'}</div></div>
    <div className="walkthrough-layout"><div className="steps-column">
      <article className="walkthrough-step"><div className="step-number">01</div><div className="step-content"><p className="step-kicker">Request cycle</p><h2>Start repeated polls</h2><p>The server holds each GET for up to ten seconds. A 204 timeout causes the browser to issue another request.</p><div className="button-row"><button className="primary-action compact" disabled={running} onClick={start}>Start polling</button><button className="secondary-action" disabled={!running} onClick={stop}>Stop and cancel</button></div><div className="sequence-summary">Cursor <strong>{sequence}</strong></div></div></article>
      <article className="walkthrough-step"><div className="step-number">02</div><div className="step-content"><p className="step-kicker">Wake-up</p><h2>Publish while a poll waits</h2><p>The append operation signals waiting requests, which return every event after their sequence cursor.</p><div className="input-action"><input value={message} onChange={(e) => setMessage(e.target.value)} /><button className="primary-action compact" onClick={() => void publish()}>Publish</button></div></div></article>
      <article className="walkthrough-step"><div className="step-number">03</div><div className="step-content"><p className="step-kicker">Observe lifecycle</p><h2>Compare connection churn</h2><p>Unlike WebSocket or SSE, the console shows a new HTTP request after every event and timeout.</p></div></article>
    </div><aside className="event-console"><div className="console-header"><div><p>HTTP cycle</p><h2>Poll console</h2></div><button onClick={() => setEntries([])}>Clear</button></div><div className="console-body">{entries.length === 0 ? <div className="empty-console"><span>_</span><p>Start polling to begin.</p></div> : entries.map((entry) => <div className="log-entry fresh-entry http" key={entry.id}><div className="log-meta"><span>{entry.id}</span><span>HTTP</span></div><strong>{entry.text}</strong></div>)}</div><div className="console-footer"><span>{entries.length} entries</span><span>Newest requests appear last</span></div></aside></div>
  </section>;
}
