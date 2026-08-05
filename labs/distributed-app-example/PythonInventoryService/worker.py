import pika
import json
import time
from opentelemetry import trace
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.sdk.resources import Resource

import os
rabbit_host = os.getenv("ConnectionStrings__RabbitMQ", "localhost")
otel_endpoint = os.getenv("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317")
connection = pika.BlockingConnection(pika.ConnectionParameters(host=rabbit_host))


# Configure OpenTelemetry Tracing for Worker Process
resource = Resource.create(attributes={"service.name": "PythonOrderWorker"})
provider = TracerProvider(resource=resource)
processor = BatchSpanProcessor(OTLPSpanExporter(endpoint=otel_endpoint, insecure=True))
provider.add_span_processor(processor)
trace.set_tracer_provider(provider)
tracer = trace.get_tracer(__name__)

def process_order(ch, method, properties, body):
    # Parse raw payload bytes 
    event_data = json.loads(body.decode('utf-8'))

    # Track messaging processing work inside its own Trace Span context
    with tracer.start_as_current_span("ProcessAsyncOrderPayload") as span:
        span.set_attribute("order.id", event_data.get("OrderId"))
        span.set_attribute("product.id", event_data.get("ProductId"))

        print(f" [x] Processing Order: {event_data.get('OrderId')} for Product {event_data.get('ProductId')}")

        # Mimic real async heavy lifter workload (e.g. charging card, updating DB)
        time.sleep(2) 

        print(" [x] Order finalized and saved successfully.")

        # Acknowledge completion back to RabbitMQ broker engine
        ch.basic_ack(delivery_tag=method.delivery_tag)

def main():
    # Connect to RabbitMQ container
    connection = pika.BlockingConnection(pika.ConnectionParameters(host=rabbit_host))
    channel = connection.channel()

    # Match exchange layout declared in .NET application
    channel.exchange_declare(exchange='order_exchange', exchange_type='topic', durable=True)

    # Declare temporary local worker processing queue
    result = channel.queue_declare(queue='order_processing_queue', durable=True)
    queue_name = result.method.queue

    # Bind local queue to the exchange matching the correct Routing Key
    channel.queue_bind(exchange='order_exchange', queue=queue_name, routing_key='order.placed')

    print(' [*] Waiting for Order Placement Messages. To exit press CTRL+C')

    # Restrict worker to pull 1 message at a time to distribute workload
    channel.basic_qos(prefetch_count=1)
    channel.basic_consume(queue=queue_name, on_message_callback=process_order)

    channel.start_consuming()

if __name__ == '__main__':
    try:
        main()
    except KeyboardInterrupt:
        print('Worker stopped.')
