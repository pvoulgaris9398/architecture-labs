const areas = [
  {
    number: '01',
    title: 'Transports',
    description: 'Compare connection models and message flow.',
  },
  {
    number: '02',
    title: 'Brokers',
    description: 'Explore messaging infrastructure and routing.',
  },
  {
    number: '03',
    title: 'Reliability',
    description: 'Exercise delivery, replay, ordering, and pressure.',
  },
  {
    number: '04',
    title: 'Benchmarks',
    description: 'Observe behavior under controlled workloads.',
  },
];

export default function App() {
  return (
    <main className="page-shell">
      <header className="site-header">
        <a className="brand" href="/" aria-label="Realtime Communication Lab home">
          <span className="brand-mark" aria-hidden="true">
            <span />
            <span />
            <span />
          </span>
          <span>Realtime Communication Lab</span>
        </a>

        <div className="status" aria-label="Application status: foundation ready">
          <span className="status-dot" aria-hidden="true" />
          Foundation ready
        </div>
      </header>

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
          <p>Planned workspace</p>
          <h2 id="exploration-title">The lab will grow here.</h2>
        </div>

        <div className="area-grid">
          {areas.map((area) => (
            <article className="area-card" key={area.title}>
              <div className="card-topline">
                <span>{area.number}</span>
                <span className="planned-label">Planned</span>
              </div>
              <h3>{area.title}</h3>
              <p>{area.description}</p>
            </article>
          ))}
        </div>
      </section>

      <footer>
        <span>Start simple. Measure carefully.</span>
        <span>Interactive controls coming next.</span>
      </footer>
    </main>
  );
}
