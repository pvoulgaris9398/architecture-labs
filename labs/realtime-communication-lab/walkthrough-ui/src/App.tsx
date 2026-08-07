import { useState } from 'react';
import WebSocketWalkthrough from './WebSocketWalkthrough';
import SseWalkthrough from './SseWalkthrough';
import LongPollingWalkthrough from './LongPollingWalkthrough';

type View = 'overview' | 'websocket' | 'sse' | 'long-polling';

const areas = [
  {
    number: '01',
    title: 'Transports',
    description: 'Compare connection models and message flow.',
    status: 'In progress',
  },
  {
    number: '02',
    title: 'Brokers',
    description: 'Explore messaging infrastructure and routing.',
    status: 'Planned',
  },
  {
    number: '03',
    title: 'Reliability',
    description: 'Exercise delivery, replay, ordering, and pressure.',
    status: 'Planned',
  },
  {
    number: '04',
    title: 'Benchmarks',
    description: 'Observe behavior under controlled workloads.',
    status: 'Planned',
  },
];

function Overview({ openWebSocket }: { openWebSocket: () => void }) {
  return (
    <>
      <section className="hero" aria-labelledby="page-title">
        <div className="eyebrow">
          <span>Architecture Labs</span>
          <span className="eyebrow-line" aria-hidden="true" />
          <span>Realtime systems</span>
        </div>

        <h1 id="page-title">
          Explore how messages
          <span> move in real time.</span>
        </h1>

        <p className="hero-copy">
          A hands-on workspace for comparing transports, brokers, and reliability patterns through
          small, observable experiments.
        </p>

        <button className="primary-action" type="button" onClick={openWebSocket}>
          Start the WebSocket walkthrough
          <span aria-hidden="true">→</span>
        </button>

        <div className="signal-track" aria-hidden="true">
          <span className="signal-node" />
          <span className="signal-line" />
          <span className="signal-pulse" />
          <span className="signal-line" />
          <span className="signal-node signal-node-end" />
        </div>
      </section>

      <section className="exploration" aria-labelledby="exploration-title">
        <div className="section-heading">
          <p>Lab workspace</p>
          <h2 id="exploration-title">Build understanding one path at a time.</h2>
        </div>

        <div className="area-grid">
          {areas.map((area) => (
            <article className="area-card" key={area.title}>
              <div className="card-topline">
                <span>{area.number}</span>
                <span className={area.status === 'In progress' ? 'active-label' : 'planned-label'}>
                  {area.status}
                </span>
              </div>
              <h3>{area.title}</h3>
              <p>{area.description}</p>
            </article>
          ))}
        </div>
      </section>
    </>
  );
}

export default function App() {
  const [view, setView] = useState<View>('overview');

  return (
    <main className="page-shell">
      <header className="site-header">
        <button
          className="brand"
          type="button"
          aria-label="Open Realtime Communication Lab overview"
          onClick={() => setView('overview')}
        >
          <span className="brand-mark" aria-hidden="true">
            <span />
            <span />
            <span />
          </span>
          <span>Realtime Communication Lab</span>
        </button>
        <button type="button" className={view === 'long-polling' ? 'view-tab active' : 'view-tab'} aria-current={view === 'long-polling' ? 'page' : undefined} onClick={() => setView('long-polling')}>
          Long polling <span className="tab-available">Available</span>
        </button>

        <div className="status" aria-label="Application status: interactive lab ready">
          <span className="status-dot" aria-hidden="true" />
          Interactive lab
        </div>
      </header>

      <nav className="view-tabs" aria-label="Lab sections">
        <button
          type="button"
          className={view === 'overview' ? 'view-tab active' : 'view-tab'}
          aria-current={view === 'overview' ? 'page' : undefined}
          onClick={() => setView('overview')}
        >
          Overview
        </button>
        <button
          type="button"
          className={view === 'websocket' ? 'view-tab active' : 'view-tab'}
          aria-current={view === 'websocket' ? 'page' : undefined}
          onClick={() => setView('websocket')}
        >
          WebSocket
          <span className="tab-available">Available</span>
        </button>
        <button type="button" className="view-tab" disabled>
          SignalR
          <span>Planned</span>
        </button>
        <button
          type="button"
          className={view === 'sse' ? 'view-tab active' : 'view-tab'}
          aria-current={view === 'sse' ? 'page' : undefined}
          onClick={() => setView('sse')}
        >
          SSE
          <span className="tab-available">Available</span>
        </button>
      </nav>

      {view === 'overview' ? (
        <Overview openWebSocket={() => setView('websocket')} />
      ) : view === 'websocket' ? (
        <WebSocketWalkthrough />
      ) : view === 'sse' ? <SseWalkthrough /> : <LongPollingWalkthrough />}

      <footer>
        <span>Start simple. Measure carefully.</span>
        <span>Realtime Communication Lab</span>
      </footer>
    </main>
  );
}
