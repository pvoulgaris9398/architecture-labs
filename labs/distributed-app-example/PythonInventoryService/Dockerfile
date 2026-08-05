FROM python:3.12-slim
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY . .
# We don't declare an ENTRYPOINT here so the same image can run main.py or worker.py
