FROM python:3.12-slim

WORKDIR /srv
RUN printf '%s\n' 'TEAMLAB_P1_WEB_OK' > index.html

EXPOSE 80
CMD ["python", "-m", "http.server", "80"]
