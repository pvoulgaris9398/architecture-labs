# Shared observability Compose baseline

This directory contains stable container mechanics that multiple labs can reuse without sharing
their experimental behavior.

The baseline requires Docker Compose v2 with service `extends` support.

## Available baselines

- `otel-collector-base`
- `prometheus-base`
- `grafana-base`
- `jaeger-base`
- `seq-base`

These baselines pin the currently adopted image and define only stable startup behavior. Labs can
override a pin deliberately when validating a different version.

## OpenTelemetry Collector

Labs extend `otel-collector-base` from `service.yaml`, then provide their own collector image pin,
configuration mount, ports, resource limits, dependencies, and telemetry pipeline.

```yaml
services:
  otel-collector:
    extends:
      file: ../../shared/compose/observability/service.yaml
      service: otel-collector-base
    image: otel/opentelemetry-collector-contrib:<pinned-version>
    volumes:
      - ./otel-collector/config.yaml:/etc/otelcol-contrib/config.yaml:ro
```

The shared baseline standardizes the collector configuration path and restart policy. It does not
define receivers, processors, exporters, host ports, backends, or application instrumentation;
those affect a lab's behavior and remain visible inside that lab.

The Prometheus, Grafana, Jaeger, and Seq baselines follow the same rule. Scrape targets,
provisioning, dashboards, credentials, storage, ports, retention, and backend relationships remain
lab-owned.

Run `docker compose config --quiet` from every consuming lab after changing this baseline.
