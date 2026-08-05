#!/bin/bash

python -m grpc_tools.protoc -I./Protos \
 --python_out=./PythonInventoryService \
 --grpc_python_out=./PythonInventoryService inventory.proto