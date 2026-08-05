import asyncio
import grpc
from concurrent import futures
import inventory_pb2
import inventory_pb2_grpc

# OpenTelemetry Setup
from opentelemetry import trace
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.sdk.resources import Resource
from opentelemetry.instrumentation.grpc import GrpcInstrumentorServer

import os
otel_endpoint = os.getenv("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317")
# Update processor target variable endpoint string parameter accordingly

# Configure Tracing to route back to our local OTel Collector
resource = Resource.create(attributes={"service.name": "PythonInventoryService"})
provider = TracerProvider(resource=resource)
processor = BatchSpanProcessor(OTLPSpanExporter(endpoint=otel_endpoint, insecure=True))
provider.add_span_processor(processor)
trace.set_tracer_provider(provider)

# Instrument the gRPC server layer
grpc_server_instrumentor = GrpcInstrumentorServer()
grpc_server_instrumentor.instrument()

tracer = trace.get_tracer(__name__)

class InventoryServiceServicer(inventory_pb2_grpc.InventoryServiceServicer):
    def CheckStock(self, request, context):
        # This code runs inside the parent span passed from .NET
        with tracer.start_as_current_span("CalculateInventoryLevels") as span:
            pid = request.product_id
            span.set_attribute("product.id", pid)

            # Simulated database/warehouse inventory logic
            mock_stock = 45 if pid == 1 else 0
            is_available = mock_stock > 0

            span.set_attribute("inventory.available", mock_stock)

            return inventory_pb2.StockResponse(
                product_id=pid,
                available_stock=mock_stock,
                is_in_stock=is_available
            )

async def serve():
    server = grpc.aio.server()
    inventory_pb2_grpc.add_InventoryServiceServicer_to_server(InventoryServiceServicer(), server)
    server.add_insecure_port('[::]:50051')
    print("Python gRPC Server running smoothly on port 50051...")
    await server.start()
    await server.wait_for_termination()

if __name__ == '__main__':
    asyncio.run(serve())
