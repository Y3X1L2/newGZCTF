FROM python:3.11-alpine
WORKDIR /app
COPY server.py .
COPY index.html .
RUN chmod +x server.py
EXPOSE 8080
CMD ["python", "server.py"]
