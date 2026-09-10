FROM python:3.12-slim
WORKDIR /app
COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt
COPY server.py ./
USER 65532:65532
EXPOSE 1502 1503
CMD ["python", "-u", "server.py"]
