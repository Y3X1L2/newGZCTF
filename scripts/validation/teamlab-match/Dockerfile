FROM python:3.13-alpine

WORKDIR /app
COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt
COPY runner.py scenario.example.json ./
COPY web ./web

EXPOSE 19090
ENTRYPOINT ["python", "runner.py"]
CMD ["--scenario", "scenario.example.json", "--listen", "0.0.0.0:19090"]

